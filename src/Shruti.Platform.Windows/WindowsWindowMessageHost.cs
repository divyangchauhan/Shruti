using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Shruti.Platform.Windows;

public sealed class WindowsWindowMessageHost : IDisposable
{
    private readonly NativeMethods.SubclassProc _subclassProc;
    private readonly UIntPtr _subclassId = (UIntPtr)0x5348;
    private GCHandle _selfHandle;
    private IntPtr _windowHandle;
    private bool _isDisposed;

    public WindowsWindowMessageHost(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            throw new ArgumentException("A valid window handle is required.", nameof(windowHandle));
        }

        _windowHandle = windowHandle;
        _subclassProc = SubclassWindowProcedure;
        _selfHandle = GCHandle.Alloc(this);

        if (!NativeMethods.SetWindowSubclass(
                _windowHandle,
                _subclassProc,
                _subclassId,
                GCHandle.ToIntPtr(_selfHandle)))
        {
            int error = Marshal.GetLastWin32Error();
            _selfHandle.Free();
            _windowHandle = IntPtr.Zero;
            throw new Win32Exception(error, "Could not attach the native window message host.");
        }
    }

    public event Func<WindowsWindowMessage, bool>? MessageReceived;

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        if (_windowHandle != IntPtr.Zero)
        {
            NativeMethods.RemoveWindowSubclass(_windowHandle, _subclassProc, _subclassId);
            _windowHandle = IntPtr.Zero;
        }

        if (_selfHandle.IsAllocated)
        {
            _selfHandle.Free();
        }

        GC.SuppressFinalize(this);
    }

    private IntPtr SubclassWindowProcedure(
        IntPtr windowHandle,
        uint message,
        IntPtr windowParameter,
        IntPtr lParameter,
        UIntPtr subclassId,
        IntPtr referenceData)
    {
        if (referenceData != IntPtr.Zero)
        {
            try
            {
                var host = (WindowsWindowMessageHost?)GCHandle.FromIntPtr(referenceData).Target;
                if (host?.HandleMessage(new WindowsWindowMessage(message, windowParameter, lParameter)) == true)
                {
                    return IntPtr.Zero;
                }
            }
            catch
            {
                // Native callback boundaries must not propagate managed exceptions into comctl32.
            }
        }

        return NativeMethods.DefSubclassProc(windowHandle, message, windowParameter, lParameter);
    }

    private bool HandleMessage(WindowsWindowMessage message)
    {
        Delegate[] handlers = MessageReceived?.GetInvocationList() ?? Array.Empty<Delegate>();
        foreach (Delegate handler in handlers)
        {
            if (((Func<WindowsWindowMessage, bool>)handler)(message))
            {
                return true;
            }
        }

        return false;
    }

    private static class NativeMethods
    {
        public delegate IntPtr SubclassProc(
            IntPtr windowHandle,
            uint message,
            IntPtr windowParameter,
            IntPtr lParameter,
            UIntPtr subclassId,
            IntPtr referenceData);

        [DllImport("comctl32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowSubclass(
            IntPtr windowHandle,
            SubclassProc subclassProcedure,
            UIntPtr subclassId,
            IntPtr referenceData);

        [DllImport("comctl32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RemoveWindowSubclass(
            IntPtr windowHandle,
            SubclassProc subclassProcedure,
            UIntPtr subclassId);

        [DllImport("comctl32.dll")]
        public static extern IntPtr DefSubclassProc(
            IntPtr windowHandle,
            uint message,
            IntPtr windowParameter,
            IntPtr lParameter);
    }
}
