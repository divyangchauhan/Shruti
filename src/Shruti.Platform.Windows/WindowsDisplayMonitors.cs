using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Shruti.Platform.Windows;

public sealed record WindowsDisplayMonitor(string Id, string Name, nint Handle, bool IsPrimary);

public static class WindowsDisplayMonitors
{
    public static IReadOnlyList<WindowsDisplayMonitor> List()
    {
        var monitors = new List<WindowsDisplayMonitor>();
        Native.MonitorEnumProc callback = (nint monitor, nint dc, ref Native.Rect bounds, nint data) =>
        {
            var info = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>() };
            if (!Native.GetMonitorInfo(monitor, ref info)) return true;
            var device = new Native.DisplayDevice { Size = Marshal.SizeOf<Native.DisplayDevice>() };
            // The monitor interface path survives display-number and handle changes.
            bool hasDevice = Native.EnumDisplayDevices(info.DeviceName, 0, ref device, 1);
            string id = hasDevice && !string.IsNullOrWhiteSpace(device.DeviceId) ? device.DeviceId : info.DeviceName;
            string number = info.DeviceName.Replace(@"\\.\DISPLAY", "", StringComparison.OrdinalIgnoreCase);
            string name = hasDevice && !string.IsNullOrWhiteSpace(device.DeviceString)
                ? $"Display {number}: {device.DeviceString}" : $"Display {number}";
            bool primary = (info.Flags & 1) != 0;
            monitors.Add(new(id, primary ? $"{name} · Primary" : name, monitor, primary));
            return true;
        };
        if (!Native.EnumDisplayMonitors(0, 0, callback, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return monitors.OrderByDescending(m => m.IsPrimary).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static IReadOnlyList<WindowsDisplayMonitor> Select(
        IReadOnlyList<WindowsDisplayMonitor> monitors, bool allMonitors, string? preferredId)
    {
        if (monitors.Count == 0) return [];
        if (allMonitors) return monitors.DistinctBy(m => m.Id, StringComparer.OrdinalIgnoreCase).ToArray();
        var selected = monitors.FirstOrDefault(m => string.Equals(m.Id, preferredId, StringComparison.OrdinalIgnoreCase))
            ?? monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
        return [selected];
    }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct MonitorInfo
        {
            public int Size;
            public Rect Monitor, Work;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DisplayDevice
        {
            public int Size;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public uint StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }
        public delegate bool MonitorEnumProc(nint monitor, nint dc, ref Rect bounds, nint data);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorEnumProc callback, nint data);
        [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
        [DllImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumDisplayDevices(string deviceName, uint index, ref DisplayDevice device, uint flags);
    }
}
