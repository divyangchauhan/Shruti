# Shruti Task Plan and PR Schedule

Date: 2026-06-19

Status: PR-21 complete; PR-22 in review.

This plan turns `features.md` and `architecture.md` into an implementation roadmap. It assumes Windows-first development with WinUI 3, Windows App SDK, local transcription, and immediate system-wide text insertion as the central workflow.

## Planning Assumptions

- Work starts Monday, 2026-06-22.
- Dates are target merge dates, not hard release commitments.
- Each PR should be reviewable on its own and leave the app buildable.
- High-risk areas get mock-first PRs before real platform/provider integration.
- MVP means a user can trigger dictation on Windows, record, transcribe locally with `whisper.cpp`, and insert the final text into the previously focused app.

## Task Status Legend

- `[ ]` Not started
- `[~]` In progress
- `[x]` Done
- `[!]` Blocked or needs a decision

## MVP Task List

### 1. Repository and Build Foundation

- [ ] Create `Shruti.sln`.
- [ ] Add `src/Shruti.App.WinUI` WinUI 3 project.
- [ ] Add `src/Shruti.Core` class library.
- [ ] Add `src/Shruti.Transcription.Abstractions` class library.
- [ ] Add `src/Shruti.Audio.Windows` class library.
- [ ] Add `src/Shruti.Platform.Windows` class library.
- [ ] Add `src/Shruti.Models` class library.
- [ ] Add `src/Shruti.Storage` class library.
- [ ] Add `tests/Shruti.Tests` test project.
- [ ] Add solution-wide formatting/analyzer configuration.
- [ ] Add local build script.
- [ ] Add CI build for restore, build, and tests.
- [ ] Add basic app icon/name placeholders.

Acceptance criteria:

- Solution builds from a clean checkout.
- Tests run locally and in CI.
- WinUI app launches an empty shell window.

### 2. Core Domain and Workflow Abstractions

- [ ] Define dictation session state machine.
- [ ] Define `FocusTarget` and focus restore result models.
- [ ] Define text insertion capability/result models.
- [ ] Define audio device, audio frame, and capture session models.
- [ ] Define trigger event models for hotkey, tray, floating button, and UI button.
- [ ] Define transcription provider abstractions.
- [ ] Define model descriptor and backend capability models.
- [ ] Implement `DictationCoordinator` with mock services.
- [ ] Add cancellation path that never inserts text.
- [ ] Add preview-first and copy-only workflow branches.
- [ ] Add unit tests for state transitions.
- [ ] Add unit tests for insert/preview/cancel behavior.

Acceptance criteria:

- A mock dictation run can move from target capture to recording to final transcript to insertion.
- Core workflow has no dependency on WinUI, `whisper.cpp`, WASAPI, or Win32 insertion APIs.

### 3. Windows App Shell

- [x] Create main dictation window.
- [x] Create recording state UI: idle, requesting microphone, recording, paused, transcribing, failed, complete.
- [x] Add primary start/stop dictation button.
- [x] Add pause, resume, cancel, retry, copy controls.
- [x] Add transcript preview text area.
- [x] Add settings navigation.
- [x] Add app-level dependency injection composition.
- [x] Bind UI to `DictationCoordinator` state.
- [x] Add user-facing error/status surface.
- [x] Add theme support for system light/dark.
- [x] Add basic keyboard navigation.

Acceptance criteria:

- User can exercise the full mock dictation flow from the WinUI app.
- Preview and cancel modes are visible and functional with mock services.

### 4. Windows Target Capture and Text Insertion

- [ ] Implement foreground window capture before Shruti UI activation.
- [ ] Capture target process ID, process name, window title, `HWND`, and thread ID.
- [ ] Add UI Automation focused element capture where available.
- [ ] Implement focus restore to captured target.
- [ ] Implement direct Unicode insertion with `SendInput`.
- [ ] Implement clipboard paste fallback.
- [ ] Snapshot and restore clipboard content where practical.
- [ ] Detect elevated target limitation and report safe fallback.
- [ ] Add insertion capability inspection.
- [ ] Add selected-text safety policy.
- [x] Add per-app insertion policy model.
- [x] Add compatibility test harness for known target apps.

Acceptance criteria:

- Canned text can be inserted into Notepad after triggering dictation from Shruti.
- Clipboard fallback works when direct insertion is disabled.
- Elevated target app limitation is detected or handled without pretending insertion succeeded.

### 5. Global Triggers, Tray, and Floating Button

