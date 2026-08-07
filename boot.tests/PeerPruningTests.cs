using System.Text.Json;
using boot_portal;
using boot_portal.Models;
using boot_portal.Services;
using boot_portal.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;

namespace boot.tests;

[TestClass]
[DoNotParallelize]
public sealed class PeerPruningTests
{
    private const string SelfEndpoint = "http://127.0.0.1:5000";
    private string? _previousStatePath;
    private string? _previousHistoryPath;
    private string? _tempDirectory;

    [TestInitialize]
    public void Setup()
    {
        _previousStatePath = Environment.GetEnvironmentVariable("BOOT_PORTAL_STATE_PATH");
        _previousHistoryPath = Environment.GetEnvironmentVariable("BOOT_PORTAL_HISTORY_PATH");
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"boot-peer-prune-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
        Environment.SetEnvironmentVariable("BOOT_PORTAL_STATE_PATH", Path.Combine(_tempDirectory, "pool_state.json"));
        Environment.SetEnvironmentVariable("BOOT_PORTAL_HISTORY_PATH", Path.Combine(_tempDirectory, "pool_state.history.json"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable("BOOT_PORTAL_STATE_PATH", _previousStatePath);
        Environment.SetEnvironmentVariable("BOOT_PORTAL_HISTORY_PATH", _previousHistoryPath);
        if (!string.IsNullOrWhiteSpace(_tempDirectory) && Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void PruneStalePeersUsesLastSuccessfulSeenTimeInsteadOfRefreshingFailureTime()
    {
        var service = CreateService();
        DateTime now = DateTime.UtcNow;

        service.UpdatePeerHeartbeat("http://stale.example:5000", "relayed", 12, now.AddHours(-2));
        service.MarkPeerFailure("http://stale.example:5000", "relay-timeout");
        service.MarkPeerFailure("http://stale.example:5000", "relay-timeout");
        service.MarkPeerFailure("http://stale.example:5000", "relay-timeout");

        int removed = service.PruneStalePeers(
            now,
            TimeSpan.FromHours(1),
            minimumFailureCount: 3,
            protectedEndpoints: []);

        Assert.AreEqual(1, removed);
        Assert.IsFalse(service.GetNetworkStatus().Peers.Any(peer => peer.Endpoint == "http://stale.example:5000"));

        service.MergeDiscoveredPeers(["http://stale.example:5000"]);
        Assert.IsFalse(service.GetNetworkStatus().Peers.Any(peer => peer.Endpoint == "http://stale.example:5000"));
    }

    [TestMethod]
    public void PruneStalePeersKeepsProtectedBootstrapPeers()
    {
        var service = CreateService();
        DateTime now = DateTime.UtcNow;

        service.UpdatePeerHeartbeat("http://seed.example:5000", "relayed", 12, now.AddHours(-2));
        service.MarkPeerFailure("http://seed.example:5000", "relay-timeout");
        service.MarkPeerFailure("http://seed.example:5000", "relay-timeout");
        service.MarkPeerFailure("http://seed.example:5000", "relay-timeout");

        int removed = service.PruneStalePeers(
            now,
            TimeSpan.FromHours(1),
            minimumFailureCount: 3,
            protectedEndpoints: ["http://seed.example:5000"]);

        Assert.AreEqual(0, removed);
        Assert.IsTrue(service.GetNetworkStatus().Peers.Any(peer => peer.Endpoint == "http://seed.example:5000"));
    }

    [TestMethod]
    public void TombstonePeerRemovesPeerButNotSelf()
    {
        var service = CreateService();
        service.AnnouncePeer("http://old.example:5000");

        Assert.IsTrue(service.TombstonePeer("http://old.example:5000"));
        Assert.IsFalse(service.GetNetworkStatus().Peers.Any(peer => peer.Endpoint == "http://old.example:5000"));
        Assert.IsFalse(service.TombstonePeer(SelfEndpoint));
    }

    [TestMethod]
    public void EndpointlessPeerSessionIsVisibleButNotAdvertised()
    {
        var service = CreateService();
        const string nodeId = "9d55ff8d7dce6a8be4116ef1db98434c775ee4f2c9f047fd0376b65e7a0b33fb";
        DateTime now = DateTime.UtcNow;

        service.UpdatePeerSessionHeartbeat(string.Empty, nodeId, "session-connected", now);

        BootPeerStatus peer = service.GetNetworkStatus().Peers.Single(candidate => candidate.NodeId == nodeId);
        Assert.AreEqual(string.Empty, peer.Endpoint);
        Assert.AreEqual("outbound-only", peer.ConnectionMode);
        Assert.IsTrue(peer.SessionConnected);
        CollectionAssert.Contains(peer.Capabilities, "share-relay");
        Assert.IsFalse(service.GetPeerAddressBook().Peers.Any(candidate => candidate.Endpoint == string.Empty || candidate.Status == "session-connected"));

        service.UpdatePeerSessionClosed(string.Empty, nodeId, "session-closed", now.AddSeconds(5));

        peer = service.GetNetworkStatus().Peers.Single(candidate => candidate.NodeId == nodeId);
        Assert.AreEqual("session-closed", peer.Status);
        Assert.IsFalse(peer.SessionConnected);
    }

    [TestMethod]
    public void ResolvePeerEndpointKeepsExplicitDialedPortWhenAdvertisedEndpointOmitsPort()
    {
        var service = CreateService();

        string endpoint = service.ResolvePeerEndpoint(
            "http://evomining.farted.net:5000",
            "http://evomining.farted.net");

        Assert.AreEqual("http://evomining.farted.net:5000", endpoint);
    }

    [TestMethod]
    public void ResolvePeerEndpointStillUsesDifferentAdvertisedEndpoint()
    {
        var service = CreateService();

        string endpoint = service.ResolvePeerEndpoint(
            "http://bootstrap.gridpool.net:5000",
            "http://dallas.gridpool.net");

        Assert.AreEqual("http://dallas.gridpool.net", endpoint);
    }

    [TestMethod]
    public void PeerAliasesCollapseByAuthenticatedNodeIdentity()
    {
        var service = CreateService();
        const string nodeId = "Crm97Gm/m/Wvvl2s7rEeYXyzjScSNwcTZFXEqlOZcq4=";

        service.SeedPeers(["https://dallas.gridpool.net"]);
        service.MergeDiscoveredPeers([
            "http://dallas.gridpool.net",
            "http://dallas.gridpool.net:5000"
        ]);
        service.ReconcilePeerIdentity("https://dallas.gridpool.net", "https://dallas.gridpool.net", nodeId);
        service.ReconcilePeerIdentity("http://dallas.gridpool.net", "http://dallas.gridpool.net", nodeId);
        service.ReconcilePeerIdentity("http://dallas.gridpool.net:5000", "http://dallas.gridpool.net:5000", nodeId);

        List<BootPeerStatus> peers = service.GetNetworkStatus().Peers.Where(peer => peer.NodeId == nodeId).ToList();
        Assert.AreEqual(1, peers.Count);
        Assert.IsTrue(peers[0].IsConfiguredSeed);
    }

    [TestMethod]
    public void PeerAliasesCollapseByPublicHostnameBeforeIdentityIsKnown()
    {
        var service = CreateService();

        service.SeedPeers(["https://alias.gridpool.example"]);
        service.MergeDiscoveredPeers([
            "http://alias.gridpool.example",
            "http://alias.gridpool.example:5000"
        ]);

        List<BootPeerStatus> peers = service.GetNetworkStatus().Peers
            .Where(peer => peer.Endpoint.Contains("alias.gridpool.example", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.AreEqual(1, peers.Count);
        Assert.IsTrue(peers[0].IsConfiguredSeed);
    }

    [TestMethod]
    public void SelfAliasOnDifferentSchemeOrPortIsNotRetained()
    {
        var service = CreateService();

        service.AnnouncePeer("https://127.0.0.1:7443");

        Assert.IsFalse(service.GetNetworkStatus().Peers.Any(peer =>
            peer.Endpoint.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void StaleEndpointlessSessionDoesNotSurviveNormalization()
    {
        BootProtocolStateService service = CreateService(out PoolConfig config, null);
        config.PeerSessionIdleTimeoutSeconds = 30;
        const string nodeId = "stale-outbound-only-node";

        service.UpdatePeerSessionHeartbeat(string.Empty, nodeId, "session-connected", DateTime.UtcNow.AddMinutes(-5));

        Assert.IsFalse(service.GetNetworkStatus().Peers.Any(peer => peer.NodeId == nodeId));
    }

    [TestMethod]
    public void ReadinessFailsWhenEnabledPeerPollLoopIsStale()
    {
        var health = new BootPeerLoopHealth(DateTime.UtcNow.AddMinutes(-2));
        BootProtocolStateService service = CreateService(out PoolConfig config, health);

        config.PeerLoopStaleSeconds = 30;
        var result = (ObjectResult)new HealthController(
            config,
            service,
            new NodeSetupState(operationalAtStartup: true)).Ready();
        Assert.AreEqual(503, result.StatusCode);
        Assert.IsFalse(service.GetNetworkStatus().PeerLoopsHealthy);
    }

    [TestMethod]
    public void OutboundRelayStalenessWarnsWithoutRefusingDatumCoinbaserWork()
    {
        var health = new BootPeerLoopHealth(DateTime.UtcNow.AddMinutes(-2));
        BootProtocolStateService service = CreateService(out PoolConfig config, health);

        config.OutboundRelayStaleSeconds = 30;
        config.PauseMiningOnOutboundRelayStale = true; // Legacy deployed configuration.
        service.RecordDatumSessionOpened("datum-test", "127.0.0.1:12345");

        DatumCoinbaseTemplate template = service.GetDatumCoinbaseTemplate();
        BootNetworkStatusDto status = service.GetNetworkStatus();

        Assert.IsTrue(template.CoinbaseOutputs.Count > 0);
        Assert.IsTrue(status.MiningWorkSafe);
        Assert.IsFalse(status.OutboundRelayHealthy);
        CollectionAssert.Contains(status.ConfigWarnings, "outbound share/pulse relay is stale");
    }

    [TestMethod]
    public void NetworkSummaryExposesDatumLifecycleDiagnostics()
    {
        var health = new BootPeerLoopHealth();
        BootProtocolStateService service = CreateService(out PoolConfig config, health);
        config.DatumPublicHost = "datum.test.gridpool.net";
        config.DatumPublicPort = 3009;
        config.DatumPort = 3008;
        DateTime shareUtc = DateTime.UtcNow.AddSeconds(-2);
        DateTime closeUtc = DateTime.UtcNow.AddSeconds(-1);

        service.RecordDatumSessionOpened("datum-test", "127.0.0.1:12345");
        service.RecordDatumSessionHello("datum-test", "identity", "encryption", shareUtc);
        service.RecordDatumSessionCoinbaserFetch("datum-test", shareUtc);
        service.RecordDatumSessionShareOutcome("datum-test", accepted: true, affectedOnDeck: false, shareUtc);
        service.RecordSuccessfulDatumCoinbaserResponse(shareUtc);
        service.CompleteDatumSession("datum-test", "server-closed", "test close", timestampUtc: closeUtc);

        BootNetworkStatusDto status = service.GetNetworkStatus();
        Assert.AreEqual(0, status.ActiveDatumSessionCount);
        Assert.IsNotNull(status.LastDatumSessionOpenedUtc);
        Assert.AreEqual(shareUtc, status.LastDatumHelloReceivedUtc);
        Assert.AreEqual(shareUtc, status.LastDatumCoinbaserRequestUtc);
        Assert.AreEqual(shareUtc, status.LastValidLocalDatumShareUtc);
        Assert.AreEqual(shareUtc, status.LastSuccessfulDatumCoinbaserResponseUtc);
        Assert.AreEqual(closeUtc, status.LastDatumSessionClosedUtc);
        Assert.AreEqual("test close", status.LastDatumSessionCloseReason);
        Assert.AreEqual("datum.test.gridpool.net", status.DatumPublicHost);
        Assert.AreEqual(3009, status.DatumPublicPort);
        Assert.AreEqual(3008, status.DatumListenPort);
    }

    private static BootProtocolStateService CreateService() => CreateService(out _, null);

    private static BootProtocolStateService CreateService(out PoolConfig config, BootPeerLoopHealth? health)
    {
        config = new PoolConfig
        {
            PublicBaseUrl = SelfEndpoint,
            BootstrapPeers = [],
            EnableAdminApi = true,
            AdminApiKey = "test-admin-key",
            PoolPayoutScript = "bc1qrwsx8fs0l6z7ugp5cvzy6lhss7jlyru3kg9s8y"
        };

        var seedState = new PoolState
        {
            Metadata = new BootProtocolMetadata
            {
                NetworkId = config.BootNetworkId,
                ProtocolVersion = config.BootProtocolVersion
            },
            CurrentStateId = "seed-current",
            CandidateStateId = "seed-candidate",
            CurrentRoundNumber = 0,
            WinnersList =
            [
                new PayoutInfo
                {
                    Address = config.PoolPayoutScript,
                    Value = Program.BLOCK_REWARD / 2
                }
            ],
            OnDeckList = [],
            OnDeckProofs = []
        };
        File.WriteAllText(
            Environment.GetEnvironmentVariable("BOOT_PORTAL_STATE_PATH")!,
            JsonSerializer.Serialize(seedState));

        return new BootProtocolStateService(
            config,
            new BootShareVerifier(),
            new NoOpHubContext(),
            NullLogger<BootProtocolStateService>.Instance,
            health);
    }

    private sealed class NoOpHubContext : IHubContext<PoolStatsHub>
    {
        public IHubClients Clients { get; } = new NoOpHubClients();
        public IGroupManager Groups { get; } = new NoOpGroupManager();
    }

    private sealed class NoOpHubClients : IHubClients
    {
        private readonly IClientProxy _proxy = new NoOpClientProxy();
        public IClientProxy All => _proxy;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => _proxy;
        public IClientProxy Client(string connectionId) => _proxy;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => _proxy;
        public IClientProxy Group(string groupName) => _proxy;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => _proxy;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => _proxy;
        public IClientProxy User(string userId) => _proxy;
        public IClientProxy Users(IReadOnlyList<string> userIds) => _proxy;
    }

    private sealed class NoOpClientProxy : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoOpGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
