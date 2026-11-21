# Blind review of 256 dictations

Utter Mini produced a message ready to send on 176 of 256 dictations. Shruti's current validation and formatting steps delivered a ready message on 47 of those 256 inputs. The validator rejected 142 of Utter's ready drafts. It also rejected 90 drafts that exactly matched the intended text. Some fallback outputs were also ready to send, which is why the final count is higher than 34.

| Result, out of 256 | Qwen3.5-2B | Utter Mini 1B |
|---|---:|---:|
| Ready to send, model draft | 41 | 176 |
| Ready to send, after Shruti processes the draft | 33 | 47 |
| Exact reference match, model draft | 5 | 104 |
| Exact reference match, after Shruti processes the draft | 4 | 14 |

The audit used the first 256 entries in the frozen, hash-shuffled evaluation order. AI reviewers saw the dictation and candidate text under shuffled labels. They judged whether the text preserved the intended meaning and whether someone could send it as written. A pass required both judgments. Uncertain cases stayed in the denominator. Exact match counts are separate and require the same wording and formatting as the reference.

A fresh blind review covered the 66 cases where the original final-output grades differed between models or included an uncertain judgment. Substituting the second grades for those cases gave Qwen 35 ready messages and Utter 47. The raw Utter drafts still included 45 cases graded as changing meaning and six where meaning was uncertain. Removing the validator wholesale would expose some of those mistakes.

The model and prompt changed together. Qwen used Shruti's copy-editor prompt and JSON output. Utter used its native dictation instructions and plain-text output. Thinking was off and temperature was zero for both. This measures the two configurations as a user would use them. It does not isolate how much each prompt contributed.

The source generated the spoken renditions synthetically. Utter Mini was trained on the same project's training corpus, and this benchmark draws distinct passages from its test split. An independent human-dictation check and a CPU latency check are still needed before switching the app default.

[Dataset and reproduction commands](README.md) · [Draft judgments](../../artifacts/large-dictation/draft-audit-256/joined.json) · [Delivered-text judgments](../../artifacts/large-dictation/audit-256/joined.json) · [Second review](../../artifacts/large-dictation/final-review/)
