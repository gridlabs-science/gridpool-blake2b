using System.Security.Cryptography;
using System.Text;
using boot_portal.Models;

namespace boot_portal.Services;

public sealed class DashboardVisualizationJournalService
{
    public const int MaximumEvents = 2_048;
    public static readonly TimeSpan Retention = TimeSpan.FromMinutes(10);

    private readonly object _sync = new();
    private readonly byte[] _visualSalt = RandomNumberGenerator.GetBytes(32);
    private readonly List<DashboardDiagramEventDto> _events = [];
    private readonly Dictionary<string, DashboardDiagramPeerDto> _peerState =
        new(StringComparer.OrdinalIgnoreCase);
    private long _sequence;
    private DashboardDiagramSlotZeroDto _slotZero = new();
    private readonly HashSet<long> _bitcoinPeerIds = [];
    private bool _bitcoinPeersObserved;
    private bool? _miningSafe;
    private long? _tipHeight;
    private string _tipHash = string.Empty;

    public long LatestSequence => Interlocked.Read(ref _sequence);

    public void Reset()
    {
        lock (_sync)
        {
            _events.Clear();
            _peerState.Clear();
            _slotZero = new DashboardDiagramSlotZeroDto();
            _bitcoinPeerIds.Clear();
            _bitcoinPeersObserved = false;
            _miningSafe = null;
            _tipHeight = null;
            _tipHash = string.Empty;
            Interlocked.Exchange(ref _sequence, 0);
        }
    }

    public string VisualId(string category, string value)
    {
        string material = $"{category}:{value?.Trim()}";
        byte[] payload = Encoding.UTF8.GetBytes(material);
        byte[] keyed = new byte[_visualSalt.Length + payload.Length];
        Buffer.BlockCopy(_visualSalt, 0, keyed, 0, _visualSalt.Length);
        Buffer.BlockCopy(payload, 0, keyed, _visualSalt.Length, payload.Length);
        return Convert.ToHexStringLower(SHA256.HashData(keyed))[..16];
    }

    public (long Oldest, long Latest) Bounds(DateTime? nowUtc = null)
    {
        lock (_sync)
        {
            PruneNoLock(nowUtc ?? DateTime.UtcNow);
            return (
                _events.Count == 0 ? Math.Max(1, LatestSequence + 1) : _events[0].Sequence,
                LatestSequence);
        }
    }

    public DashboardDiagramSlotZeroDto SlotZero()
    {
        lock (_sync)
        {
            return CloneSlotZero(_slotZero);
        }
    }

    public DashboardDiagramEventDto Append(DashboardDiagramEventDto item)
    {
        ArgumentNullException.ThrowIfNull(item);
        DateTime nowUtc = item.TimestampUtc == default ? DateTime.UtcNow : NormalizeUtc(item.TimestampUtc);
        lock (_sync)
        {
            if (string.Equals(item.Kind, DashboardDiagramEventKinds.ProofRejected, StringComparison.Ordinal) &&
                _events.LastOrDefault(existing =>
                    string.Equals(existing.Kind, item.Kind, StringComparison.Ordinal) &&
                    string.Equals(existing.SourceVisualId, item.SourceVisualId, StringComparison.Ordinal) &&
                    string.Equals(existing.Category, item.Category, StringComparison.OrdinalIgnoreCase) &&
                    nowUtc - existing.TimestampUtc <= TimeSpan.FromSeconds(1)) is { } previousRejected)
            {
                item.Count = Math.Max(1, previousRejected.Count) + Math.Max(1, item.Count);
                _events.Remove(previousRejected);
            }
            item.Sequence = ++_sequence;
            item.TimestampUtc = nowUtc;
            if (!string.IsNullOrWhiteSpace(item.ProofId) && string.IsNullOrWhiteSpace(item.VisualId))
            {
                item.VisualId = VisualId("proof", item.ProofId);
            }
            if (!string.IsNullOrWhiteSpace(item.DisplacedProofId) &&
                string.IsNullOrWhiteSpace(item.DisplacedVisualId))
            {
                item.DisplacedVisualId = VisualId("proof", item.DisplacedProofId);
            }
            item.LockedVisualIds = item.LockedProofIds
                .Select(proofId => VisualId("proof", proofId))
                .ToList();
            _events.Add(CloneEvent(item));
            PruneNoLock(nowUtc);
            return CloneEvent(item);
        }
    }

