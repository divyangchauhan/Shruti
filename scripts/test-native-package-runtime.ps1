param([Parameter(Mandatory)][string]$NativeDirectory)
$ErrorActionPreference = 'Stop'
$nativePath = Join-Path (Resolve-Path -LiteralPath $NativeDirectory).Path 'shruti_whisper.dll'
# Restrict dependency lookup to the package directory plus Windows KnownDLLs.
# CFGMGR32 is an OS component used by the Vulkan loader, not a redistributable.
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class PackagedNativeProbe {
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern IntPtr LoadLibraryExW(string path, IntPtr file, uint flags);
    [DllImport("kernel32.dll", CharSet=CharSet.Ansi, SetLastError=true)]
    static extern IntPtr GetProcAddress(IntPtr module, string name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int AvailableBackends();
    public static int Probe(string path) {
        if (LoadLibraryExW(System.IO.Path.Combine(Environment.SystemDirectory, "cfgmgr32.dll"), IntPtr.Zero, 0x800) == IntPtr.Zero)
            throw new InvalidOperationException("Could not load Windows configuration manager.");
        var module = LoadLibraryExW(path, IntPtr.Zero, 0x100);
        if (module == IntPtr.Zero)
            throw new InvalidOperationException("Packaged speech runtime cannot load without machine-installed dependencies. Win32 error: " + Marshal.GetLastWin32Error());
        var entry = GetProcAddress(module, "shruti_whisper_available_backends");
        if (entry == IntPtr.Zero) throw new InvalidOperationException("Backend capability export is missing.");
        return ((AvailableBackends)Marshal.GetDelegateForFunctionPointer(entry, typeof(AvailableBackends)))();
    }
}
'@
$previousDrivers = $env:VK_DRIVER_FILES
$previousLayers = $env:VK_LOADER_LAYERS_DISABLE
try {
    # A nonexistent driver manifest makes this a CPU-only hardware test.
    $env:VK_DRIVER_FILES = Join-Path ([IO.Path]::GetTempPath()) ('shruti-no-driver-' + [guid]::NewGuid().ToString('N') + '.json')
    $env:VK_LOADER_LAYERS_DISABLE = '*'
    $flags = [PackagedNativeProbe]::Probe($nativePath)
    if (($flags -band 1) -eq 0) { throw "Provider 'whisper.cpp' did not report a usable CPU backend." }
    if (($flags -band 2) -ne 0) { throw 'CPU-only test unexpectedly discovered a GPU.' }
    Write-Output 'PASS: packaged native runtime exposes CPU with isolated DLL lookup and no Vulkan driver.'
} finally {
    $env:VK_DRIVER_FILES = $previousDrivers
    $env:VK_LOADER_LAYERS_DISABLE = $previousLayers
}
