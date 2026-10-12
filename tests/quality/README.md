# Quality corpus

The checked-in text cases cover corrections, protected tokens, formatting, and cursor joining. They do not prove recognition accuracy or Indian English support. `expectedRaw` is the reference spoken text. `savedRaw` can hold actual ASR output for independent refinement tests.

Public audio smoke fixtures are generated from authored sentences with Windows speech synthesis by `scripts/prepare-quality-audio.ps1`. Synthetic voices are not a substitute for held-out human recordings. Keep private recordings outside Git. A downloaded corpus must have an immutable revision, license, and checksums in its manifest.

Run `dotnet run --project tools/Shruti.QualityBench -- tests/quality/manifest.json`. Reports contain fixture text and belong under ignored `artifacts/quality`. Never run this tool with private recordings in public CI. Model-heavy runs are manual.