- [ ] Implement global hotkey registration for press-once-start/press-again-stop.
- [ ] Add push-to-talk trigger path.
- [ ] Add hotkey conflict validation.
- [ ] Add tray icon with start, stop, cancel, settings, and quit.
- [ ] Keep tray available when main window is closed.
- [ ] Add native notification service.
- [ ] Create compact floating mic button.
- [ ] Make floating button state-aware: idle, recording, transcribing, failed.
- [ ] Add settings toggles for hotkey, tray, and floating button.

Acceptance criteria:

- User can start and stop a mock dictation without opening the main window.
- User can enable or disable each trigger independently.

### 6. Windows Audio Capture

- [ ] Enumerate microphone devices.
- [ ] Implement default input device selection.
- [ ] Implement WASAPI capture.
- [ ] Normalize captured audio to provider-required format.
- [ ] Add resampling to 16 kHz mono PCM when needed.
- [ ] Expose audio level frames for the UI meter.
- [ ] Add pause/resume/stop support.
- [ ] Handle missing microphone.
- [ ] Handle permission denial.
- [ ] Handle device disconnect during recording.
- [ ] Add retained rolling buffer for final transcription/retry.

Acceptance criteria:

- User can record audio from the selected microphone.
- UI displays an audio level meter while recording.
- Recorded audio can be passed to a mock transcription provider.

### 7. Local Storage and Settings

- [ ] Define local app data layout.
- [ ] Implement settings repository.
- [ ] Implement SQLite schema for sessions, segments, recordings, and installed models.
- [ ] Implement retention policy: keep, delete after transcription, ask each time.
- [ ] Store recent dictation session metadata.
- [ ] Add transcript export as TXT.
- [ ] Add transcript export as Markdown.
- [ ] Add transcript export as JSON.
- [ ] Add transcript export as SRT when timestamps exist.
- [ ] Add transcript export as VTT when timestamps exist.

Acceptance criteria:

- Settings persist across app restarts.
- Dictation session metadata can be saved and loaded.
- Export outputs match expected fixtures.

### 8. Model Catalog and Download

- [x] Define model catalog JSON schema.
- [x] Add 2-3 recommended MVP models.
- [x] Include model metadata: provider, language, size, hash, download URL, supported backends.
- [x] Implement model download manager.
- [x] Implement hash verification.
- [x] Implement model import flow.
- [x] Implement model remove flow.
- [ ] Add model storage location setting.
- [x] Add offline-ready status once a model is installed.
- [ ] Add model download notifications.

Acceptance criteria:

- User can install a recommended model.
- Hash mismatch blocks model use.
- App can run offline after model installation.

### 9. `whisper.cpp` Provider

- [x] Add native `whisper.cpp` build project or external dependency strategy.
- [x] Add narrow C ABI shim.
- [x] Add C# adapter for native provider calls.
- [x] Load a verified GGML model from the local model catalog path.
- [x] Transcribe buffered audio to final text.
- [x] Emit timestamped segments when available.
- [x] Emit partial transcript events if supported by chosen integration strategy.
- [x] Map native errors into engine-neutral errors.
- [x] Add cancellation.
- [ ] Add provider-level metrics: load time, transcription time, real-time factor.
- [x] Add provider smoke test with a speech fixture audio file.

Acceptance criteria:

- App can locally transcribe recorded microphone audio with `whisper.cpp`.
- Core workflow still depends only on transcription abstractions.

### 10. Backend Selection and Readiness

- [x] Implement provider registry.
- [x] Implement backend preference setting: Auto, NPU, GPU, CPU.
- [x] Implement capability probing.
- [x] Implement model/provider/backend compatibility checks.
- [x] Implement short benchmark flow.
- [x] Cache benchmark results by provider version, model hash, backend, and device.
- [x] Gate slow non-real-time transcription behind explicit setting.
- [x] Show active provider/backend/device in settings.
- [x] Show unsupported hardware/model/backend messaging.

Acceptance criteria:

- Auto mode chooses the best eligible backend.
- Slow mode requires explicit opt-in before transcription proceeds.

### 11. End-to-End Dictation MVP

- [x] Connect trigger to target capture.
- [x] Connect target capture to audio recording.
- [x] Connect recording to `whisper.cpp` transcription.
- [x] Connect final transcript to immediate insertion.
- [x] Add preview-before-insert path.
- [x] Add copy-only path.
- [x] Add cancel-without-insert path.
- [x] Add retry transcription path.
- [x] Add safe insertion behavior for selected text.
- [x] Add per-app fallback rules for terminals and unsupported targets.

Acceptance criteria:

