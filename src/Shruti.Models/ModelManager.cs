using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shruti.Models;

public sealed class ModelManager : IModelManager
{
    private const string MetadataSuffix = ".shruti-model.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IModelDownloadClient _downloadClient;
    private readonly IModelIntegrityVerifier _integrityVerifier;

    public ModelManager(
        string modelsDirectory,
        IModelDownloadClient downloadClient,
        IModelIntegrityVerifier integrityVerifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelsDirectory);

        ModelsDirectory = Path.GetFullPath(modelsDirectory);
        _downloadClient = downloadClient ?? throw new ArgumentNullException(nameof(downloadClient));
        _integrityVerifier = integrityVerifier ?? throw new ArgumentNullException(nameof(integrityVerifier));
    }

    public string ModelsDirectory { get; }

    public async Task<IReadOnlyList<InstalledModel>> ListInstalledAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(ModelsDirectory))
        {
            return [];
        }

        var installed = new List<InstalledModel>();
        foreach (string metadataPath in Directory.EnumerateFiles(ModelsDirectory, $"*{MetadataSuffix}"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            InstalledModel? model = await ReadMetadataAsync(metadataPath, cancellationToken).ConfigureAwait(false);
            if (model is not null &&
                IsWithinModelsDirectory(model.LocalPath) &&
                (File.Exists(model.LocalPath) || Directory.Exists(model.LocalPath)))
            {
                installed.Add(model);
            }
        }

        return installed.OrderBy(model => model.CatalogEntry.DisplayName, StringComparer.Ordinal).ToArray();
    }

    public async Task<ModelInstallResult> DownloadAsync(
        ModelCatalogEntry entry,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.IsBundle)
        {
            return await DownloadBundleAsync(entry, progress, cancellationToken).ConfigureAwait(false);
        }

        if (entry.DownloadUri is null || entry.Integrity is null)
        {
            return new ModelInstallResult(
                ModelInstallStatus.Failed,
                Message: "A downloadable model requires a source URL and expected integrity hash.");
        }

        try
        {
            string destinationPath = GetDestinationPath(entry);
            Directory.CreateDirectory(ModelsDirectory);

            ModelInstallResult? existing = await TryGetExistingInstallAsync(entry, destinationPath, cancellationToken)
                .ConfigureAwait(false);
            if (existing is not null)
            {
                return existing;
            }

            string partialPath = CreatePartialPath(destinationPath);
            try
            {
                await _downloadClient
                    .DownloadAsync(entry.DownloadUri, partialPath, progress, cancellationToken)
                    .ConfigureAwait(false);

                return await CompleteInstallAsync(
                        entry,
                        partialPath,
                        destinationPath,
                        ModelInstallSource.Download,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                DeleteIfExists(partialPath);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new ModelInstallResult(ModelInstallStatus.Failed, Message: exception.Message, Error: exception);
        }
    }

    public async Task<ModelInstallResult> ImportAsync(
        ModelImportRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.CatalogEntry);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourcePath);

        if (!File.Exists(request.SourcePath))
        {
            return new ModelInstallResult(
                ModelInstallStatus.Failed,
                Message: $"The model import file does not exist: {request.SourcePath}");
        }

        try
        {
            string destinationPath = GetDestinationPath(request.CatalogEntry);
            Directory.CreateDirectory(ModelsDirectory);
            string partialPath = CreatePartialPath(destinationPath);

            try
            {
                await CopyFileAsync(request.SourcePath, partialPath, cancellationToken).ConfigureAwait(false);

                ModelCatalogEntry entry = request.CatalogEntry;
                if (entry.Integrity is null)
                {
                    string hash = await _integrityVerifier
                        .CalculateHashAsync(partialPath, ModelHashAlgorithm.Sha256, cancellationToken)
                        .ConfigureAwait(false);
                    entry = entry with { Integrity = new ModelIntegrity(ModelHashAlgorithm.Sha256, hash) };
                }

                return await CompleteInstallAsync(
                        entry,
                        partialPath,
                        destinationPath,
                        ModelInstallSource.Import,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                DeleteIfExists(partialPath);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new ModelInstallResult(ModelInstallStatus.Failed, Message: exception.Message, Error: exception);
        }
    }

    public async Task<bool> RemoveAsync(string modelId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        cancellationToken.ThrowIfCancellationRequested();

        string metadataPath = GetMetadataPath(modelId);
        InstalledModel? installed = await ReadMetadataAsync(metadataPath, cancellationToken).ConfigureAwait(false);
        if (installed is null)
        {
            return false;
        }

        if (!IsWithinModelsDirectory(installed.LocalPath))
        {
            throw new InvalidOperationException("Refusing to remove a model outside the configured model directory.");
        }

        if (File.Exists(installed.LocalPath))
        {
            File.Delete(installed.LocalPath);
        }
        else if (Directory.Exists(installed.LocalPath))
        {
            Directory.Delete(installed.LocalPath, recursive: true);
        }

        DeleteIfExists(metadataPath);
        return true;
    }

    private async Task<ModelInstallResult?> TryGetExistingInstallAsync(
        ModelCatalogEntry entry,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(destinationPath))
        {
            return null;
        }

        ModelIntegrityVerification verification = await _integrityVerifier
            .VerifyAsync(destinationPath, entry.Integrity!, cancellationToken)
            .ConfigureAwait(false);
        if (!verification.IsMatch)
        {
            return null;
        }

        InstalledModel? existingMetadata = await ReadMetadataAsync(GetMetadataPath(entry.Id), cancellationToken)
            .ConfigureAwait(false);
        InstalledModel installed = existingMetadata ?? CreateInstalledModel(
            entry,
            destinationPath,
            ModelInstallSource.Download);
        if (existingMetadata is null)
        {
            await WriteMetadataAsync(installed, cancellationToken).ConfigureAwait(false);
        }

        return new ModelInstallResult(ModelInstallStatus.AlreadyInstalled, installed);
    }

    private async Task<ModelInstallResult> DownloadBundleAsync(
        ModelCatalogEntry entry,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ModelArtifact> artifacts = entry.Artifacts!;
        if (artifacts.Count == 0)
        {
            return new ModelInstallResult(ModelInstallStatus.Failed, Message: "A model bundle requires artifacts.");
        }

        try
        {
            string destinationPath = GetDestinationPath(entry);
            Directory.CreateDirectory(ModelsDirectory);

            if (Directory.Exists(destinationPath) &&
                await VerifyBundleAsync(destinationPath, artifacts, cancellationToken).ConfigureAwait(false))
            {
                InstalledModel? existingMetadata = await ReadMetadataAsync(
                        GetMetadataPath(entry.Id),
                        cancellationToken)
                    .ConfigureAwait(false);
                InstalledModel installed = existingMetadata ?? CreateInstalledModel(
                    entry,
                    destinationPath,
                    ModelInstallSource.Download,
                    artifacts.Sum(artifact => artifact.SizeBytes));
                if (existingMetadata is null)
                {
                    await WriteMetadataAsync(installed, cancellationToken).ConfigureAwait(false);
                }

                return new ModelInstallResult(ModelInstallStatus.AlreadyInstalled, installed);
            }

            string partialPath = CreatePartialPath(destinationPath);
            Directory.CreateDirectory(partialPath);
            long completedBytes = 0;
            long totalBytes = artifacts.Sum(artifact => artifact.SizeBytes);
            try
            {
                foreach (ModelArtifact artifact in artifacts)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string artifactPath = GetArtifactPath(partialPath, artifact.RelativePath);
                    string? parentDirectory = Path.GetDirectoryName(artifactPath);
                    if (!string.IsNullOrEmpty(parentDirectory))
                    {
                        Directory.CreateDirectory(parentDirectory);
                    }

                    long completedBeforeArtifact = completedBytes;
                    IProgress<ModelDownloadProgress>? artifactProgress = progress is null
                        ? null
                        : new DelegatingProgress<ModelDownloadProgress>(value => progress.Report(
                            new ModelDownloadProgress(completedBeforeArtifact + value.BytesReceived, totalBytes)));
                    await _downloadClient.DownloadAsync(
                            artifact.DownloadUri,
                            artifactPath,
                            artifactProgress,
                            cancellationToken)
                        .ConfigureAwait(false);

                    ModelIntegrityVerification verification = await _integrityVerifier
                        .VerifyAsync(artifactPath, artifact.Integrity, cancellationToken)
                        .ConfigureAwait(false);
                    if (!verification.IsMatch)
                    {
                        return new ModelInstallResult(
                            ModelInstallStatus.IntegrityFailed,
                            Message: $"The {artifact.Integrity.Algorithm} hash did not match for '{artifact.RelativePath}'.");
                    }

                    completedBytes += artifact.SizeBytes;
                    progress?.Report(new ModelDownloadProgress(completedBytes, totalBytes));
                }

                if (Directory.Exists(destinationPath))
                {
                    Directory.Delete(destinationPath, recursive: true);
                }

                Directory.Move(partialPath, destinationPath);
                InstalledModel installed = CreateInstalledModel(
                    entry,
                    destinationPath,
                    ModelInstallSource.Download,
                    completedBytes);
                await WriteMetadataAsync(installed, cancellationToken).ConfigureAwait(false);
                return new ModelInstallResult(ModelInstallStatus.Installed, installed);
            }
            finally
            {
                DeleteDirectoryIfExists(partialPath);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new ModelInstallResult(ModelInstallStatus.Failed, Message: exception.Message, Error: exception);
        }
    }

    private async Task<bool> VerifyBundleAsync(
        string bundlePath,
        IReadOnlyList<ModelArtifact> artifacts,
        CancellationToken cancellationToken)
    {
        foreach (ModelArtifact artifact in artifacts)
        {
            string artifactPath = GetArtifactPath(bundlePath, artifact.RelativePath);
            if (!File.Exists(artifactPath))
            {
                return false;
            }

            ModelIntegrityVerification verification = await _integrityVerifier
                .VerifyAsync(artifactPath, artifact.Integrity, cancellationToken)
                .ConfigureAwait(false);
            if (!verification.IsMatch)
            {
                return false;
            }
        }

        return true;
    }

    private async Task<ModelInstallResult> CompleteInstallAsync(
        ModelCatalogEntry entry,
        string partialPath,
        string destinationPath,
        ModelInstallSource source,
        CancellationToken cancellationToken)
    {
        ModelIntegrityVerification verification = await _integrityVerifier
            .VerifyAsync(partialPath, entry.Integrity!, cancellationToken)
            .ConfigureAwait(false);
        if (!verification.IsMatch)
        {
            return new ModelInstallResult(
                ModelInstallStatus.IntegrityFailed,
                Message: $"The {entry.Integrity!.Algorithm} hash did not match for '{entry.DisplayName}'.");
        }

        File.Move(partialPath, destinationPath, overwrite: true);
        InstalledModel installed = CreateInstalledModel(entry, destinationPath, source);
        await WriteMetadataAsync(installed, cancellationToken).ConfigureAwait(false);
        return new ModelInstallResult(ModelInstallStatus.Installed, installed);
    }

    private InstalledModel CreateInstalledModel(
        ModelCatalogEntry entry,
        string destinationPath,
        ModelInstallSource source,
        long? sizeBytes = null)
    {
        return new InstalledModel(
            entry,
            destinationPath,
            source,
            DateTimeOffset.UtcNow,
            sizeBytes ?? new FileInfo(destinationPath).Length,
            IntegrityVerified: true);
    }

    private string GetDestinationPath(ModelCatalogEntry entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.LocalFileName);

        if (!string.Equals(Path.GetFileName(entry.LocalFileName), entry.LocalFileName, StringComparison.Ordinal))
        {
            throw new ArgumentException("Model file names must not contain directory components.", nameof(entry));
        }

        string destinationPath = Path.Combine(ModelsDirectory, entry.LocalFileName);
        if (!IsWithinModelsDirectory(destinationPath))
        {
            throw new InvalidOperationException("The model file path must remain within the configured model directory.");
        }

        return destinationPath;
    }

    private string GetArtifactPath(string bundlePath, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        string fullPath = Path.GetFullPath(Path.Combine(bundlePath, relativePath));
        string relativeToBundle = Path.GetRelativePath(bundlePath, fullPath);
        if (relativeToBundle.Equals("..", StringComparison.Ordinal) ||
            relativeToBundle.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            Path.IsPathRooted(relativeToBundle))
        {
            throw new ArgumentException("Model artifact paths must remain within the model bundle.", nameof(relativePath));
        }

        return fullPath;
    }

    private string GetMetadataPath(string modelId)
    {
        byte[] identifier = Encoding.UTF8.GetBytes(modelId);
        string hash = Convert.ToHexString(SHA256.HashData(identifier)).ToLowerInvariant();
        return Path.Combine(ModelsDirectory, $"{hash}{MetadataSuffix}");
    }

    private string CreatePartialPath(string destinationPath)
    {
        return $"{destinationPath}.{Guid.NewGuid():N}.partial";
    }

    private bool IsWithinModelsDirectory(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string relativePath = Path.GetRelativePath(ModelsDirectory, fullPath);
        return !relativePath.Equals("..", StringComparison.Ordinal) &&
            !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
            !Path.IsPathRooted(relativePath);
    }

    private async Task<InstalledModel?> ReadMetadataAsync(string metadataPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(metadataPath))
        {
            return null;
        }

        string json = await File.ReadAllTextAsync(metadataPath, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<InstalledModel>(json, JsonOptions);
    }

    private async Task WriteMetadataAsync(InstalledModel installed, CancellationToken cancellationToken)
    {
        string metadataPath = GetMetadataPath(installed.CatalogEntry.Id);
        string temporaryPath = CreatePartialPath(metadataPath);

        try
        {
            string json = JsonSerializer.Serialize(installed, JsonOptions);
            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, metadataPath, overwrite: true);
        }
        finally
        {
            DeleteIfExists(temporaryPath);
        }
    }

    private static async Task CopyFileAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81_920,
            useAsync: true);
        await using var output = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81_920,
            useAsync: true);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class DelegatingProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
