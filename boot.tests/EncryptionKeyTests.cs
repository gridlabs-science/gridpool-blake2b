using LibSodium;

namespace boot.tests;

[TestClass]
public sealed class EncryptionKeyTests
{
    [TestMethod]
    public void LoadingKeysWithoutNSec()
    {
        Span<byte> publicKey = stackalloc byte[CryptoSign.PublicKeyLen];
        using var privateKey = new SecureMemory<byte>(CryptoSign.PrivateKeyLen);
        
        CryptoSign.GenerateKeyPair(publicKey, privateKey);

        var pubKeyString = Convert.ToBase64String(publicKey);
        var privateKeyString = Convert.ToBase64String(privateKey.AsReadOnlySpan());
        
        ReadOnlySpan<byte> privateKeyDecoded = Convert.FromBase64String(privateKeyString);
        ReadOnlySpan<byte> publicKeyDecoded = Convert.FromBase64String(pubKeyString);
        
        Span<byte> signature = stackalloc byte[CryptoSign.SignatureLen];
        
        CryptoSign.Sign("Hello World"u8, signature, privateKeyDecoded);
        
        Assert.IsTrue(CryptoSign.Verify("Hello World"u8, signature, publicKeyDecoded));
    }
}