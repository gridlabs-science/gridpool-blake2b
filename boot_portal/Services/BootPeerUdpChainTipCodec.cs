using System.Buffers.Binary;
using boot_portal.Models;
using boot_portal.Utils;

namespace boot_portal.Services;

public static class BootPeerUdpChainTipCodec
{
    private static readonly byte[] Magic = "GPT1"u8.ToArray();
    private const byte Version = 1;
    private const int HeaderBytes = 80;
    private const int PayloadBytes = 4 + 1 + HeaderBytes + 8;

    public static bool LooksLikeChainTip(ReadOnlySpan<byte> payload) =>
        payload.Length >= Magic.Length && payload[..Magic.Length].SequenceEqual(Magic);

    public static bool TryEncode(BootChainTipAnnouncement announcement, out byte[] payload, out string reason)
    {
        payload = [];
        reason = string.Empty;

        try
        {
            string normalizedHeader = BitcoinHashes.NormalizeHex(announcement.HeaderHex);
            if (normalizedHeader.Length != HeaderBytes * 2)
            {
                reason = "header-must-be-80-bytes";
                return false;
            }

            string computedHash = BitcoinHashes.ComputeBlockHashFromHeader(normalizedHeader);
            if (!string.IsNullOrWhiteSpace(announcement.BlockHash) &&
                !BitcoinHashes.AreEquivalent(computedHash, announcement.BlockHash))
            {
                reason = "header-hash-mismatch";
                return false;
            }

            payload = new byte[PayloadBytes];
            Magic.CopyTo(payload, 0);
            payload[4] = Version;
            Convert.FromHexString(normalizedHeader).CopyTo(payload, 5);
            BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(5 + HeaderBytes, 8), announcement.BlockHeight ?? -1);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            reason = ex.Message;
            payload = [];
            return false;
        }
    }

    public static bool TryDecode(ReadOnlySpan<byte> payload, out BootChainTipAnnouncement announcement, out string reason)
    {
        announcement = new BootChainTipAnnouncement();
        reason = string.Empty;

        if (payload.Length != PayloadBytes)
        {
            reason = "invalid-payload-length";
            return false;
        }

        if (!LooksLikeChainTip(payload))
        {
            reason = "invalid-magic";
            return false;
        }

        if (payload[4] != Version)
        {
            reason = "unsupported-version";
            return false;
        }

        string headerHex = Convert.ToHexString(payload.Slice(5, HeaderBytes)).ToLowerInvariant();
        long blockHeight = BinaryPrimitives.ReadInt64LittleEndian(payload.Slice(5 + HeaderBytes, 8));
        announcement = new BootChainTipAnnouncement
        {
            HeaderHex = headerHex,
            BlockHash = BitcoinHashes.ComputeBlockHashFromHeader(headerHex),
            BlockHeight = blockHeight >= 0 ? blockHeight : null
        };
        return true;
    }
}
