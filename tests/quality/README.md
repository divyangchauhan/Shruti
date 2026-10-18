# Transcript quality fixtures

`manifest.json` is an array of `QualityCase` records. Each case has an ID,
expected raw and final text, and optional audio, cursor context, vocabulary,
explicit corrections, category tags, and protected or forbidden tokens.
`savedRaw` overrides the input to the refinement-only stage. Audio is mono
PCM16 WAV at 16 kHz.

The 15 checked-in WAV files contain Windows synthesized speech made from our
authored sentences. They contain no private recordings. `audioSource` identifies
them as smoke fixtures. Quiet speech uses one tenth of the original amplitude.
The noisy case adds seeded white noise with an amplitude of 1,000 PCM units.
These are synthetic conditions, not recordings of real speakers or rooms.

Regenerate with `scripts/prepare-quality-audio.ps1`. The installed Windows voice
affects regenerated audio. Keep the checked-in WAV files fixed for comparisons.

For a larger human recognition baseline, run `scripts/prepare-quality-corpus.ps1`
with an installed FFmpeg executable. It downloads the versioned LibriSpeech
test-clean archive, verifies OpenSLR's published checksum, converts the first
requested utterances in sorted speaker/chapter order, and writes attribution and
a manifest under ignored `artifacts/quality-corpus`. Use the recognition stage.
The unformatted reference is not a dictation formatting target.

Record Indian English, names, homophones, quiet and noisy speech, restatements,
and real app insertion separately before making a release decision. Keep private
recordings and reports outside Git. Use a held-out set that has not been used to
tune rules or model prompts. Automated protected-token checks require a human
meaning review alongside them.
