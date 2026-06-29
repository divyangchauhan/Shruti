# App Compatibility Matrix

This matrix tracks Shruti's current insertion policy for common Windows target
app classes. "Manual QA status" records whether the policy has been exercised
against a live app instance; automated policy tests are separate from manual QA.

| Target app class | Example process names | Policy | Current support level | Manual QA status |
| --- | --- | --- | --- | --- |
| Plain text editors and standard editable controls | `notepad`, custom Win32/WinUI edit fields | Direct input preferred; clipboard fallback remains available if direct input fails. | Supported by automated insertion tests. | Pending live app pass. |
| Browser text fields | `chrome`, `msedge`, `firefox` | Default policy: direct input preferred; clipboard fallback remains available if direct input fails. | Expected to work in normal editable fields; app-specific override not currently required. | Pending live app pass. |
| Electron and webview editors | `Code`, `Cursor`, `Slack`, `Teams`, `Discord` | Default policy: direct input preferred; clipboard fallback remains available if direct input fails. | Expected to work in editable fields, with fallback covering direct-input rejection. | Pending live app pass. |
| Microsoft Office document editors | `winword`, `excel`, `powerpnt`, `outlook`, `onenote` | Clipboard paste preferred; direct input is skipped for auto-insert attempts. | Policy covered by automated tests. Paste submission is recorded as submitted but unconfirmed so the workflow can complete without claiming text verification. | Pending live app pass. |
| Terminals and shells | `WindowsTerminal`, `wt`, `OpenConsole`, `conhost`, `cmd`, `powershell`, `pwsh`, `wsl`, `bash`, `ssh`, common terminal hosts | Preview required; Shruti does not auto-insert. | Policy covered by automated tests. | Pending live app pass. |
| Elevated target apps | Any process captured as elevated | Preview required by target safety checks; Shruti does not auto-insert into elevated targets. | Supported by target safety behavior. | Pending live app pass. |
| Automation-limited editable targets | Apps that accept keyboard text but expose incomplete UI Automation metadata, including modern Notepad surfaces | Direct input is allowed when UI Automation cannot confirm editability or selection state. Confirmed non-editable targets still stop at preview. | Supported by automated insertion tests and Notepad API inspection. | Pending live app pass. |
| Confirmed non-editable targets | Any target where UI Automation explicitly reports a read-only or non-editable focused field | Preview required by target safety checks. | Supported by target safety behavior. | Pending live app pass. |
| Editable target with selected text | Any editable process with selected text | Auto-insert is blocked unless explicit replacement permission is enabled; app policy does not bypass this guard. | Supported by automated insertion tests. | Pending live app pass. |
| Shruti app UI | `Shruti.App.WinUI` | Shruti's own windows are skipped as insertion targets; a foreground-window hook remembers the last valid external target when apps like Notepad become active. | Supported by automated target-capture tests. | Pending live app pass. |

## Policy Notes

- Policy matching is deterministic and process-name based. Matching ignores
  casing and an optional `.exe` suffix.
- Terminal and shell targets are preview-first because generated text may be
  interpreted as commands.
- Office targets prefer clipboard paste because document editors are more likely
  to handle pasted text reliably than low-level Unicode key injection.
- Many real Windows editors accept keyboard input while exposing incomplete UI
  Automation metadata. Shruti treats unknown editability or selection metadata
  as automation-limited rather than unsafe, but still blocks confirmed selected
  text unless explicit replacement is enabled.
- Clipboard paste reports "submitted but cannot be confirmed" after the paste
  shortcut is sent and the previous clipboard contents are restored. That
  submitted state completes the workflow, while failed or partial paste still
  falls back to preview.
- Shruti tracks foreground-window changes and keeps the most recent external
  target so clicking back into the main window to start dictation does not make
  the live transcript box the insertion destination.
