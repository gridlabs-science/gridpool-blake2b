using System.Collections.Concurrent;

namespace boot_portal.Services;

public sealed class BootPeerLoopFault
{
    public long Count { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime? LastFaultUtc { get; set; }
}

public sealed class BootPeerLoopHealth
{
    private readonly DateTime _startedUtc;
    private readonly ConcurrentDictionary<string, BootPeerLoopFault> _faults = new(StringComparer.OrdinalIgnoreCase);
    private long _localPulseAccepted;

    public BootPeerLoopHealth(DateTime? startedUtc = null)
    {
        _startedUtc = startedUtc ?? DateTime.UtcNow;
    }

    public DateTime StartedUtc => _startedUtc;
    public DateTime? LastPeerPollCompletedUtc { get; private set; }
    public DateTime? LastShareRelayDequeuedUtc { get; private set; }
    public DateTime? LastSuccessfulOutboundRelayUtc { get; private set; }
    public DateTime? LastChainTipRelayUtc { get; private set; }
    public DateTime? LastLocalPulseUtc { get; private set; }
    public long LocalPulseAcceptedCount => Interlocked.Read(ref _localPulseAccepted);

    public void RecordPeerPollCompleted() => LastPeerPollCompletedUtc = DateTime.UtcNow;
    public void RecordShareDequeued() => LastShareRelayDequeuedUtc = DateTime.UtcNow;
    public void RecordOutboundRelay() => LastSuccessfulOutboundRelayUtc = DateTime.UtcNow;
    public void RecordChainTipRelay() => LastChainTipRelayUtc = DateTime.UtcNow;

    public void RecordLocalPulse(DateTime timestampUtc)
    {
        LastLocalPulseUtc = timestampUtc;
        Interlocked.Increment(ref _localPulseAccepted);
    }

    public void RecordFault(string loop, string category, Exception exception)
    {
        string withoutUrlQueries = System.Text.RegularExpressions.Regex.Replace(
            exception.Message,
            @"https?://[^\s]+",
            match => match.Value.Split('?', 2)[0]);
        string safeMessage = withoutUrlQueries.Length > 240 ? withoutUrlQueries[..240] : withoutUrlQueries;
        _faults.AddOrUpdate(loop,
            _ => new BootPeerLoopFault { Count = 1, Category = category, Message = safeMessage, LastFaultUtc = DateTime.UtcNow },
            (_, current) => new BootPeerLoopFault
            {
                Count = current.Count + 1,
                Category = category,
                Message = safeMessage,
                LastFaultUtc = DateTime.UtcNow
            });
    }

    public Dictionary<string, BootPeerLoopFault> GetFaults() =>
        _faults.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);

    public bool IsPeerPollStale(DateTime nowUtc, int thresholdSeconds) =>
        nowUtc - (LastPeerPollCompletedUtc ?? StartedUtc) > TimeSpan.FromSeconds(Math.Max(30, thresholdSeconds));

    public bool IsOutboundRelayStale(DateTime nowUtc, int thresholdSeconds) =>
        nowUtc - (LastSuccessfulOutboundRelayUtc ?? StartedUtc) > TimeSpan.FromSeconds(Math.Max(30, thresholdSeconds));
}
