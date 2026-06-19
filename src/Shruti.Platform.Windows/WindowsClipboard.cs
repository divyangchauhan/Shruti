using System.Runtime.InteropServices;
using System.Text;

namespace Shruti.Platform.Windows;

public sealed class WindowsClipboard : IWindowsClipboard
{
    private const uint CfText = 1;
    private const uint CfOemText = 7;
    private const uint CfUnicodeText = 13;
    private const uint GmemMoveable = 0x0002;

    public WindowsClipboardSnapshot Capture()
    {
        uint sequenceNumber = NativeMethods.GetClipboardSequenceNumber();
        if (!TryOpenClipboard())
        {
            return WindowsClipboardSnapshot.Unavailable(
                "The clipboard is currently in use.",
                sequenceNumber);
        }

        try
        {
            IReadOnlyList<uint> formats = GetFormats();
            if (formats.Count == 0)
            {
                return new WindowsClipboardSnapshot(
                    CanRestore: true,
                    Text: null,
                    sequenceNumber);
            }

            if (formats.Any(format => format is not CfText and not CfOemText and not CfUnicodeText))
            {
                return WindowsClipboardSnapshot.Unavailable(
                    "Clipboard fallback would overwrite non-text clipboard data.",
                    sequenceNumber);
            }

            string? text = ReadUnicodeText();
            return text is null
                ? WindowsClipboardSnapshot.Unavailable(
                    "The existing text clipboard data could not be read as Unicode text.",
                    sequenceNumber)
                : new WindowsClipboardSnapshot(
                    CanRestore: true,
                    text,
                    sequenceNumber);
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    public WindowsClipboardWriteResult SetText(
        string text,
        uint expectedSequenceNumber)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (NativeMethods.GetClipboardSequenceNumber() != expectedSequenceNumber)
        {
            return new WindowsClipboardWriteResult(
                WindowsClipboardWriteOutcome.SkippedClipboardChanged,
                NativeMethods.GetClipboardSequenceNumber(),
                "The clipboard changed before Shruti could use it.");
        }

        IntPtr memory = AllocateUnicodeText(text);
        if (memory == IntPtr.Zero)
        {
            return new WindowsClipboardWriteResult(
                WindowsClipboardWriteOutcome.FailedWithoutModification,
                NativeMethods.GetClipboardSequenceNumber(),
                "Shruti could not allocate clipboard memory.");
        }

        if (!TryOpenClipboard())
        {
            NativeMethods.GlobalFree(memory);
            return new WindowsClipboardWriteResult(
                WindowsClipboardWriteOutcome.FailedWithoutModification,
                NativeMethods.GetClipboardSequenceNumber(),
                "The clipboard is currently in use.");
        }

        try
        {
            if (NativeMethods.GetClipboardSequenceNumber() != expectedSequenceNumber)
            {
                NativeMethods.GlobalFree(memory);
                return new WindowsClipboardWriteResult(
                    WindowsClipboardWriteOutcome.SkippedClipboardChanged,
                    NativeMethods.GetClipboardSequenceNumber(),
                    "The clipboard changed before Shruti could use it.");
            }

            if (!NativeMethods.EmptyClipboard())
            {
                NativeMethods.GlobalFree(memory);
                return new WindowsClipboardWriteResult(
                    WindowsClipboardWriteOutcome.FailedWithoutModification,
                    NativeMethods.GetClipboardSequenceNumber(),
                    "Shruti could not clear the clipboard.");
            }

            IntPtr result = NativeMethods.SetClipboardData(CfUnicodeText, memory);
            if (result != IntPtr.Zero)
            {
                return new WindowsClipboardWriteResult(
                    WindowsClipboardWriteOutcome.TemporaryTextWritten,
                    NativeMethods.GetClipboardSequenceNumber());
            }

            NativeMethods.GlobalFree(memory);
            return new WindowsClipboardWriteResult(
                WindowsClipboardWriteOutcome.FailedAfterModification,
                NativeMethods.GetClipboardSequenceNumber(),
                "Shruti could not write the transcript to the clipboard.");
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    public WindowsClipboardRestoreResult RestoreIfUnchanged(
        WindowsClipboardSnapshot snapshot,
        uint expectedSequenceNumber)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!snapshot.CanRestore)
        {
            return new WindowsClipboardRestoreResult(
                WindowsClipboardRestoreOutcome.Failed,
                snapshot.Message ?? "The original clipboard data is not restorable.");
        }

        if (NativeMethods.GetClipboardSequenceNumber() != expectedSequenceNumber)
        {
            return new WindowsClipboardRestoreResult(
                WindowsClipboardRestoreOutcome.SkippedClipboardChanged);
        }

        IntPtr memory = snapshot.Text is null
            ? IntPtr.Zero
            : AllocateUnicodeText(snapshot.Text);

        if (snapshot.Text is not null && memory == IntPtr.Zero)
        {
            return new WindowsClipboardRestoreResult(
                WindowsClipboardRestoreOutcome.Failed,
                "Shruti could not allocate memory to restore the clipboard.");
        }

        if (!TryOpenClipboard())
        {
            if (memory != IntPtr.Zero)
            {
                NativeMethods.GlobalFree(memory);
            }

            return new WindowsClipboardRestoreResult(
                WindowsClipboardRestoreOutcome.Failed,
                "The clipboard is currently in use.");
        }

        try
        {
            if (NativeMethods.GetClipboardSequenceNumber() != expectedSequenceNumber)
            {
                if (memory != IntPtr.Zero)
                {
                    NativeMethods.GlobalFree(memory);
                }

                return new WindowsClipboardRestoreResult(
                    WindowsClipboardRestoreOutcome.SkippedClipboardChanged);
            }

            if (!NativeMethods.EmptyClipboard())
            {
                if (memory != IntPtr.Zero)
                {
                    NativeMethods.GlobalFree(memory);
                }

                return new WindowsClipboardRestoreResult(
                    WindowsClipboardRestoreOutcome.Failed,
                    "Shruti could not clear the temporary clipboard text.");
            }

            if (memory == IntPtr.Zero)
            {
                return new WindowsClipboardRestoreResult(
                    WindowsClipboardRestoreOutcome.Restored);
            }

            if (NativeMethods.SetClipboardData(CfUnicodeText, memory) != IntPtr.Zero)
            {
                return new WindowsClipboardRestoreResult(
                    WindowsClipboardRestoreOutcome.Restored);
            }

            NativeMethods.GlobalFree(memory);
            return new WindowsClipboardRestoreResult(
                WindowsClipboardRestoreOutcome.Failed,
                "Shruti could not restore the previous clipboard text.");
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    private static bool TryOpenClipboard()
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            if (NativeMethods.OpenClipboard(IntPtr.Zero))
            {
                return true;
            }

            Thread.Sleep(TimeSpan.FromMilliseconds(20));
        }

        return false;
    }

    private static IReadOnlyList<uint> GetFormats()
    {
        var formats = new List<uint>();
        uint format = 0;

        while ((format = NativeMethods.EnumClipboardFormats(format)) != 0)
        {
            formats.Add(format);
        }

        return formats;
    }

    private static string? ReadUnicodeText()
    {
        IntPtr handle = NativeMethods.GetClipboardData(CfUnicodeText);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        IntPtr pointer = NativeMethods.GlobalLock(handle);
        if (pointer == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(pointer);
        }
        finally
        {
            NativeMethods.GlobalUnlock(handle);
        }
    }

    private static IntPtr AllocateUnicodeText(string text)
    {
        byte[] bytes = Encoding.Unicode.GetBytes($"{text}\0");
        IntPtr memory = NativeMethods.GlobalAlloc(GmemMoveable, checked((UIntPtr)bytes.Length));
        if (memory == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        IntPtr pointer = NativeMethods.GlobalLock(memory);
        if (pointer == IntPtr.Zero)
        {
            NativeMethods.GlobalFree(memory);
            return IntPtr.Zero;
        }

        try
        {
            Marshal.Copy(bytes, 0, pointer, bytes.Length);
            return memory;
        }
        finally
        {
            NativeMethods.GlobalUnlock(memory);
        }
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool OpenClipboard(IntPtr windowHandle);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EmptyClipboard();

        [DllImport("user32.dll")]
        public static extern uint GetClipboardSequenceNumber();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint EnumClipboardFormats(uint format);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr GetClipboardData(uint format);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetClipboardData(uint format, IntPtr memory);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GlobalLock(IntPtr memory);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GlobalUnlock(IntPtr memory);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GlobalFree(IntPtr memory);
    }
}
