using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Strider.Core.Abstractions;
using Strider.Infrastructure.Persistence;
using Strider.Infrastructure.Services;
using Strider.UI;
using Strider.UI.ViewModels;

namespace Strider.Host;

public class Program
{
    public static void Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .WriteTo.File("logs/strider-.log", rollingInterval: RollingInterval.Day)
            .CreateLogger();

        try
        {
            Log.Information("Starting Strider Mail...");

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);

            Log.Information("Strider Mail stopped.");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Strider Mail terminated unexpectedly.");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    public static IServiceProvider ConfigureServices()
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

        // Infrastructure services
        services.AddSingleton<IAccountStore>(new SqliteAccountStore(connectionString));
        services.AddSingleton<IMessageStore>(new SqliteMessageStore(connectionString));
        services.AddSingleton<IEventBus, InMemoryEventBus>();

        // TODO: Register remaining services
        // services.AddSingleton<IImapGateway, MailKitImapGateway>();
        // services.AddSingleton<ISmtpGateway, MailKitSmtpGateway>();
        // services.AddSingleton<IKeychainService, ...>();
        // services.AddSingleton<IAiGateway, ...>();
        // services.AddSingleton<IPgpService, ...>();

        // ViewModels
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<MessageListViewModel>();
        services.AddTransient<MessageReaderViewModel>();
        services.AddTransient<FolderTreeViewModel>();
        services.AddTransient<AccountWizardViewModel>();

        return services.BuildServiceProvider();
    }
}
