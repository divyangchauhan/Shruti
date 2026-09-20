using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Shruti.Platform.Windows;

// A short-lived hook owned by the shortcut dialog. Never captures another app.
public sealed class WindowsShortcutCapture : IDisposable
{
    private readonly IntPtr _owner;
    private readonly NativeMethods.KeyboardProc _callback;
    private IntPtr _hook;

    public WindowsShortcutCapture(IntPtr owner, bool allowSingleKey)
    {
        _owner = owner;
        Recorder = new WindowsShortcutRecorder(allowSingleKey);
        _callback = OnKey;
        _hook = NativeMethods.SetWindowsHookEx(13, _callback, IntPtr.Zero, 0);
        if (_hook == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not start shortcut capture.");
    }

    public WindowsShortcutRecorder Recorder { get; }
    public event EventHandler? Changed;

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
        GC.SuppressFinalize(this);
    }

    private IntPtr OnKey(int code, IntPtr message, IntPtr data)
    {
        uint kind = unchecked((uint)message.ToInt64());
        if (code < 0 || data == IntPtr.Zero || _hook == IntPtr.Zero ||
            NativeMethods.GetForegroundWindow() != _owner ||
            kind is not (0x100 or 0x101 or 0x104 or 0x105))
            return NativeMethods.CallNextHookEx(_hook, code, message, data);

        try
        {
            Recorder.Update(unchecked((uint)Marshal.ReadInt32(data)), kind is 0x100 or 0x104);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            // Never unwind a managed exception into user32.
            Dispose();
        }
        return (IntPtr)1;
    }

    private static class NativeMethods
    {
        public delegate IntPtr KeyboardProc(int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int id, KeyboardProc callback, IntPtr module, uint thread);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();
    }
}
