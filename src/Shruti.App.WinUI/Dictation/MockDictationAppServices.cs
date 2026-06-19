using Shruti.Core.Audio;
using Shruti.Core.Dictation;
using Shruti.Core.Platform;
using Shruti.Transcription.Abstractions;

namespace Shruti.App.WinUI.Dictation;

public sealed class MockDictationAppServices
{
    private MockDictationAppServices(
        MockTargetFocusService targetFocus,
        MockAudioCaptureService audioCapture,
        MockTextInsertionService textInsertion,
        MockTranscriptionProvider transcription,
        MockTranscriptClipboard clipboard)
    {
        TargetFocus = targetFocus;
        AudioCapture = audioCapture;
        TextInsertion = textInsertion;
        Transcription = transcription;
        Clipboard = clipboard;
        Coordinator = new DictationCoordinator(
            targetFocus,
            audioCapture,
            textInsertion,
            transcription);
    }

    public DictationCoordinator Coordinator { get; }

    public MockTargetFocusService TargetFocus { get; }

    public MockAudioCaptureService AudioCapture { get; }

    public MockTextInsertionService TextInsertion { get; }

    public MockTranscriptionProvider Transcription { get; }

    public MockTranscriptClipboard Clipboard { get; }

    public static MockDictationAppServices Create()
    {
        return new MockDictationAppServices(
            new MockTargetFocusService(),
            new MockAudioCaptureService(),
            new MockTextInsertionService(),
            new MockTranscriptionProvider(),
            new MockTranscriptClipboard());
    }

    public DictationShellController CreateShellController()
    {
        return new DictationShellController(
            Coordinator,
            AudioCapture,
            Clipboard);
    }

    public static TranscriptionSessionOptions CreateTranscriptionOptions()
    {
        var model = new TranscriptionModelDescriptor(
            "mock-dictation",
            "Mock dictation model",
            "mock-provider",
            "mock-dictation.gguf",
            "en",
            1024,
            new HashSet<ComputeBackend> { ComputeBackend.Cpu });

        return new TranscriptionSessionOptions(
            model,
            ComputeBackend.Cpu,
            "en",
            TranscriptionMode.Balanced);
    }
}
