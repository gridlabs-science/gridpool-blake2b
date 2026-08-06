using System.Text.Json;
using boot_portal.Models;
using boot_portal.Utils;

namespace boot_portal.Services;

public sealed class DashboardTelemetryService : BackgroundService
{
    private const int MaxWorkProofs = 100_000;
    private const int MaxFloorObservations = 20_000;
    private const int MaxPulseObservations = 100_000;
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);
    private static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(30);
    private readonly object _sync = new();
    private readonly ILogger<DashboardTelemetryService> _logger;
    private readonly string _path;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = false };
    private DashboardTelemetryDocument _document;
    private HashSet<string> _workShareIds;
    private HashSet<string> _pulseShareIds;
    private bool _dirty;

    public DashboardTelemetryService(ILogger<DashboardTelemetryService> logger)
        : this(logger, BootPortalPaths.DashboardTelemetryFilePath, null)
    {
    }

    internal DashboardTelemetryService(
        ILogger<DashboardTelemetryService> logger,
        string path,
        DateTime? trackingStartedUtc)
    {
        _logger = logger;
        _path = path;
        _document = Load(trackingStartedUtc);
        _workShareIds = _document.WorkProofs
            .Select(item => item.ShareId)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _pulseShareIds = _document.Pulses
            .Select(item => item.ShareId)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        PruneNoLock(DateTime.UtcNow);
    }

    public void ObserveAdmissionFloor(double difficulty, DateTime observedUtc)
    {
        if (!double.IsFinite(difficulty) || difficulty <= 0)
        {
            return;
        }

        DateTime timestamp = NormalizeUtc(observedUtc);
        lock (_sync)
        {
            DashboardFloorObservation? last = _document.AdmissionFloors.Count == 0
                ? null
                : _document.AdmissionFloors[^1];
            if (last != null && Math.Abs(last.Difficulty - difficulty) <= Math.Max(1d, difficulty) * 1e-12)
            {
                return;
            }

            _document.AdmissionFloors.Add(new DashboardFloorObservation
            {
                Difficulty = difficulty,
                ObservedUtc = timestamp
            });
            _dirty = true;
            PruneNoLock(timestamp);
        }
    }

    public void ObserveWorkProof(
        string shareId,
        string source,
        double difficulty,
        double admissionFloorDifficulty,
        DateTime receivedUtc,
        string address = "",
        string username = "",
        string sourceKind = "",
        bool enteredWorkSet = true,
        bool blockQuality = false)
    {
        if (string.IsNullOrWhiteSpace(shareId) ||
            !double.IsFinite(difficulty) ||
            difficulty <= 0 ||
            !double.IsFinite(admissionFloorDifficulty) ||
            admissionFloorDifficulty <= 0)
        {
            return;
        }

        DateTime timestamp = NormalizeUtc(receivedUtc);
        lock (_sync)
        {
            if (!_workShareIds.Add(shareId))
            {
                return;
            }

            _document.WorkProofs.Add(new DashboardWorkObservation
            {
                ShareId = shareId,
                Source = source?.Trim() ?? string.Empty,
                Difficulty = difficulty,
                AdmissionFloorDifficulty = admissionFloorDifficulty,
                ReceivedUtc = timestamp,
                Address = address?.Trim() ?? string.Empty,
                Username = username?.Trim() ?? string.Empty,
                SourceKind = sourceKind?.Trim() ?? string.Empty,
                EnteredWorkSet = enteredWorkSet,
                BlockQuality = blockQuality
            });
            _dirty = true;
            PruneNoLock(timestamp);
        }
    }

    public void ObservePulse(
        string shareId,
        string source,
        DateTime receivedUtc,
        string address = "",
        string username = "",
        string sourceKind = "",
        double difficulty = 0,
        bool blockQuality = false)
    {
        if (string.IsNullOrWhiteSpace(shareId))
        {
            return;
        }

        DateTime timestamp = NormalizeUtc(receivedUtc);
        lock (_sync)
        {
            if (!_pulseShareIds.Add(shareId))
            {
                return;
            }

            _document.Pulses.Add(new DashboardPulseObservation
            {
                ShareId = shareId,
                Source = source?.Trim() ?? string.Empty,
                ReceivedUtc = timestamp,
                Address = address?.Trim() ?? string.Empty,
                Username = username?.Trim() ?? string.Empty,
                SourceKind = sourceKind?.Trim() ?? string.Empty,
                Difficulty = double.IsFinite(difficulty) && difficulty > 0 ? difficulty : 0,
                BlockQuality = blockQuality
            });
            _dirty = true;
            PruneNoLock(timestamp);
        }
    }

    public DashboardWorkRateEstimateDto GetEstimate(string? windowKey = null, DateTime? nowUtc = null)
    {
        string normalizedWindow = DashboardWindows.Normalize(windowKey);
        DateTime endUtc = NormalizeUtc(nowUtc ?? DateTime.UtcNow);
        lock (_sync)
        {
            return CalculateEstimateNoLock(normalizedWindow, endUtc);
        }
    }

    public int GetPulseCount(string? windowKey = null, DateTime? nowUtc = null)
    {
        string normalizedWindow = DashboardWindows.Normalize(windowKey);
        DateTime endUtc = NormalizeUtc(nowUtc ?? DateTime.UtcNow);
        DateTime startUtc = endUtc - DashboardWindows.Supported[normalizedWindow];
        lock (_sync)
        {
            return _document.Pulses.Count(item =>
                item.ReceivedUtc >= startUtc &&
                item.ReceivedUtc <= endUtc);
        }
    }

    public DashboardHistoryDto GetHistory(string? windowKey = null, DateTime? nowUtc = null)
    {
        string normalizedWindow = DashboardWindows.Normalize(windowKey);
        TimeSpan window = DashboardWindows.Supported[normalizedWindow];
        DateTime endUtc = NormalizeUtc(nowUtc ?? DateTime.UtcNow);
        DateTime startUtc = endUtc - window;
        int pointCount = normalizedWindow == "7d" ? 56 : 48;
        TimeSpan step = TimeSpan.FromTicks(window.Ticks / pointCount);
        var result = new DashboardHistoryDto
        {
            Window = normalizedWindow,
            WindowSeconds = (int)window.TotalSeconds,
            GeneratedAtUtc = endUtc
        };

        lock (_sync)
        {
            for (int index = 1; index <= pointCount; index++)
            {
                DateTime timestamp = startUtc + TimeSpan.FromTicks(step.Ticks * index);
                DashboardWorkRateEstimateDto estimate = CalculateEstimateNoLock(normalizedWindow, timestamp);
                DateTime pulseBucketStart = timestamp - step;
                int pulseCount = _document.Pulses.Count(item =>
                    item.ReceivedUtc > pulseBucketStart &&
                    item.ReceivedUtc <= timestamp);
                result.Points.Add(new DashboardHistoryPointDto
                {
                    TimestampUtc = timestamp,
                    WorkRateThs = estimate.EstimateThs,
                    WorkObservationCount = estimate.RetainedOrderStatisticCount,
                    RelativeStandardErrorPercent = estimate.RelativeStandardErrorPercent,
                    PulseCount = pulseCount
                });
            }
        }

        return result;
    }

    public DashboardDiagramHistoryDto GetDiagramHistory(
        string? address,
        string? windowKey,
        int limit,
        bool includeOperatorDetails,
        DateTime? nowUtc = null)
    {
        string normalizedWindow = DashboardWindows.Normalize(windowKey);
        if (normalizedWindow == "6h")
        {
            normalizedWindow = "24h";
        }
        DateTime endUtc = NormalizeUtc(nowUtc ?? DateTime.UtcNow);
        DateTime startUtc = endUtc - DashboardWindows.Supported[normalizedWindow];
        string normalizedAddress = address?.Trim() ?? string.Empty;
        int boundedLimit = Math.Clamp(limit, 1, 256);
        lock (_sync)
        {
            IEnumerable<DashboardDiagramProofObservationDto> work = _document.WorkProofs
                .Where(item => item.ReceivedUtc >= startUtc && item.ReceivedUtc <= endUtc)
                .Where(item => !string.IsNullOrWhiteSpace(item.Address))
                .Where(item => string.IsNullOrWhiteSpace(normalizedAddress) ||
                    string.Equals(item.Address, normalizedAddress, StringComparison.OrdinalIgnoreCase))
                .Select(item => new DashboardDiagramProofObservationDto
                {
                    ProofId = item.ShareId,
                    Address = item.Address,
                    SourceKind = item.SourceKind,
                    Source = includeOperatorDetails ? item.Source : string.Empty,
                    Username = includeOperatorDetails ? item.Username : string.Empty,
                    ProofClass = BootProofClasses.Work,
                    Difficulty = item.Difficulty,
                    DifficultyDisplay = ClientHandler.FormatDifficulty(item.Difficulty),
                    TimestampUtc = item.ReceivedUtc,
                    EnteredWorkSet = item.EnteredWorkSet,
                    BlockQuality = item.BlockQuality
                });
            IEnumerable<DashboardDiagramProofObservationDto> pulses = _document.Pulses
                .Where(item => item.ReceivedUtc >= startUtc && item.ReceivedUtc <= endUtc && item.Difficulty > 0)
                .Where(item => !string.IsNullOrWhiteSpace(item.Address))
                .Where(item => string.IsNullOrWhiteSpace(normalizedAddress) ||
                    string.Equals(item.Address, normalizedAddress, StringComparison.OrdinalIgnoreCase))
                .Select(item => new DashboardDiagramProofObservationDto
                {
                    ProofId = item.ShareId,
                    Address = item.Address,
                    SourceKind = item.SourceKind,
                    Source = includeOperatorDetails ? item.Source : string.Empty,
                    Username = includeOperatorDetails ? item.Username : string.Empty,
                    ProofClass = BootProofClasses.Pulse,
                    Difficulty = item.Difficulty,
                    DifficultyDisplay = ClientHandler.FormatDifficulty(item.Difficulty),
                    TimestampUtc = item.ReceivedUtc,
                    EnteredWorkSet = false,
                    BlockQuality = item.BlockQuality
                });
            List<DashboardDiagramProofObservationDto> observations = work
                .Concat(pulses)
                .OrderByDescending(item => item.Difficulty)
                .ThenByDescending(item => item.TimestampUtc)
                .Take(boundedLimit)
                .ToList();
            double? best = observations.Count == 0 ? null : observations.Max(item => item.Difficulty);
            return new DashboardDiagramHistoryDto
            {
                Window = normalizedWindow,
                GeneratedAtUtc = endUtc,
                Redacted = !includeOperatorDetails,
                SlotZeroAddress = normalizedAddress,
                BestDifficulty = best,
                BestDifficultyDisplay = best.HasValue ? ClientHandler.FormatDifficulty(best.Value) : "--",
                Proofs = observations
            };
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SaveInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                SaveIfDirty();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        SaveIfDirty();
        await base.StopAsync(cancellationToken);
    }

    internal void FlushForTests() => SaveIfDirty();

    private DashboardTelemetryDocument Load(DateTime? trackingStartedUtc)
    {
        try
        {
            if (!File.Exists(_path))
            {
                return NewDocument(trackingStartedUtc);
            }

            string json = File.ReadAllText(_path);
            DashboardTelemetryDocument? document = JsonSerializer.Deserialize<DashboardTelemetryDocument>(json, _jsonOptions);
            if (document == null || document.SchemaVersion is < 1 or > 2)
            {
                _logger.LogWarning("Ignored unsupported dashboard telemetry document at {Path}.", _path);
                return NewDocument(trackingStartedUtc);
            }

            bool migrated = document.SchemaVersion == 1;
            document.WorkProofs ??= [];
            document.AdmissionFloors ??= [];
            document.Pulses ??= [];
            document.SchemaVersion = 2;
            _dirty |= migrated;
            if (document.TrackingStartedUtc == default)
            {
                document.TrackingStartedUtc = DateTime.UtcNow;
            }
            return document;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load dashboard telemetry from {Path}; starting a fresh local window.", _path);
            return NewDocument(trackingStartedUtc);
        }
    }

    private static DashboardTelemetryDocument NewDocument(DateTime? trackingStartedUtc) =>
        new()
        {
            TrackingStartedUtc = NormalizeUtc(trackingStartedUtc ?? DateTime.UtcNow)
        };

    private DashboardWorkRateEstimateDto CalculateEstimateNoLock(string windowKey, DateTime endUtc)
    {
        TimeSpan requestedWindow = DashboardWindows.Supported[windowKey];
        DateTime requestedStartUtc = endUtc - requestedWindow;
        DateTime actualStartUtc = _document.TrackingStartedUtc > requestedStartUtc
            ? _document.TrackingStartedUtc
            : requestedStartUtc;
        if (actualStartUtc > endUtc)
        {
            actualStartUtc = endUtc;
        }

        DashboardFloorObservation? anchorFloor = _document.AdmissionFloors
            .Where(item => item.ObservedUtc <= actualStartUtc)
            .OrderBy(item => item.ObservedUtc)
            .LastOrDefault();
        List<DashboardFloorObservation> windowFloors = _document.AdmissionFloors
            .Where(item => item.ObservedUtc > actualStartUtc && item.ObservedUtc <= endUtc)
            .ToList();
        double effectiveFloor = Math.Max(
            1d,
            new[] { anchorFloor?.Difficulty ?? 1d }
                .Concat(windowFloors.Select(item => item.Difficulty))
                .Max());

        List<DashboardWorkObservation> completeObservations = _document.WorkProofs
            .Where(item =>
                item.ReceivedUtc >= actualStartUtc &&
                item.ReceivedUtc <= endUtc &&
                item.Difficulty >= effectiveFloor)
            .OrderByDescending(item => item.Difficulty)
            .ThenBy(item => item.ShareId, StringComparer.Ordinal)
            .ToList();
        List<DashboardWorkObservation> retained = completeObservations.Take(897).ToList();
        int m = retained.Count;
        double elapsedSeconds = Math.Max(0d, (endUtc - actualStartUtc).TotalSeconds);
        double? orderStatisticDifficulty = m > 0 ? retained[^1].Difficulty : null;
        double? estimateThs = m > 0 && elapsedSeconds > 0
            ? m * orderStatisticDifficulty!.Value * 4_294_967_296d / elapsedSeconds / 1_000_000_000_000d
            : null;
        double? relativeError = m > 0 ? 100d / Math.Sqrt(m) : null;
        bool completeWindow =
            _document.TrackingStartedUtc <= requestedStartUtc &&
            anchorFloor != null &&
            (!_document.WorkDataTruncatedThroughUtc.HasValue ||
             _document.WorkDataTruncatedThroughUtc.Value < requestedStartUtc);
        string confidence = m switch
        {
            >= 897 => "high",
            >= 100 => "medium",
            >= 30 => "low",
            _ => "collecting"
        };

        return new DashboardWorkRateEstimateDto
        {
            Window = windowKey,
            WindowSeconds = (int)Math.Round(elapsedSeconds),
            WindowStartUtc = actualStartUtc,
            WindowEndUtc = endUtc,
            ObservationCount = completeObservations.Count,
            RetainedOrderStatisticCount = m,
            EstimateThs = estimateThs,
            EstimateDisplay = FormatHashrate(estimateThs),
            OrderStatisticDifficulty = orderStatisticDifficulty,
            OrderStatisticDifficultyDisplay = orderStatisticDifficulty.HasValue
                ? ClientHandler.FormatDifficulty(orderStatisticDifficulty.Value)
                : "--",
            EffectiveAdmissionFloorDifficulty = effectiveFloor,
            EffectiveAdmissionFloorDisplay = ClientHandler.FormatDifficulty(effectiveFloor),
            RelativeStandardErrorPercent = relativeError,
            Confidence = confidence,
            Warmup = !completeWindow,
            CompleteWindow = completeWindow,
            Note = completeWindow
                ? "Estimate uses the m-th strongest complete Work-proof observation in the selected local window."
                : "Local telemetry is still warming up; this partial-window estimate is not a full-window measurement."
        };
    }

    private void PruneNoLock(DateTime nowUtc)
    {
        DateTime cutoffUtc = nowUtc - Retention;
        _document.WorkProofs.RemoveAll(item => item.ReceivedUtc < cutoffUtc);
        _document.Pulses.RemoveAll(item => item.ReceivedUtc < cutoffUtc);

        DashboardFloorObservation? floorAnchor = _document.AdmissionFloors
            .Where(item => item.ObservedUtc < cutoffUtc)
            .OrderBy(item => item.ObservedUtc)
            .LastOrDefault();
        _document.AdmissionFloors = _document.AdmissionFloors
            .Where(item => item.ObservedUtc >= cutoffUtc)
            .OrderBy(item => item.ObservedUtc)
            .ToList();
        if (floorAnchor != null)
        {
            _document.AdmissionFloors.Insert(0, floorAnchor);
        }

        DateTime? workTruncatedThrough = TrimOldest(
            _document.WorkProofs,
            MaxWorkProofs,
            item => item.ReceivedUtc);
        DateTime? floorTruncatedThrough = TrimOldest(
            _document.AdmissionFloors,
            MaxFloorObservations,
            item => item.ObservedUtc);
        _ = TrimOldest(_document.Pulses, MaxPulseObservations, item => item.ReceivedUtc);
        DateTime? completenessCutoff = new[] { workTruncatedThrough, floorTruncatedThrough }
            .Where(item => item.HasValue)
            .Select(item => item!.Value)
            .DefaultIfEmpty()
            .Max();
        if (completenessCutoff != default &&
            (!_document.WorkDataTruncatedThroughUtc.HasValue ||
             completenessCutoff > _document.WorkDataTruncatedThroughUtc.Value))
        {
            _document.WorkDataTruncatedThroughUtc = completenessCutoff;
        }
        _workShareIds = _document.WorkProofs
            .Select(item => item.ShareId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _pulseShareIds = _document.Pulses
            .Select(item => item.ShareId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static DateTime? TrimOldest<T>(List<T> items, int maximum, Func<T, DateTime> timestamp)
    {
        int overflow = items.Count - maximum;
        if (overflow <= 0)
        {
            return null;
        }

        List<T> ordered = items.OrderBy(timestamp).ToList();
        DateTime truncatedThrough = ordered.Take(overflow).Max(timestamp);
        items.Clear();
        items.AddRange(ordered.Skip(overflow));
        return truncatedThrough;
    }

    private void SaveIfDirty()
    {
        DashboardTelemetryDocument snapshot;
        lock (_sync)
        {
            if (!_dirty)
            {
                return;
            }

            snapshot = new DashboardTelemetryDocument
            {
                SchemaVersion = _document.SchemaVersion,
                TrackingStartedUtc = _document.TrackingStartedUtc,
                WorkDataTruncatedThroughUtc = _document.WorkDataTruncatedThroughUtc,
                WorkProofs = _document.WorkProofs.ToList(),
                AdmissionFloors = _document.AdmissionFloors.ToList(),
                Pulses = _document.Pulses.ToList()
            };
            _dirty = false;
        }

        try
        {
            BootPortalPaths.EnsureParentDirectory(_path);
            string temporaryPath = $"{_path}.tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(snapshot, _jsonOptions));
            File.Move(temporaryPath, _path, overwrite: true);
            TryRestrictPermissions(_path);
        }
        catch (Exception ex)
        {
            lock (_sync)
            {
                _dirty = true;
            }
            _logger.LogWarning(ex, "Failed to persist dashboard telemetry to {Path}.", _path);
        }
    }

    private static void TryRestrictPermissions(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch
        {
        }
    }

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

    private static string FormatHashrate(double? hashrateThs)
    {
        if (!hashrateThs.HasValue || !double.IsFinite(hashrateThs.Value))
        {
            return "--";
        }

        double hashesPerSecond = hashrateThs.Value * 1_000_000_000_000d;
        string[] units = ["H/s", "kH/s", "MH/s", "GH/s", "TH/s", "PH/s", "EH/s"];
        int unit = 0;
        while (hashesPerSecond >= 1000d && unit < units.Length - 1)
        {
            hashesPerSecond /= 1000d;
            unit++;
        }

        return $"{hashesPerSecond:0.##} {units[unit]}";
    }
}