- User can focus Notepad, trigger Shruti, speak, stop, and see text inserted into Notepad.
- User can do the same in a browser text field and a Codex-style message box.
- User can cancel without inserting.

### 12. Reliability, Accessibility, and Polish

- [ ] Add structured redacted local logging.
- [ ] Add diagnostics view that omits transcript text by default.
- [ ] Add first-run privacy explanation.
- [ ] Add microphone permission recovery guidance.
- [ ] Add keyboard-first operation for core controls.
- [ ] Add screen-reader labels for controls and state changes.
- [ ] Add high-contrast theme checks.
- [ ] Add robust failure messages for unsupported hardware, missing model, and target insertion failure.
- [x] Add app compatibility matrix document.
- [ ] Add manual QA checklist.

Acceptance criteria:

- Core workflow is understandable when something fails.
- Accessibility checks pass for primary dictation and settings screens.

### 13. Packaging and Release Readiness

- [ ] Create MSIX packaging project.
- [ ] Configure Windows App SDK runtime requirements.
- [ ] Bundle required native DLLs.
- [ ] Configure code signing.
- [ ] Verify install/update/uninstall.
- [ ] Ensure app updates preserve models, settings, transcripts, and retained recordings.
- [ ] Add installer/bootstrapper if MSIX alone is insufficient.
- [ ] Prepare `winget` manifest after installer stabilizes.
- [ ] Add release notes template.

Acceptance criteria:

- A signed Windows installer can be produced.
- Installed app can complete the MVP dictation workflow on a clean Windows machine with a downloaded model.

## V1 Follow-Up Task List

### Transcript Review and Editing

- [ ] Add segment-level transcript view.
- [ ] Add timestamp navigation.
- [ ] Add segment text correction.
- [ ] Add merge/split segments.
- [ ] Add find and replace.
- [ ] Show confidence/uncertainty if provider exposes it.

### File Transcription

- [ ] Add local audio file import.
- [ ] Add optional local video audio extraction.
- [ ] Add batch queue.
- [ ] Add progress, cancel, and retry for queued files.
- [ ] Reuse model/backend readiness checks.

### Transcription Guidance

- [ ] Add custom vocabulary.
- [ ] Add cleanup mode: exact speech.
- [ ] Add cleanup mode: readable paragraphs.
- [ ] Add cleanup mode: remove filler words.
- [ ] Add domain presets.
- [ ] Add warning that cleanup can alter wording.

### Speaker and Diarization Readiness

- [ ] Add speaker field to transcript segment model.
- [ ] Add manual speaker rename.
- [ ] Add provider/post-processor interface for diarization.
- [ ] Add export with and without speaker labels.

### Organization and Integrations

- [ ] Add transcript history/library UI.
- [ ] Add search within saved transcripts.
- [ ] Add session rename.
- [ ] Add delete recording/transcript.
- [ ] Add folders/tags after history proves useful.
- [ ] Add watched-folder export.
- [ ] Add local CLI or HTTP API only after core UX stabilizes.

## Pull Request Schedule

### Phase 0: Project Foundation

| PR | Status | Target merge | Scope | Primary risk retired |
| --- | --- | --- | --- | --- |
| PR-01 | Done | 2026-06-22 | Solution scaffold, WinUI shell, test project, CI build | Build shape and repo hygiene |
| PR-02 | Done | 2026-06-24 | Core domain models, provider/platform interfaces, mock-friendly `DictationCoordinator`, and workflow tests | Architecture boundaries and workflow correctness before platform code |
| PR-03 | Done | 2026-06-26 | Formatting/lint scripts, analyzer enforcement, and PR validation workflow | Consistent local and CI validation before feature work |

### Phase 1: Mock End-to-End Product Loop

| PR | Status | Target merge | Scope | Primary risk retired |
| --- | --- | --- | --- | --- |
| PR-04 | Done | 2026-06-29 | WinUI dictation screen wired to mock coordinator | App UX can drive workflow |
| PR-05 | Done | 2026-07-01 | Windows target capture and focus restore prototype | Previously focused app can be remembered |
| PR-06 | Done | 2026-07-03 | Text insertion service with `SendInput` and clipboard fallback | System-wide insertion feasibility |
| PR-07 | Done | 2026-07-06 | Global hotkey, tray control, and floating button using mock dictation | Trigger paths work outside main window |

### Phase 2: Real Audio and Local Storage

