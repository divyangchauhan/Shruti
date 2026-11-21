# Why Shruti rejected exact Utter drafts

Two checks accounted for all 1,001 rejections of Utter drafts that exactly matched the reference. The table counts the first check to reject each edit. Passing that check could still expose a later one.

| First failing check | All nine categories | Amendments | Spoken commands |
|---|---:|---:|---:|
| A protected term's exact occurrence count changed | 796 | 174 | 122 |
| The sequence of numbers or negations changed | 205 | 38 | 6 |
| Total exact drafts rejected | 1,001 | 212 | 128 |

The term check compares literal text. The first mismatch was `p.m` on 125 cases and `a.m` on 52. Capitalized editing cues also counted as protected terms. In `utter-fa2760e305a5-amend`, the speaker said "Sorry, make that employee's father instead of employee's brother." Utter kept the sentence with "father" and removed the editing instruction. Shruti rejected the draft because it removed "Sorry."

I replayed all 2,917 changed Utter proposals with their original preprocessed inputs and spelling hints. An instrumented copy of the worktree validator recorded the first rejection and the term where applicable. Its accept or reject decision agreed with the saved pipeline outcome on every proposal. The trace describes the current checks. It does not establish which edits would pass after a rule change.

[Per-case trace](../../artifacts/large-dictation/rejection-trace/trace.json) · [Validator](../../src/Shruti.TranscriptQuality/TranscriptEditValidator.cs) · [Full comparison](full-results.md)
