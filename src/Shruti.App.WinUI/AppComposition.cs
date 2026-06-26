using Shruti.Workflow.Dictation;
using Shruti.Audio.Windows;
using Shruti.Core.Dictation;
using Shruti.Core.Platform;
using Shruti.Models;
using Shruti.Platform.Windows;
using Shruti.Storage;
using Shruti.Transcription.Abstractions;
using Shruti.Transcription.WhisperCpp;

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
    private readonly WhisperCppTranscriptionProvider transcriptionProvider = new(
        new WhisperCppTranscriptionEngine(new WhisperCppNativeApi()));
    private readonly TranscriptionOptionsProvider transcriptionOptionsProvider;
    private readonly ITargetFocusService targetFocusService;
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
        ITranscriptionProviderRegistry transcriptionProviderRegistry = new TranscriptionProviderRegistry([transcriptionProvider]);
        var benchmarkCache = new JsonTranscriptionBenchmarkCache(appDataPaths);
        transcriptionOptionsProvider = new TranscriptionOptionsProvider(
            modelCatalog,
            defaultModel,
            appDataPaths,
            new TranscriptionReadinessService(transcriptionProviderRegistry, benchmarkCache),
            typeof(WhisperCppTranscriptionProvider).Assembly.GetName().Version?.ToString() ??
                TranscriptionBenchmarkKey.UnknownProviderVersion);
    }

    public MainWindow CreateMainWindow()
    {
        appDataPaths.EnsureCreated();
        var coordinator = new DictationCoordinator(
            targetFocusService,
            audioCaptureService,
            textInsertionService,
            transcriptionProvider);
        var controller = new DictationShellController(
            coordinator,
            audioCaptureService,
            transcriptClipboard,
            transcriptionOptionsProvider.Create);
        var triggerRouter = new DictationTriggerRouter(controller);
        return new MainWindow(
            controller,
            audioCaptureService,
            settingsRepository,
            transcriptionOptionsProvider,
            modelCatalog,
            modelManager,
            triggerRouter,
            platformModule.CreateGlobalTriggerService(),
            platformModule.CreateTrayIconService(),
            platformModule.CreateWindowVisibility());
    }
}
