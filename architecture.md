# Shruti Architecture Plan

Date: 2026-06-18

Status: planning

## Goal

Shruti is a Windows-first native dictation app. The primary workflow is:

1. The user triggers dictation from a global hotkey, floating button, or tray control.
2. Shruti remembers the previously focused app/control.
3. Shruti records microphone audio.
4. Shruti transcribes locally.
5. Shruti immediately inserts the finalized text back into the previously focused app.

The Windows MVP uses WinUI 3 with Windows App SDK for the native UI and starts with `whisper.cpp` for local transcription. The architecture must keep the transcription engine replaceable so later providers can use ONNX Runtime, Windows ML, DirectML, vendor NPU SDKs, or different ASR models without rewriting the dictation workflow.

## Architecture Principles

- Windows is the first-class platform until the dictation loop feels excellent.
- Dictation workflow code must not depend directly on `whisper.cpp`.
- Platform integrations are isolated behind interfaces because global hotkeys, focus restoration, audio capture, text insertion, tray UI, and packaging all differ by OS.
- The app defaults to immediate insertion, with preview/copy-only modes available for safety.
- The app stays local-first. Audio, models, transcripts, and settings live on the user's machine unless the user explicitly exports or integrates with another tool.
- The MVP should be small enough to ship, benchmark, and harden before adding transcript library, diarization, AI cleanup, or cloud sync.

## Concrete Tech Choices

### Windows App

- UI: WinUI 3.
- App framework: Windows App SDK.
- Primary language: C# for app shell, workflow orchestration, settings, and Windows integration.
- Native interop: C++/WinRT or C ABI DLL boundaries for transcription engines and any low-level Windows APIs that are awkward from C#.
- Native source strategy: pin `whisper.cpp` in CMake FetchContent and build a small project-owned C ABI shim instead of binding managed code to upstream C++ internals.
- Runtime: current supported .NET desktop runtime at implementation time.
- Persistence: SQLite for transcript/session metadata and settings that benefit from querying; JSON files for model manifests and simple app configuration where appropriate.
- Logging: structured local logs with redaction of transcript text by default.
- Build: Visual Studio/MSBuild for the Windows solution; CMake for native transcription provider builds.

### MVP Transcription

- First provider: `whisper.cpp` compiled as a local native library.
- Model format: provider-specific. The initial `whisper.cpp` catalog uses verified GGML `.bin` models; the catalog also supports GGUF entries for future providers.
- Initial execution backend: CPU-only `whisper.cpp`; accelerated backends remain a later provider/readiness concern.
- Initial compute target: CPU path that is reliable on supported Windows machines.
- Optional acceleration in the `whisper.cpp` provider can be added only after benchmarking and packaging are stable.
- Provider contract must expose capabilities and measured real-time factor instead of assuming a model/backend is fast enough.

### Future Transcription Providers

- ONNX Runtime provider for Whisper-family and non-Whisper ASR models.
- Windows ML provider if it gives better access to Windows-managed acceleration.
- DirectML or vendor execution providers where practical.
- NPU-specific providers are expected to be fragmented; isolate them as provider implementations, not as core app assumptions.

## Proposed Solution Layout

```text
Shruti.sln
src/
  Shruti.App.WinUI/
    WinUI 3 views, windows, tray bootstrap, settings UI
  Shruti.Core/
    Dictation workflow, session state, domain models, service interfaces
  Shruti.Workflow/
    UI-agnostic shell state, trigger routing, and application orchestration
  Shruti.Audio.Windows/
    WASAPI microphone capture, device enumeration, audio level meter
  Shruti.Platform.Windows/
    Hotkeys, focus capture, text insertion, tray, notifications, startup registration
  Shruti.Transcription.Abstractions/
    Provider interfaces, model descriptors, transcript events
  Shruti.Transcription.WhisperCpp/
    C# adapter around native whisper.cpp bindings
  Shruti.Transcription.WhisperCpp.Native/
    C/C++ shim and bundled whisper.cpp build artifacts
  Shruti.Models/
    Model catalog, download, verification, import/remove
  Shruti.Storage/
    SQLite repositories, file layout, retention cleanup
  Shruti.Tests/
    Unit tests for workflow, settings, provider selection, insertion policy
```

