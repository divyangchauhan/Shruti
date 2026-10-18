namespace Shruti.Core;

public enum DictationSessionState
{
    Idle,
    PreparingTarget,
    RequestingMicrophone,
    Recording,
    Paused,
    TranscribingFinalAudio,
    RefiningTranscript,
    InsertingText,
    Complete,
    Cancelled,
    Failed
}
