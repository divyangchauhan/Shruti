namespace Shruti.Audio.Windows;

public sealed class WindowsAudioCaptureStoppedEventArgs : EventArgs
{
    public WindowsAudioCaptureStoppedEventArgs(Exception? exception)
    {
        Exception = exception;
    }

    public Exception? Exception { get; }
}
