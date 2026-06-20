namespace Shruti.Platform.Windows;

public sealed class WindowsPushToTalkKeyStateChangedEventArgs : EventArgs
{
    public WindowsPushToTalkKeyStateChangedEventArgs(bool isPressed)
    {
        IsPressed = isPressed;
    }

    public bool IsPressed { get; }
}
