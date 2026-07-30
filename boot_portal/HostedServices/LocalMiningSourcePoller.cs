using System.Text.Json;
using System.Text.RegularExpressions;
using boot_portal.Models;
using boot_portal.Services;

namespace boot_portal.HostedServices;

public sealed partial class LocalMiningSourcePoller : BackgroundService
{
    private readonly PoolConfig _config;
    private readonly BootProtocolStateService _stateService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<LocalMiningSourcePoller> _logger;

    public LocalMiningSourcePoller(
        PoolConfig config,
        BootProtocolStateService stateService,
        IHttpClientFactory httpClientFactory,
        ILogger<LocalMiningSourcePoller> logger)
    {
        _config = config;
        _stateService = stateService;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(
            Math.Clamp(_config.LocalMiningApiPollSeconds, 5, 300)));

        do
        {
            await PollDatumAsync(stoppingToken);
            await PollSv2Async(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task PollDatumAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_config.LocalDatumApiUrl))
        {
            return;
        }

        try
        {
            string html = await _httpClientFactory.CreateClient()
                .GetStringAsync(_config.LocalDatumApiUrl, cancellationToken);
            string text = HtmlTagRegex().Replace(html, " ");
            Match match = DatumHashrateRegex().Match(text);
            if (!match.Success ||
                !double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double value))
            {
                throw new InvalidOperationException("DATUM API did not expose an estimated hashrate.");
            }

            double ths = match.Groups[2].Value.ToLowerInvariant() switch
            {
                "ph/sec" => value * 1_000d,
                "th/sec" => value,
                "gh/sec" => value / 1_000d,
                "mh/sec" => value / 1_000_000d,
                _ => throw new InvalidOperationException("DATUM API returned an unknown hashrate unit.")
            };
            _stateService.RecordLocalMiningSourceGauge("datum", ths, ths > 0 ? 1 : 0, DateTime.UtcNow);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Unable to poll local DATUM hashrate API");
        }
    }

    private async Task PollSv2Async(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_config.LocalSv2ApiUrl))
        {
            return;
        }

        try
        {
            await using Stream stream = await _httpClientFactory.CreateClient()
                .GetStreamAsync(_config.LocalSv2ApiUrl, cancellationToken);
            using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            JsonElement clients = document.RootElement.GetProperty("sv2_clients");
            int count = clients.GetProperty("total_clients").GetInt32();
            double hps = clients.GetProperty("total_hashrate").GetDouble();
            _stateService.RecordLocalMiningSourceGauge("sv2", hps / 1_000_000_000_000d, count, DateTime.UtcNow);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Unable to poll local Stratum V2 hashrate API");
        }
    }

    [GeneratedRegex("<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(
        @"Estimated\s+Hashrate:\s*([0-9]+(?:\.[0-9]+)?)\s*([PTGM]h/sec)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DatumHashrateRegex();
}
