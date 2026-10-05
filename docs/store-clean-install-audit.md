# Store clean-install repair and UI audit

Date: 2026-09-25. Branch: `fix/store-clean-install`.

## Package for resubmission

- File: `artifacts/installer/output/Shruti-1.0.5.0-x64.msix`
- Identity: `DivyangChauhan.Shruti`
- Publisher: `CN=E262040C-30A8-40A4-800F-6C715C2EF7CF`
- Publisher display name: `Divyang Chauhan`
- Store ID: `9N39R62TTCPS`
- SHA-256: `415A6802EE80184CA29B05D64B9AFF9F1662C8ED7088EA1B075D0D9B9AE30B48`

Upload the file above. The separately signed `-local-test.msix` copy is for
installation testing. The Store upload file is unsigned.

## Certification failure and repair

The submitted 1.0.0.0 package contained `shruti_whisper.dll` but omitted its
Vulkan loader, Visual C++ CRT, and OpenMP dependencies. The developer PC supplied
those files from outside the package. Restricted dependency lookup reproduced
the certification error, `Provider 'whisper.cpp' did not report any usable backend`,
with Windows loader error 126.

Adding only the CRT/OpenMP files still failed. Adding the Vulkan loader made the
same DLL load. Disabling Vulkan driver discovery then exposed CPU successfully,
and CPU transcription of the speech fixture passed. A Vulkan-enabled speech DLL
needs the loader even when inference will run on CPU.

`stage-native-dependencies.ps1` now copies the official x64 Visual C++ CRT and
OpenMP redistributables and a checksum-pinned LunarG Vulkan runtime into the app
directory. The Vulkan license ships with the package. No system-wide runtime or
driver installer runs on the user's PC.

Packaging now runs `test-native-package-runtime.ps1` in a fresh PowerShell process.
It restricts native dependency lookup to the package directory and Windows
KnownDLLs, explicitly loads the Windows configuration-manager DLL, and disables
Vulkan drivers. Missing native dependencies fail the build. Package verification
also rejects the old package because its required runtime files are absent.

## Other defects fixed during the audit

- Setup previously treated device enumeration as a microphone permission check.
  It now opens capture, requests Windows permission when necessary, waits for an
  audio frame, disposes capture, and shows recovery guidance if the check fails.
- Setup download errors are now visible inside the welcome screen. Finishing
  setup handles settings-save failures without an unhandled async exception.
- Auto can be selected again in Settings. The Models page has an All filter.
- Unsupported recording-retention options were removed. Audio remains in memory;
  the UI no longer promises recording files that it never creates.
- The microphone selector refreshes its enabled state and falls back to an
  available input when the remembered device no longer exists.
- Silence, cancellation, copy-only, and ordinary preview outcomes no longer
  produce false microphone or insertion failure guidance. Silence has a visible
  main-screen message.
- Modern Notepad corrupted rapid Unicode packet input during the live audit.
  Its policy now uses clipboard paste. Exact-text insertion and edited-preview
  replacement passed against the real Notepad document.
- The floating bar's hover controls failed to appear reliably. Native cursor hit
  testing now keeps hover stable while the non-activating capsule changes size.
- Returning to Dictation refreshes the selected model and processor description.
- Replaying setup while recording is blocked with an explanatory message.

## Test results

The regression run used incremental installed builds ending at 1.0.5.0.
First-run tests started with no settings or models. Later tests exercised the
installed app, not a workspace executable. The final installed app, core library,
and native speech DLL hashes match the final package staging files.
The final installed 1.0.5.0 package also passed a fresh live dictation run with
default settings; Notepad's resulting text matched its transcript exactly.

