using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Shruti.Platform.Windows;

public sealed class WindowsProcessInspector : IWindowsProcessInspector
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenElevationInformationClass = 20;

    public WindowsProcessSnapshot? Inspect(int processId)
    {
        if (processId <= 0)
        {
            return null;
        }

        try
        {
            using Process process = Process.GetProcessById(processId);
            return new WindowsProcessSnapshot(
                processId,
                process.ProcessName,
                IsProcessElevated(processId));
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (Win32Exception)
        {
            return null;
        }
    }

    private static bool IsProcessElevated(int processId)
    {
        IntPtr processHandle = IntPtr.Zero;
        IntPtr tokenHandle = IntPtr.Zero;

        try
        {
            processHandle = NativeMethods.OpenProcess(
                ProcessQueryLimitedInformation,
                inheritHandle: false,
                checked((uint)processId));

            if (processHandle == IntPtr.Zero)
            {
                return false;
            }

            if (!NativeMethods.OpenProcessToken(processHandle, TokenQuery, out tokenHandle))
            {
                return false;
            }

            var elevation = new TokenElevation();
            int size = Marshal.SizeOf<TokenElevation>();
            bool read = NativeMethods.GetTokenInformation(
                tokenHandle,
                TokenElevationInformationClass,
                ref elevation,
                size,
                out int returnedLength);

            return read && returnedLength >= size && elevation.TokenIsElevated != 0;
        }
        finally
        {
            if (tokenHandle != IntPtr.Zero)
            {
                NativeMethods.CloseHandle(tokenHandle);
            }

            if (processHandle != IntPtr.Zero)
            {
                NativeMethods.CloseHandle(processHandle);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenElevation
    {
        public int TokenIsElevated;
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(
            uint desiredAccess,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
            uint processId);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool OpenProcessToken(
            IntPtr processHandle,
            uint desiredAccess,
            out IntPtr tokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetTokenInformation(
            IntPtr tokenHandle,
            int tokenInformationClass,
            ref TokenElevation tokenInformation,
            int tokenInformationLength,
            out int returnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr handle);
    }
}
