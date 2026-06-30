namespace boot_portal.Utils;

public static class BootPeerSource
{
    public static bool TryParsePeerSource(string? source, out string transport, out string endpoint)
    {
        return TryParsePeerSource(source, out transport, out endpoint, out _);
    }

    public static bool TryParsePeerSource(string? source, out string transport, out string endpoint, out string nodeId)
    {
        transport = string.Empty;
        endpoint = string.Empty;
        nodeId = string.Empty;
        string value = source?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.StartsWith("peer-udp:", StringComparison.OrdinalIgnoreCase))
        {
            transport = "udp";
            endpoint = value["peer-udp:".Length..].Trim();
            return true;
        }

        if (value.StartsWith("peer-session-node:", StringComparison.OrdinalIgnoreCase))
        {
            transport = "websocket";
            nodeId = value["peer-session-node:".Length..].Trim();
            return true;
        }

        if (value.StartsWith("peer-session:", StringComparison.OrdinalIgnoreCase))
        {
            transport = "websocket";
            endpoint = value["peer-session:".Length..].Trim();
            return true;
        }

        if (value.StartsWith("peer-http:", StringComparison.OrdinalIgnoreCase))
        {
            transport = "http-json";
            endpoint = value["peer-http:".Length..].Trim();
            return true;
        }

        if (value.StartsWith("peer:", StringComparison.OrdinalIgnoreCase))
        {
            transport = "http-json";
            endpoint = value["peer:".Length..].Trim();
            return true;
        }

        if (string.Equals(value, "peer-udp", StringComparison.OrdinalIgnoreCase))
        {
            transport = "udp";
            return true;
        }

        if (string.Equals(value, "peer-session", StringComparison.OrdinalIgnoreCase))
        {
            transport = "websocket";
            return true;
        }

        if (string.Equals(value, "peer-http", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "peer", StringComparison.OrdinalIgnoreCase))
        {
            transport = "http-json";
            return true;
        }

        return false;
    }
}
