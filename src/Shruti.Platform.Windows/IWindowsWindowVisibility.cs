namespace Shruti.Platform.Windows;

public interface IWindowsWindowVisibility
{
    void Hide(IntPtr windowHandle);

    void ShowAndActivate(IntPtr windowHandle);

    void ShowWithoutActivating(IntPtr windowHandle);

    void MakeNonActivating(IntPtr windowHandle);
}