Later platform shells should reuse `Shruti.Core`, `Shruti.Workflow`, `Shruti.Transcription.Abstractions`, `Shruti.Models`, and parts of `Shruti.Storage`, but should not be forced to use WinUI.

## Module Boundaries

### `Shruti.App.WinUI`

Owns the visible Windows experience:

- Main dictation window.
- Compact overlay/floating microphone control.
- Settings pages.
- Transcript preview/edit surface.
- Device/model/backend pickers.
- Status surfaces for recording, transcribing, insertion, errors, and unsupported hardware.

This project should be thin. It observes workflow state and dispatches user commands. It should not know how `whisper.cpp`, hotkeys, or text insertion work internally.

### `Shruti.Workflow`

Owns application orchestration that is not tied to a visual framework:

- Dictation shell state for start, stop, pause, cancel, retry, copy, and preview flows.
- Trigger routing and trigger-event dispatch.
- UI-facing status and audio-level notifications.

This project depends on core abstractions but not WinUI, Windows App SDK, or platform implementation classes. Native shells compose it with their platform-specific services.

### `Shruti.Core`

Owns the product workflow:

- Dictation session state machine.
- Trigger handling policy.
- Auto-insert versus preview/copy-only behavior.
- Coordination between focus capture, audio capture, transcription, and text insertion.
- Error mapping into user-facing states.
- Retention decisions after transcription completes.

Core should depend on abstractions such as `IAudioCaptureService`, `ITranscriptionProviderRegistry`, `ITextInsertionService`, and `ITargetFocusService`.

### `Shruti.Audio.Windows`

Owns Windows microphone capture:

- Enumerate input devices.
- Capture PCM audio through WASAPI.
- Convert to the engine's required internal format, preferably 16 kHz mono PCM.
- Publish audio level information for the meter/waveform.
- Detect device removal and permission failures.
- Leave room for later VAD, silence trimming, and noise suppression.

The MVP can use a proven WASAPI wrapper if it keeps the boundary clean. The important decision is that the rest of the app receives normalized audio frames, not Windows audio implementation details.

### `Shruti.Platform.Windows`

Owns Windows-specific desktop integration:

- Global hotkey registration.
- Push-to-talk key state handling.
- Foreground window and focused element capture.
- Focus restoration.
- Text insertion.
- Tray icon/menu.
- Notifications.
- Launch-at-login registration.
- Per-app metadata such as process name, window title, integrity level, and insertion capability.

This layer is expected to use Win32, UI Automation, clipboard APIs, and Windows App SDK interop.

### `Shruti.Transcription.Abstractions`

Owns engine-neutral contracts:

- Provider discovery.
- Capability probing.
- Model compatibility.
- Streaming and final transcription events.
- Segment timestamps.
- Backend selection.
- Performance metrics.
- Cancellation.

No workflow code should cast to a concrete provider.

### `Shruti.Transcription.WhisperCpp`

Owns the MVP engine adapter:

- Loads the native `whisper.cpp` DLL.
- Validates model files.
- Creates transcription sessions.
- Converts audio frames into provider input.
- Emits partial text, finalized segments, timestamps, and metrics.
- Maps `whisper.cpp` errors into engine-neutral errors.

The native binding should be intentionally small. Prefer a C ABI shim over binding the app directly to broad C++ internals.

The `scripts/run-real-transcription.ps1` integration path builds the shim, installs the verified default model in local app data, and transcribes a pinned speech fixture through the managed provider.

### `Shruti.Models`

Owns model lifecycle:

- Recommended model catalog.
- First-run download flow for 2-3 supported models.
- Hash verification.
- Local import/remove.
- Model compatibility filtering by provider, language, size, and backend.
- Disk usage reporting.

The catalog records the model file format and hash algorithm so verified GGML, GGUF, and future provider assets can coexist without assuming a single artifact format.

