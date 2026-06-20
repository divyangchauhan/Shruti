using System.Runtime.InteropServices;

namespace Shruti.Platform.Windows;

public sealed class Win32TrayIconApi : IWindowsTrayIconApi
{
    private const uint NotifyIconAdd = 0x00000000;
    private const uint NotifyIconModify = 0x00000001;
    private const uint NotifyIconDelete = 0x00000002;
    private const uint NotifyIconMessage = 0x00000001;
    private const uint NotifyIconIcon = 0x00000002;
    private const uint NotifyIconTip = 0x00000004;
    private const uint MenuString = 0x00000000;
    private const uint MenuSeparator = 0x00000800;
    private const uint MenuDisabled = 0x00000003;
    private const uint TrackPopupReturnCommand = 0x0100;
    private const uint TrackPopupRightButton = 0x0002;
    private const int ApplicationIcon = 32512;
    private const uint StartCommandId = 1;
    private const uint StopCommandId = 2;
    private const uint CancelCommandId = 3;
    private const uint SettingsCommandId = 4;
    private const uint QuitCommandId = 5;

    public bool AddIcon(IntPtr windowHandle, uint iconId, uint callbackMessage, string tooltip)
    {
        var data = CreateIconData(windowHandle, iconId, callbackMessage, tooltip);
        return NativeMethods.ShellNotifyIcon(NotifyIconAdd, ref data);
    }

    public bool UpdateIcon(IntPtr windowHandle, uint iconId, uint callbackMessage, string tooltip)
    {
        var data = CreateIconData(windowHandle, iconId, callbackMessage, tooltip);
        return NativeMethods.ShellNotifyIcon(NotifyIconModify, ref data);
    }

    public void RemoveIcon(IntPtr windowHandle, uint iconId)
    {
        var data = new NativeMethods.NotifyIconData
        {
            Size = Marshal.SizeOf<NativeMethods.NotifyIconData>(),
            WindowHandle = windowHandle,
            IconId = iconId
        };

        NativeMethods.ShellNotifyIcon(NotifyIconDelete, ref data);
    }

    public WindowsTrayCommand? ShowMenu(IntPtr windowHandle, bool isDictationRunning)
    {
        IntPtr menu = NativeMethods.CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            AppendMenuItem(menu, StartCommandId, "Start dictation", isDictationRunning);
            AppendMenuItem(menu, StopCommandId, "Stop dictation", !isDictationRunning);
            NativeMethods.AppendMenu(menu, MenuSeparator, UIntPtr.Zero, null);
            AppendMenuItem(menu, CancelCommandId, "Cancel dictation", !isDictationRunning);
            NativeMethods.AppendMenu(menu, MenuSeparator, UIntPtr.Zero, null);
            AppendMenuItem(menu, SettingsCommandId, "Settings", isDisabled: false);
            AppendMenuItem(menu, QuitCommandId, "Quit", isDisabled: false);

            NativeMethods.GetCursorPos(out NativeMethods.Point cursorPosition);
            NativeMethods.SetForegroundWindow(windowHandle);
            uint selection = NativeMethods.TrackPopupMenuEx(
                menu,
                TrackPopupReturnCommand | TrackPopupRightButton,
                cursorPosition.X,
                cursorPosition.Y,
                windowHandle,
                IntPtr.Zero);

            return selection switch
            {
                StartCommandId => WindowsTrayCommand.Start,
                StopCommandId => WindowsTrayCommand.Stop,
                CancelCommandId => WindowsTrayCommand.Cancel,
                SettingsCommandId => WindowsTrayCommand.ShowSettings,
                QuitCommandId => WindowsTrayCommand.Quit,
                _ => null
            };
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
        }
    }

    private static NativeMethods.NotifyIconData CreateIconData(
        IntPtr windowHandle,
        uint iconId,
        uint callbackMessage,
        string tooltip)
    {
        return new NativeMethods.NotifyIconData
        {
            Size = Marshal.SizeOf<NativeMethods.NotifyIconData>(),
            WindowHandle = windowHandle,
            IconId = iconId,
            Flags = NotifyIconMessage | NotifyIconIcon | NotifyIconTip,
            CallbackMessage = callbackMessage,
            IconHandle = NativeMethods.LoadIcon(IntPtr.Zero, (IntPtr)ApplicationIcon),
            Tooltip = tooltip
        };
    }

    private static void AppendMenuItem(IntPtr menu, uint commandId, string text, bool isDisabled)
    {
        uint flags = MenuString | (isDisabled ? MenuDisabled : 0);
        NativeMethods.AppendMenu(menu, flags, (UIntPtr)commandId, text);
    }

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct NotifyIconData
        {
            public int Size;
            public IntPtr WindowHandle;
            public uint IconId;
            public uint Flags;
            public uint CallbackMessage;
            public IntPtr IconHandle;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string Tooltip;

            public uint State;
            public uint StateMask;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string Info;

            public uint TimeoutOrVersion;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string InfoTitle;

            public uint InfoFlags;
            public Guid GuidItem;
            public IntPtr BalloonIconHandle;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Point
        {
            public int X;
            public int Y;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr commandId, string? text);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyMenu(IntPtr menu);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out Point point);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr windowHandle);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint TrackPopupMenuEx(
            IntPtr menu,
            uint flags,
            int x,
            int y,
            IntPtr windowHandle,
            IntPtr parameters);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr LoadIcon(IntPtr instanceHandle, IntPtr iconName);
    }
}
