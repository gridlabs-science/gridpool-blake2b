namespace boot_portal.Models;

public static class BitcoinNotificationModes
{
    public const string AttachedNode = "attached-node";
    public const string ExternalFallback = "external-fallback";

    public static string Resolve(PoolConfig config)
    {
        string configured = config.BitcoinNotificationMode?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.ToLowerInvariant();
        }

        string legacy = config.NotificationSource?.Trim() ?? string.Empty;
        return legacy.Equals("ZMQ", StringComparison.OrdinalIgnoreCase) ||
               legacy.Equals("BitcoinZmq", StringComparison.OrdinalIgnoreCase) ||
               legacy.Equals("BitcoinZMQ", StringComparison.OrdinalIgnoreCase)
            ? AttachedNode
            : ExternalFallback;
    }
}

public sealed class BootBitcoinNotificationDto
{
    public string Mode { get; set; } = BitcoinNotificationModes.ExternalFallback;
    public string AuthorityClass { get; set; } = "external-observer";
    public bool MiningSafe { get; set; } = true;
    public string DegradedReason { get; set; } = string.Empty;
    public BootBitcoinRpcHealthDto Rpc { get; set; } = new();
    public BootBitcoinNetworkHealthDto Network { get; set; } = new();
    public List<BootBitcoinZmqTopicHealthDto> ZmqTopics { get; set; } = [];
    public long ReconciliationCount { get; set; }
    public long RecoveredMissedBlockCount { get; set; }
    public DateTime? LastReconciliationUtc { get; set; }
}

public sealed class BootBitcoinNetworkHealthDto
{
    public int TotalPeerCount { get; set; }
    public int InboundPeerCount { get; set; }
    public int OutboundPeerCount { get; set; }
    public double? NetworkHashrateHs { get; set; }
    public DateTime? LastPeerCheckUtc { get; set; }
    public DateTime? LastPeerSuccessUtc { get; set; }
    public DateTime? LastHashrateCheckUtc { get; set; }
    public string LastError { get; set; } = string.Empty;
    public List<BootBitcoinPeerHealthDto> Peers { get; set; } = [];
}

public sealed class BootBitcoinPeerHealthDto
{
    public long Id { get; set; }
    public bool Inbound { get; set; }
    public double? LatencyMs { get; set; }
    public string ConnectionType { get; set; } = string.Empty;
}

public sealed class BootBitcoinRpcHealthDto
{
    public bool Configured { get; set; }
    public bool Reachable { get; set; }
    public bool Synced { get; set; }
    public bool InitialBlockDownload { get; set; }
    public long? BestHeight { get; set; }
    public long? HeaderHeight { get; set; }
    public string BestBlockHash { get; set; } = string.Empty;
    public bool ChainProfileAttestationRequired { get; set; }
    public bool ChainProfileAttested { get; set; }
    public string ChainProfileId { get; set; } = ChainDomainProfiles.LegacySha256dProfileId;
    public string ChainDomainFingerprint { get; set; } = string.Empty;
    public string AttachedNodeGenesisHash { get; set; } = string.Empty;
    public string AttachedNodeSubversion { get; set; } = string.Empty;
    public DateTime? LastChainProfileAttestationUtc { get; set; }
    public double? VerificationProgress { get; set; }
    public DateTime? LastCheckUtc { get; set; }
    public DateTime? LastSuccessUtc { get; set; }
    public string LastError { get; set; } = string.Empty;
}

public sealed class BootBitcoinZmqTopicHealthDto
{
    public string Topic { get; set; } = string.Empty;
    public string EndpointLabel { get; set; } = string.Empty;
    public bool Configured { get; set; }
    public bool SubscriberRunning { get; set; }
    public bool PublisherAdvertisedByRpc { get; set; }
    public int PublisherCount { get; set; }
    public List<string> PublisherEndpointLabels { get; set; } = [];
    public DateTime? LastEventUtc { get; set; }
    public string LastBlockHash { get; set; } = string.Empty;
    public uint? LastSequence { get; set; }
    public long SequenceGapCount { get; set; }
    public long DuplicateCount { get; set; }
    public long ResetCount { get; set; }
    public long WrapCount { get; set; }
    public long ReconnectCount { get; set; }
}