The catalog should include enough metadata to prevent users from selecting models that cannot run acceptably on their machine.

### `Shruti.Storage`

Owns local data:

- App settings.
- Transcript metadata.
- Segment metadata.
- Optional retained audio files.
- Retention cleanup.
- Export helpers for TXT, Markdown, JSON, SRT, and VTT.

Recommended Windows paths:

- Models: `%LOCALAPPDATA%\Shruti\Models`
- Transcripts database: `%LOCALAPPDATA%\Shruti\Data\shruti.db`
- Optional recordings: `%LOCALAPPDATA%\Shruti\Recordings`
- Logs: `%LOCALAPPDATA%\Shruti\Logs`

## Core Platform Interfaces

The dictation workflow should talk to platform capabilities through narrow interfaces. Windows provides the first implementations; Linux and macOS can provide their own later.

```csharp
public interface ITargetFocusService
{
    Task<FocusTarget?> CaptureCurrentTargetAsync(
        CancellationToken cancellationToken);

    Task<FocusRestoreResult> RestoreAsync(
        FocusTarget target,
        CancellationToken cancellationToken);
}

public interface ITextInsertionService
{
    Task<TextInsertionCapability> InspectAsync(
        FocusTarget target,
        CancellationToken cancellationToken);

    Task<TextInsertionResult> InsertAsync(
        FocusTarget target,
        string text,
        TextInsertionOptions options,
        CancellationToken cancellationToken);
}

public interface IAudioCaptureService
{
    Task<IReadOnlyList<AudioInputDevice>> ListInputDevicesAsync(
        CancellationToken cancellationToken);

    Task<IAudioCaptureSession> StartAsync(
        AudioCaptureOptions options,
        AudioFormat outputFormat,
        CancellationToken cancellationToken);
}

public interface IAudioCaptureSession : IAsyncDisposable
{
    IAsyncEnumerable<AudioFrame> Frames { get; }
    IAsyncEnumerable<AudioLevelFrame> Levels { get; }

    Task PauseAsync(CancellationToken cancellationToken);
    Task ResumeAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}

public interface IGlobalTriggerService
{
    IAsyncEnumerable<DictationTriggerEvent> Events { get; }

    Task ConfigureAsync(
        TriggerConfiguration configuration,
        CancellationToken cancellationToken);
}
```

Core should treat these as capabilities that can fail or degrade. For example, `ITextInsertionService` might report `DirectInputAvailable`, `ClipboardFallbackOnly`, `PreviewRecommended`, or `Unsupported`.

## Dictation State Machine

```text
Idle
  -> PreparingTarget
  -> RequestingMicrophone
  -> Recording
  -> TranscribingFinalAudio
  -> InsertingText
  -> Complete

Any active state
  -> Cancelled
  -> Failed
```

Important state notes:

- `PreparingTarget` captures the foreground window and focused element before Shruti activates any UI.
- `Recording` can emit partial transcript updates if the provider supports streaming.
- `TranscribingFinalAudio` handles the final buffered audio after stop.
- `InsertingText` is skipped in preview-first and copy-only modes.
- `Cancelled` never inserts text.
- `Failed` should preserve enough diagnostic information to help the user recover without exposing transcript text in logs by default.

## Transcription Provider Interfaces

The provider interface should model capabilities, model compatibility, and streaming sessions separately.

