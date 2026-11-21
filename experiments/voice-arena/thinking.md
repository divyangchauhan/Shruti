# Thinking budget comparison

The first prompt experiment disabled thinking for every prompt. This follow-up compares the same copy-editor prompt with reasoning budgets of 0, 32 and 128 tokens on all 35 development cases. The zero-budget baseline is the saved run from the prompt comparison. Positive budgets enable thinking in the chat template and set the matching llama-server token budget. These are token caps, not named model effort levels.

Keep Qwen3.5-2B Q4_K_M, temperature zero, four CPU threads, JSON response schema, maximum completion length, validator and ten-second deadline fixed. Record whether a completed response contains a nonempty reasoning field. Retain only that boolean. A canceled response has no completed proposal to inspect.

Reuse blind grades when the same case has exactly the same final text. Independently grade previously unseen text. Count all timed-out cases at their actual final output. Advance a budget beyond development only if it produces more sendable messages than thinking off without a newly changed meaning. Check timeout count before recommending it for interactive use.
## Results

| Reasoning token budget | Ready to send | Median cleanup | Timeouts | Completed responses with a nonempty reasoning field |
| --- | ---: | ---: | ---: | --- |
| 0 | 17 of 35 | 3.67 s | 1 | Thinking disabled in baseline |
| 32 | 16 of 35 | 4.56 s | 1 | 34 of 34 |
| 128 | 16 of 35 | 8.32 s | 6 | 29 of 29 |

Neither thinking budget made an additional final message ready to send. Both lost the same case, dict_0644. The judges found no new meaning error relative to the zero-budget copy-editor prompt. Both budgets fell short of the development threshold, so neither advanced to held-out speakers. Keep thinking off for this local Qwen configuration. This compares model settings on saved transcripts under a ten-second CPU deadline.

Run `./experiments/voice-arena/run.ps1 dev copy-editor -ReasoningBudget 32` or substitute 128. The runner writes a separately named report for each positive budget. The first grading batch reused the earlier blind grades for identical case-output pairs. Independent judges graded three new pairs from the 32-token run and two more from the 128-token run. All 338 unit tests passed.

[Case verdicts](../../artifacts/quality/thinking-dev-review/joined.json) · [32-token run](../../artifacts/quality/dev-copy-editor-thinking-32.json) · [128-token run](../../artifacts/quality/dev-copy-editor-thinking-128.json)
