namespace Shruti.Platform.Windows;

public interface IWindowsForegroundWindowTracker : IDisposable
{
    event EventHandler<IntPtr>? ForegroundWindowChanged;

    void Start();
}
