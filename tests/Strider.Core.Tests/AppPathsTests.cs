using Xunit;
using Strider.Core.Platform;

namespace Strider.Core.Tests;

/// <summary>
/// Sanity tests for the canonical path resolver introduced by ZAI Wave 4
/// (F-028). These exist primarily to lock the public surface of
/// <see cref="AppPaths"/> so refactors don't silently break callers.
/// Full I/O testing would require platform-specific arrangements;
/// these tests focus on shape and idempotency.
/// </summary>
public class AppPathsTests
{
    [Fact]
    public void AppData_IsUnderLocalAppData()
    {
        var path = AppPaths.AppData;
        Assert.False(string.IsNullOrWhiteSpace(path));
        Assert.EndsWith("StriderMail", path.Replace('\\', '/'));
    }

    [Fact]
    public void Logs_IsUnderAppData()
    {
        var logs = AppPaths.Logs;
        var appData = AppPaths.AppData;
        Assert.StartsWith(appData, logs);
        Assert.EndsWith("logs", logs.Replace('\\', '/'));
    }

    [Fact]
    public void Keychain_IsUnderAppData()
    {
        var keychain = AppPaths.Keychain;
        var appData = AppPaths.AppData;
        Assert.StartsWith(appData, keychain);
        Assert.EndsWith("keychain", keychain.Replace('\\', '/'));
    }

    [Fact]
    public void DefaultDatabasePath_IsConsistent()
    {
        var first = AppPaths.DefaultDatabasePath;
        var second = AppPaths.DefaultDatabasePath;
        Assert.Equal(first, second);
        Assert.EndsWith("strider.db", first);
    }

    [Fact]
    public void CrashLogPath_HasTimestamp()
    {
        var path = AppPaths.CrashLogPath;
        Assert.StartsWith(AppPaths.Logs, path);
        Assert.Contains("crash-", path);
        // Format: crash-yyyyMMdd-HHmmss.log
        Assert.Matches(@"crash-\d{8}-\d{6}\.log$", path);
    }

    [Fact]
    public void AppPaths_DoesNotThrow_OnFirstAccess()
    {
        // ZAI F-026: AppPaths is allowed to do I/O. Verify it doesn't throw
        // when read from an environment that has a writable %LocalAppData%.
        var ex = Record.Exception(() =>
        {
            _ = AppPaths.AppData;
            _ = AppPaths.Logs;
            _ = AppPaths.Keychain;
            _ = AppPaths.DefaultDatabasePath;
        });
        Assert.Null(ex);
    }
}
