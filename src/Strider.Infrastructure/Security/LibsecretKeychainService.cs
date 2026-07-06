using System.Diagnostics;
using Strider.Core.Abstractions;

namespace Strider.Infrastructure.Security;

/// <summary>
/// Linux libsecret-based keychain service.
/// Uses secret-tool CLI (provided by gnome-keyring or kwallet) via Process.
/// Falls back to plaintext JSON file with 0600 permissions if libsecret unavailable.
/// </summary>
public class LibsecretKeychainService : IKeychainService
{
    private const string Collection = "stridermail";
    private const string AttributeApp = "stridermail";
    // ZAI F-028: use the canonical application-data path resolver from Core.
    private static string FallbackDir => Strider.Core.Platform.AppPaths.Keychain;

    // ZAI F-026 (mirror): lazy-init the fallback directory so that constructors
    // don't do I/O that could throw before any catch handler is reached.
    private static int _fallbackDirInitialized;
    private static readonly object _fallbackInitLock = new();

    private static void EnsureFallbackDirInitialized()
    {
        if (System.Threading.Volatile.Read(ref _fallbackDirInitialized) == 1) return;
        lock (_fallbackInitLock)
        {
            if (_fallbackDirInitialized == 1) return;
            try
            {
                Directory.CreateDirectory(FallbackDir);
                System.Threading.Volatile.Write(ref _fallbackDirInitialized, 1);
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex,
                    "libsecret fallback directory could not be created at {Path}.",
                    FallbackDir);
                throw;
            }
        }
    }

    public async Task SetSecretAsync(string key, string value, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(key)) throw new ArgumentException("Key required", nameof(key));

        if (await IsSecretToolAvailableAsync(ct))
        {
            await StoreViaSecretToolAsync(key, value, ct);
        }
        else
        {
            StoreFallback(key, value);
        }
    }

    public async Task<string?> GetSecretAsync(string key, CancellationToken ct = default)
    {
        if (await IsSecretToolAvailableAsync(ct))
        {
            return await LookupViaSecretToolAsync(key, ct);
        }
        return LookupFallback(key);
    }

    public async Task DeleteSecretAsync(string key, CancellationToken ct = default)
    {
        if (await IsSecretToolAvailableAsync(ct))
        {
            await ClearViaSecretToolAsync(key, ct);
        }
        else
        {
            DeleteFallback(key);
        }
    }

    public async Task<bool> HasSecretAsync(string key, CancellationToken ct = default)
    {
        var value = await GetSecretAsync(key, ct);
        return !string.IsNullOrEmpty(value);
    }

    // === libsecret via secret-tool ===

    private static async Task<bool> IsSecretToolAvailableAsync(CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "secret-tool",
                Arguments = "--version",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            await p.WaitForExitAsync(ct);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task StoreViaSecretToolAsync(string key, string value, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "secret-tool",
            Arguments = $"store --label='Strider Mail: {key}' app {AttributeApp} key {SanitizeArg(key)}",
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start secret-tool");
        await p.StandardInput.WriteAsync(value);
        await p.StandardInput.FlushAsync();
        p.StandardInput.Close();
        await p.WaitForExitAsync(ct);
        if (p.ExitCode != 0)
        {
            var err = await p.StandardError.ReadToEndAsync(ct);
            throw new InvalidOperationException($"secret-tool store failed: {err}");
        }
    }

    private static async Task<string?> LookupViaSecretToolAsync(string key, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "secret-tool",
            Arguments = $"lookup app {AttributeApp} key {SanitizeArg(key)}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi);
        if (p == null) return null;
        var output = await p.StandardOutput.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        if (p.ExitCode != 0) return null;
        return output.Length > 0 ? output : null;
    }

    private static async Task ClearViaSecretToolAsync(string key, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "secret-tool",
            Arguments = $"clear app {AttributeApp} key {SanitizeArg(key)}",
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi);
        if (p == null) return;
        await p.WaitForExitAsync(ct);
    }

    private static string SanitizeArg(string s) =>
        // secret-tool attribute values must be alnum, dash, underscore, dot
        string.Concat(s.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' ? c : '_'));

    // === Fallback: per-user encrypted file (chmod 600) ===
    // NOT cryptographically secure — only used when libsecret is not available.
    // User is encouraged to install gnome-keyring or kwallet.

    private static void StoreFallback(string key, string value)
    {
        EnsureFallbackDirInitialized();
        var path = GetFallbackPath(key);
        File.WriteAllText(path, value);
        Chmod600(path);
    }

    private static string? LookupFallback(string key)
    {
        try { EnsureFallbackDirInitialized(); }
        catch { return null; }
        var path = GetFallbackPath(key);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private static void DeleteFallback(string key)
    {
        try { EnsureFallbackDirInitialized(); }
        catch { return; }
        var path = GetFallbackPath(key);
        if (File.Exists(path)) File.Delete(path);
    }

    private static string GetFallbackPath(string key)
    {
        var safe = string.Concat(key.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_'));
        return Path.Combine(FallbackDir, safe + ".txt");
    }

    private static void Chmod600(string path)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch
        {
            // Best effort
        }
    }
}
