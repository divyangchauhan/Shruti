# App Compatibility Matrix

This matrix tracks Shruti's current insertion policy for common Windows target
app classes. "Manual QA status" records whether the policy has been exercised
against a live app instance; automated policy tests are separate from manual QA.

| Target app class | Example process names | Policy | Current support level | Manual QA status |
| --- | --- | --- | --- | --- |
| Plain text editors and standard editable controls | `notepad`, custom Win32/WinUI edit fields | Direct input preferred; clipboard fallback remains available if direct input fails. | Supported by automated insertion tests. | Pending live app pass. |
| Browser text fields | `chrome`, `msedge`, `firefox` | Clipboard paste preferred; slow Unicode typing is available if paste cannot be sent. | Policy covered by automated tests for webview-style targets. Paste submission is recorded as unconfirmed and leaves the transcript on the clipboard for manual paste. | Pending live app pass. |
| Electron and webview editors | `Code`, `Cursor`, `Slack`, `Teams`, `Discord` | Clipboard paste preferred; slow Unicode typing is available if paste cannot be sent. | Policy covered by automated tests, including a Discord-style fallback path. | Pending live app pass. |
| Microsoft Office document editors | `winword`, `excel`, `powerpnt`, `outlook`, `onenote` | Clipboard paste preferred; direct input is skipped for auto-insert attempts. | Policy covered by automated tests. Paste submission is recorded as submitted but unconfirmed and leaves the transcript on the clipboard for manual paste. | Pending live app pass. |
| Terminals and shells | `WindowsTerminal`, `wt`, `OpenConsole`, `conhost`, `cmd`, `powershell`, `pwsh`, `wsl`, `bash`, `ssh`, common terminal hosts | Paste-safe insertion preferred. Shruti replaces line breaks with spaces, sends paste shortcuts without Enter, and tries `Ctrl+V`, `Shift+Insert`, then `Ctrl+Shift+V` if earlier shortcuts cannot be sent. | Policy covered by automated tests. | Pending live app pass. |
| Elevated target apps | Any process captured as elevated | Preview required by target safety checks; Shruti does not auto-insert into elevated targets. | Supported by target safety behavior. | Pending live app pass. |
| Automation-limited editable targets | Apps that accept keyboard text but expose incomplete UI Automation metadata, including modern Notepad surfaces | Direct input is allowed when UI Automation cannot confirm editability or selection state. Confirmed non-editable targets still stop at preview. | Supported by automated insertion tests and Notepad API inspection. | Pending live app pass. |
| Confirmed non-editable targets | Any target where UI Automation explicitly reports a read-only or non-editable focused field | Preview required by target safety checks. | Supported by target safety behavior. | Pending live app pass. |
| Editable target with selected text | Any editable process with selected text | Auto-insert is blocked unless explicit replacement permission is enabled; app policy does not bypass this guard. | Supported by automated insertion tests. | Pending live app pass. |
| Shruti app UI | `Shruti.App.WinUI` | Shruti's own windows are skipped as insertion targets; a foreground-window hook remembers the last valid external target when apps like Notepad become active. | Supported by automated target-capture tests. | Pending live app pass. |

## Policy Notes

- Policy matching is deterministic and process-name based. Matching ignores
  casing and an optional `.exe` suffix.
- Terminal and shell targets use paste-safe insertion because generated line
  breaks may be interpreted as command submission. Shruti does not send Enter
  and replaces transcript line breaks with spaces before terminal paste.
- Office targets prefer clipboard paste because document editors are more likely
  to handle pasted text reliably than low-level Unicode key injection.
- Many real Windows editors accept keyboard input while exposing incomplete UI
  Automation metadata. Shruti treats unknown editability or selection metadata
  as automation-limited rather than unsafe, but still blocks confirmed selected
  text unless explicit replacement is enabled.
- Clipboard paste reports "submitted but cannot be confirmed" after the paste
  shortcut is sent. Submitted paste no longer counts as confirmed insertion;
  Shruti leaves the transcript on the clipboard and keeps the transcript
  available in preview for manual recovery. Failed or partial paste still
  restores the previous clipboard where practical.
- Shruti tracks foreground-window changes and keeps the most recent external
  target so clicking back into the main window to start dictation does not make
  the live transcript box the insertion destination.
