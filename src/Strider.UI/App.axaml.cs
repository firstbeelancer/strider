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

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        try
        {
            Services = ConfigureServices();
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
        TryInitializeDatabaseWithTimeout();

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
    /// Runs <see cref="DatabaseInitializer.InitializeAsync"/> on a background
    /// thread with a 30-second hard timeout. If initialization hangs or fails,
    /// logs the error to Serilog and allows the app to start without a working
    /// DB. Stores will fail gracefully until the user retries.
    /// </summary>
    private void TryInitializeDatabaseWithTimeout()
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
            var task = dbInit.InitializeAsync();
            if (!task.Wait(TimeSpan.FromSeconds(30)))
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
    public static IServiceProvider ConfigureServices()
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

        var dbFactory = new EncryptedSqliteConnectionFactory(
            KeychainFactory(),
            dbPath,
            encryptionEnabled: true);

        // One-time migration from legacy plaintext DB to encrypted.
        try
        {
            dbFactory.MigrateToEncryptedAsync().GetAwaiter().GetResult();
            Serilog.Log.Information("Database encryption migration completed (or no migration needed).");
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "DB encryption migration failed.");
        }

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

        // === AI ===
        services.AddHttpClient<OpenAiCompatibleGateway>();
        services.AddHttpClient<AnthropicGateway>();
        services.AddSingleton<AiGatewayFactory>(sp => new AiGatewayFactory(sp));

        // === Events ===
        services.AddSingleton<IEventBus, InMemoryEventBus>();

        // === ViewModels ===
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<MessageListViewModel>();
        services.AddTransient<MessageReaderViewModel>();
        services.AddTransient<ComposeViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<CalendarViewModel>();
        services.AddTransient<AccountWizardViewModel>();

        return services.BuildServiceProvider();
    }
}

/// <summary>
/// Resolves the correct <see cref="IAiGateway"/> implementation based on
/// user settings (provider field in ai_settings table). Reads API key from
/// keychain on each request — never caches the key in memory.
/// </summary>
public sealed class AiGatewayFactory
{
    private readonly IServiceProvider _services;

    public AiGatewayFactory(IServiceProvider services)
    {
        _services = services;
    }

    public IAiGateway Create(string provider, string apiKeyRef, string? baseUrl = null)
    {
        var keychain = _services.GetRequiredService<IKeychainService>();
        var keyRefProvider = new Func<string?>(() => apiKeyRef);

        var httpFactory = _services.GetRequiredService<IHttpClientFactory>();

        return provider.ToLowerInvariant() switch
        {
            "anthropic" => new AnthropicGateway(
                httpFactory.CreateClient(nameof(AnthropicGateway)),
                keychain,
                baseUrl ?? "https://api.anthropic.com/v1",
                keyRefProvider),

            "openrouter" => new OpenAiCompatibleGateway(
                httpFactory.CreateClient(nameof(OpenAiCompatibleGateway)),
                keychain,
                baseUrl ?? "https://openrouter.ai/api/v1",
                keyRefProvider),

            "custom" => new OpenAiCompatibleGateway(
                httpFactory.CreateClient(nameof(OpenAiCompatibleGateway)),
                keychain,
                baseUrl ?? throw new ArgumentNullException(nameof(baseUrl), "Custom provider requires baseUrl"),
                keyRefProvider),

            _ => new OpenAiCompatibleGateway(
                httpFactory.CreateClient(nameof(OpenAiCompatibleGateway)),
                keychain,
                baseUrl ?? "https://api.openai.com/v1",
                keyRefProvider),
        };
    }

    public IAiGateway GetDefault()
    {
        var keychain = _services.GetRequiredService<IKeychainService>();
        var httpFactory = _services.GetRequiredService<IHttpClientFactory>();
        return new OpenAiCompatibleGateway(
            httpFactory.CreateClient(nameof(OpenAiCompatibleGateway)),
            keychain,
            "https://api.openai.com/v1",
            apiKeyRefProvider: () => null);
    }
}
