namespace boot_portal.Utils;

public static class BootPortalPaths
{
    public static string ConfigFilePath =>
        ResolvePath(Environment.GetEnvironmentVariable("BOOT_PORTAL_CONFIG_PATH"), "boot_portal_config.json");

    public static string LocalConfigFilePath =>
        ResolvePath(
            Environment.GetEnvironmentVariable("BOOT_PORTAL_LOCAL_CONFIG_PATH"),
            BuildLocalConfigFallbackPath(ConfigFilePath));

    public static string PoolStateFilePath =>
        ResolvePath(Environment.GetEnvironmentVariable("BOOT_PORTAL_STATE_PATH"), "pool_state.json");

    public static string PoolStateHistoryFilePath =>
        ResolvePath(
            Environment.GetEnvironmentVariable("BOOT_PORTAL_HISTORY_PATH"),
            BuildHistoryFallbackPath(PoolStateFilePath));

    public static string DashboardTelemetryFilePath =>
        ResolvePath(
            Environment.GetEnvironmentVariable("BOOT_PORTAL_DASHBOARD_TELEMETRY_PATH"),
            BuildDashboardTelemetryFallbackPath(PoolStateFilePath));

    public static void EnsureParentDirectory(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static string ResolvePath(string? candidate, string fallbackFileName)
    {
        string value = string.IsNullOrWhiteSpace(candidate) ? fallbackFileName : candidate.Trim();
        return Path.GetFullPath(value);
    }

    private static string BuildHistoryFallbackPath(string coreStatePath)
    {
        string directory = Path.GetDirectoryName(coreStatePath) ?? string.Empty;
        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(coreStatePath);
        string extension = Path.GetExtension(coreStatePath);
        string historyFileName = string.IsNullOrWhiteSpace(extension)
            ? $"{Path.GetFileName(coreStatePath)}.history.json"
            : $"{fileNameWithoutExtension}.history{extension}";

        return string.IsNullOrWhiteSpace(directory)
            ? historyFileName
            : Path.Combine(directory, historyFileName);
    }

    private static string BuildLocalConfigFallbackPath(string baseConfigPath)
    {
        string directory = Path.GetDirectoryName(baseConfigPath) ?? string.Empty;
        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(baseConfigPath);
        string extension = Path.GetExtension(baseConfigPath);
        string localConfigFileName = string.IsNullOrWhiteSpace(extension)
            ? $"{Path.GetFileName(baseConfigPath)}.local"
            : $"{fileNameWithoutExtension}.local{extension}";

        return string.IsNullOrWhiteSpace(directory)
            ? localConfigFileName
            : Path.Combine(directory, localConfigFileName);
    }

    private static string BuildDashboardTelemetryFallbackPath(string coreStatePath)
    {
        string directory = Path.GetDirectoryName(coreStatePath) ?? string.Empty;
        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(coreStatePath);
        string fileName = $"{fileNameWithoutExtension}.dashboard-telemetry.json";
        return string.IsNullOrWhiteSpace(directory)
            ? fileName
            : Path.Combine(directory, fileName);
    }
}
