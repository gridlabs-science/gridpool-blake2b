using System.Buffers.Binary;
using System.Text;
using boot_portal.Models;
using boot_portal.Utils;

namespace boot_portal.Services;

public static class BootPeerUdpShareCodec
{
    private const byte Version = 3;
    private const byte FlagPulseProof = 1 << 0;
    private const byte FlagOptimisticRelay = 1 << 1;
    private const int MaxUsernameBytes = 255;
    private const int MaxSnapshotIdBytes = 255;

    public static bool TryEncode(BootShareProof proof, PoolConfig config, out byte[] payload, out string reason)
    {
        payload = [];
        reason = string.Empty;

        try
        {
            byte[] header = DecodeFixedHex(proof.HeaderHex, 80, "header");
            byte[] coinbase = DecodeVariableHex(proof.CoinbaseHex, "coinbase");
            if (coinbase.Length * 2 > config.MaxCoinbaseHexChars)
            {
                reason = "coinbase-too-large";
                return false;
            }

            if (proof.MerklePath.Count > Math.Min(byte.MaxValue, config.MaxMerklePathEntries))
            {
                reason = "merkle-path-too-large";
                return false;
            }

            byte[] username = Encoding.UTF8.GetBytes(string.IsNullOrWhiteSpace(proof.Username) ? string.Empty : proof.Username);
            if (username.Length > MaxUsernameBytes)
            {
                username = username[..MaxUsernameBytes];
            }

            byte[] snapshotId = Encoding.ASCII.GetBytes(string.IsNullOrWhiteSpace(proof.PayoutSnapshotId) ? string.Empty : proof.PayoutSnapshotId);
            if (snapshotId.Length > MaxSnapshotIdBytes)
            {
                snapshotId = snapshotId[..MaxSnapshotIdBytes];
            }

            int size =
                1 + // version
                1 + // flags
                1 + // relay TTL
                80 +
                2 + coinbase.Length +
                1 + (proof.MerklePath.Count * 32) +
                1 + username.Length +
                1 + snapshotId.Length;
            payload = new byte[size];
            int offset = 0;

            payload[offset++] = Version;
            byte flags = 0;
            if (string.Equals(proof.ProofClass, BootProofClasses.Pulse, StringComparison.OrdinalIgnoreCase))
            {
                flags |= FlagPulseProof;
            }
            if (string.Equals(proof.RelayStage, BootRelayStages.Optimistic, StringComparison.OrdinalIgnoreCase))
            {
                flags |= FlagOptimisticRelay;
            }

            payload[offset++] = flags;
            payload[offset++] = checked((byte)Math.Clamp(proof.RelayTtl, 0, byte.MaxValue));
            Buffer.BlockCopy(header, 0, payload, offset, header.Length);
            offset += header.Length;

            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(offset, 2), checked((ushort)coinbase.Length));
            offset += 2;
            Buffer.BlockCopy(coinbase, 0, payload, offset, coinbase.Length);
            offset += coinbase.Length;

            payload[offset++] = checked((byte)proof.MerklePath.Count);
            foreach (string merkleHash in proof.MerklePath)
            {
                byte[] branch = DecodeFixedHex(merkleHash, 32, "merkle-path");
                Buffer.BlockCopy(branch, 0, payload, offset, branch.Length);
                offset += branch.Length;
            }

            payload[offset++] = checked((byte)username.Length);
            Buffer.BlockCopy(username, 0, payload, offset, username.Length);
            offset += username.Length;

            payload[offset++] = checked((byte)snapshotId.Length);
            Buffer.BlockCopy(snapshotId, 0, payload, offset, snapshotId.Length);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
        {
            reason = ex.Message;
            payload = [];
            return false;
        }
    }

    public static bool TryDecode(ReadOnlySpan<byte> payload, PoolConfig config, out RecordedShareSubmission share, out string reason)
    {
        share = new RecordedShareSubmission();
        reason = string.Empty;

        try
        {
            int offset = 0;
            if (payload.Length < 1 + 1 + 1 + 80 + 2 + 1 + 1)
            {
                reason = "payload-too-short";
                return false;
            }

            byte version = payload[offset++];
            if (version != Version)
            {
                reason = "unsupported-version";
                return false;
            }

            byte flags = payload[offset++];
            int relayTtl = payload[offset++];

            string headerHex = Convert.ToHexString(payload.Slice(offset, 80)).ToLowerInvariant();
            offset += 80;

            int coinbaseLength = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(offset, 2));
            offset += 2;
            if (coinbaseLength <= 0 || coinbaseLength * 2 > config.MaxCoinbaseHexChars)
            {
                reason = "invalid-coinbase-length";
                return false;
            }

            if (payload.Length < offset + coinbaseLength + 1)
            {
                reason = "truncated-coinbase";
                return false;
            }

            string coinbaseHex = Convert.ToHexString(payload.Slice(offset, coinbaseLength)).ToLowerInvariant();
            offset += coinbaseLength;

            int merkleCount = payload[offset++];
            if (merkleCount > config.MaxMerklePathEntries)
            {
                reason = "merkle-path-too-large";
                return false;
            }

            if (payload.Length < offset + (merkleCount * 32) + 1)
            {
                reason = "truncated-merkle-path";
                return false;
            }

            var merklePath = new List<string>(merkleCount);
            for (int i = 0; i < merkleCount; i++)
            {
                merklePath.Add(Convert.ToHexString(payload.Slice(offset, 32)).ToLowerInvariant());
                offset += 32;
            }

            int usernameLength = payload[offset++];
            if (payload.Length < offset + usernameLength + 1)
            {
                reason = "invalid-username-length";
                return false;
            }

            string username = usernameLength == 0
                ? string.Empty
                : Encoding.UTF8.GetString(payload.Slice(offset, usernameLength));
            offset += usernameLength;

            int snapshotIdLength = payload[offset++];
            if (payload.Length != offset + snapshotIdLength)
            {
                reason = "invalid-snapshot-id-length";
                return false;
            }

            string snapshotId = snapshotIdLength == 0
                ? string.Empty
                : Encoding.ASCII.GetString(payload.Slice(offset, snapshotIdLength));

            share = new RecordedShareSubmission
            {
                MinerAddress = string.Empty,
                Username = username,
                HeaderHex = headerHex,
                CoinbaseHex = coinbaseHex,
                MerklePath = merklePath,
                PayoutSnapshotId = snapshotId,
                PayloadBytes = payload.Length,
                ProofClass = (flags & FlagPulseProof) != 0 ? BootProofClasses.Pulse : BootProofClasses.Work,
                RelayStage = (flags & FlagOptimisticRelay) != 0 ? BootRelayStages.Optimistic : BootRelayStages.Validated,
                RelayTtl = relayTtl,
                Source = "peer-udp"
            };
            return true;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or DecoderFallbackException)
        {
            reason = ex.Message;
            share = new RecordedShareSubmission();
            return false;
        }
    }

    private static byte[] DecodeFixedHex(string value, int expectedBytes, string fieldName)
    {
        string normalized = BitcoinHashes.NormalizeHex(value);
        if (normalized.Length != expectedBytes * 2)
        {
            throw new ArgumentException($"{fieldName} must be {expectedBytes} bytes");
        }

        return Convert.FromHexString(normalized);
    }

    private static byte[] DecodeVariableHex(string value, string fieldName)
    {
        string normalized = BitcoinHashes.NormalizeHex(value);
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length % 2 != 0)
        {
            throw new ArgumentException($"{fieldName} is invalid");
        }

        return Convert.FromHexString(normalized);
    }
}
