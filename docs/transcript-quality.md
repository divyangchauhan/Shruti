# Transcript quality development

Development branch: `feature/transcript-quality`, based on main commit
`9c4e48ca4c36a092e12a0b6fa1400978185f0f9d`. The original dirty checkout is separate.

## Review order

The work is split into six local commits with matching branch references. These
are ready for stacked PR review, but no branches have been pushed and no remote
PRs have been opened.

| Layer | Local branch | Deliverable |
| --- | --- | --- |
| 1 | `feature/transcript-quality-01-bench` | Fixture records, scorers, baseline runner, commit `e76edb5` |
| 2 | `feature/transcript-quality-02-pipeline` | Session contracts, no-op implementation, coordinator call, commit `7ee0c85` |
| 3 | `feature/transcript-quality-03-recognition` | Native prompt ABI and model candidate, commit `f323b9e` |
| 4 | `feature/transcript-quality-04-dictionary` | SQLite repository, matcher, Dictionary control, commit `03114f9` |
| 5 | `feature/transcript-quality-05-context` | Bounded Windows context and deterministic refinement, commit `90f7bdf` |
| 6 | `feature/transcript-quality-06-cleanup` | Local editor, protection and validation, raw recovery, settings, full benchmark stages, package checks, and release audit |

For each layer, compare its branch to the previous one. Layer 1 uses main as
its base. `feature/transcript-quality` contains the complete stack.

## Architecture

The coordinator captures the target and starts a quality session. The session
loads personal vocabulary and explicit corrections from SQLite, reads bounded
cursor context, and creates per-dictation recognition hints. The transcription
provider uses those hints. The session then refines the raw result, joins it with
cursor text, and returns both raw and final text with an ordered edit log.

`Shruti.TranscriptQuality` owns hint selection, correction matching, formatting,
optional AI edits, validation, and cursor joining. `DictationCoordinator` owns
the sequence, cancellation, timeout, fallback, and insertion. Windows UI
Automation remains in `Shruti.Platform.Windows`. Storage and WinUI own their
respective adapters.

`RecognitionHints` lives in `Shruti.Transcription.Abstractions` because
`TranscriptionSessionOptions` needs it. Putting it in Core would introduce a
circular project dependency. Prompts travel through the versioned native export
`shruti_whisper_transcribe_with_prompt`. The previous export remains available.
Hints are per invocation and do not enter the cached model-session key. OpenVINO
continues to run without consuming hints.

## Initial behavior

The current ASR default remains tiny.en. small.en and large-v3-turbo Q5 are
candidates, not new defaults. Light formatting and cursor context are enabled.
The local AI model starts disabled and requires an explicit download action.

Deterministic rules handle exact saved correction spelling, Unicode phrase
boundaries, longest-match priority, spoken punctuation, line and paragraph
commands, explicit consecutive numbered lists, simple numeric restatements,
basic time numbers, sentence casing, and cursor spacing. Exact vocabulary
spellings use their saved casing. This does not replace similar sounds or
different words. Formal standalone text
in Email or Document destinations can gain terminal punctuation. Casual
messaging does not gain punctuation solely from its app category. Existing ASR
punctuation is preserved. Complex number expressions and ambiguous lists are
left unchanged.

Off disables spoken formatting and AI cleanup. Explicit dictionary rules still
apply. Cursor joining has its own nearby-text preference.

Vocabulary hints do not create phonetic replacements. A `Claude` vocabulary
entry does not replace `cloud`. An explicit correction is a different entry.
The prompt prioritizes personal terms, nearby capitalized terms, a small static
process-name term map, and recent saved corrections. A UTF-8 byte budget
conservatively caps byte-based Whisper tokens without loading a tokenizer.

The Windows reader requires a single TextPattern selection or caret range. It
reads at most 512 characters on either side and 512 selected characters, verifies
the focused process/window/control, and never falls back to reading a complete
ValuePattern field. Recognized sensitive targets return no context. Browser
website classification and exhaustive banking detection are not implemented.

## Local AI

The initial optional model is Qwen3.5-2B Q4_K_M. The 1.31 GB GGUF is pinned to
revision `56c1090cf58b925e3bf23eb8731781e4d9c9029c` and SHA256
`92b61f139fbae199718c0c283df25cb619b1cc57e71faaae5a9a6b06a1842dda`.
The CPU runtime is llama.cpp build b11574 with a pinned archive checksum.
The app package includes the runtime and its license. The app downloads model
weights and their license, rather than downloading executable updates.

The editor runs a hidden subprocess on authenticated loopback, with four CPU
threads and reasoning disabled. It reuses the loaded model. It asks for exact,
unique substring edits under a JSON schema. It never sends data to a cloud
editor. Native logs are drained without storing them. Requests disable prompt
caching. No transcript or cursor text enters application diagnostics.

Validation rejects overlapping or ambiguous edits, excessive edits, changes to
protected names, dates, numbers, negation, URLs, file names, code, or quoted text,
and edits beyond the Light word-change budget. Rejected AI proposals keep the
deterministic result. A processing exception or the five-second refinement
deadline returns raw text and keeps dictation successful. Utterances longer than
1,800 characters currently use deterministic cleanup. These checks do not prove
that every accepted edit preserves meaning.

