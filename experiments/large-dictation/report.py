"""Score both completed checkpoints and write the full comparison."""
import json
import subprocess
import sys
from collections import Counter
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "artifacts/large-dictation"
MODELS = {
    "Utter Mini 1B": "eval-inputs-utter-gpu-64",
    "Qwen3.5-2B": "eval-inputs-qwen-gpu-16",
}


def load(filename):
    return json.loads((OUT / filename).read_bytes())


def main():
    subprocess.run(
        [sys.executable, "-B", str(Path(__file__).with_name("score.py")), *MODELS.values()],
        cwd=ROOT,
        check=True,
    )
    scores = {r["model"]: r for r in load("primary-scores.json") if r["source"] == "combined"}
    refs = {r["Id"]: r["ExpectedFinal"] for r in load("primary-eval.json")}
    rows = ["| Configuration | Exact matches | Character error rate | Already-clean inputs kept exact |",
            "|---|---:|---:|---:|"]
    coverage = []
    configs = {"Rules alone": "rules"}
    for model, run in MODELS.items():
        configs[f"{model}, draft"] = run + " draft"
        configs[f"{model}, delivered"] = run + " final"
        raw = [json.loads(line) for line in (OUT / (run + ".jsonl")).read_bytes().splitlines()]
        if len(raw) != len(refs) or {r["Id"] for r in raw} != refs.keys():
            raise ValueError(f"Checkpoint ID mismatch for {model}")
        statuses = Counter(r["status"] for r in raw)
        finals = load(run + "-finals.json")
        rejected_exact = sum(
            r["AcceptedProposal"] is False and r["Proposal"] == refs[r["Id"]]
            for r in finals
        )
        status_text = ", ".join(f"{count:,} `{status}`" for status, count in sorted(statuses.items()))
        coverage.append(f"- {model} recorded {status_text}. Shruti rejected {rejected_exact:,} exact model proposals.")
    for label, key in configs.items():
        row = scores[key]
        rows.append(f"| {label} | {row['exact']:,}/{row['n']:,} | {row['cer']:.2%} | {row['clean_kept']}/{row['clean']} |")
    report = [
        "# Full dictation benchmark results",
        f"Both local models processed the same {len(refs):,} held-out English dictations. Each checkpoint contains every evaluation ID exactly once.",
        "\n".join(rows),
        "The character error rate is the total number of single-character edits divided by the total number of reference characters. A lower rate means the output is closer to the reference. Other wording can still preserve the speaker's meaning.",
        "\n".join(coverage),
        "Draft scores use the raw transcript as fallback when generation fails. Delivered scores replay Shruti's validator and finishing steps. This offline GPU run measures text quality. It does not measure the app's CPU response time.",
        "The source used synthesized speech. Utter Mini trained on the source project's training split. See the blind review for a separate judgment of meaning and readiness to send on 256 of these cases.",
        "[Dataset and settings](../../experiments/large-dictation/README.md) · [Blind review](../../experiments/large-dictation/blind-results.md) · [Scores by dictation category](primary-scores.json)",
    ]
    dest = OUT / "full-results.md"
    tmp = dest.with_suffix(".md.tmp")
    tmp.write_text("\n\n".join(report) + "\n", encoding="utf-8")
    tmp.replace(dest)
    print(dest)


if __name__ == "__main__":
    main()
