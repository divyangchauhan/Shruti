using System.Runtime.InteropServices;

namespace Shruti.Platform.Windows;

public sealed class WindowsTextInput : IWindowsTextInput
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventFKeyUp = 0x0002;
    private const uint KeyEventFUnicode = 0x0004;
    private const ushort VirtualKeyControl = 0x11;
    private const ushort VirtualKeyShift = 0x10;
    private const ushort VirtualKeyAlt = 0x12;
    private const ushort VirtualKeyV = 0x56;
    private const ushort VirtualKeyLeftShift = 0xA0;
    private const ushort VirtualKeyRightShift = 0xA1;
    private const ushort VirtualKeyLeftControl = 0xA2;
    private const ushort VirtualKeyRightControl = 0xA3;
    private const ushort VirtualKeyLeftAlt = 0xA4;
    private const ushort VirtualKeyRightAlt = 0xA5;
    private const ushort VirtualKeyLeftWindows = 0x5B;
    private const ushort VirtualKeyRightWindows = 0x5C;
    private const int KeyPressedMask = 0x8000;

    internal static int NativeInputSize => Marshal.SizeOf<Input>();

    public WindowsInputSendResult SendUnicodeText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length == 0)
        {
            return WindowsInputSendResult.FromCounts(0, 0);
        }

        Input[] textInputs = new Input[text.Length * 2];

        for (int index = 0; index < text.Length; index++)
        {
            ushort codeUnit = text[index];
            textInputs[index * 2] = CreateUnicodeInput(codeUnit, isKeyUp: false);
            textInputs[(index * 2) + 1] = CreateUnicodeInput(codeUnit, isKeyUp: true);
        }

        return SendInputs(PrependPressedModifierKeyUps(textInputs));
    }

    public WindowsInputSendResult SendPasteShortcut()
    {
        Input[] pasteInputs =
        [
            CreateVirtualKeyInput(VirtualKeyControl, isKeyUp: false),
            CreateVirtualKeyInput(VirtualKeyV, isKeyUp: false),
            CreateVirtualKeyInput(VirtualKeyV, isKeyUp: true),
            CreateVirtualKeyInput(VirtualKeyControl, isKeyUp: true)
        ];

        return SendInputs(PrependPressedModifierKeyUps(pasteInputs));
    }

    public WindowsInputSendResult ReleasePasteShortcutKeys()
    {
        Input[] inputs =
        [
            CreateVirtualKeyInput(VirtualKeyV, isKeyUp: true),
            CreateVirtualKeyInput(VirtualKeyControl, isKeyUp: true),
            CreateVirtualKeyInput(VirtualKeyLeftControl, isKeyUp: true),
            CreateVirtualKeyInput(VirtualKeyRightControl, isKeyUp: true),
            CreateVirtualKeyInput(VirtualKeyShift, isKeyUp: true),
            CreateVirtualKeyInput(VirtualKeyLeftShift, isKeyUp: true),
            CreateVirtualKeyInput(VirtualKeyRightShift, isKeyUp: true),
            CreateVirtualKeyInput(VirtualKeyAlt, isKeyUp: true),
            CreateVirtualKeyInput(VirtualKeyLeftAlt, isKeyUp: true),
            CreateVirtualKeyInput(VirtualKeyRightAlt, isKeyUp: true),
            CreateVirtualKeyInput(VirtualKeyLeftWindows, isKeyUp: true),
            CreateVirtualKeyInput(VirtualKeyRightWindows, isKeyUp: true)
        ];

        return SendInputs(inputs);
    }

    private static WindowsInputSendResult SendInputs(Input[] inputs)
    {
        uint sent = NativeMethods.SendInput(
            checked((uint)inputs.Length),
            inputs,
            Marshal.SizeOf<Input>());

        return WindowsInputSendResult.FromCounts(
            sent,
            checked((uint)inputs.Length));
    }

    private static Input[] PrependPressedModifierKeyUps(Input[] inputs)
    {
        Input[] modifierKeyUps = CreatePressedModifierKeyUpInputs();
        if (modifierKeyUps.Length == 0)
        {
            return inputs;
        }

        var allInputs = new Input[modifierKeyUps.Length + inputs.Length];
        Array.Copy(modifierKeyUps, allInputs, modifierKeyUps.Length);
        Array.Copy(inputs, 0, allInputs, modifierKeyUps.Length, inputs.Length);
        return allInputs;
    }

    private static Input[] CreatePressedModifierKeyUpInputs()
    {
        var inputs = new List<Input>(capacity: 8);

        AddPressedModifierKeyUps(
            inputs,
            VirtualKeyControl,
            VirtualKeyLeftControl,
            VirtualKeyRightControl);
        AddPressedModifierKeyUps(
            inputs,
            VirtualKeyShift,
            VirtualKeyLeftShift,
            VirtualKeyRightShift);
        AddPressedModifierKeyUps(
            inputs,
            VirtualKeyAlt,
            VirtualKeyLeftAlt,
            VirtualKeyRightAlt);

        AddPressedKeyUp(inputs, VirtualKeyLeftWindows);
        AddPressedKeyUp(inputs, VirtualKeyRightWindows);

        return inputs.ToArray();
    }

    private static void AddPressedModifierKeyUps(
        List<Input> inputs,
        ushort genericVirtualKey,
        ushort leftVirtualKey,
        ushort rightVirtualKey)
    {
        int initialCount = inputs.Count;
        AddPressedKeyUp(inputs, leftVirtualKey);
        AddPressedKeyUp(inputs, rightVirtualKey);
        if (inputs.Count == initialCount)
        {
            AddPressedKeyUp(inputs, genericVirtualKey);
        }
    }

    private static void AddPressedKeyUp(List<Input> inputs, ushort virtualKey)
    {
        if (IsKeyPressed(virtualKey))
        {
            inputs.Add(CreateVirtualKeyInput(virtualKey, isKeyUp: true));
        }
    }

    private static bool IsKeyPressed(ushort virtualKey)
    {
        return (NativeMethods.GetAsyncKeyState(virtualKey) & KeyPressedMask) != 0;
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

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int virtualKey);
    }
}