```csharp
public enum ComputeBackend
{
    Auto,
    Npu,
    Gpu,
    Cpu
}

public enum TranscriptEventKind
{
    PartialText,
    SegmentFinalized,
    Completed,
    Warning,
    Failed
}

public sealed record AudioFormat(
    int SampleRateHz,
    int ChannelCount,
    AudioSampleFormat SampleFormat);

public sealed record TranscriptionModelDescriptor(
    string Id,
    string DisplayName,
    string ProviderId,
    string LocalPath,
    string LanguageHint,
    long SizeBytes,
    IReadOnlySet<ComputeBackend> SupportedBackends);

public sealed record EngineCapability(
    string ProviderId,
    string ProviderDisplayName,
    ComputeBackend Backend,
    string DeviceName,
    bool SupportsStreaming,
    bool SupportsTimestamps,
    bool SupportsLanguageDetection,
    double? MeasuredRealtimeFactor,
    IReadOnlyList<string> Warnings);

public interface ITranscriptionProvider
{
    string Id { get; }
    string DisplayName { get; }

    Task<IReadOnlyList<EngineCapability>> ProbeAsync(
        CancellationToken cancellationToken);

    Task<bool> CanRunModelAsync(
        TranscriptionModelDescriptor model,
        ComputeBackend requestedBackend,
        CancellationToken cancellationToken);

    Task<ITranscriptionSession> CreateSessionAsync(
        TranscriptionSessionOptions options,
        CancellationToken cancellationToken);
}

public interface ITranscriptionSession : IAsyncDisposable
{
    AudioFormat RequiredInputFormat { get; }
    IAsyncEnumerable<TranscriptEvent> Events { get; }

    ValueTask PushAudioAsync(
        ReadOnlyMemory<byte> pcmAudio,
        CancellationToken cancellationToken);

    Task<TranscriptResult> CompleteAsync(
        CancellationToken cancellationToken);

    Task CancelAsync();
}
```

Provider implementations are responsible for internal buffering and native runtime details. Core workflow only sees audio frames in the requested format and transcript events.

## Provider Selection Strategy

Selection order follows the product direction:

1. User-selected backend, if explicitly set and supported.
2. Auto mode prefers supported NPU.
3. Then supported GPU.
4. Then supported CPU.

However, "supported" must mean more than "API exists." A model/backend pair is eligible only if:

- The provider can load the model.
- Required runtime files are present.
- Hardware probing succeeds.
- A benchmark or cached benchmark indicates acceptable real-time factor for the selected mode.

If no eligible backend is fast enough, Shruti should show an unsupported/slow warning and require the user to explicitly enable slower-than-real-time transcription.

## Windows Integration Strategy

### Target Capture

Before opening or focusing Shruti UI, capture:

- Foreground `HWND`.
- Owning process ID/name.
- Thread ID.
- Window title.
- Current UI Automation focused element, when available.
- Whether the target appears editable.
- Whether selected text can be detected safely.
- Integrity level compatibility.

This target record is passed through the dictation session and used for insertion.

### Global Hotkeys

- Use `RegisterHotKey` for press-once-start/press-again-stop shortcuts.
- Use a carefully scoped low-level keyboard hook or raw input strategy for push-to-talk key-down/key-up semantics.
- Reject hotkey combinations known to conflict with Windows or common accessibility shortcuts.
- Let the user disable hotkey, floating button, and tray triggers independently.

### Floating Button

- Implement as a compact topmost WinUI/Win32 interop window.
- Prefer non-activating behavior so clicking it does not destroy the target focus record before capture.
- Show only essential states: idle, recording, transcribing, failed.
- Keep the main settings/transcript UI separate from the compact control.

### Tray Control

- Provide start, stop, cancel, settings, and quit.
- Keep tray available even when the main window is closed.
- Surface errors through native notifications and a status entry in the menu.

### Text Insertion

Use a layered strategy:

1. Restore the captured target window/focus.
2. Prefer direct Unicode text input with `SendInput` when compatible.
3. Use UI Automation only where it can safely identify editability or selection state; avoid destructive `ValuePattern.SetValue` for normal insertion because it can replace entire field contents.
4. Fall back to clipboard paste:
   - Snapshot existing clipboard content where possible.
   - Put transcript text on clipboard.
   - Send paste command.
   - Restore previous clipboard content after a short delay if restoration is enabled.
5. If insertion cannot be trusted, switch to preview/copy and explain the reason.

Safe insertion policy:

- By default, avoid overwriting selected text unless the user has enabled replacement behavior.
- If a non-empty selection is detected, preview before insert or ask for confirmation.
- If selection state cannot be determined for a sensitive target, prefer preview/copy unless the user has opted into aggressive insertion.
- Maintain per-app rules so terminals, remote desktops, elevated apps, password fields, and unsupported controls can use safer defaults.

