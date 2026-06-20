namespace Shruti.Platform.Windows;

public readonly record struct WindowsWindowMessage(uint Id, IntPtr WParam, IntPtr LParam);
