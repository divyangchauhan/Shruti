# Numeric correction validator experiment

The revised validator accepted three additional edits in 151 saved Voice Arena dictations. A blind grader marked all three outputs ready to send and found that they preserved the intended meaning. This fixes an observable defect, but it explains little of the overall formatting gap.

| Input set | Prior validator | Revised validator | Inputs |
| --- | ---: | ---: | ---: |
| Voice Arena development | 17 ready | 19 ready | 35 |
| Voice Arena previously evaluated speakers | 69 ready | 70 ready | 116 |
| Fresh targeted dictations, first run | 4 ready | 5 ready | 10 |
| Same fresh dictations, repeat run | 4 ready | 6 ready | 10 |

**What changed.** The rule looks for an explicitly corrected quantity such as “to twenty, no, twenty five.” It accepts a proposal that replaces that span with the final quantity while retaining the surrounding text exactly. It recognizes small spoken English cardinals, their digit spellings and spoken decimal digits, including significant zeros. The earlier case where an arbitrary suffix could drop the starting pool size is covered by a regression test. Explicit vocabulary, quoted text and identifiers still undergo their protection checks. This experiment covers a limited grammar of number introductions and correction phrases.

**What reached the user.** Two development messages gained a usable correction, one for the Prisma pool target and another for software build 4.3. In the 116-case evaluation split an inspection message kept the final Friday time of ten thirty and removed the abandoned ten o'clock. In the fresh repeat, one message kept room 23 and another kept build 7.01. All four fresh messages containing quantities that needed to coexist remained sendable.

**Limits.** The previously evaluated public speakers form a regression comparison here. The fresh cases were written specifically for this experiment and consist of six corrections and four preservation checks. The first live run hit the ten-second timeout eight times. The repeat, run without simultaneous builds or tests, returned all ten proposals. These are two runs of the same inputs. The comparison within each run holds the proposed text fixed. The one-message difference between fresh runs cannot establish a general gain or a stable timeout rate.

**Remaining failures.** After the change, 46 of the 116 public outputs still needed editing. Qwen proposed the input unchanged in 20 of those, the validator rejected proposals in 13, nine timed out, and four accepted edits still needed work. One fresh example shows the rule's deliberate limit. Qwen applied the corrected alert threshold but also rewrote a separate “5 minutes” as “five minutes.” Because that second edit falls outside the correction span, validation kept the original. The next comparison should test the local model on the 20 inputs it left alone and examine whether a different formatter makes a usable proposal under the same deadline.

**Verification.** The original validator reproduced every saved output before changes. The replay verifies the manifest hash, case IDs and input to the editor, substitutes the saved proposal at the normal pipeline entry point, and carries forward actual raw fallbacks when no proposal completed. All 359 unit tests passed, including 21 targeted checks. Work stayed local to the experiment worktree.

To reproduce a public split from the worktree root, run:

```powershell
dotnet run --project tools/Shruti.QualityBench -- --replay-proposals artifacts/quality/heldout.json artifacts/quality/heldout-copy-editor.json artifacts/quality/heldout-validator-after.json
```

[Correction implementation](../../src/Shruti.TranscriptQuality/ExplicitNumericCorrection.cs) · [Focused tests](../../tests/Shruti.Tests/NumericCorrectionTests.cs) · [Changed public cases and first fresh batch](../../artifacts/quality/numeric-review/verdicts.json) · [Fresh repeat grades before and after](../../artifacts/quality/numeric-review-repeat/joined.json)
