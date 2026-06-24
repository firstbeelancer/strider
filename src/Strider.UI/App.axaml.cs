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

            // Load initial data
            viewModel.LoadAccountsCommand.Execute(null);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Database
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "StriderMail", "strider.db");
        var dir = Path.GetDirectoryName(dbPath)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        var connectionString = $"Data Source={dbPath}";
        services.AddSingleton(new DatabaseInitializer(connectionString));

        // Infrastructure
        services.AddSingleton<IAccountStore>(new SqliteAccountStore(connectionString));
        services.AddSingleton<IMessageStore>(new SqliteMessageStore(connectionString));
        services.AddSingleton<IEventBus, InMemoryEventBus>();

        // ViewModels
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<MessageListViewModel>();
        services.AddTransient<MessageReaderViewModel>();
        services.AddTransient<FolderTreeViewModel>();
        services.AddTransient<AccountWizardViewModel>();

        return services.BuildServiceProvider();
    }
}
