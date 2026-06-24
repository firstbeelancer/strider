using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Strider.Core.Abstractions;
using Strider.Infrastructure.Persistence;
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

        // Initialize database
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

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Database path
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "StriderMail", "strider.db");
        var dir = Path.GetDirectoryName(dbPath)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        var connectionString = $"Data Source={dbPath}";
        services.AddSingleton(new DatabaseInitializer(connectionString));

        // Infrastructure services
        services.AddSingleton<IAccountStore>(sp => new SqliteAccountStore(connectionString));
        services.AddSingleton<IMessageStore>(sp => new SqliteMessageStore(connectionString));
        services.AddSingleton<ISignatureStore>(sp => new SqliteSignatureStore(connectionString));
        services.AddSingleton<ICalendarStore>(sp => new SqliteCalendarStore(connectionString));
        services.AddSingleton<IEventBus, InMemoryEventBus>();

        // TODO: Register when fully implemented
        // services.AddSingleton<IImapGateway, MailKitImapGateway>();
        // services.AddSingleton<ISmtpGateway, MailKitSmtpGateway>();
        // services.AddSingleton<IKeychainService, ...>();
        // services.AddSingleton<IAiGateway>(sp => new OpenAiCompatibleGateway(apiKey));
        // services.AddSingleton<IPgpService, BouncyCastlePgpService>();

        // ViewModels
        services.AddTransient<MessageListViewModel>();
        services.AddTransient<MessageReaderViewModel>();
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<ComposeViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<CalendarViewModel>();

        return services.BuildServiceProvider();
    }
}
