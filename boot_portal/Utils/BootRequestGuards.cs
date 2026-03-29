namespace boot_portal.Utils;

public readonly record struct BootRequestValidationFailure(int StatusCode, string Reason);

public static class BootRequestGuards
{
    public static BootRequestValidationFailure? ValidateShareRequest(
        PoolConfig config,
        HttpRequest request,
        string? minerAddress,
        string? headerHex,
        string? coinbaseHex,
        IReadOnlyCollection<string>? merklePath)
    {
        if (request.ContentLength.HasValue && request.ContentLength.Value > config.MaxShareRequestBytes)
        {
            return new BootRequestValidationFailure(StatusCodes.Status413PayloadTooLarge, "Share payload exceeds configured size limit");
        }

        if (string.IsNullOrWhiteSpace(minerAddress))
        {
            return new BootRequestValidationFailure(StatusCodes.Status400BadRequest, "Missing miner address");
        }

        if (string.IsNullOrWhiteSpace(headerHex))
        {
            return new BootRequestValidationFailure(StatusCodes.Status400BadRequest, "Missing block header");
        }

        string normalizedHeader = headerHex.Trim();
        if (normalizedHeader.Length != 160 || !IsHex(normalizedHeader))
        {
            return new BootRequestValidationFailure(StatusCodes.Status400BadRequest, "Block header must be 80 bytes of hex");
        }

        if (string.IsNullOrWhiteSpace(coinbaseHex))
        {
            return new BootRequestValidationFailure(StatusCodes.Status400BadRequest, "Missing coinbase transaction");
        }

        string normalizedCoinbase = coinbaseHex.Trim();
        if (normalizedCoinbase.Length > config.MaxCoinbaseHexChars)
        {
            return new BootRequestValidationFailure(StatusCodes.Status400BadRequest, "Coinbase transaction exceeds configured size limit");
        }

        if (!IsHex(normalizedCoinbase))
        {
            return new BootRequestValidationFailure(StatusCodes.Status400BadRequest, "Coinbase transaction must be hex");
        }

        if (merklePath != null && merklePath.Count > config.MaxMerklePathEntries)
        {
            return new BootRequestValidationFailure(StatusCodes.Status400BadRequest, "Merkle path exceeds configured size limit");
        }

        if (merklePath != null)
        {
            foreach (string merkleHash in merklePath)
            {
                string normalizedHash = merkleHash?.Trim() ?? string.Empty;
                if (normalizedHash.Length != 64 || !IsHex(normalizedHash))
                {
                    return new BootRequestValidationFailure(StatusCodes.Status400BadRequest, "Merkle path entries must be 32-byte hex hashes");
                }
            }
        }

        return null;
    }

    private static bool IsHex(string value)
    {
        if (value.Length == 0 || value.Length % 2 != 0)
        {
            return false;
        }

        foreach (char c in value)
        {
            if (!Uri.IsHexDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}