Only the latest dictation has recovery data. Use original changes the preview.
If text was already auto-inserted, it does not automatically undo that insertion.
Review dictionary correction opens a suggestion in the Dictionary control. It
requires confirmation before saving. Suggestions only cover one word spelling
change; broader corrections can be entered manually.

## Run the benchmark

```powershell
dotnet run --project tools/Shruti.QualityBench -- tests/quality/manifest.json --stage refinement --name deterministic
./scripts/prepare-local-cleanup.ps1
dotnet run --project tools/Shruti.QualityBench -- tests/quality/manifest.json --stage refinement --name local-ai --cleanup-model artifacts/local-cleanup/qwen3.5-2b-q4_k_m.gguf --cleanup-runtime artifacts/local-cleanup/Cpu/llama-server.exe
dotnet run --project tools/Shruti.QualityBench -- tests/quality/manifest.json --stage full --name audio-final --model <ASR-model.bin> --native-directory artifacts/whispercpp-native/Release --backend Cpu
```

Recognition-only uses `--stage recognition`. Add `--no-hints` to measure the
existing raw-recognition behavior without the new vocabulary/context prompt.
Add the two cleanup arguments to
the full stage to exercise audio through AI cleanup. Reports go to ignored
`artifacts/quality` as JSON and Markdown. They include model and native DLL hashes,
raw WER, term accuracy, formatting exact match, dictionary outcomes, false
positives and deletions, protected-token changes, fallback, preparation time,
result latency, and cleanup-process peak working set. WER currently treats
spoken and digit number forms as different words. Result latency excludes model
preparation, microphone capture, focus restoration, and insertion.

The manual `quality-bench.yml` workflow runs the public smoke fixtures. Normal
CI runs deterministic unit tests without downloading inference models. The
larger public corpus download requires FFmpeg and does not run in ordinary CI.

## Laptop evidence and release gates

Test machine: Core Ultra 9 285H, 32 GB RAM, Intel Arc 140T graphics. On the
original ten saved raw fixtures, CPU Qwen cleanup produced 10/10 exact matches,
with about 2.18 seconds average refinement and 2.17 GB peak process working set.
This is a tiny smoke sample, not a model-selection or meaning-preservation study.
CPU work ran alongside other validation, so latency needs an idle-machine rerun.

After adding list, date, selection, quiet, and noise fixtures, the packaged CPU
runtime also passed all 15 saved-transcript cases. tiny.en plus cleanup reached
60% final exact match and 14.7% raw WER on the 15 synthetic audio cases. The
model download weighs 1.31 GB and its observed peak working set was 2.17 GB.
The 15-case saved-transcript run averaged 1.61 seconds; audio through tiny.en and
cleanup averaged 2.39 seconds, excluding preparation and insertion.
The no-hints, recognition-only baseline on the same 15 synthetic clips gave
18.7% raw WER and 33.3% final exact match. The new pipeline gave 14.7% raw WER
and 60% final exact match. These comparisons only establish smoke behavior on
these authored clips. They do not establish general dictation accuracy.
The turbo Q5 CPU run averaged 18.3 seconds of recognition per short clip; its
GPU run averaged 1.73 seconds. This is another reason to retain a small fallback.

The ten synthetic audio cases gave tiny.en 14.3% raw WER. small.en and turbo Q5
gave 10.2%. Final exact match remained below parity even with cleanup. The Intel
GPU was used successfully for the candidate runs. This evidence does not justify
changing the default. Reports contain per-case results and hashes.

Before release, record held-out Indian English dictation and real noise/quiet
speech, review meaning changes, compare names and false-positive vocabulary,
test longer speech and complex restatements, and measure actual stop-to-insert
latency. Compare the same audio with a pinned Wispr Windows version. No Wispr
comparison or parity claim has been completed.

Also test the installed package in empty and selected fields, mid-sentence
carets, Notepad/Word, email, messaging, browsers, excluded secure/address fields,
unavailable UIA, cancellation, timeout, AI failure, original recovery, dictionary
save/delete, clean install, upgrade, uninstall, and offline operation. Package
content verification and native DLL loading are evidence about the package, not
proof of interactive installed-app behavior.

The development MSIX is under `artifacts/installer/output` and is unsigned. It
is not a public installer. The unpacked app used to build it is
`artifacts/installer/stage/Release/x64/VFS/ProgramFilesX64/Shruti/Shruti.App.WinUI.exe`.
For a local UI check, exit another running Shruti instance and start that app.
Its unpackaged data can share `%LOCALAPPDATA%\Shruti`, so preserve existing
preferences and dictionary entries. On Models, under Dictation cleanup, use Light
cleanup first, then download Qwen3.5-2B and choose Set active. The CLI benchmark weights
are under `artifacts/local-cleanup`; they are separate from app-managed downloads.

Local checks completed with 288 passing tests, formatting verification, a full
solution build with warnings treated as errors, native smoke tests, package
content verification, and a package-only native dependency/ABI probe. The native
DLL in the package matches the rebuilt DLL. The unsigned development MSIX SHA256
is `dc49923789b3d400f4de8dd1fead611669bb0bb4ea89565f278c9e4d71c6593d`.
The larger human corpus script parses successfully but has not been run here
because FFmpeg is unavailable. Interactive installed-app checks remain pending.
