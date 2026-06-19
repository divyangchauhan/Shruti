# Shruti

Shruti is a Windows-first native dictation app. The core workflow is to trigger dictation, record microphone audio, transcribe locally, and insert the final text back into the app that was focused before dictation started.

## Current Status

This repository is in early foundation work. The initial implementation targets:

- WinUI 3 and Windows App SDK for the Windows-native shell.
- A replaceable transcription provider abstraction.
- `whisper.cpp` as the first local transcription provider.
- Windows-first system-wide dictation with direct insertion and clipboard fallback.

See `features.md`, `architecture.md`, and `tasks.md` for the current product and implementation plan.

## Build

Windows with the .NET 8 SDK and Visual Studio Build Tools is the expected development environment.

```powershell
.\scripts\build.ps1
.\scripts\test.ps1
```
