using System.Runtime.InteropServices;

namespace Shruti.Platform.Windows;

public sealed class WindowsTextInput : IWindowsTextInput
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventFKeyUp = 0x0002;
    private const uint KeyEventFUnicode = 0x0004;
    private const ushort VirtualKeyControl = 0x11;
    private const ushort VirtualKeyV = 0x56;

    internal static int NativeInputSize => Marshal.SizeOf<Input>();

    public WindowsInputSendResult SendUnicodeText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length == 0)
        {
            return WindowsInputSendResult.FromCounts(0, 0);
        }

        var inputs = new Input[text.Length * 2];

        for (int index = 0; index < text.Length; index++)
        {
            ushort codeUnit = text[index];
            inputs[index * 2] = CreateUnicodeInput(codeUnit, isKeyUp: false);
            inputs[(index * 2) + 1] = CreateUnicodeInput(codeUnit, isKeyUp: true);
        }

        uint sent = NativeMethods.SendInput(
            checked((uint)inputs.Length),
            inputs,
            Marshal.SizeOf<Input>());

        return WindowsInputSendResult.FromCounts(
            sent,
            checked((uint)inputs.Length));
    }

    public WindowsInputSendResult SendPasteShortcut()
    {
        Input[] inputs =
        [
            CreateVirtualKeyInput(VirtualKeyControl, isKeyUp: false),
            CreateVirtualKeyInput(VirtualKeyV, isKeyUp: false),
            CreateVirtualKeyInput(VirtualKeyV, isKeyUp: true),
            CreateVirtualKeyInput(VirtualKeyControl, isKeyUp: true)
        ];

        uint sent = NativeMethods.SendInput(
            checked((uint)inputs.Length),
            inputs,
            Marshal.SizeOf<Input>());

        return WindowsInputSendResult.FromCounts(
            sent,
            checked((uint)inputs.Length));
    }

    public WindowsInputSendResult ReleasePasteShortcutKeys()
    {
        Input[] inputs =
        [
            CreateVirtualKeyInput(VirtualKeyV, isKeyUp: true),
            CreateVirtualKeyInput(VirtualKeyControl, isKeyUp: true)
        ];

        uint sent = NativeMethods.SendInput(
            checked((uint)inputs.Length),
            inputs,
            Marshal.SizeOf<Input>());

        return WindowsInputSendResult.FromCounts(
            sent,
            checked((uint)inputs.Length));
    }

    private static Input CreateUnicodeInput(ushort codeUnit, bool isKeyUp)
    {
        return new Input
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    ScanCode = codeUnit,
                    Flags = KeyEventFUnicode | (isKeyUp ? KeyEventFKeyUp : 0)
                }
            }
        };
    }

    private static Input CreateVirtualKeyInput(ushort virtualKey, bool isKeyUp)
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

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;

        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KeyboardInput Keyboard;

        [FieldOffset(0)]
        public MouseInput Mouse;

        [FieldOffset(0)]
        public HardwareInput Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HardwareInput
    {
        public uint Message;
        public ushort ParameterLow;
        public ushort ParameterHigh;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(
            uint inputCount,
            [In] Input[] inputs,
            int inputSize);
    }
}
