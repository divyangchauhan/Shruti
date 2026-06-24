# Privacy and Diagnostics

Shruti is local-first. Microphone audio, transcripts, settings, model files, benchmark data, and diagnostics are expected to stay on the device unless the user explicitly exports or shares them.

## Default Diagnostics Policy

- Diagnostics must omit transcript text by default.
- Diagnostics must omit target window titles by default because titles can include document names, messages, URLs, or email subjects.
- Diagnostics may include safe operational metadata: session state, outcome, process name, model/backend readiness, microphone status, insertion method, elevated-target status, and counts such as transcript character count or segment count.
- Error details are opt-in diagnostic details. When included, known sensitive strings such as user paths, email addresses, and URLs should be redacted.
- Local logs use JSON Lines under the local app data Logs directory and should use structured fields instead of free-form transcript dumps.
- The Settings diagnostics snapshot shows state, outcome, target process, transcript counts, and recovery guidance while omitting transcript text and target window titles by default.

## User-Facing Recovery Copy

Microphone failures should point users to Windows Settings > Privacy & security > Microphone and remind them to allow Shruti and desktop apps.

Missing model failures should tell users to install or select a local speech model in Models.

Unsupported hardware/backend failures should suggest Auto or CPU, a smaller supported model, or slow mode only when slower-than-real-time transcription is acceptable.

Target insertion failures should tell users to return to the target field, confirm it accepts typing, and retry with Preview first or Copy when the target app blocks insertion.
