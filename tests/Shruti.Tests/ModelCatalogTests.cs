using System.Net;
using System.Security.Cryptography;
using System.Text;
using Shruti.Models;
using Shruti.Transcription.Abstractions;
using Xunit;

namespace Shruti.Tests;

public sealed class ModelCatalogTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "Shruti.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void RecommendedCatalog_ContainsThreeVerifiedWhisperCppModels()
    {
        ModelCatalog catalog = RecommendedModelCatalog.Create();

        Assert.Equal(1, catalog.SchemaVersion);
        Assert.Equal(3, catalog.Models.Count);
        Assert.All(catalog.Models, model =>
        {
            Assert.True(model.IsRecommended);
            Assert.Equal("whisper.cpp", model.ProviderId);
            Assert.Equal(ModelFileFormat.Ggml, model.FileFormat);
            Assert.Equal(ModelHashAlgorithm.Sha1, model.Integrity?.Algorithm);
            Assert.Equal("huggingface.co", model.DownloadUri?.Host);
            Assert.Contains(ComputeBackend.Cpu, model.SupportedBackends);
            Assert.Contains(ComputeBackend.Gpu, model.SupportedBackends);
            Assert.DoesNotContain(ComputeBackend.Npu, model.SupportedBackends);
        });
    }

    [Fact]
    public void ModelCatalogJson_RoundTripsRecommendedCatalog()
    {
        ModelCatalog catalog = RecommendedModelCatalog.Create();

        string json = ModelCatalogJson.Serialize(catalog);
        ModelCatalog roundTripped = ModelCatalogJson.Deserialize(json);

        Assert.Equal(catalog.SchemaVersion, roundTripped.SchemaVersion);
        Assert.Equal(catalog.Revision, roundTripped.Revision);
        Assert.Equal(catalog.Models.Select(model => model.Id), roundTripped.Models.Select(model => model.Id));
        Assert.Equal(
            catalog.Models.Select(model => model.Integrity?.ExpectedHash),
            roundTripped.Models.Select(model => model.Integrity?.ExpectedHash));
    }

    [Fact]
    public async Task IntegrityVerifier_SupportsSha1AndSha256()
    {
        Directory.CreateDirectory(_rootPath);
        string modelPath = Path.Combine(_rootPath, "model.bin");
        await File.WriteAllBytesAsync(modelPath, "shruti"u8.ToArray());
        var verifier = new ModelIntegrityVerifier();

        ModelIntegrityVerification sha1 = await verifier.VerifyAsync(
            modelPath,
            new ModelIntegrity(ModelHashAlgorithm.Sha1, "bb37947fc21aa326865e78262323ff102c83b981"),
            CancellationToken.None);
        ModelIntegrityVerification sha256 = await verifier.VerifyAsync(
            modelPath,
            new ModelIntegrity(ModelHashAlgorithm.Sha256, "cfbff47427fb68609fc3d9b0c28b81a6b4d7621a5c81cf5170fb7da07b92c54f"),
            CancellationToken.None);
        ModelIntegrityVerification mismatch = await verifier.VerifyAsync(
            modelPath,
            new ModelIntegrity(ModelHashAlgorithm.Sha256, "deadbeef"),
            CancellationToken.None);

        Assert.True(sha1.IsMatch);
        Assert.True(sha256.IsMatch);
        Assert.False(mismatch.IsMatch);
    }

    [Fact]
    public async Task DownloadAsync_VerifiesAtomicallyPersistsAndReportsProgress()
    {
        byte[] payload = Encoding.UTF8.GetBytes("verified model contents");
        var downloadClient = new FakeModelDownloadClient(payload);
        var progress = new RecordingProgress();
        var manager = CreateManager(downloadClient);
        ModelCatalogEntry entry = CreateEntry(payload);

        ModelInstallResult installed = await manager.DownloadAsync(entry, progress, CancellationToken.None);
        ModelInstallResult duplicate = await manager.DownloadAsync(entry, progress, CancellationToken.None);
        IReadOnlyList<InstalledModel> models = await manager.ListInstalledAsync(CancellationToken.None);

        Assert.Equal(ModelInstallStatus.Installed, installed.Status);
        Assert.Equal(ModelInstallStatus.AlreadyInstalled, duplicate.Status);
        Assert.Equal(1, downloadClient.CallCount);
        Assert.NotEmpty(progress.Values);
        Assert.Equal(payload.Length, progress.Values[^1].BytesReceived);
        Assert.Single(models);
        Assert.True(models[0].IsAvailable);
        Assert.Equal(payload, await File.ReadAllBytesAsync(models[0].LocalPath));
        Assert.Equal(entry.Id, models[0].ToTranscriptionModelDescriptor().Id);
        Assert.Empty(Directory.EnumerateFiles(_rootPath, "*.partial", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task DownloadAsync_BlocksHashMismatchAndLeavesNoModelFile()
    {
        byte[] payload = "untrusted"u8.ToArray();
        var manager = CreateManager(new FakeModelDownloadClient(payload));
        ModelCatalogEntry entry = CreateEntry(payload) with
        {
            Integrity = new ModelIntegrity(ModelHashAlgorithm.Sha256, "deadbeef")
        };

        ModelInstallResult result = await manager.DownloadAsync(entry, progress: null, CancellationToken.None);

        Assert.Equal(ModelInstallStatus.IntegrityFailed, result.Status);
        Assert.False(File.Exists(Path.Combine(_rootPath, entry.LocalFileName)));
        Assert.Empty(await manager.ListInstalledAsync(CancellationToken.None));
        Assert.Empty(Directory.EnumerateFiles(_rootPath, "*.partial", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task DownloadAsync_RegistersAnExistingVerifiedModelForOfflineDiscovery()
    {
        Directory.CreateDirectory(_rootPath);
        byte[] payload = "manually preseeded model"u8.ToArray();
        ModelCatalogEntry entry = CreateEntry(payload);
        await File.WriteAllBytesAsync(Path.Combine(_rootPath, entry.LocalFileName), payload);
        var downloadClient = new FakeModelDownloadClient([]);
        var manager = CreateManager(downloadClient);

        ModelInstallResult result = await manager.DownloadAsync(entry, progress: null, CancellationToken.None);
        IReadOnlyList<InstalledModel> installed = await manager.ListInstalledAsync(CancellationToken.None);

        Assert.Equal(ModelInstallStatus.AlreadyInstalled, result.Status);
        Assert.Equal(0, downloadClient.CallCount);
        Assert.Single(installed);
        Assert.True(installed[0].IsAvailable);
    }

    [Fact]
    public async Task ImportAndRemoveAsync_CopiesLocalModelAndKeepsSourceFile()
    {
        Directory.CreateDirectory(_rootPath);
        byte[] payload = "imported offline model"u8.ToArray();
        string sourcePath = Path.Combine(_rootPath, "source.bin");
        await File.WriteAllBytesAsync(sourcePath, payload);
        var manager = CreateManager(new FakeModelDownloadClient([]));
        ModelCatalogEntry entry = CreateEntry(payload) with
        {
            Id = "imported-model",
            LocalFileName = "imported-model.bin",
            DownloadUri = null,
            Integrity = null
        };

        ModelInstallResult imported = await manager.ImportAsync(
            new ModelImportRequest(entry, sourcePath),
            CancellationToken.None);
        IReadOnlyList<InstalledModel> installed = await manager.ListInstalledAsync(CancellationToken.None);

        Assert.Equal(ModelInstallStatus.Installed, imported.Status);
        Assert.True(File.Exists(sourcePath));
        Assert.Single(installed);
        Assert.Equal(ModelHashAlgorithm.Sha256, installed[0].CatalogEntry.Integrity?.Algorithm);
        Assert.Equal(payload, await File.ReadAllBytesAsync(installed[0].LocalPath));

        bool removed = await manager.RemoveAsync(entry.Id, CancellationToken.None);
        bool removedAgain = await manager.RemoveAsync(entry.Id, CancellationToken.None);

        Assert.True(removed);
        Assert.False(removedAgain);
        Assert.False(File.Exists(Path.Combine(_rootPath, entry.LocalFileName)));
    }

    [Fact]
    public async Task HttpDownloadClient_WritesStreamedResponse()
    {
        byte[] payload = "http model"u8.ToArray();
        using var httpClient = new HttpClient(new StaticResponseHandler(payload));
        var client = new HttpModelDownloadClient(httpClient);
        Directory.CreateDirectory(_rootPath);
        string destinationPath = Path.Combine(_rootPath, "http.bin");

        await client.DownloadAsync(new Uri("https://models.example/http.bin"), destinationPath, progress: null, CancellationToken.None);

        Assert.Equal(payload, await File.ReadAllBytesAsync(destinationPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }

    private ModelManager CreateManager(IModelDownloadClient downloadClient)
    {
        return new ModelManager(_rootPath, downloadClient, new ModelIntegrityVerifier());
    }

    private static ModelCatalogEntry CreateEntry(byte[] payload)
    {
        return new ModelCatalogEntry(
            "test-model",
            "Test model",
            "test-provider",
            "test-model.bin",
            ModelFileFormat.Ggml,
            "en",
            payload.Length,
            new Uri("https://models.example/test-model.bin"),
            new ModelIntegrity(ModelHashAlgorithm.Sha256, Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant()),
            [ComputeBackend.Cpu]);
    }

    private sealed class FakeModelDownloadClient : IModelDownloadClient
    {
        private readonly byte[] _payload;

        public FakeModelDownloadClient(byte[] payload)
        {
            _payload = payload;
        }

        public int CallCount { get; private set; }

        public async Task DownloadAsync(
            Uri source,
            string destinationPath,
            IProgress<ModelDownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            CallCount++;
            await File.WriteAllBytesAsync(destinationPath, _payload, cancellationToken);
            progress?.Report(new ModelDownloadProgress(_payload.Length, _payload.Length));
        }
    }

    private sealed class RecordingProgress : IProgress<ModelDownloadProgress>
    {
        public List<ModelDownloadProgress> Values { get; } = [];

        public void Report(ModelDownloadProgress value)
        {
            Values.Add(value);
        }
    }

    private sealed class StaticResponseHandler : HttpMessageHandler
    {
        private readonly byte[] _payload;

        public StaticResponseHandler(byte[] payload)
        {
            _payload = payload;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_payload)
            });
        }
    }
}