| PR | Status | Target merge | Scope | Primary risk retired |
| --- | --- | --- | --- | --- |
| PR-08 | Done | 2026-07-08 | WASAPI capture, device picker, audio meter | Microphone path works |
| PR-09 | Done | 2026-07-10 | Settings repository, local data layout, retention policy | Durable app configuration |
| PR-10 | Done | 2026-07-13 | SQLite session/segment storage and export TXT/Markdown/JSON/SRT/VTT | Transcript output path |

### Phase 3: Real Transcription

| PR | Status | Target merge | Scope | Primary risk retired |
| --- | --- | --- | --- | --- |
| PR-11 | Done | 2026-07-15 | Model catalog, download, verification, import/remove | Model lifecycle |
| PR-12 | Done | 2026-07-20 | `whisper.cpp` native shim and C# adapter | Local ASR provider feasibility |
| PR-13 | Done | 2026-07-22 | Audio-to-`whisper.cpp` final transcription integration | Real local transcription loop |
| PR-14 | Done | 2026-06-21 | Floating microphone popup close lifecycle fix | WinUI secondary-window stability |

### Phase 4: MVP Integration and Hardening

| PR | Status | Target merge | Scope | Primary risk retired |
| --- | --- | --- | --- | --- |
| PR-15 | Done | 2026-07-27 | End-to-end auto-insert dictation: trigger, record, transcribe, insert | Core MVP workflow |
| PR-16 | Done | 2026-07-29 | Preview-before-insert, copy-only, cancel/retry, selected-text safety | Safe user control |
| PR-17 | Done | 2026-07-31 | Live partial transcription from `whisper.cpp` through the WinUI transcript surface | Real-time dictation feedback |
| PR-18 | Done | 2026-06-23 | Live-transcription audio quality, performance, cancellation, and lifecycle fixes | Reliable real-time local transcription |
| PR-19 | Done | 2026-06-23 | README and repository metadata polish | Clear project presentation |
| PR-20 | Done | 2026-06-23 | Design-led WinUI shell, system theme support, configurable hold shortcut, optional floating control, and explicitly marked mock history/model surfaces | Complete, honest product surface around the real dictation loop |
| PR-21 | Done | 2026-08-03 | Provider registry, backend readiness, benchmark cache, slow-mode gate | Hardware/model eligibility |
| PR-22 | In review | 2026-08-05 | Per-app insertion policies and compatibility matrix | App-specific insertion reliability |
| PR-23 | Planned | 2026-08-07 | Error handling, privacy copy, diagnostics, accessibility pass | User trust and recoverability |
| PR-24 | Planned | 2026-08-09 | Manual QA fixes across Notepad, browser, Codex-style field, Word, Terminal, Electron, elevated app | Windows compatibility |

### Phase 5: Packaging

| PR | Status | Target merge | Scope | Primary risk retired |
| --- | --- | --- | --- | --- |
| PR-25 | Planned | 2026-08-12 | MSIX packaging with native DLL inclusion | Installable Windows app |
| PR-26 | Planned | 2026-08-14 | Signing, update preservation, clean-machine install test | Release readiness |
| PR-27 | Planned | 2026-08-16 | Bootstrapper or installer adjustments, release notes, optional `winget` prep | Distribution polish |

## Release Gates

### MVP Alpha Gate

- [ ] App installs on a clean Windows machine.
- [ ] User can install or import a supported model.
- [ ] User can trigger dictation globally.
- [ ] User can record microphone audio.
- [ ] User can transcribe locally without network access after model install.
- [ ] User can immediately insert text into Notepad.
- [ ] User can immediately insert text into a browser text field.
- [ ] User can cancel without insertion.
- [ ] User can use clipboard fallback.
- [ ] App clearly reports unsupported hardware or slow model/backend.

### MVP Beta Gate

- [ ] Compatibility matrix covers target Windows apps.
- [ ] Core settings persist reliably.
- [ ] Export formats pass fixture tests.
- [ ] Logs are redacted by default.
- [ ] Accessibility pass completed for primary workflow.
- [ ] Installer preserves local app data across updates.
- [ ] At least one recommended model has acceptable real-time factor on the chosen minimum hardware profile.

## Open Decisions

- [ ] Choose exact .NET version for the first Windows implementation.
- [x] Choose package manager/dependency strategy for `whisper.cpp`.
- [x] Decide whether to vendor `whisper.cpp` source or consume pinned build artifacts.
- [ ] Choose initial recommended GGUF models.
- [ ] Define minimum supported Windows version.
- [ ] Define minimum supported CPU/RAM profile for MVP.
- [ ] Decide whether the first `whisper.cpp` provider ships CPU-only or includes an accelerated option.
- [ ] Decide whether MSIX alone is enough or a bootstrapper is required from day one.
