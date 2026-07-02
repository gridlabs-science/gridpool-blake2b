namespace boot_portal.Models;

public static class BootCandidateStateSelection
{
    public static bool ShouldImportCandidate(
        double remoteTotalDifficulty,
        double localTotalDifficulty,
        string? remoteStateId,
        string? localCandidateStateId)
    {
        if (string.Equals(remoteStateId, localCandidateStateId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return remoteTotalDifficulty > localTotalDifficulty;
    }
}
