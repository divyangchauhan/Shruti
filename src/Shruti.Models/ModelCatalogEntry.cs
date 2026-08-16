using Shruti.Transcription.Abstractions;

namespace Shruti.Models;

public sealed record ModelCatalogEntry(
    string Id,
    string DisplayName,
    string ProviderId,
    string LocalFileName,
    ModelFileFormat FileFormat,
    string LanguageHint,
    long SizeBytes,
    Uri? DownloadUri,
    ModelIntegrity? Integrity,
    IReadOnlyList<ComputeBackend> SupportedBackends,
    bool IsRecommended = false,
    IReadOnlyList<ModelArtifact>? Artifacts = null)
{
    public bool IsBundle => Artifacts is { Count: > 0 };
}

public sealed record ModelArtifact(
    string RelativePath,
    Uri DownloadUri,
    long SizeBytes,
    ModelIntegrity Integrity);
