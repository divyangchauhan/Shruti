# Manual QA Checklist

Use this checklist for PR-25 real Windows workflow compatibility. Record the
Shruti build, Windows version, input device, selected model, target app version,
insertion mode, result, and any diagnostics snapshot for each failed case.

## Setup

- Build the debug native shim and run the WinUI app from the repository root.
- Install or verify the recommended tiny English model in `%LOCALAPPDATA%\Shruti\Models`.
- Confirm Auto insert, Preview first, and Copy only can be selected.
- Confirm the transcript preview updates during recording and the final transcript is non-empty.
- Keep a short, harmless phrase for each run, such as `hello from shruti qa`.

## Target App Matrix

| Target | Setup | Expected result |
| --- | --- | --- |
| Notepad | Focus an empty Notepad document. | Auto insert restores Notepad and sends direct input. |
| Browser text field | Focus a normal editable field in Chrome, Edge, or Firefox. | Auto insert restores the browser and sends direct input, with clipboard fallback only if direct input fails. |
| Codex-style web field | Focus a multiline browser text box without submitting the form. | Auto insert inserts the final transcript as text without submitting. |
| Microsoft Word | Focus a blank document. | Auto insert uses clipboard paste, restores the previous clipboard when possible, and reports the paste as submitted. |
| Terminal or shell | Focus Windows Terminal, PowerShell, cmd, WSL, or an SSH shell. | Auto insert stops at preview and does not type or paste into the terminal. |
| Electron editor | Focus an editable field in VS Code, Cursor, Slack, Teams, or Discord. | Auto insert sends direct input, with clipboard fallback only if direct input fails. |
| Elevated app | Focus an app running as administrator while Shruti is not elevated. | Auto insert stops at preview and explains the permission mismatch. |
| Selected text | Select text in any editable target. | Auto insert stops unless explicit replacement permission is used from preview insertion. |
| Confirmed non-editable surface | Focus a read-only field that exposes read-only state through UI Automation. | Auto insert stops at preview and explains that the target is not editable. |
| Automation-limited editor | Focus a real text editor that exposes incomplete UI Automation metadata, such as modern Notepad. | Auto insert still sends keyboard input to the focused app. |
| Shruti app UI | Focus an external editor, switch back to Shruti, then start dictation from the main window. | Auto insert restores the external editor and never types into Shruti's live transcript box. |

## Regression Checks

- Preview first never inserts until the user chooses Insert.
- Copy only copies the final transcript and never restores or types into the target.
- Cancel stops the run without inserting or copying.
- Empty final transcript is not inserted.
- Shruti's own window is never selected as the auto-insert target.
- Clipboard fallback never overwrites an unrestorable clipboard payload.
- Partial direct input does not retry through clipboard.
- Partial paste releases modifier keys and reports failure.
- Diagnostics snapshots omit transcript text and target window titles by default.

## Accessibility And Recovery

- Navigate Home, History, Models, and Settings with the keyboard.
- Tab through Start/Stop, Pause/Resume, Cancel, insertion mode, transcript preview, Copy, Insert, and Retry.
- Confirm screen readers announce dictation state changes and error text.
- Confirm primary controls have useful accessible names and help text.
- Check the primary workflow in Windows high contrast mode.
- Verify recovery text for blocked microphone, missing model, unsupported backend, insertion failure, and elevated target failure.
