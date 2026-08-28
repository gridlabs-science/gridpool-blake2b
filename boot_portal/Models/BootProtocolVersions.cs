using System.Reflection;

namespace boot_portal.Models;

public static class BootProtocolVersions
{
    public const int V21ConsensusVersion = 21;
    public const int ConsensusVersion = 22;
    public const int BlakeConsensusVersion = 23;
    public const int V21StateBundleSchemaVersion = 2;
    public const int StateBundleSchemaVersion = 3;
    public const int BlakeStateBundleSchemaVersion = 4;
    public const long MainnetV22ActivationBlockHeight = 959_500;
    public const int HttpApiVersion = 1;
    public const int BlakeHttpApiVersion = 2;
    public const int PeerTransportVersion = 2;
    public const int BlakePeerTransportVersion = 3;
    public const int UdpRelayVersion = 5;
    public const int BlakeUdpRelayVersion = 6;

    private static readonly Lazy<string> ReleaseVersion = new(() =>
    {
        string? configured = Environment.GetEnvironmentVariable("GRIDPOOL_RELEASE_VERSION") ??
                             Environment.GetEnvironmentVariable("BOOT_PORTAL_RELEASE_VERSION");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        string? informational = typeof(BootProtocolVersions).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        return string.IsNullOrWhiteSpace(informational) ? "dev" : informational;
    });

    public static string CurrentReleaseVersion => ReleaseVersion.Value;

    public static bool IsBareReleaseVersion(string? value)
    {
        string normalized = value?.Trim() ?? string.Empty;
        return normalized is "" or "dev" or "1.0.0" ||
               System.Text.RegularExpressions.Regex.IsMatch(normalized, "^\\d+\\.\\d+\\.\\d+$");
    }

    public static int GetActiveConsensusVersion(PoolConfig config, long? trustedLocalTipHeight)
    {
        if (config.BootProtocolVersion >= BlakeConsensusVersion &&
            ChainDomainProfiles.TryResolve(config, out ChainDomainProfile? profile, out _) &&
            profile != null)
        {
            return trustedLocalTipHeight.HasValue && trustedLocalTipHeight.Value >= profile.ActivationHeight
                ? BlakeConsensusVersion
                : ConsensusVersion;
        }

        int softwareConsensusVersion = Math.Min(config.BootProtocolVersion, ConsensusVersion);
        if (softwareConsensusVersion < ConsensusVersion)
        {
            return softwareConsensusVersion;
        }

        return config.V22ActivationBlockHeight == 0 ||
               (trustedLocalTipHeight.HasValue && trustedLocalTipHeight.Value >= config.V22ActivationBlockHeight)
            ? ConsensusVersion
            : V21ConsensusVersion;
    }

    public static int GetStateBundleSchemaVersion(int activeConsensusVersion) =>
        activeConsensusVersion >= BlakeConsensusVersion
            ? BlakeStateBundleSchemaVersion
            : activeConsensusVersion >= ConsensusVersion
            ? StateBundleSchemaVersion
            : V21StateBundleSchemaVersion;

    public static BootNodeVersionInfo Local(PoolConfig config, int activeConsensusVersion) => new()
    {
        SoftwareConsensusVersion = Math.Min(config.BootProtocolVersion, BlakeConsensusVersion),
        ConsensusVersion = activeConsensusVersion,
        ProtocolVersion = activeConsensusVersion,
        StateBundleSchemaVersion = GetStateBundleSchemaVersion(activeConsensusVersion),
        HttpApiVersion = activeConsensusVersion >= BlakeConsensusVersion ? BlakeHttpApiVersion : HttpApiVersion,
        PeerTransportVersion = activeConsensusVersion >= BlakeConsensusVersion ? BlakePeerTransportVersion : PeerTransportVersion,
        UdpRelayVersion = activeConsensusVersion >= BlakeConsensusVersion ? BlakeUdpRelayVersion : UdpRelayVersion,
        ChainDomainFingerprint = activeConsensusVersion >= BlakeConsensusVersion &&
                                 ChainDomainProfiles.TryResolve(config, out ChainDomainProfile? profile, out _)
            ? profile?.Fingerprint ?? string.Empty
            : string.Empty,
        ReleaseVersion = ReleaseVersion.Value
    };

    public static BootNodeVersionInfo FromNetworkStatus(BootNetworkStatusDto status)
    {
        if (status.VersionInfo != null && status.VersionInfo.HasAnyVersion)
        {
            return Normalize(status.VersionInfo, status.ProtocolVersion);
        }

        return new BootNodeVersionInfo
        {
            ConsensusVersion = status.ConsensusVersion != 0 ? status.ConsensusVersion : status.ProtocolVersion,
            ProtocolVersion = status.ProtocolVersion,
            StateBundleSchemaVersion = status.StateBundleSchemaVersion,
            HttpApiVersion = status.HttpApiVersion,
            PeerTransportVersion = status.PeerTransportVersion,
            UdpRelayVersion = status.UdpRelayVersion,
            ChainDomainFingerprint = status.ChainDomainFingerprint,
            ReleaseVersion = status.ReleaseVersion
        };
    }

