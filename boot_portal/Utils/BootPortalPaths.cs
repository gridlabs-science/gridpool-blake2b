namespace boot_portal.Utils;

public static class BootPortalPaths
{
    public static string ConfigFilePath =>
        ResolvePath(Environment.GetEnvironmentVariable("BOOT_PORTAL_CONFIG_PATH"), "boot_portal_config.json");

    public static string PoolStateFilePath =>
        ResolvePath(Environment.GetEnvironmentVariable("BOOT_PORTAL_STATE_PATH"), "pool_state.json");

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
}
