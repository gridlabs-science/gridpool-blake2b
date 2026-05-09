using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using boot_portal;
using boot_portal.Controllers;
using boot_portal.Models;
using boot_portal.Services;
using boot_portal.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;

namespace boot.tests;

[TestClass]
[DoNotParallelize]
public sealed class ShareAttributionTests
{
    private const string SampleSlotZeroAddress = "bc1qce93hy5rhg02s6aeu7mfdvxg76x66pqqtrvzs3";
    private const string AlternateAddress = "bc1qrwsx8fs0l6z7ugp5cvzy6lhss7jlyru3kg9s8y";
    private const string SamplePrevBlockHash = "00000000000000000002029d47c98d2ad5c020ce9a92af8ace14b882abfa1643";
    private const string OlderTipBlockHash = "0000000000000000000000000000000000000000000000000000000000012345";
    private const string SampleHeaderHex = "00804f274316faab82b814ce8aaf929ace20c0d52a8dc9479d02020000000000000000002e0c639c7934a697d14a314cea5da30f0c45660248d534db3cfb2036b5ac0d8a65a6e3696913021778491e84";
    private const string SampleCoinbaseHex = "01000000010000000000000000000000000000000000000000000000000000000000000000ffffffff2003e16d0e13426f6f742070726f746f636f6c0f626f6f74000709921015000000ffffffff06128e120000000000160014c64b1b9283ba1ea86bb9e7b696b0c8f68dad040004cc041000000000160014c64b1b9283ba1ea86bb9e7b696b0c8f68dad04000000000000000000106a0e9113b1ccf00d0000000000b9bb1952ad8b02000000001600141ba063a60ffe85ee2034c3044d7ef087a5f20f910000000000000000036a01000000000000000000266a24aa21a9edcddc611f6111ea75c5a265fba065e8eccb3d1ec8f954c738ea4586b3fffab1ce00000000";
    private static readonly List<string> SampleMerklePath =
    [
        "b6c40a03e40f9f35ff1a47dfc044a0b82dced05867abda7a3d77476f8d76ca8c",
        "e8474aac0f34d17bec62afaa624ebe64b36f9ce951daca4205dfcbb3061ce1a2",
        "e93d732b034381c7746dfb0a83ff7396336f8dd454fe5a7c7999f80e8c15b2d7",
        "0276e88a8933b31dc3f8b517bae033e18416754cca5f838320b6d3ab89be7b69",
        "254f86497567f0acd39d3a31948258459505bb0634fec20a27c6d7af3a3781c8",
        "fc970819c5af1e177e882851fe7c07b8206213422322d8571c51a9ef87a8d2e5",
        "1461f6060b70b6079c7b30ca17b0dde6657704da86b88e61844be9936be5157b"
    ];

    private static readonly IReadOnlyList<PayoutInfo> SampleExpectedWinners = BuildExpectedWinners(SampleCoinbaseHex);

    [TestMethod]
    public void ValidateShareAttributesToSlotZeroInsteadOfClaimedMinerAddress()
    {
        var verifier = new BootShareVerifier();
        var forgedSubmission = new RecordedShareSubmission
        {
            MinerAddress = AlternateAddress,
            Username = string.Empty,
            HeaderHex = SampleHeaderHex,
            CoinbaseHex = SampleCoinbaseHex,
            MerklePath = SampleMerklePath.ToList(),
            PrevBlockHash = SamplePrevBlockHash,
            Source = "http"
        };

        BootShareValidationResult result = verifier.ValidateShare(
            forgedSubmission,
            SampleExpectedWinners,
            SamplePrevBlockHash);

        Assert.IsTrue(result.IsValid, result.RejectionReason);
        Assert.AreEqual(SampleSlotZeroAddress, result.MinerAddress);
        Assert.AreEqual(SampleSlotZeroAddress, result.Username);

        var claimedSubmission = new RecordedShareSubmission
        {
            MinerAddress = SampleSlotZeroAddress,
            Username = string.Empty,
            HeaderHex = SampleHeaderHex,
            CoinbaseHex = SampleCoinbaseHex,
            MerklePath = SampleMerklePath.ToList(),
            PrevBlockHash = SamplePrevBlockHash,
            Source = "http"
        };

        BootShareValidationResult claimedResult = verifier.ValidateShare(
            claimedSubmission,
            SampleExpectedWinners,
            SamplePrevBlockHash);

        Assert.IsTrue(claimedResult.IsValid, claimedResult.RejectionReason);
        Assert.AreEqual(result.ShareId, claimedResult.ShareId, "ShareId should be independent of caller-supplied MinerAddress.");
    }

    [TestMethod]
    public void ValidateShareRejectsSlotZeroMutationWithoutHeaderRecompute()
    {
        var verifier = new BootShareVerifier();
        string mutatedCoinbaseHex = RewriteSlotZeroAddress(SampleCoinbaseHex, AlternateAddress);

        BootShareValidationResult result = verifier.ValidateShare(
            new RecordedShareSubmission
            {
                MinerAddress = AlternateAddress,
                Username = string.Empty,
                HeaderHex = SampleHeaderHex,
                CoinbaseHex = mutatedCoinbaseHex,
                MerklePath = SampleMerklePath.ToList(),
                PrevBlockHash = SamplePrevBlockHash,
                Source = "http"
            },
            SampleExpectedWinners,
            SamplePrevBlockHash);

        Assert.IsFalse(result.IsValid);
        StringAssert.Contains(result.RejectionReason ?? string.Empty, "merkle root");
    }

