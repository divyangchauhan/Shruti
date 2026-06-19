namespace Shruti.Platform.Windows;

public interface IWindowsProcessInspector
{
    WindowsProcessSnapshot? Inspect(int processId);
}
