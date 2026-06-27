using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Strider.Core.Abstractions;

namespace Strider.Infrastructure.Security;

/// <summary>
/// Windows DPAPI-based keychain service.
/// Data is encrypted per-user via CryptProtectData; only the same Windows account can decrypt.
/// </summary>
[SupportedOSPlatform("windows")]
public class DpapiKeychainService : IKeychainService
{
    private static readonly string StorageDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "StriderMail", "keychain");

    static DpapiKeychainService()
    {
        Directory.CreateDirectory(StorageDir);
    }

    public Task SetSecretAsync(string key, string value, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(key)) throw new ArgumentException("Key required", nameof(key));
        var path = GetPath(key);
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        var cipher = Protect(bytes);
        File.WriteAllBytes(path, cipher);
        return Task.CompletedTask;
    }

    public Task<string?> GetSecretAsync(string key, CancellationToken ct = default)
    {
        var path = GetPath(key);
        if (!File.Exists(path)) return Task.FromResult<string?>(null);
        var cipher = File.ReadAllBytes(path);
        try
        {
            var plain = Unprotect(cipher);
            return Task.FromResult<string?>(System.Text.Encoding.UTF8.GetString(plain));
        }
        catch
        {
            // Corrupted or moved from another machine
            return Task.FromResult<string?>(null);
        }
    }

    public Task DeleteSecretAsync(string key, CancellationToken ct = default)
    {
        var path = GetPath(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public Task<bool> HasSecretAsync(string key, CancellationToken ct = default)
    {
        var path = GetPath(key);
        return Task.FromResult(File.Exists(path));
    }

    private static string GetPath(string key)
    {
        // Sanitize key to valid filename
        var safe = string.Concat(key.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_'));
        return Path.Combine(StorageDir, safe + ".bin");
    }

    // === DPAPI P/Invoke ===

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DATA_BLOB pDataIn, string? szDataDescr,
        IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DATA_BLOB pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref DATA_BLOB pDataIn, out string? szDataDescr,
        IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DATA_BLOB pDataOut);

    [StructLayout(LayoutKind.Sequential)]
    private struct DATA_BLOB
    {
        public int cbData;
        public IntPtr pbData;
    }

    private const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;

    private static byte[] Protect(byte[] plain)
    {
        var hGch = GCHandle.Alloc(plain, GCHandleType.Pinned);
        try
        {
            var blobIn = new DATA_BLOB
            {
                cbData = plain.Length,
                pbData = hGch.AddrOfPinnedObject(),
            };
            if (!CryptProtectData(ref blobIn, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                CRYPTPROTECT_UI_FORBIDDEN, out var blobOut))
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "CryptProtectData failed");
            }
            return CopyBlobAndFree(blobOut);
        }
        finally
        {
            hGch.Free();
        }
    }

    private static byte[] Unprotect(byte[] cipher)
    {
        var hGch = GCHandle.Alloc(cipher, GCHandleType.Pinned);
        try
        {
            var blobIn = new DATA_BLOB
            {
                cbData = cipher.Length,
                pbData = hGch.AddrOfPinnedObject(),
            };
            if (!CryptUnprotectData(ref blobIn, out _, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                CRYPTPROTECT_UI_FORBIDDEN, out var blobOut))
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "CryptUnprotectData failed");
            }
            return CopyBlobAndFree(blobOut);
        }
        finally
        {
            hGch.Free();
        }
    }

    private static byte[] CopyBlobAndFree(DATA_BLOB blob)
    {
        try
        {
            if (blob.pbData == IntPtr.Zero || blob.cbData == 0) return Array.Empty<byte>();
            var data = new byte[blob.cbData];
            Marshal.Copy(blob.pbData, data, 0, blob.cbData);
            return data;
        }
        finally
        {
            if (blob.pbData != IntPtr.Zero) Marshal.FreeHGlobal(blob.pbData);
        }
    }
}
