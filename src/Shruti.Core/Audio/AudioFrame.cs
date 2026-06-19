namespace Shruti.Core.Audio;

public sealed record AudioFrame(
    ReadOnlyMemory<byte> PcmAudio,
    TimeSpan Timestamp);