Known hard boundary:

- A non-elevated Shruti process cannot reliably inject into elevated apps because of Windows integrity isolation. The app should not run elevated by default; instead, explain the limitation and offer copy/clipboard fallback.

## Audio Pipeline

```text
WASAPI microphone
  -> capture buffer
  -> resampler/channel converter
  -> level meter
  -> rolling session buffer
  -> transcription session
  -> partial/final transcript events
```

Implementation notes:

- Normalize internal capture to the transcription session's required format.
- Keep a rolling buffer for retrying final transcription after streaming errors.
- Do not persist audio unless retention settings require it.
- Add VAD and silence trimming behind the audio abstraction later.
- Treat microphone permission, missing device, exclusive-mode conflicts, and device disconnects as first-class errors.

## Model and Backend Readiness

Startup should perform lightweight checks:

- Installed models exist and match expected hashes.
- Configured provider can load.
- Configured backend is available.
- Last benchmark result is still valid for the model/provider/backend/runtime version.

Settings should show:

- Active provider.
- Active backend.
- Device name.
- Selected model.
- Whether real-time dictation is expected.
- Any warnings about slow mode, unsupported hardware, or missing runtime files.

Benchmarking should be short and user-visible. Cache results by:

- Provider version.
- Model hash.
- Backend.
- Device identifier.
- Relevant runtime version.

## Data Model

MVP entities:

- `DictationSession`
  - ID, started/ended timestamps, source trigger, target app metadata, model ID, provider ID, backend, language, status.
- `TranscriptSegment`
  - session ID, index, start/end timestamps, text, confidence if available.
- `RecordingAsset`
  - session ID, path, duration, retention status, deletion timestamp.
- `ModelInstall`
  - model ID, provider ID, path, hash, installed version/catalog revision.

Transcript history can initially be minimal and private to recent sessions. A richer library can build on the same data model later.

## Packaging and Distribution

Windows MVP packaging:

- Ship as a packaged WinUI 3 desktop app using MSIX with full-trust desktop capabilities.
- Code-sign all app and native binaries.
- Bundle required native runtime DLLs for the selected `whisper.cpp` build.
- Use an installer/bootstrapper for direct download if MSIX alone cannot provide the desired first-run experience.
- Publish a `winget` manifest when the installer is stable.
- Keep model downloads separate from app installer size, except possibly a tiny default model if licensing and size are acceptable.

Installer responsibilities:

- Install Shruti.
- Ensure Windows App SDK runtime requirements are satisfied.
- Install or verify VC++ runtime requirements if native DLLs need them.
- Register app identity needed for notifications/startup behavior.
- Avoid requiring administrator privileges for normal install when possible.

Update responsibilities:

- App updates must not delete models, settings, transcripts, or retained recordings.
- Provider/native runtime updates should invalidate stale benchmark cache.
- Model catalog updates should be signed or hash-verified.

## Privacy and Security

- No server transcription in MVP.
- No account requirement.
- No transcript text in logs by default.
- No audio retention unless user setting allows it.
- Clear first-run explanation of local storage and microphone use.
- Clipboard fallback must be visible in settings because it temporarily places transcript text on the clipboard.
- Global hooks and text insertion should be scoped narrowly and documented in-app in plain language.
- Avoid running as administrator by default.

## Testing Strategy

Unit tests:

- Dictation state transitions.
- Provider selection.
- Slow/unsupported backend gating.
- Retention policy.
- Export formatting.
- Insertion policy decisions.

Integration tests:

- Mock transcription provider with deterministic partial/final events.
- Mock audio source.
- Windows text insertion tests against controlled WinUI, Win32, browser, terminal, and elevated-app scenarios where feasible.
- Model catalog download/verification against local test server or fixture.

Manual verification matrix for Windows MVP:

- Notepad.
- Browser text field.
- Codex message box or similar app text box.
- Microsoft Word or another rich editor.
- Windows Terminal.
- Electron app.
- Elevated target app.
- Remote desktop or VM window.
- Clipboard-heavy workflow.

Performance tests:

- Cold start time.
- First dictation latency.
- Real-time factor by model/backend.
- Memory use during recording/transcription.
- Time from stop to inserted text.

## MVP Milestones

1. Skeleton WinUI app with tray and settings shell.
2. Mock provider dictation loop that inserts canned text into the previous app.
3. WASAPI capture with meter and recording state.
4. `whisper.cpp` provider with one bundled/imported model path.
5. Model catalog/download/verification.
6. Direct insertion plus clipboard fallback.
7. Global hotkey and floating button.
8. Preview-before-insert mode.
9. Export TXT, Markdown, JSON, SRT, and VTT.
10. Packaging, signing, and first-run setup.

## Risks and Mitigations

### Text insertion reliability

System-wide insertion is the highest product risk. Windows apps vary widely, and privilege boundaries are real.

Mitigation:

- Implement insertion as a dedicated platform service.
- Keep clipboard fallback polished.
- Add per-app rules early.
- Maintain a manual app compatibility matrix.
- Do not promise elevated-app insertion from a normal process.

### Real-time performance

`whisper.cpp` on CPU may be too slow for some machines or model sizes.

Mitigation:

- Benchmark before enabling real-time dictation.
- Recommend small/fast models for MVP.
- Make slow mode explicit and opt-in.
- Keep provider interface ready for ONNX Runtime and Windows ML acceleration.

### NPU fragmentation

Windows NPU support differs by hardware vendor, driver, model format, and runtime.

Mitigation:

- Treat NPU as a provider capability, not a universal backend.
- Add providers incrementally after measuring real devices.
- Keep model catalog provider-specific.

### Packaging native runtimes

Native ASR engines and acceleration libraries can complicate installers.

Mitigation:

- Start with the smallest reliable `whisper.cpp` CPU build.
- Add optional accelerated runtimes only when packaging is understood.
- Hash and version provider binaries.

### User trust

Microphone capture, global hooks, and text injection can feel invasive.

Mitigation:

- Keep all processing local.
- Make triggers and retention settings obvious.
- Avoid hidden recording.
- Show clear recording state in tray/overlay.
- Keep logs private and redacted.

## Future Linux Considerations

Reuse core workflow and provider abstractions, but create a separate platform layer.

Likely platform concerns:

- UI shell can be evaluated later; do not force WinUI assumptions into core.
- Audio through PipeWire/PulseAudio.
- Global shortcuts through desktop portals where available.
- Text insertion differs sharply between X11 and Wayland.
- Wayland may require compositor-specific protocols, portals, clipboard paste fallback, or user-granted automation tools.
- Tray support varies by desktop environment.
- Packages should include `.deb`/`.rpm` and possibly Flatpak/AppImage after the native integration story is clear.

Linux should not be considered complete until dictation insertion works predictably on the target desktop environments.

## Future macOS Considerations

Reuse core workflow and provider abstractions, but expect a separate native integration layer.

Likely platform concerns:

- Microphone permission through macOS privacy prompts.
- Accessibility and Input Monitoring permissions for focus inspection, hotkeys, and insertion.
- Text insertion through Accessibility APIs, keyboard events, and clipboard fallback.
- Menu bar control instead of Windows tray.
- Packaging through signed and notarized `.dmg` or `.pkg`.
- Apple Silicon acceleration may make a Core ML or ONNX Runtime provider attractive.

macOS should come last because the product should first prove the native dictation loop on Windows and then Linux.

## Decisions To Revisit After MVP Prototype

- Whether CPU-only `whisper.cpp` is good enough for the initial supported hardware floor.
- Whether the first accelerated provider should be ONNX Runtime, Windows ML, DirectML, or a vendor-specific path.
- Whether the app needs a C++ engine host process for isolation instead of in-process native DLL loading.
- Whether transcript history belongs in the MVP UI or remains a hidden/recent-session implementation detail.
- Whether direct `SendInput` insertion or clipboard paste should be the default for specific app categories.
- Whether packaged MSIX is sufficient for all required Windows integration, or a traditional installer path is needed for some distribution channels.