    public void ObserveVerifiedLocalSlotZero(string address, string proofId, DateTime observedUtc)
    {
        if (string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(proofId))
        {
            return;
        }
        lock (_sync)
        {
            _slotZero = new DashboardDiagramSlotZeroDto
            {
                Verified = true,
                Address = address,
                ProofId = proofId,
                ObservedUtc = NormalizeUtc(observedUtc)
            };
        }
    }

    public void ObservePeers(IEnumerable<BootPeerStatus> peers, DateTime observedUtc)
    {
        DateTime nowUtc = NormalizeUtc(observedUtc);
        lock (_sync)
        {
            var currentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (BootPeerStatus peer in peers)
            {
                string key = !string.IsNullOrWhiteSpace(peer.NodeId) ? peer.NodeId : peer.Endpoint;
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }
                currentIds.Add(key);
                bool connected = peer.SessionConnected ||
                    string.Equals(peer.Status, "connected", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(peer.Status, "ok", StringComparison.OrdinalIgnoreCase);
                if (!_peerState.TryGetValue(key, out DashboardDiagramPeerDto? previous) ||
                    previous.Connected != connected)
                {
                    AppendNoLock(new DashboardDiagramEventDto
                    {
                        TimestampUtc = nowUtc,
                        Kind = DashboardDiagramEventKinds.PeerConnection,
                        SourceKind = "peer",
                        SourceId = key,
                        SourceVisualId = VisualId("peer", key),
                        VisualId = VisualId("peer", key),
                        Connected = connected,
                        LatencyMs = peer.LatencyMs
                    });
                }
                string relation = PeerStateRelation(peer);
                if (previous != null &&
                    !string.Equals(previous.Transport, peer.ConnectionMode, StringComparison.OrdinalIgnoreCase))
                {
                    AppendNoLock(new DashboardDiagramEventDto
                    {
                        TimestampUtc = nowUtc,
                        Kind = DashboardDiagramEventKinds.PeerTransport,
                        SourceKind = "peer",
                        SourceId = key,
                        SourceVisualId = VisualId("peer", key),
                        PreviousValue = previous.Transport,
                        CurrentValue = peer.ConnectionMode
                    });
                }
                if (previous != null &&
                    !string.Equals(previous.StateRelation, relation, StringComparison.OrdinalIgnoreCase))
                {
                    AppendNoLock(new DashboardDiagramEventDto
                    {
                        TimestampUtc = nowUtc,
                        Kind = DashboardDiagramEventKinds.PeerState,
                        SourceKind = "peer",
                        SourceId = key,
                        SourceVisualId = VisualId("peer", key),
                        PreviousValue = previous.StateRelation,
                        CurrentValue = relation
                    });
                }
                _peerState[key] = new DashboardDiagramPeerDto
                {
                    VisualId = VisualId("peer", key),
                    NodeId = peer.NodeId,
                    Endpoint = peer.Endpoint,
                    Status = peer.Status,
                    Connected = connected,
                    LatencyMs = peer.LatencyMs,
                    LastActivityUtc = peer.LastSuccessUtc ?? peer.LastSeenUtc,
                    CompatibilityStatus = peer.CompatibilityStatus,
                    Transport = peer.ConnectionMode,
                    StateRelation = relation
                };
            }
            foreach (string missing in _peerState.Keys.Where(key => !currentIds.Contains(key)).ToList())
            {
                DashboardDiagramPeerDto previous = _peerState[missing];
                if (previous.Connected)
                {
                    AppendNoLock(new DashboardDiagramEventDto
                    {
                        TimestampUtc = nowUtc,
                        Kind = DashboardDiagramEventKinds.PeerConnection,
                        SourceKind = "peer",
                        SourceId = missing,
                        SourceVisualId = VisualId("peer", missing),
                        VisualId = VisualId("peer", missing),
                        Connected = false,
                        LatencyMs = previous.LatencyMs
                    });
                }
                _peerState.Remove(missing);
            }
            PruneNoLock(nowUtc);
        }
    }

