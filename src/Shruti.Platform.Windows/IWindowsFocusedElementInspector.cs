namespace Shruti.Platform.Windows;

public interface IWindowsFocusedElementInspector
{
    FocusedElementSnapshot? CaptureFocusedElement(IntPtr ownerWindowHandle);
}
