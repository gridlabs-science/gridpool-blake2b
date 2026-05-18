using System.Text.Json;
using boot_portal;
using boot_portal.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;

namespace boot.tests;

[TestClass]
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

    private static BootProtocolStateService CreateService()
    {
        var config = new PoolConfig
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
            NullLogger<BootProtocolStateService>.Instance);
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
