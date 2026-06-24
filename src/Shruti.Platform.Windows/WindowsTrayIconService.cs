namespace Shruti.Platform.Windows;

public sealed class WindowsTrayIconService : IDisposable
{
    public const uint CallbackWindowMessage = 0x8000 + 0x53;
    public static readonly uint TaskbarCreatedWindowMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");

    private const uint IconId = 1;
    private const uint LeftButtonUp = 0x0202;
    private const uint LeftButtonDoubleClick = 0x0203;
    private const uint RightButtonUp = 0x0205;

    private readonly IWindowsTrayIconApi _api;
    private readonly bool _ownsApi;
    private IntPtr _windowHandle;
    private bool _isVisible;
    private bool _isDictationRunning;
    private bool _areDictationCommandsEnabled = true;
    private bool _isDisposed;

    public WindowsTrayIconService(IWindowsTrayIconApi? api = null)
    {
        _api = api ?? new Win32TrayIconApi();
        _ownsApi = api is null;
        _api.CommandInvoked += Api_CommandInvoked;
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
            AddIcon();
            _isVisible = true;
            return;
        }

        _api.RemoveIcon(_windowHandle, IconId);
        _isVisible = false;
    }

    public void UpdateDictationState(bool isDictationRunning)
    {
        _isDictationRunning = isDictationRunning;
        _api.SetCommandState(_isDictationRunning, _areDictationCommandsEnabled);
        if (_isVisible)
        {
            UpdateIcon();
        }
    }

    public void SetDictationCommandsEnabled(bool isEnabled)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _areDictationCommandsEnabled = isEnabled;
        _api.SetCommandState(_isDictationRunning, _areDictationCommandsEnabled);
    }

    public bool HandleWindowMessage(WindowsWindowMessage message)
    {
        if (TaskbarCreatedWindowMessage != 0 &&
            message.Id == TaskbarCreatedWindowMessage &&
            _isVisible)
        {
            AddIcon();
            return true;
        }

        if (message.Id != CallbackWindowMessage || unchecked((uint)message.WParam.ToInt64()) != IconId)
        {
            return false;
        }

        uint eventMessage = unchecked((uint)message.LParam.ToInt64());
        if (eventMessage == LeftButtonUp)
        {
            CommandInvoked?.Invoke(_areDictationCommandsEnabled
                ? WindowsTrayCommand.Toggle
                : WindowsTrayCommand.ShowWindow);
            return true;
        }

        if (eventMessage == LeftButtonDoubleClick)
        {
            CommandInvoked?.Invoke(WindowsTrayCommand.ShowSettings);
            return true;
        }

        if (eventMessage == RightButtonUp)
        {
            WindowsTrayCommand? command = _api.ShowMenu(
                _windowHandle,
                _isDictationRunning,
                _areDictationCommandsEnabled);
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
        _api.CommandInvoked -= Api_CommandInvoked;
        if (_isVisible && _windowHandle != IntPtr.Zero)
        {
            _api.RemoveIcon(_windowHandle, IconId);
        }

        _isVisible = false;
        if (_ownsApi && _api is IDisposable disposableApi)
        {
            disposableApi.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private string GetTooltip()
    {
        return _isDictationRunning ? "Shruti - Dictation running" : "Shruti - Ready";
    }

    private void Api_CommandInvoked(WindowsTrayCommand command)
    {
        CommandInvoked?.Invoke(command);
    }

    private void UpdateIcon()
    {
        if (_api.UpdateIcon(_windowHandle, IconId, CallbackWindowMessage, GetTooltip()))
        {
            return;
        }

        AddIcon();
    }

    private void AddIcon()
    {
        _api.RemoveIcon(_windowHandle, IconId);
        if (!_api.AddIcon(_windowHandle, IconId, CallbackWindowMessage, GetTooltip()))
        {
            throw new InvalidOperationException("Could not add the Shruti tray icon.");
        }
    }

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        public static extern uint RegisterWindowMessage(string message);
    }
}
