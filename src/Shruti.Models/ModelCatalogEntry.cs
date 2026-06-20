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
    bool IsRecommended = false);
