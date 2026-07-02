using System.Runtime.InteropServices;
using System.Text;

namespace Shruti.Platform.Windows;

public sealed class Win32Windowing : IWindowsWindowing
{
    private const int ShowWindowRestore = 9;
    private const int WindowClassNameCapacity = 256;
    private const ushort VirtualKeyMenu = 0x12;

    public IntPtr GetForegroundWindow()
    {
        return NativeMethods.GetForegroundWindow();
    }

    public WindowsWindowSnapshot? CaptureWindow(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return null;
        }

        uint threadId = NativeMethods.GetWindowThreadProcessId(windowHandle, out uint processId);
        if (threadId == 0 || processId == 0)
        {
            return null;
        }

        return new WindowsWindowSnapshot(
            windowHandle,
            checked((int)processId),
            checked((int)threadId),
            GetWindowTitle(windowHandle));
    }

    public bool IsWindow(IntPtr windowHandle)
    {
        return NativeMethods.IsWindow(windowHandle);
    }

    public bool IsMinimized(IntPtr windowHandle)
    {
        return NativeMethods.IsIconic(windowHandle);
    }

    public bool RestoreWindow(IntPtr windowHandle)
    {
        return NativeMethods.ShowWindow(windowHandle, ShowWindowRestore);
    }

    public bool SetForegroundWindow(IntPtr windowHandle)
    {
        return NativeMethods.SetForegroundWindow(windowHandle);
    }

    public string? GetWindowClassName(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return null;
        }

        var builder = new StringBuilder(WindowClassNameCapacity);
        int copied = NativeMethods.GetClassName(windowHandle, builder, builder.Capacity);
        return copied > 0 ? builder.ToString() : null;
    }

    public bool SetForegroundWindowWithThreadAttach(IntPtr windowHandle, int windowThreadId)
    {
        uint currentThreadId = NativeMethods.GetCurrentThreadId();
        uint targetThreadId = windowThreadId > 0
            ? checked((uint)windowThreadId)
            : NativeMethods.GetWindowThreadProcessId(windowHandle, out _);
        uint foregroundThreadId = NativeMethods.GetWindowThreadProcessId(
            NativeMethods.GetForegroundWindow(),
            out _);

        bool attachedToTarget = targetThreadId != 0 &&
            targetThreadId != currentThreadId &&
            NativeMethods.AttachThreadInput(currentThreadId, targetThreadId, true);
        bool attachedToForeground = foregroundThreadId != 0 &&
            foregroundThreadId != currentThreadId &&
            foregroundThreadId != targetThreadId &&
            NativeMethods.AttachThreadInput(currentThreadId, foregroundThreadId, true);

        try
        {
            NativeMethods.BringWindowToTop(windowHandle);
            return NativeMethods.SetForegroundWindow(windowHandle);
        }
        finally
        {
            if (attachedToForeground)
            {
                NativeMethods.AttachThreadInput(currentThreadId, foregroundThreadId, false);
            }

            if (attachedToTarget)
            {
                NativeMethods.AttachThreadInput(currentThreadId, targetThreadId, false);
            }
        }
    }

    public bool SendForegroundPermissionInput()
    {
        // Injecting a benign Alt press/release marks this process as the most
        // recent input sender, which satisfies the SetForegroundWindow
        // foreground-lock rules when nothing else does.
        var inputs = new NativeMethods.Input[]
        {
            NativeMethods.Input.ForVirtualKey(VirtualKeyMenu, isKeyUp: false),
            NativeMethods.Input.ForVirtualKey(VirtualKeyMenu, isKeyUp: true)
        };

        uint sent = NativeMethods.SendInput(
            checked((uint)inputs.Length),
            inputs,
            System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.Input>());
        return sent == inputs.Length;
    }

    private static string? GetWindowTitle(IntPtr windowHandle)
    {
        int length = NativeMethods.GetWindowTextLength(windowHandle);
        if (length <= 0)
        {
            return null;
        }

        var builder = new StringBuilder(length + 1);
        int copied = NativeMethods.GetWindowText(windowHandle, builder, builder.Capacity);
        return copied > 0 ? builder.ToString() : null;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", EntryPoint = "GetWindowTextLengthW", SetLastError = true)]
        public static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", EntryPoint = "GetWindowTextW", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(
            IntPtr hWnd,
            StringBuilder text,
            int maxCount);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(
            IntPtr hWnd,
            int command);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", EntryPoint = "GetClassNameW", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern int GetClassName(
            IntPtr hWnd,
            StringBuilder className,
            int maxCount);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AttachThreadInput(
            uint attachingThreadId,
            uint attachedThreadId,
            [MarshalAs(UnmanagedType.Bool)] bool attach);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(
            uint inputCount,
            [In] Input[] inputs,
            int inputSize);

        [StructLayout(LayoutKind.Sequential)]
        public struct Input
        {
            private const uint InputKeyboard = 1;
            private const uint KeyEventFKeyUp = 0x0002;

            public uint Type;
            public InputUnion Data;

            public static Input ForVirtualKey(ushort virtualKey, bool isKeyUp)
            {
                return new Input
                {
                    Type = InputKeyboard,
                    Data = new InputUnion
                    {
                        Keyboard = new KeyboardInput
                        {
                            VirtualKey = virtualKey,
                            Flags = isKeyUp ? KeyEventFKeyUp : 0
                        }
                    }
                };
            }
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)]
            public KeyboardInput Keyboard;

            [FieldOffset(0)]
            public MouseInput Mouse;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KeyboardInput
        {
            public ushort VirtualKey;
            public ushort ScanCode;
            public uint Flags;
            public uint Time;
            public UIntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MouseInput
        {
            public int X;
            public int Y;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public UIntPtr ExtraInfo;
        }
    }
}
