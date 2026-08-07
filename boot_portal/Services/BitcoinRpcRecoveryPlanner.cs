namespace boot_portal.Services;

public sealed record BitcoinRpcRecoveryPlan(
    IReadOnlyList<long> Heights,
    bool EstablishesBaseline,
    bool Reorganization);

public static class BitcoinRpcRecoveryPlanner
{
    public static BitcoinRpcRecoveryPlan Build(
        long? localHeight,
        string? localHash,
        long rpcHeight,
        string rpcBestHash)
    {
        if (!localHeight.HasValue || string.IsNullOrWhiteSpace(localHash))
        {
            return new BitcoinRpcRecoveryPlan([rpcHeight], true, false);
        }

        if (Utils.BitcoinHashes.AreEquivalent(localHash, rpcBestHash))
        {
            return localHeight.Value == rpcHeight
                ? new BitcoinRpcRecoveryPlan([], false, false)
                : new BitcoinRpcRecoveryPlan([rpcHeight], true, false);
        }

        if (localHeight.Value > rpcHeight)
        {
            return new BitcoinRpcRecoveryPlan([], false, true);
        }

        if (localHeight.Value == rpcHeight)
        {
            return new BitcoinRpcRecoveryPlan([rpcHeight], false, true);
        }

        return new BitcoinRpcRecoveryPlan(
            Enumerable.Range(
                    checked((int)(localHeight.Value + 1)),
                    checked((int)(rpcHeight - localHeight.Value)))
                .Select(height => (long)height)
                .ToList(),
            false,
            false);
    }
}
