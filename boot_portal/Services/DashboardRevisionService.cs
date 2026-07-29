using boot_portal.Models;
using Microsoft.AspNetCore.SignalR;

namespace boot_portal.Services;

public sealed class DashboardRevisionService : BackgroundService
{
    private readonly BootProtocolStateService _stateService;
    private readonly IHubContext<DashboardHub> _hubContext;
    private readonly ILogger<DashboardRevisionService> _logger;
    private DashboardFingerprint? _last;
    private long _revision;

    public DashboardRevisionService(
        BootProtocolStateService stateService,
        IHubContext<DashboardHub> hubContext,
        ILogger<DashboardRevisionService> logger)
    {
        _stateService = stateService;
        _hubContext = hubContext;
        _logger = logger;
    }

    public long CurrentRevision => Interlocked.Read(ref _revision);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                BootNetworkStatusDto status = _stateService.GetPublicNetworkStatus();
                DashboardFingerprint current = DashboardFingerprint.From(status);
                List<string> changed = current.DescribeChanges(_last);
                if (changed.Count == 0)
                {
                    continue;
                }

                _last = current;
                long revision = Interlocked.Increment(ref _revision);
                await _hubContext.Clients.All.SendAsync(
                    "DashboardChanged",
                    new DashboardChangedDto
                    {
                        Revision = revision,
                        TimestampUtc = DateTime.UtcNow,
                        Topics = changed
                    },
                    stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dashboard revision publisher stopped unexpectedly.");
            throw;
        }
    }

    private sealed record DashboardFingerprint(
        string CurrentStateId,
        string CandidateStateId,
        string ActiveSnapshotId,
        string CurrentTipBlockHash,
        long? CurrentTipBlockHeight,
        int WorkSetCount,
        int WinnersCount,
        int PeerCount,
        bool MiningWorkSafe,
        bool OutboundRelayHealthy,
        DateTime? LastLocalPulseUtc,
        double? LocalMiningHashrateThs)
    {
        public static DashboardFingerprint From(BootNetworkStatusDto status) =>
            new(
                status.CurrentStateId,
                status.CandidateStateId,
                status.ActiveSnapshotId,
                status.CurrentTipBlockHash ?? string.Empty,
                status.CurrentTipBlockHeight,
                status.WorkSetCount,
                status.WinnersCount,
                status.PeerCount,
                status.MiningWorkSafe,
                status.OutboundRelayHealthy,
                status.LastLocalPulseUtc,
                status.LocalMiningHashrateThs);

        public List<string> DescribeChanges(DashboardFingerprint? previous)
        {
            if (previous == null)
            {
                return ["status", "snapshot", "reserve", "network", "pulse", "miners"];
            }

            var topics = new List<string>();
            if (CurrentStateId != previous.CurrentStateId ||
                CandidateStateId != previous.CandidateStateId ||
                ActiveSnapshotId != previous.ActiveSnapshotId ||
                WinnersCount != previous.WinnersCount)
            {
                topics.Add("snapshot");
            }
            if (WorkSetCount != previous.WorkSetCount)
            {
                topics.Add("reserve");
                topics.Add("work-rate");
            }
            if (CurrentTipBlockHash != previous.CurrentTipBlockHash ||
                CurrentTipBlockHeight != previous.CurrentTipBlockHeight ||
                MiningWorkSafe != previous.MiningWorkSafe)
            {
                topics.Add("status");
            }
            if (PeerCount != previous.PeerCount ||
                OutboundRelayHealthy != previous.OutboundRelayHealthy)
            {
                topics.Add("network");
            }
            if (LastLocalPulseUtc != previous.LastLocalPulseUtc)
            {
                topics.Add("pulse");
            }
            if (LocalMiningHashrateThs != previous.LocalMiningHashrateThs)
            {
                topics.Add("miners");
            }

            return topics.Distinct(StringComparer.Ordinal).ToList();
        }
    }
}
