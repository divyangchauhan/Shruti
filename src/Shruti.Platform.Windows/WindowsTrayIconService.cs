namespace Shruti.Platform.Windows;

public sealed class WindowsTrayIconService : IDisposable
{
    public const uint CallbackWindowMessage = 0x8000 + 0x53;

    private const uint IconId = 1;
    private const uint LeftButtonUp = 0x0202;
    private const uint LeftButtonDoubleClick = 0x0203;
    private const uint RightButtonUp = 0x0205;

    private readonly IWindowsTrayIconApi _api;
    private IntPtr _windowHandle;
    private bool _isVisible;
    private bool _isDictationRunning;
    private bool _isDisposed;

    public WindowsTrayIconService(IWindowsTrayIconApi? api = null)
    {
        _api = api ?? new Win32TrayIconApi();
    }

    public event Action<WindowsTrayCommand>? CommandInvoked;

    public void AttachWindow(IntPtr windowHandle)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (windowHandle == IntPtr.Zero)
        {
            throw new ArgumentException("A valid window handle is required for the tray icon.", nameof(windowHandle));
        }

        if (_windowHandle != IntPtr.Zero && _windowHandle != windowHandle && _isVisible)
        {
            _api.RemoveIcon(_windowHandle, IconId);
            _isVisible = false;
        }

        _windowHandle = windowHandle;
    }

    public void SetVisible(bool isVisible)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (isVisible == _isVisible)
        {
            if (isVisible)
            {
                UpdateIcon();
            }

            return;
        }

        if (_windowHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Attach a window before displaying the tray icon.");
        }

        if (isVisible)
        {
            if (!_api.AddIcon(_windowHandle, IconId, CallbackWindowMessage, GetTooltip()))
            {
                throw new InvalidOperationException("Could not add the Shruti tray icon.");
            }

            _isVisible = true;
            return;
        }

        _api.RemoveIcon(_windowHandle, IconId);
        _isVisible = false;
    }

    public void UpdateDictationState(bool isDictationRunning)
    {
        _isDictationRunning = isDictationRunning;
        if (_isVisible)
        {
            UpdateIcon();
        }
    }

    public bool HandleWindowMessage(WindowsWindowMessage message)
    {
        if (message.Id != CallbackWindowMessage || unchecked((uint)message.WParam.ToInt64()) != IconId)
        {
            return false;
        }

        uint eventMessage = unchecked((uint)message.LParam.ToInt64());
        if (eventMessage == LeftButtonUp)
        {
            CommandInvoked?.Invoke(WindowsTrayCommand.Toggle);
            return true;
        }

        if (eventMessage == LeftButtonDoubleClick)
        {
            CommandInvoked?.Invoke(WindowsTrayCommand.ShowSettings);
            return true;
        }

        if (eventMessage == RightButtonUp)
        {
            WindowsTrayCommand? command = _api.ShowMenu(_windowHandle, _isDictationRunning);
            if (command is not null)
            {
                CommandInvoked?.Invoke(command.Value);
            }

            return true;
        }

        return false;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        if (_isVisible && _windowHandle != IntPtr.Zero)
        {
            _api.RemoveIcon(_windowHandle, IconId);
        }

        _isVisible = false;
        GC.SuppressFinalize(this);
    }

    private string GetTooltip()
    {
        return _isDictationRunning ? "Shruti - Dictation running" : "Shruti - Ready";
    }

    private void UpdateIcon()
    {
        if (!_api.UpdateIcon(_windowHandle, IconId, CallbackWindowMessage, GetTooltip()))
        {
            throw new InvalidOperationException("Could not update the Shruti tray icon.");
        }
    }
}
