namespace Shruti.Models;

public sealed record ModelIntegrity(ModelHashAlgorithm Algorithm, string ExpectedHash);

public sealed record ModelIntegrityVerification(bool IsMatch, string ActualHash);