    public static BootNodeVersionInfo FromStateBundle(BootStateBundle bundle) => new()
    {
        ConsensusVersion = bundle.ConsensusVersion != 0 ? bundle.ConsensusVersion : bundle.ProtocolVersion,
        ProtocolVersion = bundle.ProtocolVersion,
        StateBundleSchemaVersion = bundle.StateBundleSchemaVersion,
        HttpApiVersion = bundle.HttpApiVersion,
        PeerTransportVersion = bundle.PeerTransportVersion,
        UdpRelayVersion = bundle.UdpRelayVersion,
        ChainDomainFingerprint = bundle.ChainDomainFingerprint,
        ReleaseVersion = bundle.ReleaseVersion
    };

    public static BootNodeVersionInfo FromPeerShare(PeerShareAnnouncement announcement) => new()
    {
        ConsensusVersion = announcement.ConsensusVersion != 0 ? announcement.ConsensusVersion : announcement.ProtocolVersion,
        ProtocolVersion = announcement.ProtocolVersion,
        StateBundleSchemaVersion = announcement.StateBundleSchemaVersion,
        HttpApiVersion = announcement.HttpApiVersion,
        PeerTransportVersion = announcement.PeerTransportVersion,
        UdpRelayVersion = announcement.UdpRelayVersion,
        ChainDomainFingerprint = announcement.ChainDomainFingerprint,
        ReleaseVersion = announcement.ReleaseVersion
    };

    public static BootNodeVersionInfo FromPeerHello(BootPeerSessionHello hello) => new()
    {
        ConsensusVersion = hello.ConsensusVersion != 0 ? hello.ConsensusVersion : hello.ProtocolVersion,
        ProtocolVersion = hello.ProtocolVersion,
        StateBundleSchemaVersion = hello.StateBundleSchemaVersion,
        HttpApiVersion = hello.HttpApiVersion,
        PeerTransportVersion = hello.PeerTransportVersion,
        UdpRelayVersion = hello.UdpRelayVersion,
        ChainDomainFingerprint = hello.ChainDomainFingerprint,
        ReleaseVersion = hello.ReleaseVersion
    };

    public static BootVersionCompatibilityDto Evaluate(
        BootNodeVersionInfo local,
        BootNodeVersionInfo remote,
        string localNetworkId,
        string? remoteNetworkId,
        bool requireStateBundleSchema)
    {
        string normalizedLocalNetwork = localNetworkId.Trim();
        string normalizedRemoteNetwork = (remoteNetworkId ?? string.Empty).Trim();
        var result = new BootVersionCompatibilityDto
        {
            LocalVersion = Normalize(local, local.ConsensusVersion),
            RemoteVersion = Normalize(remote, remote.ConsensusVersion),
            NetworkCompatible = string.Equals(normalizedLocalNetwork, normalizedRemoteNetwork, StringComparison.OrdinalIgnoreCase),
            ConsensusCompatible = EffectiveConsensusVersion(remote) == EffectiveConsensusVersion(local),
            StateBundleSchemaCompatible = !requireStateBundleSchema ||
                                          remote.StateBundleSchemaVersion == local.StateBundleSchemaVersion,
            HttpApiCompatible = remote.HttpApiVersion == 0 || remote.HttpApiVersion == local.HttpApiVersion,
            PeerTransportCompatible = remote.PeerTransportVersion == 0 ||
                                      remote.PeerTransportVersion == local.PeerTransportVersion,
            UdpRelayCompatible = remote.UdpRelayVersion == 0 || remote.UdpRelayVersion == local.UdpRelayVersion
        };
        bool domainRequired = EffectiveConsensusVersion(local) >= BlakeConsensusVersion ||
                              EffectiveConsensusVersion(remote) >= BlakeConsensusVersion;
        result.ChainDomainCompatible = !domainRequired ||
            IsCanonicalFingerprint(local.ChainDomainFingerprint) &&
            IsCanonicalFingerprint(remote.ChainDomainFingerprint) &&
            string.Equals(local.ChainDomainFingerprint, remote.ChainDomainFingerprint, StringComparison.Ordinal);

        if (!result.NetworkCompatible)
        {
            result.Status = "incompatible";
            result.Reason = $"network id mismatch: local={normalizedLocalNetwork}, remote={normalizedRemoteNetwork}";
        }
        else if (!result.ConsensusCompatible)
        {
            result.Status = "incompatible";
            result.Reason = $"consensus version mismatch: local={EffectiveConsensusVersion(local)}, remote={EffectiveConsensusVersion(remote)}";
        }
        else if (!result.ChainDomainCompatible)
        {
            result.Status = "incompatible";
            result.Reason = "chain domain fingerprint is missing or mismatched";
        }
        else if (!result.StateBundleSchemaCompatible)
        {
            result.Status = "incompatible";
            string remoteText = remote.StateBundleSchemaVersion == 0 ? "missing" : remote.StateBundleSchemaVersion.ToString();
            result.Reason = $"state bundle schema mismatch: local={local.StateBundleSchemaVersion}, remote={remoteText}";
        }
        else if (!result.HttpApiCompatible)
        {
            result.Status = "incompatible";
            result.Reason = $"HTTP API version mismatch: local={local.HttpApiVersion}, remote={remote.HttpApiVersion}";
        }
        else if (!result.PeerTransportCompatible)
        {
            result.Status = "compatible-with-transport-fallback";
            result.Reason = $"peer transport version differs: local={local.PeerTransportVersion}, remote={remote.PeerTransportVersion}; using HTTP fallback";
        }
        else
        {
            result.Status = "compatible";
            result.Reason = "versions compatible";
        }

        if (!result.UdpRelayCompatible)
        {
            result.Warnings.Add($"UDP relay version differs: local={local.UdpRelayVersion}, remote={remote.UdpRelayVersion}; UDP fast relay disabled for this peer");
        }

        return result;
    }

