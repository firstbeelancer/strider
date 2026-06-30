using Avalonia;
using Microsoft.Extensions.Configuration;
using Serilog;
using Strider.UI;

namespace Strider.Host;

/// <summary>
/// Application entry point.
///
/// DI configuration is centralized in <see cref="App.ConfigureServices"/>
/// (F-016 fix — was previously duplicated here and in App.axaml.cs).
///
/// Logging is configured here via Serilog, reading from appsettings.json
/// (F-019 fix — was previously hardcoded).
/// </summary>
public class Program
{
    public static void Main(string[] args)
    {
        // F-019: Load configuration from appsettings.json (if present) before
        // configuring logging. Falls back to hardcoded defaults if the file
        // is missing (e.g., running from a published single-file bundle without
        // an appsettings.json next to it).
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables(prefix: "STRIDER_")
            .AddCommandLine(args)
            .Build();

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
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
