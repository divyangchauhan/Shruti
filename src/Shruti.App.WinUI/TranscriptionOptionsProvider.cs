using Shruti.Models;
using Shruti.Storage;
using Shruti.Transcription.Abstractions;

namespace Shruti.App.WinUI;

public sealed class TranscriptionOptionsProvider
{
    private readonly object _settingsSync = new();
    private readonly ModelCatalog _modelCatalog;
    private readonly ModelCatalogEntry _defaultModel;
    private readonly AppDataPaths _appDataPaths;
    private readonly TranscriptionReadinessService _readinessService;
    private readonly string _providerVersion;
    private ShrutiSettings _settings = ShrutiSettings.Default;

    public TranscriptionOptionsProvider(
        ModelCatalog modelCatalog,
        ModelCatalogEntry defaultModel,
        AppDataPaths appDataPaths,
        TranscriptionReadinessService readinessService,
        string providerVersion)
    {
        _modelCatalog = modelCatalog ?? throw new ArgumentNullException(nameof(modelCatalog));
        _defaultModel = defaultModel ?? throw new ArgumentNullException(nameof(defaultModel));
        _appDataPaths = appDataPaths ?? throw new ArgumentNullException(nameof(appDataPaths));
        _readinessService = readinessService ?? throw new ArgumentNullException(nameof(readinessService));
        _providerVersion = string.IsNullOrWhiteSpace(providerVersion)
            ? TranscriptionBenchmarkKey.UnknownProviderVersion
            : providerVersion;
    }

    public ModelCatalogEntry DefaultModel => _defaultModel;

    public IReadOnlyList<ModelCatalogEntry> CatalogModels => _modelCatalog.Models;

    public void ApplySettings(ShrutiSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_settingsSync)
        {
            _settings = settings;
        }
    }

    public async Task<TranscriptionSessionOptions> CreateAsync(CancellationToken cancellationToken)
    {
        TranscriptionReadinessResult readiness = await EvaluateReadinessAsync(cancellationToken)
            .ConfigureAwait(false);
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

        ModelCatalogEntry selectedModel = GetSelectedModelEntry(settings);
        return _readinessService.EvaluateAsync(
            new TranscriptionReadinessRequest(
                CreateModelDescriptor(selectedModel),
                settings.BackendPreference,
                settings.AllowSlowTranscription,
                _providerVersion,
                selectedModel.Integrity?.ExpectedHash),
            cancellationToken);
    }

    public TranscriptionModelDescriptor CreateModelDescriptor()
    {
        ShrutiSettings settings;
        lock (_settingsSync)
        {
            settings = _settings;
        }

        return CreateModelDescriptor(GetSelectedModelEntry(settings));
    }

    public ModelCatalogEntry GetSelectedModelEntry()
    {
        ShrutiSettings settings;
        lock (_settingsSync)
        {
            settings = _settings;
        }

        return GetSelectedModelEntry(settings);
    }

    public ModelCatalogEntry GetSelectedModelEntry(ShrutiSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return FindModel(settings.SelectedModelId) ?? _defaultModel;
    }

    public ModelCatalogEntry? FindModel(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return null;
        }

        return _modelCatalog.Models.FirstOrDefault(model =>
            string.Equals(model.Id, modelId, StringComparison.Ordinal));
    }

    public TranscriptionModelDescriptor CreateModelDescriptor(ModelCatalogEntry model)
    {
        ArgumentNullException.ThrowIfNull(model);

        return new TranscriptionModelDescriptor(
            model.Id,
            model.DisplayName,
            model.ProviderId,
            Path.Combine(_appDataPaths.ModelsDirectory, model.LocalFileName),
            model.LanguageHint,
            model.SizeBytes,
            model.SupportedBackends.ToHashSet());
    }
}
