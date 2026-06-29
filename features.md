# Shruti Feature Inventory

Date: 2026-06-18

## Product Direction

Shruti is a native desktop speech-to-text app for Windows, Linux, and macOS. It is local-first and inspired by WhisperAI, but with a different center of gravity: press a button, speak, and insert useful text into another app immediately. The primary workflow is dictation into the currently focused app, such as writing a Codex message by pressing a button, speaking, and having the final text appear in the Codex message box. The app should run an open-source transcription model locally, prefer NPU when available, then GPU, then CPU, and only support hardware that can run the selected model fast enough for the intended experience.

The first product should feel like a focused native dictation utility rather than a website, PWA, or full SaaS transcription platform. A transcript library can exist, but it supports the main job: quickly turning speech into text inside the app the user is already using. WhisperAI's public surface suggests features like upload transcription, speaker labels, timestamps, transcript editing, export, translation, AI summaries, folders/tags, account plans, cloud/team features, and PWA behavior. Shruti should borrow the useful workflow ideas without inheriting web-app, SaaS, account, or admin complexity.

Platform priority:
- Windows should be perfected first.
- Linux support comes after Windows.
- macOS support comes last.

Distribution:
- Native installers/packages are required for supported platforms.
- Windows and Linux installers are required before those platforms are considered complete.
- macOS packaging is required when macOS support is added.

Reference signals:
- https://whisperai.com/
- https://whisperai.com/help
- https://whisperai.com/howto
- https://whisperai.com/pricingandplans
- https://whisperai.com/sitemap.xml
- https://whisperai.com/site.webmanifest

## Core MVP Features

### Recording and Transcription

- One primary dictation button to start and stop microphone capture.
- Live transcription display while the user speaks.
- Finalized transcript after recording stops.
- Clear recording states: idle, requesting microphone permission, recording, transcribing, paused, failed, complete.
- Basic audio level meter or waveform so the user knows the microphone is active.
- Microphone device picker.
- Start, pause/resume, stop, cancel, and retry controls.
- Native desktop audio capture on Windows, Linux, and macOS.
- Graceful handling for denied microphone permission, unavailable audio input, silence, very noisy audio, and unsupported hardware.

### System-Wide Dictation

- Insert finalized text into the app/control that was focused before dictation started.
- Preserve or restore focus to the target app after recording.
- Global hotkey to start/stop dictation without opening the main window.
- Floating mic button or compact overlay for users who prefer mouse/touch interaction.
- Tray/menu-bar control for start, stop, cancel, and settings.
- User can enable any combination of global hotkey, floating button, and tray/menu-bar controls.
- Clipboard-based insertion fallback when direct text injection is unavailable.
- Safe insertion behavior that avoids overwriting selected text unless the user chooses that behavior.
- Auto-insert finalized text immediately after recording stops by default.
- Optional preview-before-insert mode for users who want review before insertion.
- Cancel dictation without inserting text.
- Support both press-once-start/press-again-stop and push-to-talk.
- Basic punctuation handling suitable for messages, prompts, notes, and documents.

### Local Model Execution

- Local open-source speech recognition model.
- Hardware capability detection at startup and in settings.
- Backend preference order: NPU if available and supported, GPU if available and supported, otherwise CPU.
- User-visible status showing which backend is active.
- Real-time readiness check for the selected model/backend.
- Clear unsupported-state messaging when a model is too slow for real-time use on the current machine.
- Model selection with sensible defaults.
- Automatically download 2-3 recommended models during setup or first run.
- Option to browse/download additional supported models.
- Model import/remove flow for advanced users.
- Slower-than-real-time transcription is allowed only when the user enables it in settings.
- Warn clearly before using slow non-real-time transcription.
- Offline operation once the model is installed.

### Immediate Transcript Output

- Lightweight post-dictation transcript view for copy, retry, and troubleshooting.
- Editable transcript text area when preview-before-insert is enabled.
- Copy transcript button.
- Mark transcripts created through system-wide dictation separately from longer recording sessions.

### Settings

- Microphone input device.
- Preferred compute backend: Auto, NPU, GPU, CPU.
- Model selection.
- Language selection plus auto-detect if supported by the chosen model.
- English-first MVP.
- Later multilingual support, with Indian languages as a priority after English.
- Transcription mode: balanced default, faster/lower accuracy, slower/higher accuracy.
- Insertion mode: auto-insert, preview first, copy only.
- Insertion backend preference: direct text injection when possible, clipboard paste fallback.
- Global hotkey configuration.
- Floating button / tray behavior.
- Audio retention setting: keep recordings, delete recordings after transcription, or ask each time.
- Slower non-real-time transcription toggle, disabled by default.
- Storage location for transcripts/models.
- Privacy controls explaining what stays local.

