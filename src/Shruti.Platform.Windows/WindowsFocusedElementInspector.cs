using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace Shruti.Platform.Windows;

public sealed class WindowsFocusedElementInspector : IWindowsFocusedElementInspector
{
    private const int EditControlTypeId = 50004;
    private const int DocumentControlTypeId = 50030;

    public FocusedElementSnapshot? CaptureFocusedElement(IntPtr ownerWindowHandle)
    {
        if (ownerWindowHandle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            AutomationElement focusedElement = AutomationElement.FocusedElement;
            if (!BelongsToOwnerWindow(focusedElement, ownerWindowHandle))
            {
                return null;
            }

            return new FocusedElementSnapshot(
                Normalize(ReadAutomationId(focusedElement)),
                InspectEditability(focusedElement),
                InspectSelectedText(focusedElement));
        }
        catch (COMException)
        {
            return null;
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static bool BelongsToOwnerWindow(
        AutomationElement element,
        IntPtr ownerWindowHandle)
    {
        int ownerNativeHandle = unchecked((int)ownerWindowHandle.ToInt64());
        int ownerProcessId = GetWindowProcessId(ownerWindowHandle);
        int? elementProcessId = ReadProcessId(element);
        if (ownerProcessId > 0 && elementProcessId == ownerProcessId)
        {
            return true;
        }

        AutomationElement? current = element;
        for (int depth = 0; current is not null && depth < 32; depth++)
        {
            int? nativeWindowHandle = ReadNativeWindowHandle(current);
            if (nativeWindowHandle == ownerNativeHandle)
            {
                return true;
            }

            try
            {
                current = TreeWalker.ControlViewWalker.GetParent(current);
            }
            catch (ElementNotAvailableException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        return false;
    }

    private static bool? InspectEditability(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object valuePattern) &&
                valuePattern is ValuePattern pattern)
            {
                return !pattern.Current.IsReadOnly;
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (ElementNotAvailableException)
        {
        }

        int? controlType = ReadControlTypeId(element);
        if (controlType is EditControlTypeId or DocumentControlTypeId)
        {
            return true;
        }

        return null;
    }

    private static bool? InspectSelectedText(AutomationElement element)
    {
        try
        {
            if (!element.TryGetCurrentPattern(TextPattern.Pattern, out object textPattern) ||
                textPattern is not TextPattern pattern)
            {
                return null;
            }

            foreach (TextPatternRange range in pattern.GetSelection())
            {
                string? selectedText = range.GetText(-1);
                if (!string.IsNullOrEmpty(selectedText))
                {
                    return true;
                }
            }

            return false;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }

    private static string? ReadAutomationId(AutomationElement element)
    {
        try
        {
            return element.Current.AutomationId;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }

    private static int? ReadProcessId(AutomationElement element)
    {
        try
        {
            return element.Current.ProcessId;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }

    private static int? ReadNativeWindowHandle(AutomationElement element)
    {
        try
        {
            return element.Current.NativeWindowHandle;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }

    private static int? ReadControlTypeId(AutomationElement element)
    {
        try
        {
            return element.Current.ControlType.Id;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static int GetWindowProcessId(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return 0;
        }

        uint threadId = NativeMethods.GetWindowThreadProcessId(windowHandle, out uint processId);
        return threadId == 0 || processId == 0 ? 0 : checked((int)processId);
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);
    }
}