    private static BootNodeVersionInfo Normalize(BootNodeVersionInfo version, int fallbackConsensusVersion) => new()
    {
        SoftwareConsensusVersion = version.SoftwareConsensusVersion != 0
            ? version.SoftwareConsensusVersion
            : (version.ConsensusVersion != 0 ? version.ConsensusVersion : fallbackConsensusVersion),
        ConsensusVersion = version.ConsensusVersion != 0 ? version.ConsensusVersion : fallbackConsensusVersion,
        ProtocolVersion = version.ProtocolVersion != 0 ? version.ProtocolVersion : fallbackConsensusVersion,
        StateBundleSchemaVersion = version.StateBundleSchemaVersion,
        HttpApiVersion = version.HttpApiVersion,
        PeerTransportVersion = version.PeerTransportVersion,
        UdpRelayVersion = version.UdpRelayVersion,
        ChainDomainFingerprint = version.ChainDomainFingerprint ?? string.Empty,
        ReleaseVersion = version.ReleaseVersion ?? string.Empty
    };

    private static bool IsCanonicalFingerprint(string? value) =>
        value?.Length == 64 && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static int EffectiveConsensusVersion(BootNodeVersionInfo version) =>
        version.ConsensusVersion != 0 ? version.ConsensusVersion : version.ProtocolVersion;
}

public class BootNodeVersionInfo
{
    public int SoftwareConsensusVersion { get; set; }
    public int ConsensusVersion { get; set; }
    public int ProtocolVersion { get; set; }
    public int StateBundleSchemaVersion { get; set; }
    public int HttpApiVersion { get; set; }
    public int PeerTransportVersion { get; set; }
    public int UdpRelayVersion { get; set; }
    public string ChainDomainFingerprint { get; set; } = string.Empty;
    public string ReleaseVersion { get; set; } = string.Empty;

    public bool HasAnyVersion =>
        SoftwareConsensusVersion != 0 ||
        ConsensusVersion != 0 ||
        ProtocolVersion != 0 ||
        StateBundleSchemaVersion != 0 ||
        HttpApiVersion != 0 ||
        PeerTransportVersion != 0 ||
        UdpRelayVersion != 0 ||
        !string.IsNullOrWhiteSpace(ReleaseVersion);
}

public class BootVersionCompatibilityDto
{
    public string Status { get; set; } = "unknown";
    public string Reason { get; set; } = string.Empty;
    public bool NetworkCompatible { get; set; }
    public bool ChainDomainCompatible { get; set; }
    public bool ConsensusCompatible { get; set; }
    public bool StateBundleSchemaCompatible { get; set; }
    public bool HttpApiCompatible { get; set; }
    public bool PeerTransportCompatible { get; set; }
    public bool UdpRelayCompatible { get; set; }
    public BootNodeVersionInfo LocalVersion { get; set; } = new();
    public BootNodeVersionInfo RemoteVersion { get; set; } = new();
    public List<string> Warnings { get; set; } = [];

    public bool CanSyncState =>
        NetworkCompatible &&
        ChainDomainCompatible &&
        ConsensusCompatible &&
        StateBundleSchemaCompatible &&
        HttpApiCompatible;
}
