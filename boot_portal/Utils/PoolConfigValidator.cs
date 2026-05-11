using System.Text;

namespace boot_portal.Utils;

public static class PoolConfigValidator
{
    public const int MaxDatumCoinbaseTagBytes = 255;
    private static readonly HashSet<string> ValidNodeModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "development",
        "developer-preview",
        "staging",
        "production"
    };

    public static void ValidateOrThrow(PoolConfig config)
    {
        List<string> errors = Validate(config);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException("Invalid Boot pool config: " + string.Join("; ", errors));
        }
    }

    public static List<string> Validate(PoolConfig config)
    {
        var errors = new List<string>();

        if (!ValidNodeModes.Contains(config.NodeMode))
        {
            errors.Add("node_mode must be one of development, developer-preview, staging, or production");
        }

        if (!BitcoinScript.TryAddressToScriptPubKey(config.PoolPayoutScript, out _))
        {
            errors.Add("pool_payout_script must be a supported Bitcoin mainnet payout address");
        }

        int coinbaseTagBytes = Encoding.UTF8.GetByteCount(config.CoinbaseTag ?? string.Empty);
        if (coinbaseTagBytes > MaxDatumCoinbaseTagBytes)
        {
            errors.Add($"coinbase_tag is {coinbaseTagBytes} UTF-8 bytes, maximum is {MaxDatumCoinbaseTagBytes}");
        }

        if (config.WinnersListSize <= 0)
        {
            errors.Add("winners_list_size must be greater than 0");
        }

        if (config.WinnersListSize > 10_000)
        {
            errors.Add("winners_list_size is unreasonably large; expected 10000 or less");
        }

        if (config.MinDiff == 0)
        {
            errors.Add("min_diff must be greater than 0");
        }

        ValidateRequiredPort(errors, config.DatumPort, "Datum_Port");
        ValidateNonNegativePort(errors, config.WebUiPortHttp, "WebUI_Port_http");
        ValidateNonNegativePort(errors, config.WebUiPortHttps, "WebUI_Port_https");
        if (config.WebUiPortHttp == 0 && config.WebUiPortHttps == 0)
        {
            errors.Add("at least one WebUI port must be greater than 0");
        }

        ValidatePositive(errors, config.PeerSyncIntervalSeconds, "peer_sync_interval_seconds");
        ValidatePositive(errors, config.PeerRequestTimeoutSeconds, "peer_request_timeout_seconds");
        ValidatePositive(errors, config.MaxPeers, "max_peers");
        ValidatePositive(errors, config.NetworkReadRateLimitPerMinute, "network_read_rate_limit_per_minute");
        ValidatePositive(errors, config.PeerWriteRateLimitPerMinute, "peer_write_rate_limit_per_minute");
        ValidatePositive(errors, config.MiningApiShareRateLimitPerMinute, "mining_api_share_rate_limit_per_minute");
        ValidatePositive(errors, config.AdminRateLimitPerMinute, "admin_rate_limit_per_minute");
        ValidatePositive(errors, config.MaxShareRequestBytes, "max_share_request_bytes");
        ValidatePositive(errors, config.MaxCoinbaseHexChars, "max_coinbase_hex_chars");
        ValidatePositive(errors, config.MaxMerklePathEntries, "max_merkle_path_entries");
        ValidatePositive(errors, config.HashrateSampleIntervalSeconds, "hashrate_sample_interval_seconds");
        ValidatePositive(errors, config.HashrateLocalWindowSeconds, "hashrate_local_window_seconds");
        ValidatePositive(errors, config.LocalDatumMinerSummaryLimit, "local_datum_miner_summary_limit");
        ValidatePositive(errors, config.LocalDatumHashratePerAddressMaxSamples, "local_datum_hashrate_per_address_max_samples");
        ValidatePositive(errors, config.LocalDatumHashrateMaxAddresses, "local_datum_hashrate_max_addresses");
        ValidatePositive(errors, config.MaxAcceptedShareTelemetryEntries, "max_accepted_share_telemetry_entries");
        ValidatePositive(errors, config.HashrateSampleRetentionDays, "hashrate_sample_retention_days");
        ValidatePositive(errors, config.AcceptedShareTelemetryRetentionHours, "accepted_share_telemetry_retention_hours");
        ValidatePositive(errors, config.ShareDiagnosticRetentionHours, "share_diagnostic_retention_hours");
        ValidatePositive(errors, config.DatumShareResponseSlowMs, "datum_share_response_slow_ms");
        ValidatePositive(errors, config.DatumShareResponseAcceptedSampleEvery, "datum_share_response_accepted_sample_every");
        ValidatePositive(errors, config.StaleDatumPayoutMismatchThreshold, "stale_datum_payout_mismatch_threshold");
        ValidatePositive(errors, config.StaleDatumDisconnectMinSeconds, "stale_datum_disconnect_min_seconds");
        ValidatePositive(errors, config.StaleDatumDisconnectCooldownSeconds, "stale_datum_disconnect_cooldown_seconds");
        ValidatePositive(errors, config.StaleDatumRefreshIntervalSeconds, "stale_datum_refresh_interval_seconds");

        if (!string.Equals(config.TestingRoundResetMode, "none", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(config.TestingRoundResetMode, "block_hash_low_nibble", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("testing_round_reset_mode must be none or block_hash_low_nibble");
        }

        if (config.TestingRoundResetLowNibbleThreshold is < 0 or > 16)
        {
            errors.Add("testing_round_reset_low_nibble_threshold must be between 0 and 16");
        }

        if (IsProduction(config))
        {
            ValidateRequiredAbsoluteUrl(errors, config.PublicBaseUrl, "public_base_url");
            ValidateRequiredHost(errors, config.DatumPublicHost, "datum_public_host");

            if (!string.Equals(config.TestingRoundResetMode, "none", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("testing_round_reset_mode must be none when node_mode is production");
            }

            if (config.EnableAdminApi && !HasStrongAdminKey(config.AdminApiKey))
            {
                errors.Add("admin_api_key must be a strong non-placeholder value when enable_admin_api is true in production");
            }
        }

        return errors;
    }

    private static bool IsProduction(PoolConfig config)
    {
        return string.Equals(config.NodeMode, "production", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasStrongAdminKey(string? adminKey)
    {
        string value = adminKey?.Trim() ?? string.Empty;
        return value.Length >= 32 &&
               !string.Equals(value, "change-this-admin-key", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(value, "changeme", StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidatePositive(List<string> errors, int value, string name)
    {
        if (value <= 0)
        {
            errors.Add($"{name} must be greater than 0");
        }
    }

    private static void ValidateNonNegativePort(List<string> errors, int value, string name)
    {
        if (value is < 0 or > 65535)
        {
            errors.Add($"{name} must be between 0 and 65535");
        }
    }

    private static void ValidateRequiredPort(List<string> errors, int value, string name)
    {
        if (value is <= 0 or > 65535)
        {
            errors.Add($"{name} must be between 1 and 65535");
        }
    }

    private static void ValidateRequiredAbsoluteUrl(List<string> errors, string value, string name)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            errors.Add($"{name} must be an absolute http or https URL in production");
        }
    }

    private static void ValidateRequiredHost(List<string> errors, string value, string name)
    {
        string trimmed = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed) ||
            trimmed.Contains(' ') ||
            trimmed.Contains('/'))
        {
            errors.Add($"{name} must be a host name or IP address without a URL scheme or path in production");
        }
    }
}
