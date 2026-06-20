namespace Shruti.Audio.Windows;

public sealed class WindowsAudioDataAvailableEventArgs : EventArgs
{
    public WindowsAudioDataAvailableEventArgs(byte[] buffer, int bytesRecorded)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        if (bytesRecorded is < 0 or > int.MaxValue || bytesRecorded > buffer.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(bytesRecorded));
        }

        Buffer = buffer;
        BytesRecorded = bytesRecorded;
    }

    public byte[] Buffer { get; }

    public int BytesRecorded { get; }
}
