namespace Shruti.Core.Audio;

public sealed record AudioLevelFrame(
    float Peak,
    float Rms,
    DateTimeOffset CapturedAt);