| Area | Result |
| --- | --- |
| Original app uninstall and data reset | Passed. The old development-identity package was removed. Original app data was moved to `artifacts/clean-install-backup-20260925`. |
| Fresh Store-identity installation | Passed. Correct Store publisher and package family installed. |
| Welcome flow | Get started, actual microphone check, default model download, shortcut page, finish, and replay passed. Windows microphone consent was granted by the user. |
| Default processor selection | Auto used a usable GPU on the test PC. CPU remained available with GPU driver discovery disabled. |
| Live dictation | Speaker-to-microphone transcription passed. Explicit CPU and the optional NPU model also produced transcripts. |
| Main recording controls | Start, stop, pause, resume, cancel, and retry passed. |
| Auto-insert | Real Notepad received the exact transcript after the clipboard-policy repair. |
| Preview mode | Editable preview, Copy, Insert, refusal to overwrite selected text, and explicitly permitted replacement passed. |
| Copy-only mode | Clipboard matched the preview exactly; the target document remained unchanged. |
| Failure recovery | Temporarily unavailable model produced an error and Retry. Restoring the model allowed Retry to start capture. |
| Models page | Navigation, All/CPU/GPU/NPU filters, download, valid import, invalid import rejection, picker cancellation, activation, and removal passed. Removing an imported model preserved its source file. |
| Compute settings | CPU, GPU, Auto, and NPU with a compatible model passed. NPU was disabled for the incompatible tiny.en model. |
| Microphone selector | Available device enumeration and selection passed. Only one physical input was available. |
| Appearance and toggles | System, Light, Dark, hold-to-dictate, floating bar, tray commands, global trigger, and slow-mode controls updated and persisted. |
| Shortcut editors | Capture, record again, save, and cancel passed. F8 hold/release and Ctrl+Alt+F9 start/stop were exercised. The Windows-key default chord was not synthesized by automation. |
| Floating bar | Hover, Start, Finish, Cancel, Open settings, context menu, Hide, and re-enable passed. |
| Tray | Show, Settings, Start, Stop, Cancel, enabled-state transitions, and Exit passed. |
| Lifecycle | Closing to tray, reopening, normal exit/relaunch, persisted settings, and second-launch single-instance behavior passed. |
| Managed regression suite | 246 passed, zero failed or skipped. New cases cover normal-outcome diagnostics and Notepad's clipboard policy. |
| Package checks | Correct identity/version/architecture, required runtime files, native dependency isolation, and installed-file hash comparison passed. |

The downloaded test-only base models were removed. The installed app is left with
tiny.en, Auto compute, automatic insertion, system theme, and the default shortcut.
Original pre-audit settings and models remain in the backup directory.

## Evidence

- `artifacts/store-clean-install-build.log`
- `artifacts/store-clean-install-tests.log`
- `artifacts/old-store-package-regression.log`
- `artifacts/installed-cpu-transcription.log`
- `artifacts/installed-gpu-transcription.log`
- `artifacts/final-signed-package-verification.log`
- `artifacts/clean-install-live-transcript.png`
- `artifacts/final-package-live-transcript.png`
- `artifacts/audit-light-settings.png`
- `artifacts/audit-dark-settings.png`

Native speech DLL SHA-256:
`799E6BB8B05409E7268C23C3EB1463C9C9DC1A805CB7ACB5EF3B6E32F22F7612`.
The GPU fixture log reports `using Vulkan0 backend`; the isolated CPU log reports
no GPU and completes the expected transcript.

## Limits and remaining release checks

Shared Windows/.NET/Visual C++ runtimes and graphics drivers were retained to
avoid breaking unrelated apps. The native dependency regression isolates package
loading instead. This is not a completely clean Windows VM, and it does not
prove compatibility with every supported PC. Windows Sandbox was unavailable.

WACK is installed but was not run because this session is not elevated. A clean
Windows VM run and elevated WACK run remain release checks. Permission denial,
physical microphone removal, interrupted downloads, and all other applications'
insertion behavior were not exhaustively exercised in the live UI audit.

## Suggested certification notes

Fixed the missing native runtime dependencies that prevented dictation on clean
machines. The package includes the speech runtime dependencies and supports CPU
transcription without GPU drivers or separately installed developer tools.

On first launch, complete the welcome flow, allow microphone access, and download
the recommended English model. Leave compute on Auto. Open a normal Notepad
document, start dictation, speak, then stop. The transcript appears in Shruti and
is inserted into the target. Internet is needed for the first model download;
subsequent transcription runs locally.
