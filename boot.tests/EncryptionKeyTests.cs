using LibSodium;

namespace boot.tests;

[TestClass]
public sealed class EncryptionKeyTests
{
    [TestMethod]
    public void SignKeys()
    {
        // Arrange
        Span<byte> publicKey = stackalloc byte[CryptoSign.PublicKeyLen];
        using var privateKey = new SecureMemory<byte>(CryptoSign.PrivateKeyLen);
        
        CryptoSign.GenerateKeyPair(publicKey, privateKey);

        var pubKeyString = Convert.ToBase64String(publicKey);
        var privateKeyString = Convert.ToBase64String(privateKey.AsReadOnlySpan());
        
        ReadOnlySpan<byte> privateKeyDecoded = Convert.FromBase64String(privateKeyString);
        ReadOnlySpan<byte> publicKeyDecoded = Convert.FromBase64String(pubKeyString);
        
        Span<byte> signature = stackalloc byte[CryptoSign.SignatureLen];
        
        // Act
        CryptoSign.Sign("Hello World"u8, signature, privateKeyDecoded);
        
        // Assert
        Assert.IsTrue(CryptoSign.Verify("Hello World"u8, signature, publicKeyDecoded));
    }
    
    [TestMethod]
    public void PublicKeyFromPrivate()
    {
        // Arrange
        Span<byte> publicKey = stackalloc byte[CryptoSign.PublicKeyLen];
        using var privateKey = new SecureMemory<byte>(CryptoSign.PrivateKeyLen);
        
        CryptoSign.GenerateKeyPair(publicKey, privateKey);

        var pubKeyString = Convert.ToBase64String(publicKey);
        var privateKeyString = Convert.ToBase64String(privateKey.AsReadOnlySpan());
        
        ReadOnlySpan<byte> privateKeyDecoded = Convert.FromBase64String(privateKeyString);
        ReadOnlySpan<byte> publicKeyDecoded = Convert.FromBase64String(pubKeyString);
        
        Span<byte> signature = stackalloc byte[CryptoSign.SignatureLen];
        
        // Act
        CryptoSign.Sign("Hello World"u8, signature, privateKeyDecoded);
        
        // Assert
        Assert.IsTrue(CryptoSign.Verify("Hello World"u8, signature, publicKeyDecoded));
    }
    
    [TestMethod]
    public void CryptoBoxKeys()
    {
        // Arrange
        Span<byte> publicKey = stackalloc byte[CryptoBox.PublicKeyLen];
        using var privateKey = new SecureMemory<byte>(CryptoBox.PrivateKeyLen);
        
        CryptoBox.GenerateKeypair(publicKey, privateKey);

        var pubKeyString = Convert.ToBase64String(publicKey);
        var privateKeyString = Convert.ToBase64String(privateKey.AsReadOnlySpan());
        
        ReadOnlySpan<byte> privateKeyDecoded = Convert.FromBase64String(privateKeyString);
        ReadOnlySpan<byte> publicKeyDecoded = Convert.FromBase64String(pubKeyString);
        
        var plaintext = "Hello World"u8;
        Span<byte> cipherText = stackalloc byte[plaintext.Length + CryptoBox.SealOverheadLen];
        
        CryptoBox.EncryptWithPublicKey(cipherText, plaintext, publicKeyDecoded);
        
        // Act
        Span<byte> decrypted = stackalloc byte[cipherText.Length - CryptoBox.SealOverheadLen];
        CryptoBox.DecryptWithPrivateKey(decrypted, cipherText, privateKeyDecoded);
        
        // Assert
        Assert.That.SpansAreEqual(plaintext, decrypted);;
    }
}

public static class Extensions
{
    public static void SpansAreEqual(this Assert _, ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual)
    {
        if (expected.Length != actual.Length)
        {
            Assert.Fail("Spans are not the same length");
        }

        for (var i = 0; i < expected.Length; i++)
        {
            Assert.AreEqual(expected[i], actual[i], $"Spans are not the same. Difference at index {i}: {expected[i]} != {actual[i]}");
        }
    }
}