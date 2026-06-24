# App Compatibility Matrix

This matrix tracks Shruti's current insertion policy for common Windows target
app classes. "Manual QA status" records whether the policy has been exercised
against a live app instance; automated policy tests are separate from manual QA.

| Target app class | Example process names | Policy | Current support level | Manual QA status |
| --- | --- | --- | --- | --- |
| Plain text editors and standard editable controls | `notepad`, custom Win32/WinUI edit fields | Direct input preferred; clipboard fallback remains available if direct input fails. | Supported by automated insertion tests. | Pending live app pass. |
| Browser text fields | `chrome`, `msedge`, `firefox` | Default policy: direct input preferred; clipboard fallback remains available if direct input fails. | Expected to work in normal editable fields; app-specific override not currently required. | Pending live app pass. |
| Electron and webview editors | `Code`, `Cursor`, `Slack`, `Teams`, `Discord` | Default policy: direct input preferred; clipboard fallback remains available if direct input fails. | Expected to work in editable fields, with fallback covering direct-input rejection. | Pending live app pass. |
| Microsoft Office document editors | `winword`, `excel`, `powerpnt`, `outlook`, `onenote` | Clipboard paste preferred; direct input is skipped for auto-insert attempts. | Policy covered by automated tests. Paste submission is treated as unconfirmed by the current result model. | Pending live app pass. |
| Terminals and shells | `WindowsTerminal`, `wt`, `OpenConsole`, `conhost`, `cmd`, `powershell`, `pwsh`, `wsl`, `bash`, `ssh`, common terminal hosts | Preview required; Shruti does not auto-insert. | Policy covered by automated tests. | Pending live app pass. |
| Elevated target apps | Any process captured as elevated | Preview required by target safety checks; Shruti does not auto-insert into elevated targets. | Supported by target safety behavior. | Pending live app pass. |
| Unknown or non-editable targets | Any process where editability or selected-text state cannot be confirmed | Preview required by target safety checks. | Supported by target safety behavior. | Pending live app pass. |
| Editable target with selected text | Any editable process with selected text | Auto-insert is blocked unless explicit replacement permission is enabled; app policy does not bypass this guard. | Supported by automated insertion tests. | Pending live app pass. |

## Policy Notes

- Policy matching is deterministic and process-name based. Matching ignores
  casing and an optional `.exe` suffix.
- Terminal and shell targets are preview-first because generated text may be
  interpreted as commands.
- Office targets prefer clipboard paste because document editors are more likely
  to handle pasted text reliably than low-level Unicode key injection.
- Clipboard paste currently reports "submitted but cannot be confirmed" after
  the paste shortcut is sent and the previous clipboard contents are restored.