    [TestMethod]
    public async Task HttpShareWithForgedMinerAddressIsAcceptedAndAttributedToSlotZeroAsync()
    {
        using var harness = TestHarness.Create();
        var response = await harness.MiningController.SubmitShare(new ShareSubmissionDto
        {
            MinerAddress = AlternateAddress,
            Username = string.Empty,
            HeaderHex = SampleHeaderHex,
            CoinbaseHex = SampleCoinbaseHex,
            MerklePath = SampleMerklePath.ToList(),
            PrevBlockHash = SamplePrevBlockHash
        });

        JsonObject payload = ParseObjectResult(response, StatusCodes.Status200OK);

        Assert.AreEqual("accepted", payload["status"]?.GetValue<string>());
        List<PayoutInfo> onDeckList = harness.StateService.GetOnDeckList();
        Assert.AreEqual(1, onDeckList.Count);
        Assert.AreEqual(SampleSlotZeroAddress, onDeckList[0].Address);
    }

    [TestMethod]
    public async Task HttpShareWithMissingBodyIsRejectedCleanlyAsync()
    {
        using var harness = TestHarness.Create();

        IActionResult response = await harness.MiningController.SubmitShare(null);
        JsonObject payload = ParseObjectResult(response, StatusCodes.Status400BadRequest);

        Assert.AreEqual("rejected", payload["status"]?.GetValue<string>());
        Assert.AreEqual("Missing share payload", payload["reason"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task HttpShareValidationIgnoresConfiguredGridPoolCoinbaseTagAsync()
    {
        using var harness = TestHarness.Create();
        harness.Config.CoinbaseTag = "Grid Pool";

        IActionResult response = await harness.MiningController.SubmitShare(CreateSampleShareDto());
        JsonObject payload = ParseObjectResult(response, StatusCodes.Status200OK);

        Assert.AreEqual("accepted", payload["status"]?.GetValue<string>());
        Assert.AreEqual(SampleSlotZeroAddress, harness.StateService.GetOnDeckList()[0].Address);
    }

    [TestMethod]
    public async Task HttpShareValidationAllowsEmptyConfiguredCoinbaseTagAsync()
    {
        using var harness = TestHarness.Create();
        harness.Config.CoinbaseTag = string.Empty;

        IActionResult response = await harness.MiningController.SubmitShare(CreateSampleShareDto());
        JsonObject payload = ParseObjectResult(response, StatusCodes.Status200OK);

        Assert.AreEqual("accepted", payload["status"]?.GetValue<string>());
        Assert.AreEqual(SampleSlotZeroAddress, harness.StateService.GetOnDeckList()[0].Address);
    }

    [TestMethod]
    public async Task PeerShareWithForgedMinerAddressIsAcceptedAndAttributedToSlotZeroAsync()
    {
        using var harness = TestHarness.Create();
        var response = await harness.PeerController.SubmitPeerShare(new PeerShareAnnouncement
        {
            SenderEndpoint = "https://peer.example",
            ProtocolVersion = harness.Config.BootProtocolVersion,
            NetworkId = harness.Config.BootNetworkId,
            Share = new BootShareProof
            {
                ShareId = string.Empty,
                MinerAddress = AlternateAddress,
                Username = string.Empty,
                HeaderHex = SampleHeaderHex,
                CoinbaseHex = SampleCoinbaseHex,
                MerklePath = SampleMerklePath.ToList(),
                PrevBlockHash = SamplePrevBlockHash,
                Source = "peer"
            }
        });

        JsonObject payload = ParseObjectResult(response, StatusCodes.Status200OK);

        Assert.AreEqual("accepted", payload["status"]?.GetValue<string>());
        List<PayoutInfo> onDeckList = harness.StateService.GetOnDeckList();
        Assert.AreEqual(1, onDeckList.Count);
        Assert.AreEqual(SampleSlotZeroAddress, onDeckList[0].Address);
    }

    [TestMethod]
    public async Task PeerShareWithMissingBodyIsRejectedCleanlyAsync()
    {
        using var harness = TestHarness.Create();

        IActionResult response = await harness.PeerController.SubmitPeerShare(null);
        JsonObject payload = ParseObjectResult(response, StatusCodes.Status400BadRequest);

        Assert.AreEqual("rejected", payload["status"]?.GetValue<string>());
        Assert.AreEqual("Missing share payload", payload["reason"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task PeerShareOnUnknownParentIsRejectedWithoutAdvancingTipAsync()
    {
        using var harness = TestHarness.Create(currentTipBlockHash: OlderTipBlockHash);

        IActionResult response = await harness.PeerController.SubmitPeerShare(CreateSamplePeerAnnouncement(harness.Config));
        JsonObject payload = ParseObjectResult(response, StatusCodes.Status400BadRequest);

        Assert.AreEqual("rejected", payload["status"]?.GetValue<string>());
        StringAssert.StartsWith(
            payload["reason"]?.GetValue<string>() ?? string.Empty,
            "Share builds on the wrong parent block");

        BootNetworkStatusDto status = harness.StateService.GetNetworkStatus();
        Assert.AreEqual(OlderTipBlockHash, status.CurrentTipBlockHash);
        Assert.AreEqual(0, harness.StateService.GetOnDeckList().Count);
    }

    [TestMethod]
    public async Task ProoflessNewerCurrentStateFastForwardsStaleNodeAsync()
    {
        using var harness = TestHarness.Create(currentTipBlockHash: OlderTipBlockHash);
        List<PayoutInfo> remoteWinners = SampleExpectedWinners.Select(ClonePayout).ToList();
        remoteWinners[0].Difficulty += 1024;

        bool adopted = await harness.StateService.TryAdoptCurrentStateAsync(
            new BootStateBundle
            {
                StateId = "remote-current-state",
                Kind = "current",
                CurrentRoundNumber = 2,
                ProtocolVersion = harness.Config.BootProtocolVersion,
                NetworkId = harness.Config.BootNetworkId,
                LockedByBlockHash = SamplePrevBlockHash,
                LockedByBlockHeight = 945001,
                CreatedAtUtc = DateTime.UtcNow,
                TotalDifficulty = remoteWinners.Sum(x => x.Difficulty),
                WinnersList = remoteWinners
            },
            SamplePrevBlockHash,
            945001,
            "https://peer.example");

        Assert.IsTrue(adopted);
        BootNetworkStatusDto status = harness.StateService.GetNetworkStatus();
        Assert.AreEqual("remote-current-state", status.CurrentStateId);
        Assert.AreEqual(2, status.CurrentRoundNumber);
        Assert.AreEqual(remoteWinners.Count, harness.StateService.GetWinnersList().Count);
    }

    [TestMethod]
    public async Task ProoflessSameRoundCurrentStateDoesNotOverrideEstablishedStateAsync()
    {
        using var harness = TestHarness.Create(currentTipBlockHash: OlderTipBlockHash);
        List<PayoutInfo> remoteWinners = SampleExpectedWinners.Select(ClonePayout).ToList();
        remoteWinners[0].Difficulty += 1000000;

        bool adopted = await harness.StateService.TryAdoptCurrentStateAsync(
            new BootStateBundle
            {
                StateId = "remote-proofless-stronger-state",
                Kind = "current",
                CurrentRoundNumber = 1,
                ProtocolVersion = harness.Config.BootProtocolVersion,
                NetworkId = harness.Config.BootNetworkId,
                LockedByBlockHash = OlderTipBlockHash,
                LockedByBlockHeight = 945000,
                CreatedAtUtc = DateTime.UtcNow,
                TotalDifficulty = remoteWinners.Sum(x => x.Difficulty),
                WinnersList = remoteWinners
            },
            OlderTipBlockHash,
            945000,
            "https://peer.example");

        Assert.IsFalse(adopted);
        BootNetworkStatusDto status = harness.StateService.GetNetworkStatus();
        Assert.AreEqual("seed-current", status.CurrentStateId);
        Assert.AreEqual(1, status.CurrentRoundNumber);
    }

    [TestMethod]
    public async Task ProofBackedSameRoundCurrentStateOverridesProoflessLocalStateAsync()
    {
        using var remoteHarness = TestHarness.Create(currentTipBlockHash: OlderTipBlockHash);
        ShareRecordingResult shareResult = await remoteHarness.StateService.SubmitShareAsync(
            new RecordedShareSubmission
            {
                MinerAddress = AlternateAddress,
                Username = string.Empty,
                HeaderHex = SampleHeaderHex,
                CoinbaseHex = SampleCoinbaseHex,
                MerklePath = SampleMerklePath.ToList(),
                PrevBlockHash = SamplePrevBlockHash,
                Source = "datum"
            },
            "datum-block");
        Assert.IsTrue(shareResult.Accepted, shareResult.RejectionReason);

        RoundRotationResult rotation = await remoteHarness.StateService.RotateToNextRoundAsync(
            SamplePrevBlockHash,
            "test-rotation",
            manual: false,
            blockHeight: 945001);
        BootStateBundle remoteBundle = rotation.LockedStateBundle!;
        Assert.IsTrue(remoteBundle.ShareProofs.Count > 0);

        using var localHarness = TestHarness.Create(
            currentTipBlockHash: SamplePrevBlockHash,
            currentRoundNumber: remoteBundle.CurrentRoundNumber);
        bool adopted = await localHarness.StateService.TryAdoptCurrentStateAsync(
            remoteBundle,
            SamplePrevBlockHash,
            945001,
            "https://peer.example");

        Assert.IsTrue(adopted);
        BootNetworkStatusDto status = localHarness.StateService.GetNetworkStatus();
        Assert.AreEqual(remoteBundle.StateId, status.CurrentStateId);
        Assert.AreEqual(remoteBundle.CurrentRoundNumber, status.CurrentRoundNumber);
    }

    [TestMethod]
    public async Task DatumShareOnFreshParentIsAcceptedAndLearnsParentWithoutTipAdvanceAsync()
    {
        using var harness = TestHarness.Create(currentTipBlockHash: OlderTipBlockHash);

        ShareRecordingResult result = await harness.StateService.SubmitShareAsync(new RecordedShareSubmission
        {
            MinerAddress = AlternateAddress,
            Username = string.Empty,
            HeaderHex = SampleHeaderHex,
            CoinbaseHex = SampleCoinbaseHex,
            MerklePath = SampleMerklePath.ToList(),
            PrevBlockHash = SamplePrevBlockHash,
            Source = "datum"
        }, "datum-block");

        Assert.IsTrue(result.Accepted, result.RejectionReason);
        Assert.AreEqual(SamplePrevBlockHash, result.AcceptedProof?.PrevBlockHash);

        BootNetworkStatusDto status = harness.StateService.GetNetworkStatus();
        Assert.AreEqual(OlderTipBlockHash, status.CurrentTipBlockHash);
        Assert.AreEqual(1, harness.StateService.GetOnDeckList().Count);

        Thread.Sleep(1200);
        PoolState persisted = JsonSerializer.Deserialize<PoolState>(File.ReadAllText(harness.StatePath))!;
        CollectionAssert.Contains(persisted.AcceptedParentBlockHashes, OlderTipBlockHash);
        CollectionAssert.Contains(persisted.AcceptedParentBlockHashes, SamplePrevBlockHash);
    }

    [TestMethod]
    public async Task DatumShareOnFreshParentWithInvalidCoinbaseReportsRetryFailureAsync()
    {
        using var harness = TestHarness.Create(currentTipBlockHash: OlderTipBlockHash);

        ShareRecordingResult result = await harness.StateService.SubmitShareAsync(new RecordedShareSubmission
        {
            MinerAddress = AlternateAddress,
            Username = string.Empty,
            HeaderHex = SampleHeaderHex,
            CoinbaseHex = RewriteSlotZeroAddress(SampleCoinbaseHex, AlternateAddress),
            MerklePath = SampleMerklePath.ToList(),
            PrevBlockHash = SamplePrevBlockHash,
            Source = "datum"
        }, "datum-block");

        Assert.IsFalse(result.Accepted);
        StringAssert.Contains(result.RejectionReason ?? string.Empty, "merkle root");

        BootNetworkEventSeriesDto events = harness.StateService.GetNetworkEvents(eventType: "fresh-parent-retry-failed");
        Assert.AreEqual(1, events.Events.Count);
        StringAssert.Contains(events.Events[0].Message ?? string.Empty, "merkle root");
        Assert.AreEqual(SamplePrevBlockHash, events.Events[0].BlockHash);
        Assert.AreEqual(0, harness.StateService.GetOnDeckList().Count);
    }

    [TestMethod]
    public async Task HttpShareOnFreshParentIsRejectedUntilTipIsKnownAsync()
    {
        using var harness = TestHarness.Create(currentTipBlockHash: OlderTipBlockHash);

        IActionResult response = await harness.MiningController.SubmitShare(CreateSampleShareDto());
        JsonObject payload = ParseObjectResult(response, StatusCodes.Status400BadRequest);

        Assert.AreEqual("rejected", payload["status"]?.GetValue<string>());
        StringAssert.StartsWith(
            payload["reason"]?.GetValue<string>() ?? string.Empty,
            "Share builds on the wrong parent block");
        Assert.AreEqual(0, harness.StateService.GetOnDeckList().Count);
        Assert.AreEqual(OlderTipBlockHash, harness.StateService.GetNetworkStatus().CurrentTipBlockHash);
    }

    [TestMethod]
    public async Task LocalSubmitFollowedByPeerReplayReturnsDuplicateWithoutChangingCandidateStateAsync()
    {
        using var harness = TestHarness.Create();
        IActionResult firstResponse = await harness.MiningController.SubmitShare(new ShareSubmissionDto
        {
            MinerAddress = AlternateAddress,
            Username = string.Empty,
            HeaderHex = SampleHeaderHex,
            CoinbaseHex = SampleCoinbaseHex,
            MerklePath = SampleMerklePath.ToList(),
            PrevBlockHash = SamplePrevBlockHash
        });
        JsonObject firstPayload = ParseObjectResult(firstResponse, StatusCodes.Status200OK);
        Assert.AreEqual("accepted", firstPayload["status"]?.GetValue<string>());

        BootNetworkStatusDto stateAfterFirst = harness.StateService.GetNetworkStatus();
        IActionResult secondResponse = await harness.PeerController.SubmitPeerShare(new PeerShareAnnouncement
        {
            SenderEndpoint = "https://peer.example",
            ProtocolVersion = harness.Config.BootProtocolVersion,
            NetworkId = harness.Config.BootNetworkId,
            Share = new BootShareProof
            {
                ShareId = "forged-legacy-id",
                MinerAddress = "bc1qtotallydifferent000000000000000000000000000",
                Username = "forged-user",
                HeaderHex = SampleHeaderHex,
                CoinbaseHex = SampleCoinbaseHex,
                MerklePath = SampleMerklePath.ToList(),
                PrevBlockHash = SamplePrevBlockHash,
                Source = "peer"
            }
        });

        JsonObject secondPayload = ParseObjectResult(secondResponse, StatusCodes.Status200OK);
        BootNetworkStatusDto stateAfterSecond = harness.StateService.GetNetworkStatus();

        Assert.AreEqual("duplicate", secondPayload["status"]?.GetValue<string>());
        Assert.AreEqual(stateAfterFirst.CandidateStateId, stateAfterSecond.CandidateStateId);
        Assert.AreEqual(1, harness.StateService.GetOnDeckList().Count);
    }

    [TestMethod]
    public async Task LocalSubmitFollowedByDuplicateLocalSubmitReturnsDuplicateWithoutChangingCandidateStateAsync()
    {
        using var harness = TestHarness.Create();

        IActionResult firstResponse = await harness.MiningController.SubmitShare(CreateSampleShareDto());
        JsonObject firstPayload = ParseObjectResult(firstResponse, StatusCodes.Status200OK);
        Assert.AreEqual("accepted", firstPayload["status"]?.GetValue<string>());

        BootNetworkStatusDto stateAfterFirst = harness.StateService.GetNetworkStatus();

        IActionResult secondResponse = await harness.MiningController.SubmitShare(CreateSampleShareDto());
        JsonObject secondPayload = ParseObjectResult(secondResponse, StatusCodes.Status200OK);
        BootNetworkStatusDto stateAfterSecond = harness.StateService.GetNetworkStatus();

        Assert.AreEqual("duplicate", secondPayload["status"]?.GetValue<string>());
        Assert.AreEqual(stateAfterFirst.CandidateStateId, stateAfterSecond.CandidateStateId);
        Assert.AreEqual(1, harness.StateService.GetOnDeckList().Count);
    }

    [TestMethod]
    public async Task PeerSubmitFollowedByDuplicatePeerSubmitReturnsDuplicateWithoutChangingCandidateStateAsync()
    {
        using var harness = TestHarness.Create();

        IActionResult firstResponse = await harness.PeerController.SubmitPeerShare(CreateSamplePeerAnnouncement(harness.Config));
        JsonObject firstPayload = ParseObjectResult(firstResponse, StatusCodes.Status200OK);
        Assert.AreEqual("accepted", firstPayload["status"]?.GetValue<string>());

        BootNetworkStatusDto stateAfterFirst = harness.StateService.GetNetworkStatus();

        IActionResult secondResponse = await harness.PeerController.SubmitPeerShare(CreateSamplePeerAnnouncement(harness.Config));
        JsonObject secondPayload = ParseObjectResult(secondResponse, StatusCodes.Status200OK);
        BootNetworkStatusDto stateAfterSecond = harness.StateService.GetNetworkStatus();

        Assert.AreEqual("duplicate", secondPayload["status"]?.GetValue<string>());
        Assert.AreEqual(stateAfterFirst.CandidateStateId, stateAfterSecond.CandidateStateId);
        Assert.AreEqual(1, harness.StateService.GetOnDeckList().Count);
    }

    [TestMethod]
    public async Task PeerSubmitWithWrongNetworkIsRejectedBeforeInsertionAsync()
    {
        using var harness = TestHarness.Create();

        IActionResult response = await harness.PeerController.SubmitPeerShare(CreateSamplePeerAnnouncement(
            harness.Config,
            protocolVersion: harness.Config.BootProtocolVersion,
            networkId: "wrong-network"));

        JsonObject payload = ParseObjectResult(response, StatusCodes.Status400BadRequest);

        Assert.AreEqual("rejected", payload["status"]?.GetValue<string>());
        Assert.AreEqual("Network mismatch", payload["reason"]?.GetValue<string>());
        Assert.AreEqual(0, harness.StateService.GetOnDeckList().Count);
    }

    [TestMethod]
    public async Task PeerSubmitWithWrongProtocolVersionIsRejectedBeforeInsertionAsync()
    {
        using var harness = TestHarness.Create();

        IActionResult response = await harness.PeerController.SubmitPeerShare(CreateSamplePeerAnnouncement(
            harness.Config,
            protocolVersion: harness.Config.BootProtocolVersion + 1,
            networkId: harness.Config.BootNetworkId));

        JsonObject payload = ParseObjectResult(response, StatusCodes.Status400BadRequest);

        Assert.AreEqual("rejected", payload["status"]?.GetValue<string>());
        Assert.AreEqual("Network mismatch", payload["reason"]?.GetValue<string>());
        Assert.AreEqual(0, harness.StateService.GetOnDeckList().Count);
    }

    [TestMethod]
    public async Task DuplicateBlockRotationIsIgnoredAfterFirstApplyAsync()
    {
        using var harness = TestHarness.Create();

        IActionResult shareResponse = await harness.MiningController.SubmitShare(CreateSampleShareDto());
        JsonObject sharePayload = ParseObjectResult(shareResponse, StatusCodes.Status200OK);
        Assert.AreEqual("accepted", sharePayload["status"]?.GetValue<string>());

        const string blockHash = "0000000000000000000000000000000000000000000000000000000000abc123";

        RoundRotationResult firstRotation = await harness.StateService.RotateToNextRoundAsync(
            blockHash,
            "test-block",
            manual: false,
            blockHeight: 945001);

        Assert.IsTrue(firstRotation.Rotated);
        string stateAfterFirstRotation = firstRotation.NetworkStatus.CurrentStateId;
        int roundAfterFirstRotation = firstRotation.NetworkStatus.CurrentRoundNumber;

        RoundRotationResult secondRotation = await harness.StateService.RotateToNextRoundAsync(
            blockHash,
            "test-block",
            manual: false,
            blockHeight: 945001);

        Assert.IsFalse(secondRotation.Rotated);
        Assert.AreEqual("Block already applied", secondRotation.Reason);
        Assert.AreEqual(stateAfterFirstRotation, secondRotation.NetworkStatus.CurrentStateId);
        Assert.AreEqual(roundAfterFirstRotation, secondRotation.NetworkStatus.CurrentRoundNumber);
    }

    [TestMethod]
    public async Task LowerHeightChainTipObservationIsIgnoredWithoutRegressingTipAsync()
    {
        using var harness = TestHarness.Create();
        BootNetworkStatusDto before = harness.StateService.GetNetworkStatus();

        BootNetworkStatusDto after = await harness.StateService.ObserveChainTipAsync(
            "0000000000000000000000000000000000000000000000000000000000bad001",
            "test-stale-tip",
            before.CurrentTipBlockHeight - 1);

        Assert.AreEqual(before.CurrentTipBlockHash, after.CurrentTipBlockHash);
        Assert.AreEqual(before.CurrentTipBlockHeight, after.CurrentTipBlockHeight);

        BootNetworkEventSeriesDto staleEvents = harness.StateService.GetNetworkEvents(eventType: "chain-tip-stale");
        Assert.AreEqual(1, staleEvents.Events.Count);
        Assert.AreEqual(before.CurrentTipBlockHeight - 1, staleEvents.Events[0].BlockHeight);
    }

    [TestMethod]
    public async Task LowerHeightRoundRotationIsIgnoredWithoutAdvancingRoundAsync()
    {
        using var harness = TestHarness.Create();

        IActionResult shareResponse = await harness.MiningController.SubmitShare(CreateSampleShareDto());
        JsonObject sharePayload = ParseObjectResult(shareResponse, StatusCodes.Status200OK);
        Assert.AreEqual("accepted", sharePayload["status"]?.GetValue<string>());

        BootNetworkStatusDto before = harness.StateService.GetNetworkStatus();
        RoundRotationResult rotation = await harness.StateService.RotateToNextRoundAsync(
            "0000000000000000000000000000000000000000000000000000000000bad002",
            "test-stale-block",
            manual: false,
            blockHeight: before.CurrentTipBlockHeight - 1);

        Assert.IsFalse(rotation.Rotated);
        Assert.AreEqual("Stale block notification", rotation.Reason);
        Assert.AreEqual(before.CurrentRoundNumber, rotation.NetworkStatus.CurrentRoundNumber);
        Assert.AreEqual(before.CurrentTipBlockHash, rotation.NetworkStatus.CurrentTipBlockHash);
        Assert.AreEqual(before.CurrentTipBlockHeight, rotation.NetworkStatus.CurrentTipBlockHeight);
        Assert.AreEqual(1, harness.StateService.GetOnDeckList().Count);
    }

    [TestMethod]
    public async Task RotationPreservesImmediatePreviousParentInAcceptedParentSetAsync()
    {
        using var harness = TestHarness.Create();

        IActionResult shareResponse = await harness.MiningController.SubmitShare(CreateSampleShareDto());
        JsonObject sharePayload = ParseObjectResult(shareResponse, StatusCodes.Status200OK);
        Assert.AreEqual("accepted", sharePayload["status"]?.GetValue<string>());

        const string newBlockHash = "0000000000000000000000000000000000000000000000000000000000def456";
        RoundRotationResult rotation = await harness.StateService.RotateToNextRoundAsync(
            newBlockHash,
            "test-block",
            manual: false,
            blockHeight: 945002);

        Assert.IsTrue(rotation.Rotated);

        Thread.Sleep(1200);

        PoolState persisted = JsonSerializer.Deserialize<PoolState>(File.ReadAllText(harness.StatePath))!;
        CollectionAssert.Contains(persisted.AcceptedParentBlockHashes, SamplePrevBlockHash);
        CollectionAssert.Contains(persisted.AcceptedParentBlockHashes, newBlockHash);
    }

    [TestMethod]
    public async Task SlotZeroMutationWithRecomputedHeaderButLowPowIsRejectedAsync()
    {
        using var harness = TestHarness.Create();
        (string mutatedHeaderHex, string mutatedCoinbaseHex) = BuildLowDifficultySlotZeroMutation(
            SampleHeaderHex,
            SampleCoinbaseHex,
            SampleMerklePath,
            AlternateAddress);

        IActionResult response = await harness.MiningController.SubmitShare(new ShareSubmissionDto
        {
            MinerAddress = AlternateAddress,
            Username = string.Empty,
            HeaderHex = mutatedHeaderHex,
            CoinbaseHex = mutatedCoinbaseHex,
            MerklePath = SampleMerklePath.ToList(),
            PrevBlockHash = SamplePrevBlockHash
        });

        JsonObject payload = ParseObjectResult(response, StatusCodes.Status400BadRequest);

        Assert.AreEqual("rejected", payload["status"]?.GetValue<string>());
        Assert.AreEqual("Low difficulty", payload["reason"]?.GetValue<string>());
        Assert.AreEqual(0, harness.StateService.GetOnDeckList().Count);
    }

    [TestMethod]
    public void DatumSessionTelemetryTracksLifecycleAndFiltersActiveSessions()
    {
        using var harness = TestHarness.Create();
        DateTime startedUtc = DateTime.UtcNow;

        harness.StateService.RecordDatumSessionOpened("session-1", "127.0.0.1:5001", startedUtc);
        harness.StateService.RecordDatumSessionProtocol("session-1", "datum", startedUtc.AddMilliseconds(1));
        harness.StateService.RecordDatumSessionHello("session-1", "signing-key-1", "encrypt-key-1", startedUtc.AddMilliseconds(2));
        harness.StateService.RecordDatumSessionPayoutLock("session-1", AlternateAddress, startedUtc.AddMilliseconds(3));
        harness.StateService.RecordDatumSessionCoinbaserFetch("session-1", startedUtc.AddMilliseconds(4));
        harness.StateService.RecordDatumSessionRefreshRequest("session-1", startedUtc.AddMilliseconds(5));
        harness.StateService.RecordDatumSessionShareOutcome("session-1", accepted: true, affectedOnDeck: true, startedUtc.AddMilliseconds(6));
        harness.StateService.RecordDatumSessionShareOutcome("session-1", accepted: false, affectedOnDeck: false, startedUtc.AddMilliseconds(7));
        harness.StateService.CompleteDatumSession(
            "session-1",
            "client-disconnected-no-data",
            "Client closed DATUM session before sending a full header.",
            serverInitiated: false,
            serverCloseEventType: null,
            timestampUtc: startedUtc.AddSeconds(30));

        harness.StateService.RecordDatumSessionOpened("session-2", "127.0.0.1:5002", startedUtc.AddSeconds(1));
        harness.StateService.RecordDatumSessionProtocol("session-2", "datum", startedUtc.AddSeconds(1).AddMilliseconds(1));

        BootDatumSessionSeriesDto allSessions = harness.StateService.GetDatumSessions(windowKey: "1h", limit: 10, protocol: "datum");
        Assert.AreEqual(2, allSessions.TotalEvents);

        BootDatumSessionTelemetry closedSession = allSessions.Events.Single(item => item.SessionId == "session-1");
        Assert.IsTrue(closedSession.HandshakeCompleted);
        Assert.AreEqual(1, closedSession.HelloCount);
        Assert.AreEqual(1, closedSession.CoinbaserFetchCount);
        Assert.AreEqual(1, closedSession.RefreshRequestCount);
        Assert.AreEqual(2, closedSession.ShareResponseCount);
        Assert.AreEqual(1, closedSession.AcceptedShareCount);
        Assert.AreEqual(1, closedSession.RejectedShareCount);
        Assert.AreEqual(1, closedSession.AffectedOnDeckCount);
        Assert.AreEqual(BitcoinScript.NormalizeAddress(AlternateAddress), closedSession.LockedPayoutAddress);
        Assert.AreEqual("client-disconnected-no-data", closedSession.CloseDisposition);
        Assert.IsFalse(closedSession.ServerInitiatedClose);
        Assert.IsTrue(closedSession.DurationMs >= 30000 - 1);
        Assert.IsTrue(closedSession.IdleBeforeCloseMs >= 29990);

        BootDatumSessionSeriesDto activeSessions = harness.StateService.GetDatumSessions(windowKey: "1h", limit: 10, active: true);
        Assert.AreEqual(1, activeSessions.TotalEvents);
        Assert.AreEqual("session-2", activeSessions.Events[0].SessionId);
        Assert.IsNull(activeSessions.Events[0].ClosedUtc);
    }

    private static ShareSubmissionDto CreateSampleShareDto()
    {
        return new ShareSubmissionDto
        {
            MinerAddress = AlternateAddress,
            Username = string.Empty,
            HeaderHex = SampleHeaderHex,
            CoinbaseHex = SampleCoinbaseHex,
            MerklePath = SampleMerklePath.ToList(),
            PrevBlockHash = SamplePrevBlockHash
        };
    }

    private static PeerShareAnnouncement CreateSamplePeerAnnouncement(
        PoolConfig config,
        int? protocolVersion = null,
        string? networkId = null)
    {
        return new PeerShareAnnouncement
        {
            SenderEndpoint = "https://peer.example",
            ProtocolVersion = protocolVersion ?? config.BootProtocolVersion,
            NetworkId = networkId ?? config.BootNetworkId,
            Share = new BootShareProof
            {
                ShareId = string.Empty,
                MinerAddress = AlternateAddress,
                Username = string.Empty,
                HeaderHex = SampleHeaderHex,
                CoinbaseHex = SampleCoinbaseHex,
                MerklePath = SampleMerklePath.ToList(),
                PrevBlockHash = SamplePrevBlockHash,
                Source = "peer"
            }
        };
    }

    private static JsonObject ParseObjectResult(IActionResult actionResult, int expectedStatusCode)
    {
        var objectResult = actionResult as ObjectResult;
        Assert.IsNotNull(objectResult, $"Expected ObjectResult, got {actionResult.GetType().Name}.");
        Assert.AreEqual(expectedStatusCode, objectResult.StatusCode ?? expectedStatusCode);
        return JsonNode.Parse(JsonSerializer.Serialize(objectResult.Value))!.AsObject();
    }

    private static IReadOnlyList<PayoutInfo> BuildExpectedWinners(string coinbaseHex)
    {
        var outputs = BitcoinTransactionParser.ParseOutputs(Convert.FromHexString(coinbaseHex));
        return outputs
            .Skip(1)
            .Select(output => new PayoutInfo
            {
                Value = output.Value,
                Address = BitcoinScript.ScriptToAddress(output.ScriptPubKey)
            })
            .Where(output => !string.Equals(output.Address, "UNKNOWN", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static PayoutInfo ClonePayout(PayoutInfo payout)
    {
        return new PayoutInfo
        {
            Value = payout.Value,
            Address = payout.Address,
            Username = payout.Username,
            Difficulty = payout.Difficulty,
            DiffString = payout.DiffString
        };
    }

    private static string RewriteSlotZeroAddress(string coinbaseHex, string replacementAddress)
    {
        byte[] transaction = Convert.FromHexString(coinbaseHex);
        byte[] replacementScript = BitcoinScript.AddressToScriptPubKey(replacementAddress);
        int offset = 0;

        offset += 4; // version
        bool hasWitness = transaction[offset] == 0x00 && transaction[offset + 1] != 0x00;
        if (hasWitness)
        {
            offset += 2;
        }

        ulong inputCount = ReadVarInt(transaction, ref offset);
        for (ulong index = 0; index < inputCount; index++)
        {
            offset += 32; // prev txid
            offset += 4;  // prev index
            ulong scriptLength = ReadVarInt(transaction, ref offset);
            offset += checked((int)scriptLength);
            offset += 4; // sequence
        }

        _ = ReadVarInt(transaction, ref offset); // output count
        offset += 8; // first output value
        ulong firstScriptLength = ReadVarInt(transaction, ref offset);
        Assert.AreEqual((ulong)replacementScript.Length, firstScriptLength, "Test helper expects equal-length slot-0 scripts.");
        Array.Copy(replacementScript, 0, transaction, offset, replacementScript.Length);

        return Convert.ToHexString(transaction).ToLowerInvariant();
    }

    private static (string HeaderHex, string CoinbaseHex) BuildLowDifficultySlotZeroMutation(
        string headerHex,
        string coinbaseHex,
        IReadOnlyList<string> merklePath,
        string replacementAddress)
    {
        string mutatedCoinbaseHex = RewriteSlotZeroAddress(coinbaseHex, replacementAddress);
        byte[] headerBytes = Convert.FromHexString(headerHex);
        byte[] coinbaseHash = DoubleSha256(Convert.FromHexString(mutatedCoinbaseHex));
        byte[] merkleRoot = ComputeMerkleRoot(coinbaseHash, merklePath.Select(Convert.FromHexString).ToList());
        Array.Copy(merkleRoot, 0, headerBytes, 36, 32);

        for (uint nonce = 0; nonce < 1024; nonce++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(76, 4), nonce);
            if (ComputeDifficulty(headerBytes) < 1)
            {
                return (Convert.ToHexString(headerBytes).ToLowerInvariant(), mutatedCoinbaseHex);
            }
        }

        Assert.Fail("Failed to produce a low-difficulty mutated header.");
        return (string.Empty, string.Empty);
    }

    private static byte[] ComputeMerkleRoot(byte[] coinbaseHash, IReadOnlyList<byte[]> merkleBranches)
    {
        byte[] current = coinbaseHash;
        foreach (byte[] branch in merkleBranches)
        {
            current = DoubleSha256(current.Concat(branch).ToArray());
        }

        return current;
    }

    private static byte[] DoubleSha256(byte[] bytes)
    {
        return SHA256.HashData(SHA256.HashData(bytes));
    }

    private static double ComputeDifficulty(byte[] headerBytes)
    {
        byte[] headerHash = DoubleSha256(headerBytes);
        BigInteger difficultyOneTarget = DecodeCompactTarget(0x1d00ffff);
        BigInteger hashValue = ToPositiveBigInteger(headerHash);
        return hashValue.IsZero ? double.MaxValue : (double)difficultyOneTarget / (double)hashValue;
    }

    private static BigInteger DecodeCompactTarget(uint compact)
    {
        int exponent = (int)(compact >> 24);
        uint mantissa = compact & 0x007fffff;
        BigInteger target = mantissa;
        int shift = 8 * (exponent - 3);
        if (shift >= 0)
        {
            target <<= shift;
        }
        else
        {
            target >>= -shift;
        }

        return target;
    }

    private static BigInteger ToPositiveBigInteger(byte[] littleEndianBytes)
    {
        byte[] extended = new byte[littleEndianBytes.Length + 1];
        Array.Copy(littleEndianBytes, extended, littleEndianBytes.Length);
        return new BigInteger(extended);
    }

    private static ulong ReadVarInt(byte[] buffer, ref int offset)
    {
        byte prefix = buffer[offset++];
        return prefix switch
        {
            < 0xFD => prefix,
            0xFD => ReadUInt16(buffer, ref offset),
            0xFE => ReadUInt32(buffer, ref offset),
            _ => ReadUInt64(buffer, ref offset)
        };
    }

    private static ushort ReadUInt16(byte[] buffer, ref int offset)
    {
        ushort value = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(offset, 2));
        offset += 2;
        return value;
    }

    private static uint ReadUInt32(byte[] buffer, ref int offset)
    {
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset, 4));
        offset += 4;
        return value;
    }

    private static ulong ReadUInt64(byte[] buffer, ref int offset)
    {
        ulong value = BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(offset, 8));
        offset += 8;
        return value;
    }

    private sealed class TestHarness : IDisposable
    {
        private readonly string? _previousStatePath;
        private readonly string? _previousHistoryPath;
        private readonly string _tempDirectory;

        public PoolConfig Config { get; }
        public BootProtocolStateService StateService { get; }
        public MiningApiController MiningController { get; }
        public BootPeerController PeerController { get; }
        public string StatePath => Path.Combine(_tempDirectory, "pool_state.json");

        private TestHarness(
            string tempDirectory,
            string? previousStatePath,
            string? previousHistoryPath,
            PoolConfig config,
            BootProtocolStateService stateService)
        {
            _tempDirectory = tempDirectory;
            _previousStatePath = previousStatePath;
            _previousHistoryPath = previousHistoryPath;
            Config = config;
            StateService = stateService;
            MiningController = new MiningApiController(config, stateService, NullLogger<MiningApiController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            };
            PeerController = new BootPeerController(config, stateService, NullLogger<BootPeerController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            };
        }

        public static TestHarness Create(string? currentTipBlockHash = null, int currentRoundNumber = 1)
        {
            string? previousStatePath = Environment.GetEnvironmentVariable("BOOT_PORTAL_STATE_PATH");
            string? previousHistoryPath = Environment.GetEnvironmentVariable("BOOT_PORTAL_HISTORY_PATH");
            string tempDirectory = Path.Combine(Path.GetTempPath(), $"boot-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);
            string statePath = Path.Combine(tempDirectory, "pool_state.json");
            string historyPath = Path.Combine(tempDirectory, "pool_state.history.json");
            Environment.SetEnvironmentVariable("BOOT_PORTAL_STATE_PATH", statePath);
            Environment.SetEnvironmentVariable("BOOT_PORTAL_HISTORY_PATH", historyPath);

            var config = new PoolConfig
            {
                BootNetworkId = "testnet",
                BootProtocolVersion = 1,
                WinnersListSize = Math.Max(8, SampleExpectedWinners.Count),
                PoolPayoutScript = SampleSlotZeroAddress
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
                CurrentRoundNumber = currentRoundNumber,
                CurrentTipBlockHash = currentTipBlockHash ?? SamplePrevBlockHash,
                CurrentTipBlockHeight = 945000,
                AcceptedParentBlockHashes = [currentTipBlockHash ?? SamplePrevBlockHash],
                WinnersList = SampleExpectedWinners.Select(ClonePayout).ToList(),
                OnDeckList = [],
                OnDeckProofs = []
            };
            File.WriteAllText(statePath, JsonSerializer.Serialize(seedState));

            var stateService = new BootProtocolStateService(
                config,
                new BootShareVerifier(),
                new NoOpHubContext(),
                NullLogger<BootProtocolStateService>.Instance);

            return new TestHarness(tempDirectory, previousStatePath, previousHistoryPath, config, stateService);
        }

        public void Dispose()
        {
            Thread.Sleep(1200);
            Environment.SetEnvironmentVariable("BOOT_PORTAL_STATE_PATH", _previousStatePath);
            Environment.SetEnvironmentVariable("BOOT_PORTAL_HISTORY_PATH", _previousHistoryPath);
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }

        private static PayoutInfo ClonePayout(PayoutInfo payout)
        {
            return new PayoutInfo
            {
                Value = payout.Value,
                Address = payout.Address,
                Username = payout.Username,
                Difficulty = payout.Difficulty,
                DiffString = payout.DiffString
            };
        }
    }

    private sealed class NoOpHubContext : IHubContext<PoolStatsHub>
    {
        public IHubClients Clients { get; } = new NoOpHubClients();
        public IGroupManager Groups { get; } = new NoOpGroupManager();
    }

    private sealed class NoOpHubClients : IHubClients, IHubClients<IClientProxy>
    {
        private static readonly IClientProxy Proxy = new NoOpClientProxy();

        public IClientProxy All => Proxy;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => Proxy;
        public IClientProxy Client(string connectionId) => Proxy;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => Proxy;
        public IClientProxy Group(string groupName) => Proxy;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Proxy;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => Proxy;
        public IClientProxy User(string userId) => Proxy;
        public IClientProxy Users(IReadOnlyList<string> userIds) => Proxy;
    }

    private class NoOpClientProxy : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoOpGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
