using System.Runtime.InteropServices;

namespace Shruti.Platform.Windows;

public sealed class WindowsFocusedElementInspector : IWindowsFocusedElementInspector
{
    private const int ValuePatternId = 10002;
    private const int TextPatternId = 10014;
    private const int EditControlTypeId = 50004;
    private const int DocumentControlTypeId = 50030;

    private readonly IWindowsAutomationClientFactory _automationClientFactory;

    public WindowsFocusedElementInspector()
        : this(new WindowsAutomationClientFactory())
    {
    }

    public WindowsFocusedElementInspector(IWindowsAutomationClientFactory automationClientFactory)
    {
        _automationClientFactory = automationClientFactory ?? throw new ArgumentNullException(nameof(automationClientFactory));
    }

    public FocusedElementSnapshot? CaptureFocusedElement(IntPtr ownerWindowHandle)
    {
        if (ownerWindowHandle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            dynamic? automation = _automationClientFactory.CreateAutomationClient();
            if (automation is null)
            {
                return null;
            }

            dynamic focusedElement = automation.GetFocusedElement();
            if (!BelongsToOwnerWindow(automation, focusedElement, ownerWindowHandle))
            {
                return null;
            }

            return new FocusedElementSnapshot(
                Normalize(ReadString(focusedElement, "CurrentAutomationId")),
                InspectEditability(focusedElement),
                InspectSelectedText(focusedElement));
        }
        catch (COMException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (Exception ex) when (IsAutomationFailure(ex))
        {
            return null;
        }
    }

    private static bool BelongsToOwnerWindow(
        dynamic automation,
        dynamic element,
        IntPtr ownerWindowHandle)
    {
        int ownerNativeHandle = unchecked((int)ownerWindowHandle.ToInt64());
        dynamic? current = element;

        for (int depth = 0; current is not null && depth < 32; depth++)
        {
            int? nativeWindowHandle = ReadInt(current, "CurrentNativeWindowHandle");
            if (nativeWindowHandle == ownerNativeHandle)
            {
                return true;
            }

            current = automation.ControlViewWalker.GetParentElement(current);
        }

        return false;
    }

    private static bool? InspectEditability(dynamic element)
    {
        try
        {
            dynamic valuePattern = element.GetCurrentPattern(ValuePatternId);
            bool? isReadOnly = ReadBool(valuePattern, "CurrentIsReadOnly");
            if (isReadOnly.HasValue)
            {
                return !isReadOnly.Value;
            }
        }
        catch (Exception ex) when (IsAutomationFailure(ex))
        {
        }

        int? controlType = ReadInt(element, "CurrentControlType");
        if (controlType is EditControlTypeId or DocumentControlTypeId)
        {
            return true;
        }

        return null;
    }

    private static bool? InspectSelectedText(dynamic element)
    {
        try
        {
            dynamic textPattern = element.GetCurrentPattern(TextPatternId);
            Array selection = textPattern.GetSelection();

            foreach (dynamic range in selection)
            {
                string? selectedText = range.GetText(-1);
                if (!string.IsNullOrEmpty(selectedText))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex) when (IsAutomationFailure(ex))
        {
            return null;
        }
    }

    private static string? ReadString(dynamic source, string propertyName)
    {
        try
        {
            return propertyName switch
            {
                "CurrentAutomationId" => source.CurrentAutomationId,
                _ => null
            };
        }
        catch (Exception ex) when (IsAutomationFailure(ex))
        {
            return null;
        }
    }

    private static int? ReadInt(dynamic source, string propertyName)
    {
        try
        {
            object? value = propertyName switch
            {
                "CurrentNativeWindowHandle" => source.CurrentNativeWindowHandle,
                "CurrentControlType" => source.CurrentControlType,
                _ => null
            };

            return value is null ? null : Convert.ToInt32(value);
        }
        catch (Exception ex) when (IsAutomationFailure(ex))
        {
            return null;
        }
    }

    private static bool? ReadBool(dynamic source, string propertyName)
    {
        try
        {
            object? value = propertyName switch
            {
                "CurrentIsReadOnly" => source.CurrentIsReadOnly,
                _ => null
            };

            return value is null ? null : Convert.ToBoolean(value);
        }
        catch (Exception ex) when (IsAutomationFailure(ex))
        {
            return null;
        }
    }

    private static bool IsAutomationFailure(Exception ex)
    {
        return ex is COMException
            or InvalidOperationException
            or NotSupportedException
            or MissingMethodException
            || ex.GetType().FullName == "Microsoft.CSharp.RuntimeBinder.RuntimeBinderException";
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
