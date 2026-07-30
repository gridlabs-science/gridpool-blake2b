using System.Net;
using System.Net.Sockets;
using boot_portal.Models;

namespace boot_portal.Utils;

public static class BootRequestIdentity
{
    public static string GetClientKey(HttpContext context, PoolConfig? config = null)
    {
        string? forwardedIp = GetForwardedClientIp(context, config);
        if (!string.IsNullOrWhiteSpace(forwardedIp))
        {
            return forwardedIp;
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    public static string? GetForwardedClientIp(HttpContext context, PoolConfig? config = null)
    {
        if (!IsTrustedForwardedProxy(context.Connection.RemoteIpAddress, config))
        {
            return null;
        }

        return GetForwardedClientIp(context.Request.Headers);
    }

    public static string? GetForwardedClientIp(IHeaderDictionary headers)
    {
        string? cfConnectingIp = NormalizeIp(headers["CF-Connecting-IP"].FirstOrDefault());
        if (!string.IsNullOrWhiteSpace(cfConnectingIp))
        {
            return cfConnectingIp;
        }

        string? xForwardedFor = headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(xForwardedFor))
        {
            string firstHop = xForwardedFor.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? string.Empty;
            string? normalizedFirstHop = NormalizeIp(firstHop);
            if (!string.IsNullOrWhiteSpace(normalizedFirstHop))
            {
                return normalizedFirstHop;
            }
        }

        return NormalizeIp(headers["X-Real-IP"].FirstOrDefault());
    }

    public static bool IsTrustedForwardedProxy(IPAddress? remoteIpAddress, PoolConfig? config)
    {
        if (remoteIpAddress == null || config == null || config.TrustedForwardedProxyRanges.Count == 0)
        {
            return false;
        }

        return config.TrustedForwardedProxyRanges.Any(range => IsInRange(remoteIpAddress, range));
    }

    private static string? NormalizeIp(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        return IPAddress.TryParse(candidate.Trim(), out IPAddress? ipAddress)
            ? ipAddress.ToString()
            : null;
    }

    private static bool IsInRange(IPAddress address, string candidateRange)
    {
        if (string.IsNullOrWhiteSpace(candidateRange))
        {
            return false;
        }

        string normalizedRange = candidateRange.Trim();
        int slashIndex = normalizedRange.IndexOf('/');
        if (slashIndex < 0)
        {
            return IPAddress.TryParse(normalizedRange, out IPAddress? exact) &&
                   AddressesEqual(address, exact);
        }

        string networkPart = normalizedRange[..slashIndex];
        string prefixPart = normalizedRange[(slashIndex + 1)..];
        if (!IPAddress.TryParse(networkPart, out IPAddress? networkAddress) ||
            !int.TryParse(prefixPart, out int prefixLength))
        {
            return false;
        }

        if (networkAddress.AddressFamily != address.AddressFamily)
        {
            return false;
        }

        int addressBitLength = address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        if (prefixLength < 0 || prefixLength > addressBitLength)
        {
            return false;
        }

        byte[] addressBytes = address.GetAddressBytes();
        byte[] networkBytes = networkAddress.GetAddressBytes();
        int fullBytes = prefixLength / 8;
        int remainingBits = prefixLength % 8;

        for (int index = 0; index < fullBytes; index++)
        {
            if (addressBytes[index] != networkBytes[index])
            {
                return false;
            }
        }

        if (remainingBits == 0)
        {
            return true;
        }

        int mask = 0xFF << (8 - remainingBits);
        return (addressBytes[fullBytes] & mask) == (networkBytes[fullBytes] & mask);
    }

    private static bool AddressesEqual(IPAddress left, IPAddress right)
    {
        if (left.AddressFamily != right.AddressFamily)
        {
            return false;
        }

        return left.Equals(right);
    }
}
