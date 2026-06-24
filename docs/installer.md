# Installer

Shruti's installer foundation produces an unsigned x64 MSIX package from the WinUI app publish output.

## Build

Run from the repository root:

```powershell
.\scripts\package-msix.ps1 -Configuration Release -Platform x64
```

The script:

- builds the `whisper.cpp` native shim with CMake,
- publishes `src/Shruti.App.WinUI` for `win-x64`,
- stages a full-trust MSIX layout under `artifacts\installer\stage`,
- verifies `shruti_whisper.dll` is present in the package layout, and
- writes the unsigned package to `artifacts\installer\output\Shruti-0.1.0.0-x64.msix`.

## Current Limitations

- The package is unsigned. Install testing requires a trusted signing certificate, which is tracked for the signing/release-readiness PR.
- The first package is x64-only.
- Windows App SDK runtime policy is still tracked separately; this PR establishes the installer layout and native DLL inclusion.
