using Avalonia;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Strider.UI;

namespace Strider.Host;

/// <summary>
/// Application entry point.
///
/// DI configuration is centralized in <see cref="App.ConfigureServices"/>
/// (F-016 fix — was previously duplicated here and in App.axaml.cs).
///
/// Logging:
/// - Reads from appsettings.json if present (F-019)
/// - ALWAYS adds Console + File sinks as fallback (so we get logs even when
///   appsettings.json is missing, e.g., in single-file publish)
/// - File sink writes to %LocalAppData%\StriderMail\logs\ (absolute path,
///   works regardless of working directory)
/// </summary>
public class Program
{
    public static void Main(string[] args)
    {
        // Configure logging FIRST, before anything else can fail.
        // This must be bulletproof — if it throws, the app crashes silently.
        try
        {
            ConfigureLogging(args);
        }
        catch (Exception ex)
        {
            // Last-resort: write to a known location so the user can send us the error
            try
            {
                var crashLogPath = GetCrashLogPath();
                File.WriteAllText(crashLogPath,
                    $"[{DateTime.UtcNow:O}] Failed to configure logging:\n{ex}\n\n");
            }
            catch { }
        }

        try
        {
            Log.Information("Starting Strider Mail v0.1.0-rc2...");
            Log.Information("OS: {OS} {Version}",
                Environment.OSVersion.Platform,
                Environment.OSVersion.VersionString);
            Log.Information("Runtime: .NET {Version}", Environment.Version);
            Log.Information("BaseDirectory: {BaseDir}", AppContext.BaseDirectory);
            Log.Information("CurrentDirectory: {Cwd}", Environment.CurrentDirectory);

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);

            Log.Information("Strider Mail stopped normally.");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Strider Mail terminated unexpectedly.");
            ShowCrashDialog(ex);
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static void ConfigureLogging(string[] args)
    {
        // Load configuration from appsettings.json (if present)
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables(prefix: "STRIDER_")
            .AddCommandLine(args)
            .Build();

        // Determine log directory: %LocalAppData%\StriderMail\logs\ (absolute path)
        var logDir = GetLogDirectory();
        var logFilePath = Path.Combine(logDir, "strider-.log");

        // Build logger: ALWAYS include Console + File as fallback, then add
        // anything from configuration on top. This ensures we get logs even
        // when appsettings.json is missing (e.g., single-file publish).
        var loggerConfig = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.WithProperty("App", "StriderMail")
            .Enrich.WithProperty("Version", "0.1.0-rc2")
            .WriteTo.Console(outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(logFilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate:
                    "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}");

        // Add sinks from configuration (if Serilog section exists)
        // ReadFrom.Configuration reads the Serilog:WriteTo section and adds
        // those sinks ON TOP of the ones we already configured.
        try
        {
            loggerConfig = loggerConfig.ReadFrom.Configuration(configuration);
        }
        catch (Exception ex)
        {
            // Don't crash if configuration is malformed — we already have Console+File
            Console.Error.WriteLine($"Failed to read Serilog config: {ex.Message}");
        }

        Log.Logger = loggerConfig.CreateLogger();
    }

    /// <summary>
    /// Returns the absolute path to the log directory.
    /// Uses %LocalAppData%\StriderMail\logs\ on Windows, ~/.local/share/StriderMail/logs/ on Linux.
    /// Creates the directory if it doesn't exist.
    /// </summary>
    private static string GetLogDirectory()
    {
        string baseDir;
        try
        {
            baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "StriderMail", "logs");
        }
        catch
        {
            // Fallback: next to the executable
            baseDir = Path.Combine(AppContext.BaseDirectory, "logs");
        }

        try
        {
            Directory.CreateDirectory(baseDir);
        }
        catch
        {
            // If we can't create the dir, fall back to temp
            baseDir = Path.GetTempPath();
        }

        return baseDir;
    }

    /// <summary>
    /// Path for crash log when even Serilog fails to initialize.
    /// </summary>
    private static string GetCrashLogPath()
    {
        try
        {
            var dir = GetLogDirectory();
            return Path.Combine(dir, $"crash-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log");
        }
        catch
        {
            return Path.Combine(Path.GetTempPath(), $"strider-crash-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log");
        }
    }

    /// <summary>
    /// Shows a crash dialog to the user with the exception details.
    /// Uses Win32 MessageBox on Windows (works even if Avalonia fails to initialize).
    /// On Linux, writes to stderr (GUI dialog would require GTK which may not be available).
    /// </summary>
    private static void ShowCrashDialog(Exception ex)
    {
        var message = $"Strider Mail failed to start.\n\n" +
                      $"Error: {ex.GetType().Name}\n" +
                      $"Message: {ex.Message}\n\n" +
                      $"Stack trace:\n{ex.StackTrace}\n\n" +
                      $"Logs are at: {GetLogDirectory()}";

        // Write to stderr (visible if launched from console)
        Console.Error.WriteLine(message);

        // On Windows, show a MessageBox via P/Invoke (doesn't require Avalonia)
        if (OperatingSystem.IsWindows())
        {
            try
            {
                ShowWindowsMessageBox("Strider Mail — Fatal Error", message);
            }
            catch { }
        }
    }

    /// <summary>
    /// Win32 MessageBox P/Invoke for Windows. Works even when Avalonia/Managed fails.
    /// </summary>
    [System.Runtime.InteropServices.DllImport("user32.dll",
        CharSet = System.Runtime.InteropServices.CharSet.Unicode,
        SetLastError = true)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    private static void ShowWindowsMessageBox(string title, string message)
    {
        // MB_ICONERROR | MB_OK | MB_TOPMOST
        const uint MB_ICONERROR = 0x00000010;
        const uint MB_OK = 0x00000000;
        const uint MB_TOPMOST = 0x00040000;
        MessageBox(IntPtr.Zero, message, title, MB_ICONERROR | MB_OK | MB_TOPMOST);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
