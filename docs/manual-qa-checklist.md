# Manual QA Checklist

Use this checklist for PR-23 accessibility, privacy, diagnostics, and recovery messaging checks.

## Privacy and Diagnostics

- Confirm Settings shows the privacy and diagnostics explanation.
- Confirm Settings shows a redacted diagnostics snapshot after a dictation run.
- Confirm diagnostics snapshots omit transcript text by default.
- Confirm diagnostics snapshots omit target window titles by default.
- Confirm included error details redact user paths, email addresses, and URLs.
- Confirm user-facing failures do not expose raw transcript text.

## Accessibility

- Navigate Home, History, Models, and Settings with the keyboard.
- Tab through Start/Stop, Pause/Resume, Cancel, insertion mode, transcript preview, Copy, Insert, and Retry.
- Confirm screen readers announce dictation state changes and error text.
- Confirm primary controls have useful accessible names and help text.
- Check the primary workflow in Windows high contrast mode.

## Recovery Messages

- No microphone or blocked microphone permission shows recovery guidance.
- Missing or unavailable model shows model-install guidance.
- Unsupported backend or slow model shows Auto/CPU/smaller-model guidance.
- Target insertion failure shows Preview first or Copy guidance.
- Elevated target insertion failure explains the permission mismatch or fallback.
