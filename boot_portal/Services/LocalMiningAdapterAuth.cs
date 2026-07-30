using System.Security.Cryptography;
using boot_portal.Models;
using boot_portal.Utils;

namespace boot_portal.Services;

public sealed class LocalMiningAdapterAuth
{
    public const string HeaderName = "X-GridPool-Adapter-Token";
    private readonly byte[] _tokenBytes;

    public LocalMiningAdapterAuth(PoolConfig config, ILogger<LocalMiningAdapterAuth> logger)
    {
        string configuredPath = config.LocalAdapterTokenFile.Trim();
        string relativePath = configuredPath.Replace('\\', Path.DirectorySeparatorChar);
        string dataPrefix = $"data{Path.DirectorySeparatorChar}";
        if (relativePath.StartsWith(dataPrefix, StringComparison.OrdinalIgnoreCase))
        {
            relativePath = relativePath[dataPrefix.Length..];
        }

        string path = Path.IsPathRooted(configuredPath)
            ? Path.GetFullPath(configuredPath)
            : Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(BootPortalPaths.PoolStateFilePath) ?? string.Empty,
                relativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        if (!File.Exists(path))
        {
            string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            File.WriteAllText(path, token + Environment.NewLine);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            logger.LogInformation("Generated local mining adapter token at {Path}", path);
        }

        string configuredToken = File.ReadAllText(path).Trim();
        if (configuredToken.Length < 32)
        {
            throw new InvalidOperationException("Local mining adapter token must contain at least 32 characters.");
        }

        _tokenBytes = System.Text.Encoding.UTF8.GetBytes(configuredToken);
    }

    public bool IsAuthorized(string? suppliedToken)
    {
        if (string.IsNullOrWhiteSpace(suppliedToken))
        {
            return false;
        }

        byte[] suppliedBytes = System.Text.Encoding.UTF8.GetBytes(suppliedToken.Trim());
        return suppliedBytes.Length == _tokenBytes.Length &&
               CryptographicOperations.FixedTimeEquals(suppliedBytes, _tokenBytes);
    }
}
