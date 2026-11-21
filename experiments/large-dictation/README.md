# Shruti dictation evaluation set

Use primary-dev.json to change a prompt or select a model. Freeze that choice before measuring it on primary-eval.json. Both files are JSON arrays in Shruti.QualityBench format. primary.json contains their union. primary-sources.json maps every case ID to its upstream passage, source URL, category, domain and split.

The primary set contains 3,500 distinct English passages from the published test split of ductai199x/dictation-to-text. There are 500 development cases and 3,000 evaluation cases. The selection prioritizes everyday writing, messages, email, notes, prompts and chat. Of the 3,500 cases, 3,159 belong to everyday or chat domains. Each passage, normalized raw transcript and normalized reference occurs once. Normalization here means case-folding and collapsing whitespace. Related topics and near duplicates may remain.

ExpectedRaw is the recognizer's transcript. ExpectedFinal is the upstream intended writing. Vocabulary contains any supplied spelling hints. The tags identify the source, writing domain and dictation behavior. The selected Utter cases permit list formatting. Raw inputs can include amendments, fillers, spoken punctuation and self-corrections. The source generated the spoken renditions, synthesized audio, and used Parakeet for recognition. These are simulated dictations based on real and generated passages. The texts do not establish how any particular user would edit their own message.

A separate diagnostic file contains 2,000 unique Fluid evaluation documents. A blind reference check marked five of twenty sampled Fluid targets as clear mismatches against the speech shown and eight more as doubtful. In the Utter sample there were no clear mismatches and three doubtful references among twenty. The primary model comparison uses the Utter set. The original 151 human-speaker Voice Arena cases remain available for checking transfer to a different source.

[Utter dataset and provenance](https://huggingface.co/datasets/ductai199x/dictation-to-text/tree/f21f954c0092ad88b1195ce557f3ae3f69717fae) · [Fluid evaluation data](https://huggingface.co/datasets/johnbean393/fluid-2-sft-eval/tree/3b03c4029cf539a720890867c09ddb8a97b70c49)

## Reproduce

Run these in PowerShell from the experiment worktree with Python 3.14 and the .NET SDK. The data preparation installs DuckDB in the local artifacts directory and verifies both public source files by SHA-256.

```powershell
python -m pip install --target artifacts/large-dictation/python duckdb==2.0.0
python -B experiments/large-dictation/prepare.py
python -B experiments/large-dictation/primary.py
dotnet run --project tools/Shruti.QualityBench -- --export-formatter-inputs artifacts/large-dictation/primary-eval.json artifacts/large-dictation/eval-inputs.json
```

The offline comparison uses the local Qwen3.5-2B Q4_K_M, the Utter Mini Gemma 3 1B Q4_0 model, and the b11574 Windows x64 Vulkan build of llama.cpp. Qwen uses Shruti's copy-editor prompt with JSON output. Utter uses its published native dictation prompt and returns plain text. Both receive the same preprocessed dictation and preserve Shruti's validator. The exact prompt, executable hash, model hash, input hash and slot count are recorded next to each run. The GGUFs and llama-server are in the local artifacts directory on this machine. This batch comparison uses the Intel GPU, prompt caching and parallel requests with a 300-second transport timeout. It measures quality offline. CPU latency and the installed app's ten-second fallback need a separate check before adoption.

```powershell
.\experiments\large-dictation\run-comparison.ps1
python -B experiments/large-dictation/audit.py
```

The batch writes a checkpoint for each completed case and resumes against the same settings. The comparison script retries interrupted runs and writes full-results.md after both checkpoints pass verification. The scorer checks for missing or duplicate IDs, measures draft and final text separately, and replays each draft through Shruti's pipeline. A truncated or invalid response uses the raw transcript as fallback. The reported character error rate divides the total character edit distance by the total number of reference characters, including spaces and punctuation. Compare exact matches, reference distance and preservation of already-clean inputs separately. An exact mismatch can still be good writing. The first 256 cases in the frozen, hash-shuffled evaluation order form the separate blind ready-to-send audit.

[Utter model card](https://huggingface.co/ductai199x/utter/tree/5c5b6e0e9d3d1eeca81e5f19908d640b1dbb2212) · [Pinned runtime](https://github.com/ggml-org/llama.cpp/releases/tag/b11574)
