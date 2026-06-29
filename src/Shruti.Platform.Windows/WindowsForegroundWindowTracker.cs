using System.Runtime.InteropServices;

namespace Shruti.Platform.Windows;

public sealed class WindowsForegroundWindowTracker : IWindowsForegroundWindowTracker
{
    private const uint EventSystemForeground = 0x0003;
    private const uint WineventOutOfContext = 0x0000;
    private const uint WineventSkipOwnProcess = 0x0002;

    private readonly NativeMethods.WinEventProc _callback;
    private IntPtr _hook;
    private bool _isDisposed;

    public WindowsForegroundWindowTracker()
    {
        _callback = HandleWinEvent;
    }

    public event EventHandler<IntPtr>? ForegroundWindowChanged;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (_hook != IntPtr.Zero)
        {
            return;
        }

        _hook = NativeMethods.SetWinEventHook(
            EventSystemForeground,
            EventSystemForeground,
            IntPtr.Zero,
            _callback,
            idProcess: 0,
            idThread: 0,
            WineventOutOfContext | WineventSkipOwnProcess);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private void HandleWinEvent(
        IntPtr hook,
        uint eventType,
        IntPtr windowHandle,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime)
    {
        if (eventType == EventSystemForeground && windowHandle != IntPtr.Zero)
        {
            ForegroundWindowChanged?.Invoke(this, windowHandle);
        }
    }

    private static class NativeMethods
    {
        public delegate void WinEventProc(
            IntPtr hook,
            uint eventType,
            IntPtr windowHandle,
            int objectId,
            int childId,
            uint eventThread,
            uint eventTime);

        [DllImport("user32.dll")]
        public static extern IntPtr SetWinEventHook(
            uint eventMin,
            uint eventMax,
            IntPtr eventHookAssembly,
            WinEventProc eventProc,
            uint idProcess,
            uint idThread,
            uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWinEvent(IntPtr hook);
    }
}
