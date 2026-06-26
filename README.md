# Shruti

[![Windows CI](https://github.com/divyangchauhan/Shruti/actions/workflows/windows-ci.yml/badge.svg)](https://github.com/divyangchauhan/Shruti/actions/workflows/windows-ci.yml)

Shruti is a Windows-native, local-first dictation app. Trigger dictation, speak, and have the finalized transcript inserted into the app that was focused before recording began.

The first implementation uses WinUI 3, WASAPI microphone capture, and `whisper.cpp` for local speech recognition. No account or hosted transcription service is required for the core workflow.

## Status

Shruti is under active development and is not packaged for end users yet. The Windows development build currently supports:

- Microphone capture with an audio level meter.
- Local `whisper.cpp` transcription with live partial text and a final transcript.
- Auto-insert, preview-before-insert, and copy-only dictation modes.
- Direct text insertion with clipboard fallback when direct insertion is unavailable.
- A default `Ctrl+Win+Space` hold-to-dictate shortcut, tray control, and optional floating microphone control. Press `Ctrl+Alt+M` to show or hide the floating control for the current session. Closing the main window keeps Shruti running in the Windows notification area; launching Shruti again restores the running instance instead of opening a duplicate. Right-click the tray icon and choose **Exit Shruti** to stop the process.
- Local model download, verification, import, and removal primitives.
- System, light, and dark theme preferences.

The final transcript is the only text inserted into another application. Live text is preview-only.

## How It Works

1. Start dictation from the app, a configured trigger, the tray, or the floating microphone control.
2. Shruti captures the current foreground target before recording.
3. Audio is normalized to 16 kHz mono PCM and transcribed locally.
4. Shruti shows live partial text while recording.
5. On stop, Shruti finalizes the transcript and inserts it, opens a preview, or copies it according to the selected mode.

If the maximum recording duration is reached, Shruti stops capture and finalizes the audio already recorded instead of discarding the dictation.

## Requirements

- Windows 10 version 2004 (build 19041) or later, 64-bit.
- .NET 8 SDK.
- Visual Studio 2022 Build Tools with the Desktop development with C++ workload and a Windows SDK.
- CMake 3.21 or later.
- Git.

The repository targets `x64` builds only.

## Build And Test

From PowerShell at the repository root:

```powershell
.\scripts\format.ps1 -Verify
.\scripts\lint.ps1
dotnet test tests\Shruti.Tests\Shruti.Tests.csproj --configuration Debug -p:Platform=x64
.\scripts\build.ps1
```

`lint.ps1` verifies formatting and builds the solution with warnings treated as errors. GitHub Actions runs formatting, linting, the test matrix, and a clean native `whisper.cpp` build for pull requests to `main`.

## Run A Local Transcription Smoke Test

The real integration command builds the release native shim, downloads the verified tiny English model and a pinned speech fixture on first run, then checks live and final local transcription:

```powershell
.\scripts\run-real-transcription.ps1
```

Model files and integration data are stored under `%LOCALAPPDATA%\Shruti` and are intentionally excluded from Git.

## Run The Windows App

Build the debug native shim first, then start the WinUI project:

```powershell
.\scripts\build-whispercpp.ps1 -Configuration Debug
dotnet run --project src\Shruti.App.WinUI\Shruti.App.WinUI.csproj --configuration Debug -p:Platform=x64
```

The default native shim is CPU-only. To build a GPU-enabled `whisper.cpp` shim, pass a concrete backend:

```powershell
.\scripts\build-whispercpp.ps1 -Configuration Debug -GpuBackend Vulkan
# or, on machines with the CUDA toolkit configured:
.\scripts\build-whispercpp.ps1 -Configuration Debug -GpuBackend CUDA
```

Vulkan builds require the Vulkan SDK, including `glslc`. The build script uses a short GPU build directory to avoid Windows path-length failures in the upstream Vulkan shader generator, then copies `shruti_whisper.dll` back into `artifacts\whispercpp-native\<Configuration>` for the app and package scripts.

GPU is exposed in the app only when the native shim was built with a GPU backend and `whisper.cpp` reports a GPU device. NPU is not implemented by the current `whisper.cpp` GGML provider; selecting NPU reports unsupported until a separate NPU-capable provider is added.

The app expects the recommended `ggml-tiny.en.bin` model in `%LOCALAPPDATA%\Shruti\Models`. Run the local transcription smoke test once to download it, or install/import a verified model through the model workflow.

## Repository Layout

- `src/Shruti.App.WinUI`: WinUI 3 application shell, tray integration, settings, and floating microphone control.
- `src/Shruti.Core`: dictation workflow, state machine, and platform-independent service contracts.
- `src/Shruti.Audio.Windows` and `src/Shruti.Platform.Windows`: WASAPI capture, triggers, focus restoration, insertion, and Windows integration.
- `src/Shruti.Transcription.*`: provider abstractions plus the `whisper.cpp` managed adapter and native C ABI shim.
- `src/Shruti.Models` and `src/Shruti.Storage`: local model lifecycle, settings, and persistence.
- `tests/Shruti.Tests`: unit and integration-style tests.
- `tools/Shruti.RealIntegration`: real local `whisper.cpp` smoke test.

## Documentation

- [Feature inventory](features.md)
- [Architecture](architecture.md)
- [Implementation roadmap](tasks.md)
- [Model catalog schema](docs/model-catalog.schema.json)

## Contributing

Keep changes focused, preserve the separation between the core workflow and Windows/provider implementations, and run the commands in [Build And Test](#build-and-test) before opening a pull request.

## Privacy

Shruti is designed around local transcription. The development workflow keeps models, recordings, transcripts, and settings on the local machine unless the user explicitly exports data or enables a future integration.
