namespace Shruti.Models;

public interface IModelIntegrityVerifier
{
    Task<ModelIntegrityVerification> VerifyAsync(
        string filePath,
        ModelIntegrity integrity,
        CancellationToken cancellationToken);

    Task<string> CalculateHashAsync(
        string filePath,
        ModelHashAlgorithm algorithm,
        CancellationToken cancellationToken);
}
