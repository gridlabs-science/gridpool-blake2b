using System.Net;

namespace boot_portal.Utils;

public static class BootRequestIdentity
{
    public static string GetClientKey(HttpContext context)
    {
        string? forwardedIp = GetForwardedClientIp(context.Request.Headers);
        if (!string.IsNullOrWhiteSpace(forwardedIp))
        {
            return forwardedIp;
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
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
}