    public void ObserveSystemHealth(BootNetworkStatusDto status, DateTime observedUtc)
    {
        DateTime nowUtc = NormalizeUtc(observedUtc);
        lock (_sync)
        {
            if (_miningSafe.HasValue && _miningSafe.Value != status.MiningWorkSafe)
            {
                AppendNoLock(new DashboardDiagramEventDto
                {
                    TimestampUtc = nowUtc,
                    Kind = DashboardDiagramEventKinds.MiningSafety,
                    SourceKind = "bitcoin",
                    Safe = status.MiningWorkSafe,
                    Category = status.MiningWorkSafe ? "recovered" : "unsafe"
                });
            }
            _miningSafe = status.MiningWorkSafe;

            if (_tipHeight.HasValue && status.CurrentTipBlockHeight.HasValue &&
                status.CurrentTipBlockHeight.Value < _tipHeight.Value)
            {
                AppendNoLock(new DashboardDiagramEventDto
                {
                    TimestampUtc = nowUtc,
                    Kind = DashboardDiagramEventKinds.ChainReorganization,
                    SourceKind = "bitcoin",
                    PreviousValue = _tipHash,
                    CurrentValue = status.CurrentTipBlockHash ?? string.Empty,
                    BlockHash = status.CurrentTipBlockHash ?? string.Empty,
                    BlockHeight = status.CurrentTipBlockHeight,
                    Count = (int)Math.Min(int.MaxValue, _tipHeight.Value - status.CurrentTipBlockHeight.Value)
                });
            }
            _tipHeight = status.CurrentTipBlockHeight;
            _tipHash = status.CurrentTipBlockHash ?? string.Empty;

            HashSet<long> currentBitcoinPeers = status.BitcoinNotification.Network.Peers
                .Select(peer => peer.Id)
                .ToHashSet();
            if (_bitcoinPeersObserved)
            {
                foreach (long peerId in currentBitcoinPeers.Except(_bitcoinPeerIds))
                {
                    AppendNoLock(new DashboardDiagramEventDto
                    {
                        TimestampUtc = nowUtc,
                        Kind = DashboardDiagramEventKinds.BitcoinPeerConnection,
                        SourceKind = "bitcoin-peer",
                        SourceId = peerId.ToString(),
                        SourceVisualId = VisualId("bitcoin-peer", peerId.ToString()),
                        Connected = true
                    });
                }
                foreach (long peerId in _bitcoinPeerIds.Except(currentBitcoinPeers))
                {
                    AppendNoLock(new DashboardDiagramEventDto
                    {
                        TimestampUtc = nowUtc,
                        Kind = DashboardDiagramEventKinds.BitcoinPeerConnection,
                        SourceKind = "bitcoin-peer",
                        SourceId = peerId.ToString(),
                        SourceVisualId = VisualId("bitcoin-peer", peerId.ToString()),
                        Connected = false
                    });
                }
            }
            _bitcoinPeersObserved = true;
            _bitcoinPeerIds.Clear();
            _bitcoinPeerIds.UnionWith(currentBitcoinPeers);
            PruneNoLock(nowUtc);
        }
    }

    public DashboardDiagramEventPageDto Read(long after, int limit, bool redacted, DateTime? nowUtc = null)
    {
        DateTime generatedUtc = NormalizeUtc(nowUtc ?? DateTime.UtcNow);
        lock (_sync)
        {
            PruneNoLock(generatedUtc);
            long oldest = _events.Count == 0 ? Math.Max(1, LatestSequence + 1) : _events[0].Sequence;
            long latest = LatestSequence;
            bool gap = after > latest || after > 0 && after < oldest - 1;
            int boundedLimit = Math.Clamp(limit, 1, 256);
            List<DashboardDiagramEventDto> available = _events
                .Where(item => item.Sequence > after)
                .Take(boundedLimit + 1)
                .ToList();
            bool hasMore = available.Count > boundedLimit;
            List<DashboardDiagramEventDto> page = available
                .Take(boundedLimit)
                .Select(item => redacted ? Redact(item) : CloneEvent(item))
                .ToList();
            return new DashboardDiagramEventPageDto
            {
                GeneratedAtUtc = generatedUtc,
                Redacted = redacted,
                OldestSequence = oldest,
                LatestSequence = latest,
                NextSequence = page.Count == 0 ? after : page[^1].Sequence,
                HasMore = hasMore,
                Gap = gap,
                Events = page
            };
        }
    }

    private void AppendNoLock(DashboardDiagramEventDto item)
    {
        item.Sequence = ++_sequence;
        if (item.TimestampUtc == default)
        {
            item.TimestampUtc = DateTime.UtcNow;
        }
        _events.Add(CloneEvent(item));
    }

    private void PruneNoLock(DateTime nowUtc)
    {
        DateTime cutoff = NormalizeUtc(nowUtc) - Retention;
        int remove = _events.FindIndex(item => item.TimestampUtc >= cutoff);
        if (remove < 0)
        {
            _events.Clear();
        }
        else if (remove > 0)
        {
            _events.RemoveRange(0, remove);
        }
        if (_events.Count > MaximumEvents)
        {
            _events.RemoveRange(0, _events.Count - MaximumEvents);
        }
    }

