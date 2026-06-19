namespace Shruti.Core;

public enum DictationSessionState
{
    Idle,
    PreparingTarget,
    RequestingMicrophone,
    Recording,
    Paused,
    TranscribingFinalAudio,
    InsertingText,
    Complete,
    Cancelled,
    Failed
}
