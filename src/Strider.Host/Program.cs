using Avalonia;
using Serilog;
using Strider.UI;

namespace Strider.Host;

/// <summary>
/// Application entry point.
///
/// DI configuration is centralized in <see cref="App.ConfigureServices"/>
/// (F-016 fix — was previously duplicated here and in App.axaml.cs).
/// Logging is configured here via Serilog, reading from appsettings.json
/// when available (F-019 — TODO: load appsettings.json properly).
/// </summary>
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
}
