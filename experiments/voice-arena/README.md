# Reproduce the Voice Arena text comparison

Use the experiment worktree and the public spoken references. The local model receives the transcript, spelling hints and app category. Only the grader receives the intended text. This is the local Qwen cleanup path from the sentence-restart checkout. The source commit and hashes of its in-progress edits are in run-metadata.json.

The three prompts are incumbent.txt, sendable.txt and copy-editor.txt. The incumbent run uses the formatter's built-in prompt. The two other runs use QualityBench's --cleanup-prompt override. All three use the same validator. The benchmark records the text proposed by Qwen before validation and the text returned to the user. These diagnostic strings are recorded in the offline benchmark report.

From the worktree root run the following commands in PowerShell.

```powershell
python experiments/voice-arena/prepare_data.py
dotnet build tools/Shruti.QualityBench
./experiments/voice-arena/run.ps1 dev incumbent
./experiments/voice-arena/run.ps1 dev sendable
./experiments/voice-arena/run.ps1 dev copy-editor
python experiments/voice-arena/grade.py prepare dev sendable copy-editor
```

Give an independent model judge.md and the packet files under artifacts/quality/blind-dev-sendable-copy-editor. Put each JSON grade array beside its packet as grade-N.json. The script checks for a complete set of judgments before joining them back to the prompt names.

```powershell
python experiments/voice-arena/grade.py summarize dev sendable copy-editor
python -B experiments/voice-arena/summary.py dev sendable copy-editor
```

For the held-out comparison, use the same commands with heldout, the incumbent and the selected challenger. Freeze that choice before reading held-out results. The reports under artifacts/quality are local experiment data. Split membership is recorded by public case ID and speaker ID in split.json. Each reference in all.json was checked against the public Voice Arena data when joining the speakers.

[Source dataset](https://huggingface.co/datasets/VoiceArena/Dictation_Dataset/tree/ab671bb28d33defac2955fda2a588e75599a368b)
## Held-out grading

We graded the first anonymous output for every held-out case while the second local inference run was in progress. A byte-identical final output on the same case inherits the same judgment. Grade each remaining distinct candidate using the same rubric. For any apparent change in readiness or meaning between prompts, or an uncertain verdict, have a fresh reviewer see both anonymous candidates in shuffled order. Record any disagreement. Resolve concrete errors against the transcript and reference, and report genuinely ambiguous cases as uncertain.
