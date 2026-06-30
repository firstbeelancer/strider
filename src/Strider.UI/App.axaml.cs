using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Strider.Core.Abstractions;
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
        Services = ConfigureServices();

        // Initialize database — runs migrations in order
        var dbInit = Services.GetRequiredService<DatabaseInitializer>();
        dbInit.InitializeAsync().GetAwaiter().GetResult();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = Services.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel,
            };

            viewModel.LoadAccountsCommand.Execute(null);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Centralized DI configuration. Strider.Host/Program.cs delegates here.
    /// This is the single source of truth for service registrations (F-016).
    /// </summary>
    public static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // === Database ===
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "StriderMail", "strider.db");
        var dir = Path.GetDirectoryName(dbPath)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        // F-009: SQLCipher-encrypted database. The encryption key is generated on
        // first launch and stored in the OS keychain. The factory handles both
        // new encrypted DBs and one-time migration from legacy plaintext DBs.
        var dbFactory = new EncryptedSqliteConnectionFactory(
            OperatingSystem.IsWindows()
                ? new DpapiKeychainService()
                : new LibsecretKeychainService(),
            dbPath,
            encryptionEnabled: true);

        // One-time migration: if a v0.1 plaintext database exists, convert it to encrypted.
        // This must happen before DatabaseInitializer runs, so the schema is applied to the
        // encrypted DB.
        try
        {
            dbFactory.MigrateToEncryptedAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            // Log but don't crash — app can still run if migration failed (e.g., keychain unavailable)
            System.Diagnostics.Debug.WriteLine($"DB encryption migration failed: {ex.Message}");
        }

        var connectionString = dbFactory.GetConnectionString();
        services.AddSingleton(dbFactory);
        services.AddSingleton(new DatabaseInitializer(connectionString));
        services.AddSingleton<IAccountStore>(_ => new SqliteAccountStore(connectionString));
        services.AddSingleton<IMessageStore>(_ => new SqliteMessageStore(connectionString));
        services.AddSingleton<ISignatureStore>(_ => new SqliteSignatureStore(connectionString));
        services.AddSingleton<ICalendarStore>(_ => new SqliteCalendarStore(connectionString));

        // === Security ===
        // Platform-specific keychain (F-005)
        services.AddSingleton<IKeychainService>(sp =>
            OperatingSystem.IsWindows()
                ? new DpapiKeychainService()
                : new LibsecretKeychainService());
        services.AddSingleton<HtmlSanitizer>(_ => new HtmlSanitizer(allowExternalImages: false));
        services.AddSingleton<IPgpService, BouncyCastlePgpService>();

        // === Mail gateways (F-007: per-account IMAP factory) ===
        services.AddSingleton<IImapGatewayFactory>(sp =>
            new MailKitImapGatewayFactory(
                sp.GetRequiredService<IKeychainService>(),
                sp.GetRequiredService<IAccountStore>()));
        services.AddTransient<ISmtpGateway>(sp =>
            new MailKitSmtpGateway(sp.GetRequiredService<IKeychainService>()));

        // === AI (F-011: IHttpClientFactory) ===
        // Gateways accept HttpClient via DI — never `new HttpClient()`. API keys
        // are read from keychain per-request via Func<string?> provider.
        // The AiGatewayFactory resolves the correct provider at runtime based on
        // user settings (openai/anthropic/openrouter/custom).
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
///
/// F-011: Both gateways are constructed with HttpClient from IHttpClientFactory
/// (via DI) and IKeychainService for per-request API key lookup.
/// </summary>
public sealed class AiGatewayFactory
{
    private readonly IServiceProvider _services;

    public AiGatewayFactory(IServiceProvider services)
    {
        _services = services;
    }

    /// <summary>
    /// Creates an IAiGateway for the given provider name and API key reference.
    /// The gateway will read the actual API key from keychain on each request.
    /// </summary>
    /// <param name="provider">One of: "openai", "anthropic", "openrouter", "custom".</param>
    /// <param name="apiKeyRef">Keychain key where the API key is stored.</param>
    /// <param name="baseUrl">Custom base URL (for "custom" or "openrouter").</param>
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

            // "openai" and default
            _ => new OpenAiCompatibleGateway(
                httpFactory.CreateClient(nameof(OpenAiCompatibleGateway)),
                keychain,
                baseUrl ?? "https://api.openai.com/v1",
                keyRefProvider),
        };
    }

    /// <summary>
    /// Returns a default OpenAI-compatible gateway with no API key configured.
    /// Used for testing and before user configures AI in Settings.
    /// </summary>
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
