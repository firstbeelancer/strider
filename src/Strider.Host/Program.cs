using Avalonia;
using Microsoft.Extensions.Configuration;
using Serilog;
using Strider.Core.Platform;
using Strider.UI;

namespace Strider.Host;

/// <summary>
/// Application entry point.
///
/// DI configuration is centralized in <see cref="App.ConfigureServices"/>.
///
/// Logging:
/// - Reads from appsettings.json if present (F-019)
/// - ALWAYS adds Console + File sinks as fallback (so we get logs even when
///   appsettings.json is missing, e.g., in single-file publish)
/// - File sink writes to %LocalAppData%\StriderMail\logs\ (absolute path)
/// - Process-level crash handlers are installed via
///   <see cref="CrashReporter.Install"/> BEFORE any user code runs (ZAI F-029).
/// </summary>
public class Program
{
    /// <summary>Application version string baked into the binary.</summary>
    public const string AppVersion = "0.1.0-rc3";

    [STAThread]
    public static int Main(string[] args)
    {
        // Configure logging FIRST, before anything else can fail.
        try
        {
            ConfigureLogging(args);
        }
        catch (Exception ex)
        {
            // Last-resort crash log.
            try
            {
                File.WriteAllText(AppPaths.CrashLogPath,
                    $"[{DateTime.UtcNow:O}] Failed to configure logging:\n{ex}\n\n");
            }
            catch { /* ignore */ }
        }

        // ZAI F-029: install process-level crash handlers BEFORE any user code
        // runs (including Avalonia / DI). Without these, an exception thrown
        // inside OnFrameworkInitializationCompleted — or in any background
        // task scheduled before the message pump starts — will kill the
        // process without the user ever seeing why.
        CrashReporter.Install();

        try
        {
            Log.Information("Starting Strider Mail {Version}...", AppVersion);
            Log.Information("OS: {OS} {Version}",
                Environment.OSVersion.Platform,
                Environment.OSVersion.VersionString);
            Log.Information("Runtime: .NET {Version}", Environment.Version);
            Log.Information("BaseDirectory: {BaseDir}", AppContext.BaseDirectory);
            Log.Information("CurrentDirectory: {Cwd}", Environment.CurrentDirectory);

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);

            Log.Information("Strider Mail stopped normally.");
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Strider Mail terminated unexpectedly.");
            CrashReporter.Show(ex, "Startup");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static void ConfigureLogging(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables(prefix: "STRIDER_")
            .AddCommandLine(args)
            .Build();

        var logFilePath = Path.Combine(AppPaths.Logs, "strider-.log");

        var loggerConfig = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.WithProperty("App", "StriderMail")
            .Enrich.WithProperty("Version", AppVersion)
            .WriteTo.Console(outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(logFilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate:
                    "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}");

        try
        {
            loggerConfig = loggerConfig.ReadFrom.Configuration(configuration);
        }
        catch (Exception ex)
        {
            try { Console.Error.WriteLine($"Failed to read Serilog config: {ex.Message}"); }
            catch { /* ignore */ }
        }

        Log.Logger = loggerConfig.CreateLogger();
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
