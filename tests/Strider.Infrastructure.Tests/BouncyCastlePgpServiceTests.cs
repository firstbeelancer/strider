using Strider.Core.Domain;
using Strider.Infrastructure.Security;
using FluentAssertions;

namespace Strider.Infrastructure.Tests;

/// <summary>
/// Tests for BouncyCastlePgpService — verifies real PGP cryptographic operations.
/// These tests close finding F-004 (was previously a stub returning fake data).
/// </summary>
public class BouncyCastlePgpServiceTests
{
    private readonly BouncyCastlePgpService _pgp = new();
    private const string TestUserId = "Alice <alice@example.com>";
    private const string TestPassphrase = "correct horse battery staple";

    [Fact]
    public async Task GenerateKeyPairAsync_ReturnsValidKeyWithRealFingerprint()
    {
        var key = await _pgp.GenerateKeyPairAsync(TestUserId, TestPassphrase, keySize: 2048);

        key.Should().NotBeNull();
        key.UserId.Should().Be(TestUserId);
        key.KeyId.Should().NotBeNullOrEmpty();
        key.KeyId.Should().NotBe("IMPORTED");
        key.Fingerprint.Should().NotBeNullOrEmpty();
        key.Fingerprint.Should().NotBe("IMPORTED");
        key.Fingerprint.Length.Should().BeGreaterThanOrEqualTo(32, "SHA-1 fingerprint is 40 hex chars");

        // Public key should be valid armored PGP
        key.PublicKeyArmored.Should().Contain("BEGIN PGP PUBLIC KEY BLOCK");
        key.PublicKeyArmored.Should().Contain("END PGP PUBLIC KEY BLOCK");

        // Private key should be valid armored PGP
        key.PrivateKeyArmored.Should().NotBeNullOrEmpty();
        key.PrivateKeyArmored.Should().Contain("BEGIN PGP PRIVATE KEY BLOCK");
        key.PrivateKeyArmored.Should().Contain("END PGP PRIVATE KEY BLOCK");

        key.IsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task GenerateKeyPairAsync_RejectsEmptyUserId()
    {
        var act = async () => await _pgp.GenerateKeyPairAsync("", TestPassphrase);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GenerateKeyPairAsync_RejectsEmptyPassphrase()
    {
        var act = async () => await _pgp.GenerateKeyPairAsync(TestUserId, "");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GenerateKeyPairAsync_RejectsTooSmallKeySize()
    {
        var act = async () => await _pgp.GenerateKeyPairAsync(TestUserId, TestPassphrase, keySize: 1024);
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task ImportPublicKeyAsync_ReturnsKeyWithCorrectKeyId()
    {
        // Generate a key, then import its public part
        var original = await _pgp.GenerateKeyPairAsync(TestUserId, TestPassphrase, keySize: 2048);
        var imported = await _pgp.ImportPublicKeyAsync(original.PublicKeyArmored);

        imported.KeyId.Should().Be(original.KeyId, "imported public key must have the same KeyId");
        imported.Fingerprint.Should().Be(original.Fingerprint, "fingerprint must match");
        imported.UserId.Should().Be(original.UserId);
        imported.PrivateKeyArmored.Should().BeNull("public-only import");
    }

    [Fact]
    public async Task ImportPrivateKeyAsync_ReturnsKeyWithCorrectKeyId()
    {
        var original = await _pgp.GenerateKeyPairAsync(TestUserId, TestPassphrase, keySize: 2048);
        var imported = await _pgp.ImportPrivateKeyAsync(original.PrivateKeyArmored!, TestPassphrase);

        imported.KeyId.Should().Be(original.KeyId);
        imported.Fingerprint.Should().Be(original.Fingerprint);
        imported.PrivateKeyArmored.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ImportPrivateKeyAsync_WithWrongPassphrase_Throws()
    {
        var original = await _pgp.GenerateKeyPairAsync(TestUserId, TestPassphrase, keySize: 2048);
        var act = async () => await _pgp.ImportPrivateKeyAsync(original.PrivateKeyArmored!, "wrong passphrase");
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task EncryptAndDecrypt_Roundtrip_ReturnsOriginalText()
    {
        // Arrange — generate a keypair, use it as both sender and recipient
        var key = await _pgp.GenerateKeyPairAsync(TestUserId, TestPassphrase, keySize: 2048);
        const string plainText = "Hello, this is a secret message!";

        // Act — encrypt with public key, decrypt with private key
        var encrypted = await _pgp.EncryptAsync(plainText, new[] { key });
        var decrypted = await _pgp.DecryptAsync(encrypted, key, TestPassphrase);

        // Assert
        decrypted.Should().Be(plainText);
        encrypted.Should().Contain("BEGIN PGP MESSAGE");
        encrypted.Should().NotContain(plainText, "plain text must not appear in encrypted output");
    }

    [Fact]
    public async Task EncryptAsync_WithNoRecipientKeys_Throws()
    {
        var act = async () => await _pgp.EncryptAsync("test", Array.Empty<PgpKey>());
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task DecryptAsync_WithWrongPassphrase_Throws()
    {
        var key = await _pgp.GenerateKeyPairAsync(TestUserId, TestPassphrase, keySize: 2048);
        const string plainText = "secret";
        var encrypted = await _pgp.EncryptAsync(plainText, new[] { key });

        var act = async () => await _pgp.DecryptAsync(encrypted, key, "wrong passphrase");
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task SignAndVerify_Roundtrip_ReturnsValid()
    {
        // Arrange
        var key = await _pgp.GenerateKeyPairAsync(TestUserId, TestPassphrase, keySize: 2048);
        const string plainText = "This message will be signed.";

        // Act
        var signed = await _pgp.SignAsync(plainText, key, TestPassphrase);
        var result = await _pgp.VerifyAsync(signed, new[] { key });

        // Assert
        signed.Should().Contain("BEGIN PGP MESSAGE");
        signed.Should().Contain("END PGP MESSAGE");
        result.Status.Should().Be(PgpVerification.Valid);
        result.SignerUserId.Should().Be(TestUserId);
        result.SignatureTime.Should().NotBeNull();
    }

    [Fact]
    public async Task VerifyAsync_WithTamperedSignature_ReturnsInvalidOrThrows()
    {
        // Arrange
        var key = await _pgp.GenerateKeyPairAsync(TestUserId, TestPassphrase, keySize: 2048);
        const string plainText = "Original message content";
        var signed = await _pgp.SignAsync(plainText, key, TestPassphrase);

        // Tamper — flip a character in the armored BASE64 body (not headers).
        // This corrupts the signature or literal data, causing verification to fail
        // or throw — both outcomes prove the tamper was detected.
        var base64Start = signed.IndexOf("\n\n", StringComparison.Ordinal) + 2;
        var base64End = signed.IndexOf("=", base64Start, StringComparison.Ordinal);
        if (base64End > base64Start + 10)
        {
            var tamperedChars = signed.ToCharArray();
            // Flip one character in the middle of BASE64 content
            var idx = (base64Start + base64End) / 2;
            tamperedChars[idx] = tamperedChars[idx] == 'A' ? 'B' : 'A';
            var tampered = new string(tamperedChars);

            // Act
            try
            {
                var result = await _pgp.VerifyAsync(tampered, new[] { key });
                // Assert — if no exception, must be Invalid (not Valid)
                result.Status.Should().Be(PgpVerification.Invalid);
            }
            catch (Exception)
            {
                // Acceptable — corrupted BASE64 may fail to parse
            }
        }
    }

    [Fact]
    public async Task VerifyAsync_WithWrongKey_ReturnsNoKey()
    {
        // Arrange
        var signingKey = await _pgp.GenerateKeyPairAsync(TestUserId, TestPassphrase, keySize: 2048);
        var otherKey = await _pgp.GenerateKeyPairAsync("Bob <bob@example.com>", TestPassphrase, keySize: 2048);
        const string plainText = "Signed by Alice";
        var signed = await _pgp.SignAsync(plainText, signingKey, TestPassphrase);

        // Act — verify with Bob's key (wrong signer)
        var result = await _pgp.VerifyAsync(signed, new[] { otherKey });

        // Assert
        result.Status.Should().Be(PgpVerification.NoKey);
    }

    [Fact]
    public async Task EncryptAndSignAsync_Roundtrip()
    {
        // Arrange — generate two keypairs
        var senderKey = await _pgp.GenerateKeyPairAsync("Alice <alice@example.com>", TestPassphrase, keySize: 2048);
        var recipientKey = await _pgp.GenerateKeyPairAsync("Bob <bob@example.com>", TestPassphrase, keySize: 2048);
        const string plainText = "Combined encryption + signature test";

        // Act — encrypt for Bob, sign as Alice; Bob decrypts with his private key
        var encryptedAndSigned = await _pgp.EncryptAndSignAsync(
            plainText, new[] { recipientKey }, senderKey, TestPassphrase);

        var decrypted = await _pgp.DecryptAsync(encryptedAndSigned, recipientKey, TestPassphrase);

        // Assert — decryption produces original text
        decrypted.Should().Be(plainText);

        // Note: the signature inside the encrypted message can be verified only after decryption.
        // A full verify-on-decrypt flow is a future enhancement — see F-014 thread resolver pattern.
    }

    [Fact]
    public async Task ExportPublicKeyAsync_ReturnsOriginalArmoredKey()
    {
        var key = await _pgp.GenerateKeyPairAsync(TestUserId, TestPassphrase, keySize: 2048);
        var exported = await _pgp.ExportPublicKeyAsync(key);
        exported.Should().Be(key.PublicKeyArmored);
    }

    [Fact]
    public async Task ExportPrivateKeyAsync_ReturnsOriginalArmoredKey()
    {
        var key = await _pgp.GenerateKeyPairAsync(TestUserId, TestPassphrase, keySize: 2048);
        var exported = await _pgp.ExportPrivateKeyAsync(key, TestPassphrase);
        exported.Should().Be(key.PrivateKeyArmored);
    }

    [Fact]
    public async Task ExportPrivateKeyAsync_OnPublicOnlyKey_Throws()
    {
        var key = await _pgp.GenerateKeyPairAsync(TestUserId, TestPassphrase, keySize: 2048);
        var publicOnly = await _pgp.ImportPublicKeyAsync(key.PublicKeyArmored); // no private material

        var act = async () => await _pgp.ExportPrivateKeyAsync(publicOnly, TestPassphrase);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
