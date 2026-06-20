using Shruti.Transcription.Abstractions;

namespace Shruti.Models;

public enum ModelInstallSource
{
    Download,
    Import
}

public enum ModelInstallStatus
{
    Installed,
    AlreadyInstalled,
    IntegrityFailed,
    Failed
}

public sealed record InstalledModel(
    ModelCatalogEntry CatalogEntry,
    string LocalPath,
    ModelInstallSource Source,
    DateTimeOffset InstalledAt,
    long SizeBytes,
    bool IntegrityVerified)
{
    public bool IsAvailable => IntegrityVerified && File.Exists(LocalPath);

    public TranscriptionModelDescriptor ToTranscriptionModelDescriptor()
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException("The installed model is not available for transcription.");
        }

        return new TranscriptionModelDescriptor(
            CatalogEntry.Id,
            CatalogEntry.DisplayName,
            CatalogEntry.ProviderId,
            LocalPath,
            CatalogEntry.LanguageHint,
            SizeBytes,
            CatalogEntry.SupportedBackends.ToHashSet());
    }
}

public sealed record ModelInstallResult(
    ModelInstallStatus Status,
    InstalledModel? Model = null,
    string? Message = null,
    Exception? Error = null)
{
    public bool Succeeded => Status is ModelInstallStatus.Installed or ModelInstallStatus.AlreadyInstalled;
}

public sealed record ModelImportRequest(ModelCatalogEntry CatalogEntry, string SourcePath);

public sealed record ModelDownloadProgress(long BytesReceived, long? TotalBytes)
{
    public double? Fraction => TotalBytes is > 0 ? (double)BytesReceived / TotalBytes : null;
}
