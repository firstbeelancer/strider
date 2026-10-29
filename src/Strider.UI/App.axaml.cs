using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Strider.Core.Abstractions;
using Strider.Core.Platform;
using Strider.Infrastructure.Ai;
using Strider.Infrastructure.Mail;
using Strider.Infrastructure.Persistence;
using Strider.Infrastructure.Security;
using Strider.Infrastructure.Services;
using Strider.UI.ViewModels;
using Strider.UI.Views;

namespace Strider.UI;

public class App : Application
{
    public IServiceProvider Services { get; private set; } = null!;

    /// <summary>The SQLite connection factory, kept so startup can run the encryption migration.</summary>
    public EncryptedSqliteConnectionFactory? DatabaseFactory { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        try
        {
            Services = ConfigureServices(out var dbFactory);
            DatabaseFactory = dbFactory;
        }
        catch (Exception ex)
        {
            // ZAI F-029: DI failures were previously killing the process before
            // the user could see any feedback. Surface them as a startup crash.
            Serilog.Log.Fatal(ex, "DI configuration failed.");
            CrashReporter.Show(ex, "DI Configuration");
            throw;
        }

        // ZAI F-030: Initialize database with a hard timeout. If migration or
        // first-time SQLCipher key-derivation hangs (slow disk, locked file),
        // don't freeze the UI thread forever — continue without a writable DB
        // and show a warning.
        TryInitializeDatabaseWithTimeout(DatabaseFactory);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try
            {
                var viewModel = Services.GetRequiredService<MainWindowViewModel>();
                desktop.MainWindow = new MainWindow
                {
                    DataContext = viewModel,
                };

                // ZAI F-037: don't run the async command via Execute(null) — that
                // pattern swallows exceptions as unobserved tasks. Wrap explicitly.
                _ = LoadAccountsSafelyAsync(viewModel);
            }
            catch (Exception ex)
            {
                Serilog.Log.Fatal(ex, "MainWindow construction failed.");
                CrashReporter.Show(ex, "MainWindow Construction");
                throw;
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Brings the SQLite database online on a background thread:
    ///   1. one-time plaintext → SQLCipher migration,
    ///   2. schema migrations via <see cref="DatabaseInitializer"/>.
    ///
    /// Wave 5 fix: the encryption migration used to run synchronously inside
    /// <see cref="ConfigureServices"/> — that is, on the UI thread *outside*
    /// the 30-second guard added in Wave 4 (F-030), so a slow disk or a locked
    /// database file froze the window before any handler could fire. Both
    /// steps now happen inside one timed background task.
    /// </summary>
    private void TryInitializeDatabaseWithTimeout(EncryptedSqliteConnectionFactory? dbFactory)
    {
        DatabaseInitializer? dbInit;
        try
        {
            dbInit = Services.GetRequiredService<DatabaseInitializer>();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "DatabaseInitializer could not be resolved from DI.");
            return;
        }

        try
        {
            // Task.Run keeps the work off the UI thread; .Wait(timeout) cannot
            // deadlock because nothing inside awaits the UI thread.
            var initTask = Task.Run(async () =>
            {
                if (dbFactory != null) await dbFactory.MigrateToEncryptedAsync();
                await dbInit.InitializeAsync();
            });

            if (!initTask.Wait(TimeSpan.FromSeconds(30)))
            {
                Serilog.Log.Error(
                    "Database initialization timed out after 30s. App will start without a writable DB.");
                CrashReporter.Show(
                    new TimeoutException("Database initialization timed out after 30 seconds."),
                    "Database Init Timeout",
                    isTerminating: false);
                return;
            }

            Serilog.Log.Information("Database initialized successfully.");
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Database initialization failed. App will run but data won't persist.");
            CrashReporter.Show(ex, "Database Init", isTerminating: false);
        }
    }

    private static async Task LoadAccountsSafelyAsync(MainWindowViewModel viewModel)
    {
        try
        {
            await viewModel.LoadAccountsCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Initial LoadAccounts failed.");
        }
    }

    /// <summary>
    /// Centralized DI configuration. Strider.Host/Program.cs delegates here.
    /// This is the single source of truth for service registrations (F-016).
    ///
    /// ZAI F-028: All persistent state lives under %LocalAppData%\StriderMail\
    /// (resolved by <see cref="AppPaths"/>) instead of the previous split
    /// between ApplicationData (Roaming) for DB/keychain and LocalApplicationData
    /// for logs.
    /// </summary>
    public static IServiceProvider ConfigureServices() => ConfigureServices(out _);

    /// <summary>
    /// Centralized DI configuration (F-016). <paramref name="dbFactory"/> is
    /// returned so the startup sequence can migrate the database without
    /// re-resolving the factory.
    /// </summary>
    public static IServiceProvider ConfigureServices(out EncryptedSqliteConnectionFactory dbFactory)
    {
        var services = new ServiceCollection();

        // === Database ===
        // ZAI F-028: use canonical LocalApplicationData path
        var dbPath = AppPaths.DefaultDatabasePath;

        // ZAI F-026: Lazy keychain factory — constructors do no I/O; directory
        // creation is deferred to first use.
        IKeychainService KeychainFactory() => OperatingSystem.IsWindows()
            ? new DpapiKeychainService()
            : new LibsecretKeychainService();

        dbFactory = new EncryptedSqliteConnectionFactory(
            KeychainFactory(),
            dbPath,
            encryptionEnabled: true);

        // Wave 5: the plaintext → SQLCipher migration moved out of here
        // (it used to block the UI thread before any error handler existed).
        // See TryInitializeDatabaseWithTimeout.
        var connectionString = dbFactory.GetConnectionString();
        services.AddSingleton(dbFactory);
        services.AddSingleton(new DatabaseInitializer(connectionString));
        services.AddSingleton<IAccountStore>(_ => new SqliteAccountStore(connectionString));
        services.AddSingleton<IMessageStore>(_ => new SqliteMessageStore(connectionString));
        services.AddSingleton<ISignatureStore>(_ => new SqliteSignatureStore(connectionString));
        services.AddSingleton<ICalendarStore>(_ => new SqliteCalendarStore(connectionString));

        // === Security ===
        services.AddSingleton<IKeychainService>(_ => KeychainFactory());
        services.AddSingleton<HtmlSanitizer>(_ => new HtmlSanitizer(allowExternalImages: false));
        services.AddSingleton<IPgpService, BouncyCastlePgpService>();

        // === Mail gateways ===
        services.AddSingleton<IImapGatewayFactory>(sp =>
            new MailKitImapGatewayFactory(
                sp.GetRequiredService<IKeychainService>(),
                sp.GetRequiredService<IAccountStore>()));
        services.AddTransient<ISmtpGateway>(sp =>
            new MailKitSmtpGateway(sp.GetRequiredService<IKeychainService>()));
        services.AddSingleton<ImapSessionRunner>();
        services.AddSingleton<IMailSyncService, MailSyncService>();
        services.AddSingleton<IMailActionService>(sp => new MailActionService(
            sp.GetRequiredService<IAccountStore>(),
            sp.GetRequiredService<IMessageStore>(),
            sp.GetRequiredService<ImapSessionRunner>(),
            () => sp.GetRequiredService<ISmtpGateway>()));

        // === AI ===
        // F-036: explicit timeout. LLM completions can be slow, so 60s rather than 30s.
        services.AddHttpClient<OpenAiCompatibleGateway>(c => c.Timeout = TimeSpan.FromSeconds(60));
        services.AddHttpClient<AnthropicGateway>(c => c.Timeout = TimeSpan.FromSeconds(60));
        services.AddSingleton<IAiSettingsStore>(_ => new SqliteAiSettingsStore(connectionString));
        services.AddSingleton<IAiGatewayFactory>(sp => new AiGatewayFactory(
            sp.GetRequiredService<IKeychainService>(),
            sp.GetRequiredService<IHttpClientFactory>()));
        services.AddSingleton<IAiAssistant, AiAssistant>();

        // === Events ===
        services.AddSingleton<IEventBus, InMemoryEventBus>();

        // === ViewModels ===
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<MessageListViewModel>();
        services.AddTransient<MessageReaderViewModel>();
        services.AddTransient<ComposeViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<CalendarViewModel>();
        services.AddTransient<EventEditorViewModel>();
        services.AddTransient<AccountWizardViewModel>();

        return services.BuildServiceProvider();
    }
}
