using System.Net;
using boot_portal.Models;

namespace boot_portal.Utils;

public static class BootPrivacy
{
    public static BootNetworkStatusDto RedactPublicNetworkStatus(BootNetworkStatusDto status)
    {
        status.SelfEndpoint = KeepPublicDnsEndpoint(status.SelfEndpoint);
        status.DatumPublicHost = KeepPublicDnsHost(status.DatumPublicHost);
        status.DatumListenPort = 0;
        status.PeerUdpPublicHost = KeepPublicDnsHost(status.PeerUdpPublicHost);
        status.LastDatumSessionCloseReason = string.Empty;
        status.PeerLoopFaults.Clear();
        status.LastGridPoolBlockMinerAddress = null;
        status.MiningWorkSafetyReason = status.MiningWorkSafe
            ? string.Empty
            : "Local mining work is currently unsafe.";
        status.LocalDatumDiagnostics.LastRejectionReason = string.Empty;
        status.LocalDatumDiagnostics.RejectionReasons.Clear();
        status.LocalDatumMiners.Clear();
        status.LocalMiningSources.Clear();
        status.Peers.Clear();

        status.BitcoinNotification.Rpc.LastError = string.Empty;
        status.BitcoinNotification.DegradedReason =
            string.IsNullOrWhiteSpace(status.BitcoinNotification.DegradedReason)
                ? string.Empty
                : "Bitcoin notification source is degraded.";
        foreach (BootBitcoinZmqTopicHealthDto topic in status.BitcoinNotification.ZmqTopics)
        {
            topic.EndpointLabel = string.Empty;
            topic.PublisherEndpointLabels.Clear();
        }

        return status;
    }

    public static string DescribeEndpointForLog(string? endpoint)
    {
        string value = endpoint?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return "outbound-only-peer";
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
        {
            string host = KeepPublicDnsHost(uri.Host);
            if (string.IsNullOrWhiteSpace(host))
            {
                return "private-endpoint";
            }

            return uri.IsDefaultPort ? host : $"{host}:{uri.Port}";
        }

        if (Uri.TryCreate($"tcp://{value}", UriKind.Absolute, out Uri? socketUri))
        {
            string host = KeepPublicDnsHost(socketUri.Host);
            if (string.IsNullOrWhiteSpace(host))
            {
                return "private-endpoint";
            }

            return socketUri.IsDefaultPort ? host : $"{host}:{socketUri.Port}";
        }

        return KeepPublicDnsHost(value) is { Length: > 0 } hostLabel
            ? hostLabel
            : "private-endpoint";
    }

    private static string KeepPublicDnsEndpoint(string? endpoint)
    {
        if (!Uri.TryCreate(endpoint?.Trim(), UriKind.Absolute, out Uri? uri))
        {
            return string.Empty;
        }

        string host = KeepPublicDnsHost(uri.Host);
        if (string.IsNullOrWhiteSpace(host))
        {
            return string.Empty;
        }

        return new UriBuilder(uri.Scheme, host, uri.IsDefaultPort ? -1 : uri.Port)
            .Uri
            .GetLeftPart(UriPartial.Authority)
            .TrimEnd('/');
    }

    public static string KeepPublicDnsHost(string? host)
    {
        string value = host?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value) ||
            string.Equals(value, "localhost", StringComparison.OrdinalIgnoreCase) ||
            value.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            IPAddress.TryParse(value, out _))
        {
            return string.Empty;
        }

        return value;
    }
}
