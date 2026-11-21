# Voice Arena formatter results

Keep the existing app default. The copy-editor prompt is the better experimental choice for a Wispr Flow-style edit. It preserved one explanation the current prompt dropped and wrote one benefits URL correctly. Its advantage was small, and it still left 47 of 116 held-out messages needing edits.

## Held-out results

| Measure | Current prompt | Copy-editor prompt |
| --- | ---: | ---: |
| Ready to send, meaning preserved | 66 of 116 | 69 of 116 |
| Judged to change the intended meaning | 2 | 1 |
| Exact match with the supplied reference | 23 | 25 |
| Median time for refinement on this CPU | 4.48 s | 3.62 s |
| Ten-second cancellations with raw fallback | 9 | 9 |
| Model proposals rejected by validation | 33 | 15 |

The two prompts produced identical final text in 88 cases. The copy-editor prompt won five cases for readiness and lost two. One win was a second recording with the same marathon transcript. Counting each distinct spoken transcript once leaves four wins and two losses. A second blind reviewer agreed with all 14 verdicts on the seven cases with different grades. The public text pairs contain 112 distinct held-out raw transcripts. No held-out raw transcript or reference matched a development example after case and whitespace normalization.

For dict_0572 the current prompt omitted the speaker's explanation that they were running late. The copy-editor output kept both the explanation and the request to start eating. On dict_0729 it wrote the enrollment portal as a usable domain. On dict_0279 it left a filler in a short request about Jest, and on dict_0407 it left a duplicated phrase in a stock check. Both configurations missed the change of intended visitor in dict_0467.

## What limited the rewrite

On development case dict_0308, the speaker asked to raise a Prisma connection pool from ten to twenty, then corrected the target to twenty-five. The copy-editor proposal used the original starting value and the corrected target. The validator rejected the edit and Shruti returned the spoken correction. In a separate blinded grading pass, five of the six rejected copy-editor proposals on development data preserved meaning and were ready to send. All six final messages still needed cleanup. In the sixth proposal the earlier and corrected commit counts both remained.

The current validator compares number and negation tokens in order, and protects occurrences of inferred names. A formatting change or a genuinely superseded number can fail that check. Its protection also matters: the other experimental prompt tried to compose an out-of-office email when the speaker had dictated an instruction to write one. For the next experiment, test narrowly scoped validation of explicit numeric corrections with paired cases that change a surviving amount. Leave the held-out results from this run as published evidence and use fresh cases for the next adoption decision.

## Method and limits

The benchmark used the current working formatter from the sentence-restart worktree, copied into experiment/voice-arena-formatter. The comparison covered all 151 public cases. Speaker ID determined the split of 35 development messages from 17 speakers and 116 held-out messages from 54 other speakers. All three prompts scored 17 of 35 ready on development. The shorter sendable prompt introduced two new meaning errors. The copy-editor prompt introduced none, so it advanced. Selection was recorded before inspecting held-out results.

The same local Qwen3.5-2B Q4_K_M model ran every configuration at temperature zero through Shruti's normal deterministic cleanup, model and validator. The grader was a separate GPT-6.2 Astra agent. Graders saw spoken text, intended text and anonymous final outputs. Equivalent wording was acceptable. Every input received a verdict including timeouts. Grades measure this review rubric on public transcripts. The evaluation used text references with punctuation and case already present. It did not exercise Whisper recognition, the installed app or Wispr Flow itself.

[Reproduction instructions](README.md) · [Experiment metadata](run-metadata.json) · [Copy-editor prompt](copy-editor.txt) · [Development grades](../../artifacts/quality/blind-dev-sendable-copy-editor/joined.json) · [Held-out grades by case](../../artifacts/quality/blind-heldout-copy-editor/joined.json) · [Independent recheck](../../artifacts/quality/blind-heldout-copy-editor/review-verdicts.json) · [Rejected proposal review](../../artifacts/quality/dev-diagnostic-grades.json)
