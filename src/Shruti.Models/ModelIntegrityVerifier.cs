using System.Security.Cryptography;

namespace Shruti.Models;

public sealed class ModelIntegrityVerifier : IModelIntegrityVerifier
{
    public async Task<ModelIntegrityVerification> VerifyAsync(
        string filePath,
        ModelIntegrity integrity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrity);

        string actualHash = await CalculateHashAsync(filePath, integrity.Algorithm, cancellationToken)
            .ConfigureAwait(false);
        bool isMatch = string.Equals(
            integrity.ExpectedHash,
            actualHash,
            StringComparison.OrdinalIgnoreCase);

        return new ModelIntegrityVerification(isMatch, actualHash);
    }

    public async Task<string> CalculateHashAsync(
        string filePath,
        ModelHashAlgorithm algorithm,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81_920,
            useAsync: true);
        using HashAlgorithm hashAlgorithm = algorithm switch
        {
            ModelHashAlgorithm.Sha1 => SHA1.Create(),
            ModelHashAlgorithm.Sha256 => SHA256.Create(),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm))
        };

        byte[] hash = await hashAlgorithm.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