### Export

- Export transcript as TXT and Markdown.
- Export structured transcript as JSON.
- Export subtitles as SRT/VTT when timestamps are available.
- Copy plain text and copy with timestamps.

## Important V1 Features

### Transcript Review and Editing

- Segment-level transcript view with timestamps.
- Click a segment to jump to matching audio if audio retention is enabled.
- Manual correction of segment text.
- Merge/split transcript segments.
- Find and replace.
- Optional confidence/uncertainty indicators if the selected engine exposes them.

### File Transcription

- Upload/import local audio files.
- Optional import for video files if the app can extract audio locally.
- Batch queue for multiple files.
- Progress, cancel, and retry for queued files.

### Speaker Support

- Speaker labels for multi-speaker recordings are not required for MVP.
- Architecture should leave room for later diarization without a major rewrite.
- Manual speaker rename.
- Optional diarization as a post-processing step if real-time diarization is not fast enough.
- Export with or without speaker labels.

### Transcription Guidance

- Custom vocabulary / important terms.
- Light cleanup options: preserve exact speech, clean filler words, readable paragraphs.
- Domain presets such as general notes, meeting, interview, technical, legal, medical, and support call.
- Warning that cleanup can alter wording and should be optional.

### Desktop Convenience

- Optional auto-copy finalized transcript.
- Native notifications for completed transcription, failures, and model downloads.
- Launch at login option.
- Per-app behavior rules, such as paste-safe terminal insertion and direct input for standard note apps.

## Advanced / Later Features

### AI Post-Processing

- Not required for MVP; first version should stay transcription-focused.
- Local or user-configured AI summary generation.
- Action items and key points.
- "AI-ready transcript" prompt/export flow.
- Chat with transcript.
- Translation to another language.
- Title suggestion.

### Organization

- Transcript history/library after core dictation workflow is solid.
- Local transcript history with created date, duration, language, backend, and model metadata.
- Save transcript locally.
- Session title/rename support.
- Delete recording/transcript.
- Search within saved transcripts.
- Folders.
- Tags.
- Favorites/pinned transcripts.
- Transcript metadata filters.
- Archive.
- Import/export app data backup.

### Integrations

- Export to clipboard, file, Notion, Google Docs, Obsidian, or a watched folder.
- Optional cloud sync.
- Optional local HTTP API or CLI for automation.
- Optional webhooks for advanced users.

### Quality and Accessibility

- Noise suppression toggle.
- Voice activity detection settings.
- Silence trimming.
- Keyboard-first operation.
- Screen-reader-friendly controls.
- High-contrast theme.
- Light/dark/system theme.
- Future UIAccess accessibility helper for elevated-target insertion, gated behind signing, secure install location, and explicit user trust/security review.

## Non-Goals for the First Version

- Website/PWA-first delivery.
- User accounts, billing, pricing plans, admin panels, team workspaces, and subscription gates.
- Server-side transcription as the default path.
- AI summaries, action items, transcript chat, and translation.
- Speaker labels as a user-facing MVP feature.
- Non-English language support as a required MVP feature.
- Supporting every NPU/GPU/CPU combination.
- Guaranteeing real-time transcription for every model size.
- Using transcription output as certified medical, legal, or safety-critical record without human review.

## Settled Decisions

- Windows is the first platform to perfect.
- Linux is second.
- macOS is last.
- Native installers/packages are required.
- Global hotkey, floating mic button, and tray/menu-bar controls should all be supported and individually configurable.
- Audio retention is a setting: keep recordings, delete after transcription, or ask each time.
- English-only is acceptable for MVP.
- Indian language support is important after the MVP.
- Speaker labels are not required for MVP, but the architecture should not make later diarization painful.
- Initial exports: TXT, Markdown, JSON, SRT, and VTT.
- MVP should focus on transcription only, without summaries/action items.
- The app should automatically download 2-3 recommended models and let users download more.
- Slow non-real-time transcription is allowed only behind a user-enabled setting and should show a warning.
- Transcript history/library comes after the core dictation workflow is solid.
- Insertion should happen immediately by default.
- Both push-to-talk and press-once-start/press-again-stop should be supported.
