using boot_portal.Models;
using boot_portal.Services;
using boot_portal.Utils;

namespace boot.tests;

[TestClass]
public sealed class BootPrivacyTests
{
    [DataTestMethod]
    [DataRow("https://dallas.gridpool.net:5000", "Dallas")]
    [DataRow("https://evomining.farted.net:5000", "evomining.farted.net")]
    [DataRow("https://private.example.net:5000", null)]
    public void DiagramOnlyNamesExplicitlyAllowlistedPublicPeers(string endpoint, string? expected)
    {
        Assert.AreEqual(expected, DashboardReadModelService.PublicPeerDisplayName(endpoint));
    }

    [TestMethod]
    public void PublicSummaryRedactsPrivateEndpointsMinerIdentitiesAndOperatorDiagnostics()
    {
        var status = new BootNetworkStatusDto
        {
            NodeId = "public-node-id",
            SelfEndpoint = "http://192.168.1.10:5000",
            DatumPublicHost = "192.168.1.10",
            DatumListenPort = 3008,
            PeerUdpPublicHost = "192.168.1.10",
            PeerCount = 2,
            WorkSetCount = 897,
            LocalMiningHashrateThs = 1.5,
            MiningWorkSafe = false,
            MiningWorkSafetyReason = "RPC failed at http://bitcoin.internal:8332",
            LastDatumSessionCloseReason = "client 192.168.1.20 disconnected",
            LastGridPoolBlockMinerAddress = "bc1qprivate",
            LocalDatumDiagnostics = new BootDatumDiagnosticsDto
            {
                LastRejectionReason = "miner bc1qprivate rejected",
                RejectionReasons = [new BootReasonCountDto { Reason = "private detail", Count = 1 }]
            },
            LocalDatumMiners =
            [
                new BootLocalDatumMinerSummaryDto { Address = "bc1qprivate", Username = "worker" }
            ],
            LocalMiningSources =
            [
                new BootLocalMiningSourceSummaryDto { Source = "sv2", ActiveMinerCount = 1 }
            ],
            Peers =
            [
                new BootPeerStatus { Endpoint = "http://192.168.1.20:5000", NodeId = "peer-node-id" }
            ],
            BitcoinNotification = new BootBitcoinNotificationDto
            {
                DegradedReason = "RPC failed at http://bitcoin.internal:8332",
                Rpc = new BootBitcoinRpcHealthDto { LastError = "RPC failed at http://127.0.0.1:8332" },
                Network = new BootBitcoinNetworkHealthDto { LastError = "peer RPC failed at http://127.0.0.1:8332" },
                ZmqTopics =
                [
                    new BootBitcoinZmqTopicHealthDto
                    {
                        Topic = "hashblock",
                        EndpointLabel = "tcp://127.0.0.1:28332",
                        PublisherEndpointLabels = ["tcp://127.0.0.1:28332"]
                    }
                ]
            }
        };

        BootNetworkStatusDto redacted = BootPrivacy.RedactPublicNetworkStatus(status);

        Assert.AreEqual("public-node-id", redacted.NodeId);
        Assert.AreEqual(2, redacted.PeerCount);
        Assert.AreEqual(897, redacted.WorkSetCount);
        Assert.AreEqual(1.5, redacted.LocalMiningHashrateThs);
        Assert.AreEqual(string.Empty, redacted.SelfEndpoint);
        Assert.AreEqual(string.Empty, redacted.DatumPublicHost);
        Assert.AreEqual(0, redacted.DatumListenPort);
        Assert.AreEqual(string.Empty, redacted.PeerUdpPublicHost);
        Assert.AreEqual(string.Empty, redacted.LastDatumSessionCloseReason);
        Assert.IsNull(redacted.LastGridPoolBlockMinerAddress);
        Assert.AreEqual("Local mining work is currently unsafe.", redacted.MiningWorkSafetyReason);
        Assert.AreEqual(string.Empty, redacted.LocalDatumDiagnostics.LastRejectionReason);
        Assert.AreEqual(0, redacted.LocalDatumDiagnostics.RejectionReasons.Count);
        Assert.AreEqual(0, redacted.LocalDatumMiners.Count);
        Assert.AreEqual(0, redacted.LocalMiningSources.Count);
        Assert.AreEqual(0, redacted.Peers.Count);
        Assert.AreEqual(string.Empty, redacted.BitcoinNotification.Rpc.LastError);
        Assert.AreEqual(string.Empty, redacted.BitcoinNotification.Network.LastError);
        Assert.AreEqual("Bitcoin notification source is degraded.", redacted.BitcoinNotification.DegradedReason);
        Assert.AreEqual(string.Empty, redacted.BitcoinNotification.ZmqTopics[0].EndpointLabel);
        Assert.AreEqual(0, redacted.BitcoinNotification.ZmqTopics[0].PublisherEndpointLabels.Count);
    }

    [TestMethod]
    public void PublicSummaryKeepsIntentionallyAdvertisedDnsNames()
    {
        var status = new BootNetworkStatusDto
        {
            SelfEndpoint = "https://main.gridpool.net",
            DatumPublicHost = "datum.main.gridpool.net",
            PeerUdpPublicHost = "udp.main.gridpool.net"
        };

        BootNetworkStatusDto redacted = BootPrivacy.RedactPublicNetworkStatus(status);

        Assert.AreEqual("https://main.gridpool.net", redacted.SelfEndpoint);
        Assert.AreEqual("datum.main.gridpool.net", redacted.DatumPublicHost);
        Assert.AreEqual("udp.main.gridpool.net", redacted.PeerUdpPublicHost);
    }

    [DataTestMethod]
    [DataRow("192.168.1.10", "")]
    [DataRow("localhost", "")]
    [DataRow("gridpool-node.local", "")]
    [DataRow("datum.main.gridpool.net", "datum.main.gridpool.net")]
    public void PublicDnsHostFilterDropsPrivateOrLocalNames(string host, string expected)
    {
        Assert.AreEqual(expected, BootPrivacy.KeepPublicDnsHost(host));
    }

    [DataTestMethod]
    [DataRow("http://192.168.1.10:5000", "private-endpoint")]
    [DataRow("203.0.113.10:5001", "private-endpoint")]
    [DataRow("", "outbound-only-peer")]
    [DataRow("https://main.gridpool.net", "main.gridpool.net")]
    public void LogEndpointDescriptionNeverPrintsIpLiterals(string endpoint, string expected)
    {
        Assert.AreEqual(expected, BootPrivacy.DescribeEndpointForLog(endpoint));
    }
}
