# Full offline dictation results

After validation, Utter matched the reference on 241 of 3,000 cases and Qwen on 76. Their corpus character error rates were 21.56% and 23.24%, respectively.

## Overall scores

Exact match counts whole transcripts. Corpus character error rate, or CER, divides the total character edit distance by the total number of reference characters. Both metrics include case, spaces and punctuation. Scoring converts CRLF to LF. Clean inputs already match the reference before formatting.

| Output | Exact | Exact rate | Character edits | Reference characters | Corpus CER | Clean inputs kept exact |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Rules | 39 | 1.30% | 144,945 | 604,658 | 23.97% | 35 / 35 |
| Utter draft | 1,234 | 41.13% | 16,775 | 604,658 | 2.77% | 20 / 35 |
| Utter final | 241 | 8.03% | 130,355 | 604,658 | 21.56% | 27 / 35 |
| Qwen draft | 83 | 2.77% | 131,971 | 604,658 | 21.83% | 33 / 35 |
| Qwen final | 76 | 2.53% | 140,518 | 604,658 | 23.24% | 33 / 35 |

## Generation and validator replay

Each checkpoint, proposal file and final file contained the same 3,000 unique IDs as the evaluation manifest and exported requests. Each replay input matched its checkpoint proposal and exported transcript. Each generation fallback produced the original raw transcript.

| Model | Finish status and count | Generation fallbacks to raw | Finals different from draft | Exact drafts that lost exact match | Inexact drafts that became exact |
| --- | --- | ---: | ---: | ---: | ---: |
| Utter | `length` 1, `stop` 2,999 | 1 | 2,189 | 1,001 | 8 |
| Qwen | `stop` 3,000 | 0 | 663 | 8 | 1 |

Of the 1,234 exact Utter drafts, 1,001 ceased to match the reference after replay through Shruti. Replay also brought 8 other drafts to an exact match. The replay records the final text and the generation fallback flag. It does not identify which validator rule caused a change.

## By dictation category

Each model column gives the exact count and corpus CER within that category. The last column counts Utter drafts that matched the reference before validation and differed afterward.

| Category | Cases | Utter draft | Utter final | Qwen draft | Qwen final | Utter exact matches lost |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| amend | 392 | 234 / 1.84% | 22 / 35.33% | 0 / 36.70% | 0 / 38.52% | 212 |
| command | 346 | 144 / 1.84% | 16 / 44.76% | 0 / 50.89% | 0 / 51.40% | 128 |
| correction | 344 | 139 / 2.22% | 14 / 23.97% | 2 / 22.99% | 1 / 25.61% | 125 |
| disfluent | 340 | 114 / 4.21% | 22 / 17.35% | 3 / 15.10% | 2 / 17.31% | 92 |
| explicit | 341 | 149 / 2.43% | 48 / 7.84% | 29 / 8.03% | 26 / 8.48% | 104 |
| grammar | 562 | 128 / 4.56% | 56 / 5.95% | 23 / 5.96% | 23 / 6.03% | 74 |
| list | 116 | 83 / 1.56% | 26 / 13.55% | 0 / 17.39% | 0 / 17.42% | 57 |
| natural | 340 | 146 / 2.27% | 34 / 3.60% | 26 / 3.36% | 24 / 3.47% | 115 |
| restate | 219 | 97 / 2.52% | 3 / 48.13% | 0 / 43.81% | 0 / 49.29% | 94 |

## Method

The held-out set contains 3,000 synthetic English dictations from the Utter dataset. Both models received the same preprocessed transcript and available spelling hints. Utter Mini Gemma 3 1B Q4_0 used its native plain-text dictation prompt with 64 slots. Qwen3.5-2B Q4_K_M used Shruti's copy-editor prompt with JSON output and 16 slots. Qwen retained the first 256 cases from the pilot and resumed again after a standby interruption at 1,067 completed cases. Each case contributes once.

This was a GPU-throughput test on the Intel GPU using the local llama.cpp b11574 Vulkan runtime, temperature zero, seed 42, 2,048 context tokens per slot and prompt caching. Installed CPU latency and the app's ten-second timeout require a separate measurement.

The scorer used each saved proposal as the draft and replayed it through Shruti's transcript pipeline to obtain the final. For failed generations it scored the raw transcript at both stages. The rules row scores the exported rules-only output. Exact match measures agreement with one supplied reference. Other valid edits can differ. [Runner](batch.py) · [Scorer](score.py) · [Reproduction steps](README.md)

## Per-case files

Before opening the PR, we replayed both sets of 3,000 saved drafts after adding a guard against treating a complete preceding sentence as a false start. Three Utter outputs and one Qwen output changed. The delivered exact counts stayed at 241 and 76. The tables above describe the original frozen run.

- Utter. [Generation checkpoint](../../artifacts/large-dictation/eval-inputs-utter-gpu-64.jsonl) · [Replay inputs](../../artifacts/large-dictation/eval-inputs-utter-gpu-64-proposals.json) · [Validator outputs](../../artifacts/large-dictation/eval-inputs-utter-gpu-64-finals.json) · [Run settings and hashes](../../artifacts/large-dictation/eval-inputs-utter-gpu-64.meta.json)
- Qwen. [Generation checkpoint](../../artifacts/large-dictation/eval-inputs-qwen-gpu-16.jsonl) · [Replay inputs](../../artifacts/large-dictation/eval-inputs-qwen-gpu-16-proposals.json) · [Validator outputs](../../artifacts/large-dictation/eval-inputs-qwen-gpu-16-finals.json) · [Run settings and hashes](../../artifacts/large-dictation/eval-inputs-qwen-gpu-16.meta.json)
- [Evaluation references](../../artifacts/large-dictation/primary-eval.json) · [Exported formatter requests](../../artifacts/large-dictation/eval-inputs.json) · [Full-precision scores](../../artifacts/large-dictation/primary-scores.json)
