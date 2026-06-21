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
    private readonly MockTranscriptClipboard transcriptClipboard = new();
    private readonly WindowsPlatformModule platformModule = new();
    private readonly ISettingsRepository settingsRepository = new StorageModule().CreateSettingsRepository();
    private readonly AppDataPaths appDataPaths = AppDataPaths.CreateDefault();
    private readonly ModelCatalogEntry defaultModel = RecommendedModelCatalog.Create().GetRequiredModel("whisper-tiny-en");
    private readonly WhisperCppTranscriptionProvider transcriptionProvider = new(
        new WhisperCppTranscriptionEngine(new WhisperCppNativeApi()));
    private readonly ITargetFocusService targetFocusService;
    private readonly ITextInsertionService textInsertionService;

    public AppComposition()
    {
        targetFocusService = platformModule.CreateTargetFocusService();
        textInsertionService = platformModule.CreateTextInsertionService();
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
            CreateTranscriptionOptions);
        var triggerRouter = new DictationTriggerRouter(controller);
        return new MainWindow(
            controller,
            audioCaptureService,
            settingsRepository,
            triggerRouter,
            platformModule.CreateGlobalTriggerService(),
            platformModule.CreateTrayIconService(),
            platformModule.CreateWindowVisibility());
    }

    private TranscriptionSessionOptions CreateTranscriptionOptions()
    {
        var descriptor = new TranscriptionModelDescriptor(
            defaultModel.Id,
            defaultModel.DisplayName,
            defaultModel.ProviderId,
            Path.Combine(appDataPaths.ModelsDirectory, defaultModel.LocalFileName),
            defaultModel.LanguageHint,
            defaultModel.SizeBytes,
            defaultModel.SupportedBackends.ToHashSet());

        return new TranscriptionSessionOptions(
            descriptor,
            ComputeBackend.Cpu,
            descriptor.LanguageHint,
            TranscriptionMode.Balanced);
    }
}
