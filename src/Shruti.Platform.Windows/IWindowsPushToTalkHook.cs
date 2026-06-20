namespace Shruti.Platform.Windows;

public interface IWindowsPushToTalkHook : IDisposable
{
    event EventHandler<WindowsPushToTalkKeyStateChangedEventArgs>? KeyStateChanged;

    void Configure(bool enabled, uint virtualKey);
}
