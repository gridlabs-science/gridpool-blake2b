using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using boot_portal.Models;
using NSec.Cryptography;

namespace boot_portal.Services;

public sealed class BootPeerIdentity
{
    private const string HelloDomain = "GridPool peer session v2 hello";
    private readonly Key _ed25519Key;
    private readonly byte[] _x25519PrivateKeyBytes;

    public BootPeerIdentity(Key ed25519Key, Key x25519Key)
    {
        _ed25519Key = ed25519Key;
        _x25519PrivateKeyBytes = x25519Key.Export(KeyBlobFormat.RawPrivateKey);
        NodeId = Convert.ToBase64String(ed25519Key.PublicKey.Export(KeyBlobFormat.RawPublicKey));
        X25519PublicKey = Convert.ToBase64String(x25519Key.PublicKey.Export(KeyBlobFormat.RawPublicKey));
    }

    public string NodeId { get; }

    public string X25519PublicKey { get; }

    public BootPeerSessionHello CreateHello(PoolConfig config, string endpoint)
    {
        BootNodeVersionInfo localVersion = BootProtocolVersions.Local(config);
        var hello = new BootPeerSessionHello
        {
            ProtocolVersion = config.BootProtocolVersion,
            ConsensusVersion = localVersion.ConsensusVersion,
            StateBundleSchemaVersion = localVersion.StateBundleSchemaVersion,
            HttpApiVersion = localVersion.HttpApiVersion,
            PeerTransportVersion = localVersion.PeerTransportVersion,
            UdpRelayVersion = localVersion.UdpRelayVersion,
            ReleaseVersion = localVersion.ReleaseVersion,
            NetworkId = config.BootNetworkId,
            Endpoint = endpoint ?? string.Empty,
            NodeId = NodeId,
            X25519PublicKey = X25519PublicKey,
            Nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            TimestampUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
        };
        hello.Signature = SignHello(hello);
        return hello;
    }

    public bool ValidateHello(BootPeerSessionHello? hello, PoolConfig config, out string rejectionReason)
    {
        rejectionReason = string.Empty;
        if (hello == null)
        {
            rejectionReason = "missing-hello";
            return false;
        }

        if (!string.Equals(hello.Type, "hello", StringComparison.OrdinalIgnoreCase))
        {
            rejectionReason = "invalid-hello-type";
            return false;
        }

        BootVersionCompatibilityDto compatibility = BootProtocolVersions.Evaluate(
            BootProtocolVersions.Local(config),
            BootProtocolVersions.FromPeerHello(hello),
            config.BootNetworkId,
            hello.NetworkId,
            requireStateBundleSchema: true);
        if (!compatibility.CanSyncState)
        {
            rejectionReason = compatibility.Reason;
            return false;
        }

        if (!compatibility.PeerTransportCompatible)
        {
            rejectionReason = compatibility.Reason;
            return false;
        }

        if (!TryDecodeFixedBase64(hello.NodeId, 32, out byte[] nodeIdBytes) ||
            !TryDecodeFixedBase64(hello.X25519PublicKey, 32, out _) ||
            !TryDecodeFixedBase64(hello.Nonce, 32, out _) ||
            !TryDecodeBase64(hello.Signature, out byte[] signatureBytes))
        {
            rejectionReason = "invalid-key-material";
            return false;
        }

        if (!DateTime.TryParse(
                hello.TimestampUtc,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTime timestampUtc))
        {
            rejectionReason = "invalid-timestamp";
            return false;
        }

        double skewSeconds = Math.Abs((DateTime.UtcNow - timestampUtc).TotalSeconds);
        if (skewSeconds > Math.Max(60, config.PeerSessionClockSkewSeconds))
        {
            rejectionReason = "stale-hello";
            return false;
        }

        try
        {
            PublicKey publicKey = PublicKey.Import(SignatureAlgorithm.Ed25519, nodeIdBytes, KeyBlobFormat.RawPublicKey);
            byte[] payload = Encoding.UTF8.GetBytes(BuildHelloSigningPayload(hello));
            if (!SignatureAlgorithm.Ed25519.Verify(publicKey, payload, signatureBytes))
            {
                rejectionReason = "bad-signature";
                return false;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException)
        {
            rejectionReason = "invalid-signature";
            return false;
        }

        return true;
    }

    public byte[] ComputeSharedSecret(BootPeerSessionHello remoteHello)
    {
        byte[] remotePublicKey = Convert.FromBase64String(remoteHello.X25519PublicKey);
        byte[] sharedSecret = new byte[LibSodium.CryptoBox.SharedKeyLen];
        LibSodium.CryptoBox.CalculateSharedKey(sharedSecret, remotePublicKey, _x25519PrivateKeyBytes);
        return sharedSecret;
    }

    private string SignHello(BootPeerSessionHello hello)
    {
        byte[] payload = Encoding.UTF8.GetBytes(BuildHelloSigningPayload(hello));
        byte[] signature = SignatureAlgorithm.Ed25519.Sign(_ed25519Key, payload);
        return Convert.ToBase64String(signature);
    }

    private static string BuildHelloSigningPayload(BootPeerSessionHello hello)
    {
        return string.Join('\n',
            HelloDomain,
            hello.ProtocolVersion.ToString(CultureInfo.InvariantCulture),
            hello.ConsensusVersion.ToString(CultureInfo.InvariantCulture),
            hello.StateBundleSchemaVersion.ToString(CultureInfo.InvariantCulture),
            hello.HttpApiVersion.ToString(CultureInfo.InvariantCulture),
            hello.PeerTransportVersion.ToString(CultureInfo.InvariantCulture),
            hello.UdpRelayVersion.ToString(CultureInfo.InvariantCulture),
            hello.ReleaseVersion ?? string.Empty,
            hello.NetworkId ?? string.Empty,
            hello.Endpoint ?? string.Empty,
            hello.NodeId ?? string.Empty,
            hello.X25519PublicKey ?? string.Empty,
            hello.Nonce ?? string.Empty,
            hello.TimestampUtc ?? string.Empty);
    }

    private static bool TryDecodeFixedBase64(string? value, int expectedLength, out byte[] bytes)
    {
        if (!TryDecodeBase64(value, out bytes))
        {
            return false;
        }

        return bytes.Length == expectedLength;
    }

    private static bool TryDecodeBase64(string? value, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            bytes = Convert.FromBase64String(value);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
