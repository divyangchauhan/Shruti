using Shruti.Workflow.Dictation;
using Shruti.Audio.Windows;
using Shruti.Core.Dictation;
using Shruti.Core.Platform;
using Shruti.Models;
using Shruti.Platform.Windows;
using Shruti.Storage;
using Shruti.Transcription.Abstractions;
using Shruti.Transcription.WhisperCpp;
using Shruti.Transcription.OpenVino;
using Shruti.TranscriptQuality;

namespace Shruti.App.WinUI;

public sealed class AppComposition
{
    private readonly WindowsAudioCaptureService audioCaptureService = new();
    private readonly WindowsTranscriptClipboard transcriptClipboard = new();
    private readonly WindowsPlatformModule platformModule = new();
    private readonly ISettingsRepository settingsRepository = new StorageModule().CreateSettingsRepository();
    private readonly AppDataPaths appDataPaths = AppDataPaths.CreateDefault();
    private readonly ModelCatalog modelCatalog = RecommendedModelCatalog.Create();
    private readonly HttpClient modelHttpClient = new();
    private readonly IModelManager modelManager;
    private readonly ModelCatalogEntry defaultModel;
    private readonly WhisperCppTranscriptionProvider whisperCppProvider = new(
        new WhisperCppTranscriptionEngine(new WhisperCppNativeApi()));
    private readonly OpenVinoTranscriptionProvider openVinoProvider = new();
    private readonly TranscriptionOptionsProvider transcriptionOptionsProvider;
    private readonly WindowsTargetFocusService targetFocusService;
    private readonly ITextInsertionService textInsertionService;

    public AppComposition()
    {
        defaultModel = modelCatalog.GetRequiredModel(ShrutiSettings.DefaultModelId);
        modelManager = new ModelManager(
            appDataPaths.ModelsDirectory,
            new HttpModelDownloadClient(modelHttpClient),
            new ModelIntegrityVerifier());
        targetFocusService = platformModule.CreateTargetFocusService();
        textInsertionService = platformModule.CreateTextInsertionService();
        ITranscriptionProviderRegistry transcriptionProviderRegistry = new TranscriptionProviderRegistry(
            [whisperCppProvider, openVinoProvider]);
        var benchmarkCache = new JsonTranscriptionBenchmarkCache(appDataPaths);
        transcriptionOptionsProvider = new TranscriptionOptionsProvider(
            modelCatalog,
            defaultModel,
            appDataPaths,
            new TranscriptionReadinessService(transcriptionProviderRegistry, benchmarkCache),
            transcriptionProviderRegistry,
            typeof(AppComposition).Assembly.GetName().Version?.ToString() ??
                TranscriptionBenchmarkKey.UnknownProviderVersion);
    }

    public MainWindow CreateMainWindow()
    {
        appDataPaths.EnsureCreated();
        var qualityPipeline = new TranscriptQualityPipeline(new SqliteVocabularyRepository(appDataPaths), new WindowsTextContextReader());
        var coordinator = new DictationCoordinator(
            targetFocusService,
            audioCaptureService,
            textInsertionService,
            new RoutingTranscriptionProvider(
                new TranscriptionProviderRegistry([whisperCppProvider, openVinoProvider])),
            qualityPipeline);
        var controller = new DictationShellController(
            coordinator,
            audioCaptureService,
            transcriptClipboard,
            transcriptionOptionsProvider.CreateAsync,
            async cancellationToken => (await settingsRepository.LoadAsync(cancellationToken).ConfigureAwait(false)).TranscriptQuality with
            { LocalRuntimePath = Path.Combine(AppContext.BaseDirectory, "CleanupRuntime", "llama-server.exe") });
        var triggerRouter = new DictationTriggerRouter(controller);
        var window = new MainWindow(
            controller,
            audioCaptureService,
            settingsRepository,
            transcriptionOptionsProvider,
            modelCatalog,
            modelManager,
            triggerRouter,
            targetFocusService,
            platformModule.CreateGlobalTriggerService(),
            platformModule.CreateTrayIconService(),
            platformModule.CreateWindowVisibility());
        window.Closed += (_, _) => qualityPipeline.Dispose();
        return window;
    }
}
