using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Strider.UI;

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

            // Build DI host
            var host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder(args)
                .UseSerilog()
                .ConfigureServices(ConfigureServices)
                .Build();

            // Start Avalonia
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

    private static void ConfigureServices(HostBuilderContext context, IServiceCollection services)
    {
        // TODO: Register infrastructure services
        // services.AddSingleton<IImapGateway, MailKitImapGateway>();
        // services.AddSingleton<ISmtpGateway, MailKitSmtpGateway>();
        // services.AddSingleton<IMessageStore, SqliteMessageStore>();
        // services.AddSingleton<IAccountStore, SqliteAccountStore>();
        // services.AddSingleton<IKeychainService, DpapiKeychainService>();
        // services.AddSingleton<IAiGateway, OpenAiCompatibleGateway>();
        // services.AddSingleton<IPgpService, BouncyCastlePgpService>();
        // services.AddSingleton<IEventBus, InMemoryEventBus>();
    }
}
