using System.Runtime.InteropServices;

namespace Strider.Core.Platform;

/// <summary>
/// Canonical paths for Strider Mail's persistent state. All implementations
/// MUST use these helpers instead of hard-coding
/// <c>Environment.SpecialFolder.ApplicationData</c> / <c>UserProfile</c>.
///
/// ZAI F-028: previous versions used Roaming ApplicationData for the DB and
/// keychain, but LocalApplicationData for logs. That split was inconsistent
/// and on Windows Server Core / sandboxed environments Roaming may resolve
/// to a junction that is not writable. LocalApplicationData is writable on
/// every supported Windows install (including Server Core).
/// </summary>
public static class AppPaths
{
    /// <summary>
    /// Root directory for all Strider Mail persistent state:
    /// database, keychain fallback, attachments cache, logs.
    /// On Windows: <c>%LocalAppData%\StriderMail\</c>.
    /// On Linux: <c>~/.local/share/StriderMail/</c>.
    /// </summary>
    public static string AppData
    {
        get
        {
            string baseDir;
            try
            {
                baseDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "StriderMail");
            }
            catch
            {
                baseDir = Path.Combine(AppContext.BaseDirectory, ".stridermail");
            }

            try { Directory.CreateDirectory(baseDir); } catch { /* ignore */ }
            return baseDir;
        }
    }

    /// <summary>
    /// Logs directory: <c>%LocalAppData%\StriderMail\logs\</c>.
    /// Always writable — falls back to %TEMP% if creation fails.
    /// </summary>
    public static string Logs
    {
        get
        {
            string baseDir;
            try
            {
                baseDir = Path.Combine(AppData, "logs");
            }
            catch
            {
                baseDir = Path.Combine(AppContext.BaseDirectory, "logs");
            }

            try { Directory.CreateDirectory(baseDir); }
            catch { baseDir = Path.GetTempPath(); }
            return baseDir;
        }
    }

    /// <summary>
    /// Keychain fallback directory (used when the OS keychain is unavailable):
    /// <c>%LocalAppData%\StriderMail\keychain\</c>.
    /// </summary>
    public static string Keychain
    {
        get
        {
            string baseDir;
            try
            {
                baseDir = Path.Combine(AppData, "keychain");
            }
            catch
            {
                baseDir = Path.Combine(AppContext.BaseDirectory, ".stridermail", "keychain");
            }

            try { Directory.CreateDirectory(baseDir); } catch { /* ignore */ }
            return baseDir;
        }
    }

    /// <summary>
    /// SQLite database file. Default: <c>strider.db</c> in <see cref="AppData"/>.
    /// </summary>
    public static string DefaultDatabasePath => Path.Combine(AppData, "strider.db");

    /// <summary>
    /// Crash log file path with a timestamp suffix.
    /// </summary>
    public static string CrashLogPath =>
        Path.Combine(Logs, $"crash-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log");
}

/// <summary>
/// Cross-platform crash reporter.
///
/// On Windows: shows a Win32 <c>MessageBox</c> via <c>user32.dll</c> P/Invoke.
/// The dialog displays even when Avalonia / .NET UI fails to start, so the
/// user always sees why the app didn't launch.
///
/// On Linux: writes the same text to stderr (a GUI dialog would require
/// GTK which may not be installed).
///
/// ZAI F-029: this is the last-resort reporter. It is invoked from three
/// places — see <see cref="Install"/> — covering every known silent-crash
/// path through the runtime.
/// </summary>
public static class CrashReporter
{
    public static void Install()
    {
        // Synchronous unhandled exceptions on any thread.
        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            try
            {
                var ex = e.ExceptionObject as Exception
                    ?? new Exception(e.ExceptionObject?.ToString() ?? "(unknown)");
                Show(ex, "AppDomain.UnhandledException", e.IsTerminating);
            }
            catch { /* swallow */ }
        };

        // Unobserved task exceptions (async/await mistakes).
        TaskScheduler.UnobservedTaskException += (sender, e) =>
        {
            try
            {
                Show(e.Exception, "TaskScheduler.UnobservedTaskException", isTerminating: false);
                e.SetObserved();
            }
            catch { /* swallow */ }
        };
    }

    /// <summary>
    /// Display the exception to the user. Always returns.
    /// </summary>
    /// <param name="ex">Exception to display.</param>
    /// <param name="where">Free-form label, e.g. "DI Configuration".</param>
    /// <param name="isTerminating">Whether the app is about to exit.</param>
    public static void Show(Exception ex, string where, bool isTerminating = true)
    {
        var message = $"Strider Mail failed to start.\n\n" +
                      $"Where: {where}\n" +
                      $"Terminating: {isTerminating}\n\n" +
                      $"Error: {ex.GetType().Name}\n" +
                      $"Message: {ex.Message}\n\n" +
                      $"Stack trace:\n{ex.StackTrace}\n\n" +
                      $"Logs are at: {AppPaths.Logs}\n" +
                      $"Last crash log: {AppPaths.CrashLogPath}";

        // stderr first — works on every platform.
        try { Console.Error.WriteLine(message); } catch { /* swallow */ }

        if (OperatingSystem.IsWindows())
        {
            try { ShowWindowsMessageBox($"Strider Mail — Fatal Error ({where})", message); }
            catch { /* swallow */ }
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    private static void ShowWindowsMessageBox(string title, string message)
    {
        const uint MB_ICONERROR = 0x00000010;
        const uint MB_OK = 0x00000000;
        const uint MB_TOPMOST = 0x00040000;
        MessageBox(IntPtr.Zero, message, title, MB_ICONERROR | MB_OK | MB_TOPMOST);
    }
}
