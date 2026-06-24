using Shruti.Models;
using Shruti.Storage;
using Shruti.Transcription.Abstractions;

namespace Shruti.App.WinUI;

public sealed class TranscriptionOptionsProvider
{
    private readonly object _settingsSync = new();
    private readonly ModelCatalogEntry _defaultModel;
    private readonly AppDataPaths _appDataPaths;
    private readonly TranscriptionReadinessService _readinessService;
    private readonly string _providerVersion;
    private ShrutiSettings _settings = ShrutiSettings.Default;

    public TranscriptionOptionsProvider(
        ModelCatalogEntry defaultModel,
        AppDataPaths appDataPaths,
        TranscriptionReadinessService readinessService,
        string providerVersion)
    {
        _defaultModel = defaultModel ?? throw new ArgumentNullException(nameof(defaultModel));
        _appDataPaths = appDataPaths ?? throw new ArgumentNullException(nameof(appDataPaths));
        _readinessService = readinessService ?? throw new ArgumentNullException(nameof(readinessService));
        _providerVersion = string.IsNullOrWhiteSpace(providerVersion)
            ? TranscriptionBenchmarkKey.UnknownProviderVersion
            : providerVersion;
    }

    public ModelCatalogEntry DefaultModel => _defaultModel;

    public void ApplySettings(ShrutiSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_settingsSync)
        {
            _settings = settings;
        }
    }

    public TranscriptionSessionOptions Create()
    {
        TranscriptionReadinessResult readiness = EvaluateReadinessAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        if (!readiness.CanProceed || readiness.SelectedBackend is null)
        {
            throw new InvalidOperationException(readiness.Message);
        }

        TranscriptionModelDescriptor descriptor = CreateModelDescriptor();
        return new TranscriptionSessionOptions(
            descriptor,
            readiness.SelectedBackend.Value,
            descriptor.LanguageHint,
            TranscriptionMode.Balanced);
    }

    public Task<TranscriptionReadinessResult> EvaluateReadinessAsync(CancellationToken cancellationToken)
    {
        ShrutiSettings settings;
        lock (_settingsSync)
        {
            settings = _settings;
        }

        return _readinessService.EvaluateAsync(
            new TranscriptionReadinessRequest(
                CreateModelDescriptor(),
                settings.BackendPreference,
                settings.AllowSlowTranscription,
                _providerVersion,
                _defaultModel.Integrity?.ExpectedHash),
            cancellationToken);
    }

    public TranscriptionModelDescriptor CreateModelDescriptor()
    {
        return new TranscriptionModelDescriptor(
            _defaultModel.Id,
            _defaultModel.DisplayName,
            _defaultModel.ProviderId,
            Path.Combine(_appDataPaths.ModelsDirectory, _defaultModel.LocalFileName),
            _defaultModel.LanguageHint,
            _defaultModel.SizeBytes,
            _defaultModel.SupportedBackends.ToHashSet());
    }
}
