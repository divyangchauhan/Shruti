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
- A default `Ctrl+Win+Space` hold-to-dictate shortcut, tray control, and optional floating microphone control. Press `Ctrl+Alt+M` to show or hide the floating control for the current session. Closing the main window keeps Shruti running in the Windows notification area; launching Shruti again restores the running instance instead of opening a duplicate. Right-click the tray icon and choose **Exit Shruti** to stop the process. `Ctrl+Win+Space` overlaps Windows' input-language shortcut, so the text injector clears active shortcut modifiers before sending the final transcript.
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

To exercise the installed NPU with the OpenVINO INT8 model:

```powershell
.\scripts\run-real-transcription.ps1 -Npu
```

Model files and integration data are stored under `%LOCALAPPDATA%\Shruti` and are intentionally excluded from Git.

## Run The Windows App

Build the native runtimes, then start the WinUI project:

```powershell
.\scripts\build.ps1 -Configuration Debug
dotnet run --project src\Shruti.App.WinUI\Shruti.App.WinUI.csproj --configuration Debug -p:Platform=x64
```

The default build includes CPU, Vulkan GPU, and OpenVINO CPU/GPU/NPU runtimes. The app probes the current machine and disables a compute choice when the selected model or hardware cannot use it. No feature flag is required for the normal build.

The Vulkan SDK, including `glslc`, is required to produce the default GPU-enabled `whisper.cpp` runtime. The build uses a short GPU build directory to avoid Windows path-length failures in the upstream Vulkan shader generator, then stages `shruti_whisper.dll` for the app and package scripts.

For an explicit native-only rebuild:

```powershell
.\scripts\build-whispercpp.ps1 -Configuration Debug
.\scripts\build-openvino-genai.ps1 -Configuration Debug
```

The model screen offers the GGML English models for CPU/GPU and the official OpenVINO Whisper Base INT8 model for CPU/GPU/NPU. Downloads are SHA-verified and stored under `%LOCALAPPDATA%\Shruti\Models`.

## Repository Layout

- `src/Shruti.App.WinUI`: WinUI 3 application shell, tray integration, settings, and floating microphone control.
- `src/Shruti.Core`: dictation workflow, state machine, and platform-independent service contracts.
- `src/Shruti.Audio.Windows` and `src/Shruti.Platform.Windows`: WASAPI capture, triggers, focus restoration, insertion, and Windows integration.
- `src/Shruti.Transcription.*`: provider abstractions plus `whisper.cpp` and OpenVINO GenAI adapters.
- `src/Shruti.Models` and `src/Shruti.Storage`: local model lifecycle, settings, and persistence.
- `tests/Shruti.Tests`: unit and integration-style tests.
- `tools/Shruti.RealIntegration`: real local `whisper.cpp` smoke test.

## Documentation

- [Feature inventory](features.md)
- [Architecture](architecture.md)
- [Implementation roadmap](tasks.md)
- [Model catalog schema](docs/model-catalog.schema.json)
- [App compatibility matrix](docs/app-compatibility-matrix.md)
- [Manual QA checklist](docs/manual-qa-checklist.md)

## Contributing

Keep changes focused, preserve the separation between the core workflow and Windows/provider implementations, and run the commands in [Build And Test](#build-and-test) before opening a pull request.

## Privacy

Shruti is designed around local transcription. The development workflow keeps models, recordings, transcripts, and settings on the local machine unless the user explicitly exports data or enables a future integration.
