# Privacy policy

Effective date: October 18, 2026

This policy covers Shruti, the Windows dictation app developed by Divyang
Chauhan.

## Speech and transcription

Shruti processes microphone audio and converts it to text on your computer.
It does not send your audio or transcripts to the developer or a cloud
transcription service. You do not need an account to use dictation.

Shruti processes audio in memory and does not automatically save microphone
recordings. It saves non-empty dictations in a local transcript history,
including the original and final text, time, recording duration, word and edit
counts, and destination process name when available. The Transcriptions page
lets you delete individual entries or clear the history. Insights summarizes
that local history; it does not upload it.

Optional transcript cleanup also runs on your computer. Both the original and
cleaned text remain available in your local history until you delete them.

## Keyboard, active applications, and clipboard

Shruti monitors keyboard events to recognize your configured dictation
shortcuts. It uses information about the active window and focused text field
to insert your transcript into the application you selected.

When nearby cursor text is enabled, Shruti may read up to 512 characters before
the caret, 512 selected characters, and 512 characters after it. This helps
recognize nearby names and join the dictated text with existing text. Shruti
discards this context after that dictation. It does not send or save the context.
The reader skips password fields, recognized address bars and sensitive fields,
recognized banking and payment applications, Shruti's windows, and controls
whose caret cannot be determined. You can disable this option on the Models page.

Copy and paste operations place transcript text on the Windows clipboard.
Paste fallback may read and temporarily preserve existing clipboard contents
so it can restore them after insertion. Other applications, Windows clipboard
history, and clipboard synchronization may retain clipboard text according to
your settings. The destination application handles inserted text under its own
privacy practices.

## Local files and diagnostics

Shruti stores downloaded models, settings such as your microphone and shortcut
choices, and transcription performance measurements on your computer. Its
default data location is `%LOCALAPPDATA%\Shruti`. Windows packaging can redirect
local application data into the app's package storage.

Your Dictionary vocabulary, explicit correction rules, snippets, and transcript
history are saved in a local SQLite database. You can remove entries on their
respective pages. Cleanup diagnostics record rule names or failures, without
recording transcript text or cursor context.

The app displays local diagnostic information to help explain failures. This
can include the target process name, window identifiers, processing device,
insertion results, and transcript length. The diagnostic summary omits
transcript text and window titles. It is not automatically sent to the
developer.

Shruti has no built-in advertising or analytics service and does not
automatically upload usage or diagnostic information to the developer.

## Model downloads and other services

Downloading a model makes an HTTPS request to Hugging Face and may follow
redirects to its download infrastructure. Those services receive your IP
address and request information, including which model files you request.
Model downloads do not include your microphone audio or transcripts.
See the [Hugging Face privacy policy](https://huggingface.co/privacy).

Once a model is installed, local dictation does not require an internet
connection. You can also import a compatible model from your computer.

Microsoft handles Store installation, updates, and any Store or Windows
diagnostic collection under its own settings and
[privacy statement](https://privacy.microsoft.com/privacystatement).

## Your choices and deletion

You can stop dictation, exit Shruti, or revoke microphone access through
Windows privacy settings. You can remove downloaded models in the app.
You can disable cleanup or nearby cursor text on the Models page. Dictionary changes
require an explicit save action. Reviewing a suggested spelling correction does
not save it until you confirm the rule.

To remove remaining local data, exit Shruti and delete its data folder. This
removes saved preferences, vocabulary, snippets, history, and models. Preferences
and models must be configured or downloaded again. Uninstalling the app may leave data outside its package storage, so
check `%LOCALAPPDATA%\Shruti` as well. Delete any copies you saved elsewhere
separately. Removing Shruti's files does not remove text already inserted into
other apps or retained in clipboard history or backups.

## Support and contact

For privacy questions, contact Divyang Chauhan at
[code@divyang.dev](mailto:code@divyang.dev). If you email diagnostic details,
screenshots, audio, or transcripts, the developer receives the information you
choose to include and uses it to respond to your request.

You can also report issues at
[Shruti's GitHub issue tracker](https://github.com/divyangchauhan/Shruti/issues).
GitHub issues are public. Do not post private dictation, recordings, or other
sensitive information there. GitHub handles information submitted to its
service under its [privacy statement](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement).

## Changes to this policy

Updates will appear on this page with a revised effective date.
