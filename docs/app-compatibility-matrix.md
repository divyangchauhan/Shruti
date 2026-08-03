# App Compatibility Matrix

This matrix tracks Shruti's current insertion policy for common Windows target
app classes. "Manual QA status" records whether the policy has been exercised
against a live app instance; automated policy tests are separate from manual QA.

| Target app class | Example process names | Policy | Current support level | Manual QA status |
| --- | --- | --- | --- | --- |
| Plain text editors and standard editable controls | `notepad`, custom Win32/WinUI edit fields | Direct input preferred; clipboard fallback remains available if direct input fails. | Supported by automated insertion tests. | Passed live app test on 2026-07-30. |
| Browser text fields | `chrome`, `msedge`, `firefox` | Clipboard paste preferred; slow Unicode typing is available if paste cannot be sent. | Policy covered by automated tests for webview-style targets. A paste delivered while the target stays foreground counts as inserted; the transcript also remains on the clipboard as a recovery copy. | Passed live browser and Codex-style field tests on 2026-07-30. |
| Electron and webview editors | `Code`, `Cursor`, `Slack`, `Teams`, `Discord` | Clipboard paste preferred; slow Unicode typing is available if paste cannot be sent. | Policy covered by automated tests, including a Discord-style fallback path. | Passed live app test on 2026-07-30. |
| Microsoft Office document editors | `winword`, `excel`, `powerpnt`, `outlook`, `onenote` | Clipboard paste preferred; direct input is skipped for auto-insert attempts. | Policy covered by automated tests. A paste delivered while the target stays foreground counts as inserted; the transcript also remains on the clipboard as a recovery copy. | Passed live Microsoft Word test on 2026-07-30. |
| Terminals and shells | `WindowsTerminal`, `wt`, `OpenConsole`, `conhost`, `cmd`, `powershell`, `pwsh`, `wsl`, `bash`, `ssh`, common terminal hosts | Paste-safe insertion preferred. Shruti replaces line breaks with spaces, sends paste shortcuts without Enter, and tries `Ctrl+V`, `Shift+Insert`, then `Ctrl+Shift+V` if earlier shortcuts cannot be sent. | Policy covered by automated tests. | Passed live terminal test on 2026-07-30. |
| Elevated target apps | Any process captured as elevated | Preview required by target safety checks; Shruti does not auto-insert into elevated targets. | Supported by target safety behavior. Direct elevated insertion is deferred to a future signed UIAccess-capable helper. | Passed expected Preview/Copy safety fallback on 2026-07-30. |
| Automation-limited editable targets | Apps that accept keyboard text but expose incomplete UI Automation metadata, including modern Notepad surfaces | Direct input is allowed when UI Automation cannot confirm editability or selection state. Confirmed non-editable targets still stop at preview. | Supported by automated insertion tests and Notepad API inspection. | Passed live modern Notepad test on 2026-07-30. |
| Confirmed non-editable targets | Any target where UI Automation explicitly reports a read-only or non-editable focused field | Preview required by target safety checks. | Supported by target safety behavior. | Pending live app pass. |
| Editable target with selected text | Any editable process with selected text | Auto-insert is blocked unless explicit replacement permission is enabled; app policy does not bypass this guard. | Supported by automated insertion tests. | Passed live selected-text safety test on 2026-07-30. |
| Shruti app UI | `Shruti.App.WinUI` | Shruti's own windows are skipped as insertion targets; a foreground-window hook remembers the last valid external target when apps like Notepad become active. Shell surfaces (taskbar, Start menu, search, task view) are never remembered as targets. | Supported by automated target-capture tests. | Pending live app pass. |

## Policy Notes

- Policy matching is deterministic and process-name based. Matching ignores
  casing and an optional `.exe` suffix.
- Terminal and shell targets use paste-safe insertion because generated line
  breaks may be interpreted as command submission. Shruti does not send Enter
  and replaces transcript line breaks with spaces before terminal paste.
- Office targets prefer clipboard paste because document editors are more likely
  to handle pasted text reliably than low-level Unicode key injection.
- Elevated-target QA passes when Shruti blocks unsafe direct insertion, retains
  the transcript for Preview/Copy, and explains the permission mismatch. Direct
  insertion into elevated apps remains future work requiring a signed
  UIAccess-capable helper, secure installation, installer changes, and a
  dedicated security review.
- Many real Windows editors accept keyboard input while exposing incomplete UI
  Automation metadata. Shruti treats unknown editability or selection metadata
  as automation-limited rather than unsafe, but still blocks confirmed selected
  text unless explicit replacement is enabled.
- Immediately before sending any input, Shruti verifies that the captured
  target is still the foreground window and stops at preview if it is not, so
  keystrokes cannot land in an unrelated window and be reported as inserted.
- A clipboard paste that is delivered while the target stays foreground is
  reported as inserted; the transcript also remains on the clipboard as a
  recovery copy. If the target loses focus before the paste can be confirmed,
  the result is "submitted but unconfirmed" and the transcript stays available
  in preview. Failed or partial paste still restores the previous clipboard
  where practical.
- Windows adds `CF_LOCALE` and synthesized text formats alongside any text on
  the clipboard; Shruti treats those as restorable text. Non-text clipboard
  content (images, files, rich formats) no longer blocks insertion — the paste
  proceeds and the result notes that the previous clipboard content could not
  be preserved.
- Focus restore escalates from `SetForegroundWindow` through an
  `AttachThreadInput` attempt and a synthetic-input foreground-permission
  grant, polling for the target to settle between attempts.
- Shruti tracks foreground-window changes and keeps the most recent external
  target so clicking back into the main window to start dictation does not make
  the live transcript box the insertion destination. Shell windows such as the
  taskbar, Start menu, and search are excluded from that memory, and cached
  target metadata is refreshed when the target is promoted for insertion.
