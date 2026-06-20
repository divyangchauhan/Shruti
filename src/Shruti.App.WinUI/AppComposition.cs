using Shruti.Workflow.Dictation;
using Shruti.Audio.Windows;
using Shruti.Core.Dictation;
using Shruti.Platform.Windows;
using Shruti.Storage;

namespace Shruti.App.WinUI;

public sealed class AppComposition
{
    private readonly MockTargetFocusService targetFocusService = new();
    private readonly WindowsAudioCaptureService audioCaptureService = new();
    private readonly MockTextInsertionService textInsertionService = new();
    private readonly MockTranscriptionProvider transcriptionProvider = new();
    private readonly MockTranscriptClipboard transcriptClipboard = new();
    private readonly WindowsPlatformModule platformModule = new();
    private readonly ISettingsRepository settingsRepository = new StorageModule().CreateSettingsRepository();

    public MainWindow CreateMainWindow()
    {
        var coordinator = new DictationCoordinator(
            targetFocusService,
            audioCaptureService,
            textInsertionService,
            transcriptionProvider);
        var controller = new DictationShellController(
            coordinator,
            audioCaptureService,
            transcriptClipboard);
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
}