    private static DashboardDiagramEventDto Redact(DashboardDiagramEventDto item)
    {
        DashboardDiagramEventDto clone = CloneEvent(item);
        clone.SourceId = string.Empty;
        clone.Transport = string.Empty;
        if (string.Equals(item.Kind, DashboardDiagramEventKinds.ProofRejected, StringComparison.Ordinal))
        {
            clone.Category = RejectionCategory(item.Category, item.Reason);
        }
        clone.Reason = string.Empty;
        if (!string.Equals(item.Kind, DashboardDiagramEventKinds.ProofAdmitted, StringComparison.Ordinal))
        {
            clone.ProofId = string.Empty;
            clone.Address = string.Empty;
            clone.Difficulty = null;
            clone.DisplacedProofId = string.Empty;
        }
        if (!string.Equals(item.Kind, DashboardDiagramEventKinds.BoundaryValidated, StringComparison.Ordinal))
        {
            clone.LockedProofIds = [];
        }
        return clone;
    }

    public static string RejectionCategory(string? category, string? reason = null)
    {
        string value = $"{category} {reason}".Trim().ToLowerInvariant();
        if (value.Contains("rate") || value.Contains("limit")) return "rate-limited";
        if (value.Contains("previous-parent") || value.Contains("boundary") ||
            value.Contains("snapshot") || value.Contains("quarant")) return "boundary";
        if (value.Contains("duplicate") || value.Contains("replay")) return "duplicate";
        if (value.Contains("difficulty") || value.Contains("floor") || value.Contains("target")) return "below-floor";
        if (value.Contains("transport") || value.Contains("timeout")) return "transport";
        if (value.Contains("signature") || value.Contains("coinbase") || value.Contains("payout") ||
            value.Contains("hash") || value.Contains("proof") || value.Contains("invalid") ||
            value.Contains("malformed")) return "invalid-proof";
        return "rejected";
    }

    private static DashboardDiagramSlotZeroDto CloneSlotZero(DashboardDiagramSlotZeroDto item) => new()
    {
        Verified = item.Verified,
        Address = item.Address,
        ObservedUtc = item.ObservedUtc,
        ProofId = item.ProofId
    };

    private static DashboardDiagramEventDto CloneEvent(DashboardDiagramEventDto item) => new()
    {
        Sequence = item.Sequence,
        TimestampUtc = item.TimestampUtc,
        Kind = item.Kind,
        SourceKind = item.SourceKind,
        SourceId = item.SourceId,
        SourceVisualId = item.SourceVisualId,
        Transport = item.Transport,
        VisualId = item.VisualId,
        ProofId = item.ProofId,
        Address = item.Address,
        Difficulty = item.Difficulty,
        BlockQuality = item.BlockQuality,
        ReceivedUtc = item.ReceivedUtc,
        ValidatedUtc = item.ValidatedUtc,
        MutatedUtc = item.MutatedUtc,
        Rank = item.Rank,
        DisplacedVisualId = item.DisplacedVisualId,
        DisplacedProofId = item.DisplacedProofId,
        Connected = item.Connected,
        LatencyMs = item.LatencyMs,
        AcceptedShareDelta = item.AcceptedShareDelta,
        HashrateThs = item.HashrateThs,
        BlockHash = item.BlockHash,
        BlockHeight = item.BlockHeight,
        SnapshotId = item.SnapshotId,
        LockedVisualIds = item.LockedVisualIds.ToList(),
        LockedProofIds = item.LockedProofIds.ToList(),
        Category = item.Category,
        Reason = item.Reason,
        PreviousValue = item.PreviousValue,
        CurrentValue = item.CurrentValue,
        BoundaryKind = item.BoundaryKind,
        Count = item.Count,
        Safe = item.Safe
    };

    private static string PeerStateRelation(BootPeerStatus peer)
    {
        if (string.Equals(peer.CompatibilityStatus, "incompatible", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(peer.CompatibilityStatus, "mismatch", StringComparison.OrdinalIgnoreCase))
        {
            return "incompatible";
        }
        if (string.IsNullOrWhiteSpace(peer.LastCurrentStateId))
        {
            return "unknown";
        }
        return string.Equals(peer.LastCurrentStateId, peer.LastCandidateStateId, StringComparison.OrdinalIgnoreCase)
            ? "current"
            : "candidate";
    }

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
