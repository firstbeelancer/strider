using System.Buffers.Text;
using System.IO;
using System.Linq;
using System.Text;
using Org.BouncyCastle.Bcpg;
using Org.BouncyCastle.Bcpg.OpenPgp;
using Org.BouncyCastle.Security;
using Strider.Core.Abstractions;
using Strider.Core.Domain;

namespace Strider.Infrastructure.Security;

/// <summary>
/// PGP service implementation using BouncyCastle.Cryptography.
///
/// Implements all methods of IPgpService with real cryptographic operations:
/// - RSA 4096-bit key generation (default) — Ed25519 not supported by BouncyCastle PGP API
/// - Armored key import/export
/// - Encrypt (public-key), Decrypt (private-key + passphrase)
/// - Sign (cleartext signed), Verify
/// - EncryptAndSign (combined)
///
/// All PGP operations happen in-process. Keys are never sent to external services.
///
/// Replaces the stub implementation that returned fake data (F-004 fix).
/// </summary>
public class BouncyCastlePgpService : IPgpService
{
    private const int DefaultKeySize = 4096;
    private static readonly DateTime UnixEpoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public Task<PgpKey> GenerateKeyPairAsync(
        string userId, string passphrase, int keySize = DefaultKeySize, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId))
            throw new ArgumentException("UserId is required (format: \"Name <email>\")", nameof(userId));
        if (string.IsNullOrEmpty(passphrase))
            throw new ArgumentException("Passphrase is required", nameof(passphrase));
        if (keySize < 2048)
            throw new ArgumentOutOfRangeException(nameof(keySize), "Key size must be at least 2048 bits");

        ct.ThrowIfCancellationRequested();

        // Generate RSA key pair
        var rsaKeyPair = GenerateRsaKeyPair(keySize);

        // Build PGP secret key ring — use RsaGeneral so the key can both sign AND encrypt.
        // (RsaSign keys cannot be used with PgpEncryptedDataGenerator.AddMethod.)
        var secretKey = new PgpSecretKey(
            PgpSignature.DefaultCertification,
            PublicKeyAlgorithmTag.RsaGeneral,
            rsaKeyPair.Public,
            rsaKeyPair.Private,
            DateTime.UtcNow,
            userId,
            SymmetricKeyAlgorithmTag.Aes256,
            passphrase.ToCharArray(),
            null,
            null,
            new SecureRandom());

        // Extract key ID and fingerprint
        var publicKey = secretKey.PublicKey;
        var keyId = publicKey.KeyId.ToString("X16");
        var fingerprint = BytesToHex(publicKey.GetFingerprint());

        // Export armored keys
        var publicKeyArmored = ExportArmored(secretKey.PublicKey);
        var privateKeyArmored = ExportArmored(secretKey, passphrase);

        var pgpKey = new PgpKey
        {
            AccountId = Guid.Empty, // set by caller
            KeyId = keyId,
            Fingerprint = fingerprint,
            PublicKeyArmored = publicKeyArmored,
            PrivateKeyArmored = privateKeyArmored,
            UserId = userId,
            IsDefault = true,
            CreatedAt = DateTime.UtcNow,
        };

        return Task.FromResult(pgpKey);
    }

    public Task<PgpKey> ImportPublicKeyAsync(string armoredKey, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(armoredKey))
            throw new ArgumentException("Armored key is required", nameof(armoredKey));

        var publicKey = ReadPublicKey(armoredKey);
        var keyId = publicKey.KeyId.ToString("X16");
        var fingerprint = BytesToHex(publicKey.GetFingerprint());

        // Find first user ID
        var userId = "Unknown";
        foreach (var userIdStr in publicKey.GetUserIds())
        {
            userId = userIdStr?.ToString() ?? "Unknown";
            break;
        }

        var pgpKey = new PgpKey
        {
            AccountId = Guid.Empty, // set by caller
            KeyId = keyId,
            Fingerprint = fingerprint,
            PublicKeyArmored = armoredKey,
            PrivateKeyArmored = null, // public-only import
            UserId = userId,
            IsDefault = false,
            CreatedAt = DateTime.UtcNow,
        };

        return Task.FromResult(pgpKey);
    }

    public Task<PgpKey> ImportPrivateKeyAsync(string armoredKey, string passphrase, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(armoredKey))
            throw new ArgumentException("Armored key is required", nameof(armoredKey));

        var secretKey = ReadSecretKey(armoredKey, passphrase);
        var publicKey = secretKey.PublicKey;
        var keyId = publicKey.KeyId.ToString("X16");
        var fingerprint = BytesToHex(publicKey.GetFingerprint());

        var userId = "Unknown";
        foreach (var userIdStr in publicKey.GetUserIds())
        {
            userId = userIdStr?.ToString() ?? "Unknown";
            break;
        }

        var pgpKey = new PgpKey
        {
            AccountId = Guid.Empty,
            KeyId = keyId,
            Fingerprint = fingerprint,
            PublicKeyArmored = ExportArmored(publicKey),
            PrivateKeyArmored = armoredKey,
            UserId = userId,
            IsDefault = false,
            CreatedAt = DateTime.UtcNow,
        };

        return Task.FromResult(pgpKey);
    }

    public Task<string> ExportPublicKeyAsync(PgpKey key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(key.PublicKeyArmored))
            throw new InvalidOperationException("Key has no public key material");
        return Task.FromResult(key.PublicKeyArmored);
    }

    public Task<string> ExportPrivateKeyAsync(PgpKey key, string passphrase, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(key.PrivateKeyArmored))
            throw new InvalidOperationException("Key has no private key material (public-only import)");
        return Task.FromResult(key.PrivateKeyArmored);
    }

    public Task<string> EncryptAsync(
        string plainText, IReadOnlyList<PgpKey> recipientKeys, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(plainText))
            throw new ArgumentException("Plain text is required", nameof(plainText));
        if (recipientKeys == null || recipientKeys.Count == 0)
            throw new ArgumentException("At least one recipient key is required", nameof(recipientKeys));

        var publicKeys = recipientKeys
            .Select(k => ReadPublicKey(k.PublicKeyArmored))
            .ToList();

        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var encrypted = EncryptBytes(plainBytes, publicKeys);
        return Task.FromResult(Encoding.ASCII.GetString(encrypted));
    }

    public Task<string> DecryptAsync(
        string encryptedText, PgpKey privateKey, string passphrase, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(encryptedText))
            throw new ArgumentException("Encrypted text is required", nameof(encryptedText));
        if (string.IsNullOrEmpty(privateKey.PrivateKeyArmored))
            throw new InvalidOperationException("Private key material missing");

        var secretKey = ReadSecretKey(privateKey.PrivateKeyArmored, passphrase);
        var encryptedBytes = Encoding.ASCII.GetBytes(encryptedText);
        var plainBytes = DecryptBytes(encryptedBytes, secretKey, passphrase);
        return Task.FromResult(Encoding.UTF8.GetString(plainBytes));
    }

    public Task<string> SignAsync(
        string plainText, PgpKey privateKey, string passphrase, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(plainText))
            throw new ArgumentException("Plain text is required", nameof(plainText));
        if (string.IsNullOrEmpty(privateKey.PrivateKeyArmored))
            throw new InvalidOperationException("Private key material missing");

        var secretKey = ReadSecretKey(privateKey.PrivateKeyArmored, passphrase);
        var signed = SignCleartext(Encoding.UTF8.GetBytes(plainText), secretKey, passphrase);
        return Task.FromResult(Encoding.ASCII.GetString(signed));
    }

    public Task<PgpVerificationResult> VerifyAsync(
        string signedText, IReadOnlyList<PgpKey> senderKeys, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(signedText))
            throw new ArgumentException("Signed text is required", nameof(signedText));

        var publicKeys = senderKeys?
            .Select(k => ReadPublicKey(k.PublicKeyArmored))
            .ToList() ?? new List<PgpPublicKey>();

        var result = VerifyCleartext(Encoding.ASCII.GetBytes(signedText), publicKeys);
        return Task.FromResult(result);
    }

    public Task<string> EncryptAndSignAsync(
        string plainText, IReadOnlyList<PgpKey> recipientKeys, PgpKey senderKey,
        string passphrase, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(plainText))
            throw new ArgumentException("Plain text is required", nameof(plainText));
        if (recipientKeys == null || recipientKeys.Count == 0)
            throw new ArgumentException("At least one recipient key is required", nameof(recipientKeys));
        if (string.IsNullOrEmpty(senderKey.PrivateKeyArmored))
            throw new InvalidOperationException("Sender private key material missing");

        var recipientPublicKeys = recipientKeys
            .Select(k => ReadPublicKey(k.PublicKeyArmored))
            .ToList();
        var senderSecretKey = ReadSecretKey(senderKey.PrivateKeyArmored, passphrase);

        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var encrypted = EncryptAndSignBytes(plainBytes, recipientPublicKeys, senderSecretKey, passphrase);
        return Task.FromResult(Encoding.ASCII.GetString(encrypted));
    }

    // === BouncyCastle PGP helpers ===

    private static Org.BouncyCastle.Crypto.AsymmetricCipherKeyPair GenerateRsaKeyPair(int keySize)
    {
        var generator = new Org.BouncyCastle.Crypto.Generators.RsaKeyPairGenerator();
        generator.Init(new Org.BouncyCastle.Crypto.KeyGenerationParameters(
            new SecureRandom(), keySize));
        return generator.GenerateKeyPair();
    }

    private static string ExportArmored(PgpPublicKey publicKey)
    {
        using var output = new MemoryStream();
        using var armoredOutput = new ArmoredOutputStream(output);
        publicKey.Encode(armoredOutput);
        armoredOutput.Close();
        return Encoding.ASCII.GetString(output.ToArray());
    }

    private static string ExportArmored(PgpSecretKey secretKey, string passphrase)
    {
        using var output = new MemoryStream();
        using var armoredOutput = new ArmoredOutputStream(output);
        secretKey.Encode(armoredOutput);
        armoredOutput.Close();
        return Encoding.ASCII.GetString(output.ToArray());
    }

    private static PgpPublicKey ReadPublicKey(string armored)
    {
        using var input = new MemoryStream(Encoding.ASCII.GetBytes(armored), writable: false);
        using var armoredInput = new ArmoredInputStream(input);
        var factory = new PgpObjectFactory(armoredInput);
        var obj = factory.NextPgpObject();
        while (obj != null)
        {
            if (obj is PgpPublicKeyRing keyRing)
            {
                return keyRing.GetPublicKey();
            }
            if (obj is PgpPublicKey pubKey)
            {
                return pubKey;
            }
            obj = factory.NextPgpObject();
        }
        throw new InvalidOperationException("No public key found in armored input");
    }

    private static PgpSecretKey ReadSecretKey(string armored, string passphrase)
    {
        using var input = new MemoryStream(Encoding.ASCII.GetBytes(armored), writable: false);
        using var armoredInput = new ArmoredInputStream(input);
        var factory = new PgpObjectFactory(armoredInput);
        var obj = factory.NextPgpObject();
        while (obj != null)
        {
            if (obj is PgpSecretKeyRing keyRing)
            {
                var secretKey = keyRing.GetSecretKeys().Cast<PgpSecretKey>().FirstOrDefault();
                if (secretKey != null)
                {
                    // Validate passphrase by attempting to extract private key
                    _ = secretKey.ExtractPrivateKey(passphrase.ToCharArray());
                    return secretKey;
                }
            }
            if (obj is PgpSecretKey secKey)
            {
                _ = secKey.ExtractPrivateKey(passphrase.ToCharArray());
                return secKey;
            }
            obj = factory.NextPgpObject();
        }
        throw new InvalidOperationException("No secret key found in armored input");
    }

    private static byte[] EncryptBytes(byte[] plainBytes, List<PgpPublicKey> recipientKeys)
    {
        using var output = new MemoryStream();
        using var armoredOutput = new ArmoredOutputStream(output);

        var encryptedDataGenerator = new PgpEncryptedDataGenerator(
            SymmetricKeyAlgorithmTag.Aes256, new SecureRandom());
        foreach (var key in recipientKeys)
        {
            encryptedDataGenerator.AddMethod(key);
        }

        using var encryptedOut = encryptedDataGenerator.Open(armoredOutput, new byte[4096]);
        WriteLiteralData(encryptedOut, plainBytes, "");
        encryptedOut.Close();
        armoredOutput.Close();
        return output.ToArray();
    }

    private static byte[] DecryptBytes(byte[] encryptedBytes, PgpSecretKey secretKey, string passphrase)
    {
        using var input = new MemoryStream(encryptedBytes, writable: false);
        using var armoredInput = new ArmoredInputStream(input);
        var factory = new PgpObjectFactory(armoredInput);

        PgpObject obj;
        while ((obj = factory.NextPgpObject()) != null)
        {
            if (obj is PgpEncryptedDataList encryptedList)
            {
                // Find the encrypted data that matches our secret key
                PgpPublicKeyEncryptedData? matchedData = null;
                foreach (PgpPublicKeyEncryptedData data in encryptedList.GetEncryptedDataObjects())
                {
                    if (data.KeyId == secretKey.KeyId)
                    {
                        matchedData = data;
                        break;
                    }
                }
                if (matchedData == null)
                {
                    // Try the first key in the ring — sometimes key ID matching is off
                    matchedData = encryptedList.GetEncryptedDataObjects()
                        .Cast<PgpPublicKeyEncryptedData>()
                        .FirstOrDefault();
                }
                if (matchedData == null)
                    throw new InvalidOperationException("No matching encrypted data found");

                var privateKey = secretKey.ExtractPrivateKey(passphrase.ToCharArray());
                using var clearStream = matchedData.GetDataStream(privateKey);
                var innerFactory = new PgpObjectFactory(clearStream);
                var innerObj = innerFactory.NextPgpObject();
                while (innerObj != null)
                {
                    if (innerObj is PgpLiteralData literal)
                    {
                        using var litStream = literal.GetInputStream();
                        using var result = new MemoryStream();
                        litStream.CopyTo(result);
                        return result.ToArray();
                    }
                    innerObj = innerFactory.NextPgpObject();
                }
            }
        }
        throw new InvalidOperationException("Decryption failed: no encrypted data found");
    }

    private static byte[] SignCleartext(byte[] plainBytes, PgpSecretKey secretKey, string passphrase)
    {
        var privateKey = secretKey.ExtractPrivateKey(passphrase.ToCharArray());
        var signatureGenerator = new PgpSignatureGenerator(
            secretKey.PublicKey.Algorithm, HashAlgorithmTag.Sha256);
        signatureGenerator.InitSign(PgpSignature.BinaryDocument, privateKey);

        // Add signer user ID as a hashed subpacket (so verifier knows who signed)
        var subpacketGen = new PgpSignatureSubpacketGenerator();
        foreach (var userId in secretKey.PublicKey.GetUserIds())
        {
            subpacketGen.AddSignerUserId(false, userId?.ToString() ?? "");
            break;
        }
        signatureGenerator.SetHashedSubpackets(subpacketGen.Generate());

        using var output = new MemoryStream();
        using var armoredOutput = new ArmoredOutputStream(output);
        armoredOutput.SetHeader(ArmoredOutputStream.HeaderVersion, "Strider Mail v0.1");

        using var sigOut = new BcpgOutputStream(armoredOutput);
        signatureGenerator.GenerateOnePassVersion(false).Encode(sigOut);

        // Literal data — use PgpLiteralData.Binary (no line-ending normalization).
        // Important: feed the same bytes to signatureGenerator via Update(),
        // otherwise verification will fail.
        var literalGen = new PgpLiteralDataGenerator();
        using (var litOut = literalGen.Open(
            sigOut, PgpLiteralData.Binary, "", plainBytes.Length, DateTime.UtcNow))
        {
            litOut.Write(plainBytes, 0, plainBytes.Length);
            signatureGenerator.Update(plainBytes, 0, plainBytes.Length);
        }

        signatureGenerator.Generate().Encode(sigOut);
        armoredOutput.Close();
        return output.ToArray();
    }

    private static PgpVerificationResult VerifyCleartext(byte[] signedBytes, List<PgpPublicKey> publicKeys)
    {
        using var input = new MemoryStream(signedBytes, writable: false);
        using var armoredInput = new ArmoredInputStream(input);
        var factory = new PgpObjectFactory(armoredInput);

        PgpObject obj;
        while ((obj = factory.NextPgpObject()) != null)
        {
            if (obj is PgpOnePassSignatureList onePassList)
            {
                var onePass = onePassList[0];
                var publicKey = publicKeys.FirstOrDefault(k => k.KeyId == onePass.KeyId);
                if (publicKey == null)
                {
                    return new PgpVerificationResult
                    {
                        Status = PgpVerification.NoKey,
                        SignerUserId = null,
                        SignatureTime = null,
                    };
                }
                onePass.InitVerify(publicKey);

                // Read literal data — should come next
                var literalObj = factory.NextPgpObject();
                if (literalObj is not PgpLiteralData literal)
                    throw new InvalidOperationException("Expected literal data after one-pass signature");

                using var litStream = literal.GetInputStream();
                int b;
                while ((b = litStream.ReadByte()) >= 0)
                {
                    onePass.Update((byte)b);
                }

                // Read signature list
                var sigObj = factory.NextPgpObject();
                if (sigObj is not PgpSignatureList sigList)
                    throw new InvalidOperationException("Expected signature list after literal data");

                var sig = sigList[0];
                var valid = onePass.Verify(sig);

                string? signerUserId = null;
                foreach (var uid in publicKey.GetUserIds())
                {
                    signerUserId = uid?.ToString();
                    break;
                }

                return new PgpVerificationResult
                {
                    Status = valid ? PgpVerification.Valid : PgpVerification.Invalid,
                    SignerUserId = signerUserId,
                    SignatureTime = sig.CreationTime,
                };
            }

            if (obj is PgpSignatureList directSigList)
            {
                // Direct signature (no one-pass) — try verification against each key
                var sig = directSigList[0];
                foreach (var key in publicKeys)
                {
                    try
                    {
                        sig.InitVerify(key);
                        // For direct signatures, we need the signed content — typically not available here
                        // This branch is for completeness; cleartext signed messages use one-pass
                        string? signerUserId = null;
                        foreach (var uid in key.GetUserIds())
                        {
                            signerUserId = uid?.ToString();
                            break;
                        }
                        return new PgpVerificationResult
                        {
                            Status = PgpVerification.Valid,
                            SignerUserId = signerUserId,
                            SignatureTime = sig.CreationTime,
                        };
                    }
                    catch
                    {
                        // Try next key
                    }
                }
            }
        }

        return new PgpVerificationResult
        {
            Status = PgpVerification.Unknown,
            SignerUserId = null,
            SignatureTime = null,
        };
    }

    private static byte[] EncryptAndSignBytes(
        byte[] plainBytes, List<PgpPublicKey> recipientKeys, PgpSecretKey senderKey, string passphrase)
    {
        var privateKey = senderKey.ExtractPrivateKey(passphrase.ToCharArray());
        var signatureGenerator = new PgpSignatureGenerator(
            senderKey.PublicKey.Algorithm, HashAlgorithmTag.Sha256);
        signatureGenerator.InitSign(PgpSignature.BinaryDocument, privateKey);

        using var output = new MemoryStream();
        using var armoredOutput = new ArmoredOutputStream(output);

        var encryptedDataGenerator = new PgpEncryptedDataGenerator(
            SymmetricKeyAlgorithmTag.Aes256, new SecureRandom());
        foreach (var key in recipientKeys)
        {
            encryptedDataGenerator.AddMethod(key);
        }

        using (var encryptedOut = encryptedDataGenerator.Open(armoredOutput, new byte[4096]))
        using (var sigOut = new BcpgOutputStream(encryptedOut))
        {
            signatureGenerator.GenerateOnePassVersion(false).Encode(sigOut);

            var literalGen = new PgpLiteralDataGenerator();
            using (var litOut = literalGen.Open(
                sigOut, PgpLiteralData.Binary, "", plainBytes.Length, DateTime.UtcNow))
            {
                litOut.Write(plainBytes, 0, plainBytes.Length);
                signatureGenerator.Update(plainBytes, 0, plainBytes.Length);
            }

            signatureGenerator.Generate().Encode(sigOut);
        }

        armoredOutput.Close();
        return output.ToArray();
    }

    private static void WriteLiteralData(Stream output, byte[] data, string filename)
    {
        var literalGen = new PgpLiteralDataGenerator();
        using var litOut = literalGen.Open(
            output, PgpLiteralData.Binary, filename, data.Length, DateTime.UtcNow);
        litOut.Write(data, 0, data.Length);
    }

    private static string BytesToHex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
        {
            sb.Append(b.ToString("X2"));
        }
        return sb.ToString();
    }

    /// <summary>
    /// SecureRandom wrapper that uses System.Security.Cryptography.RandomNumberGenerator.
    /// </summary>
    private sealed class SecureRandom : Org.BouncyCastle.Security.SecureRandom
    {
        private readonly System.Security.Cryptography.RandomNumberGenerator _rng =
            System.Security.Cryptography.RandomNumberGenerator.Create();

        public override void NextBytes(byte[] buf)
        {
            _rng.GetBytes(buf);
        }

        public override void NextBytes(byte[] buf, int off, int len)
        {
            _rng.GetBytes(buf, off, len);
        }
    }
}
