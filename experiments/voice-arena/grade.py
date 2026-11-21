"""Build blinded grading packets and join completed judgments to both runs."""
import argparse
import hashlib
import json
import random
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
QUALITY = ROOT / "artifacts/quality"


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def write(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding="utf-8")


def index(rows):
    by_id = {row["Id"]: row for row in rows}
    if len(by_id) != len(rows):
        raise ValueError("Duplicate case IDs")
    return by_id


def grade_packets(split, variants):
    references = index(read(QUALITY / f"{split}.json"))
    runs = {}
    digest = hashlib.sha256((QUALITY / f"{split}.json").read_bytes()).hexdigest()
    for name in variants:
        report = read(QUALITY / f"{split}-{name}.json")
        if report["ManifestSha256"].lower() != digest:
            raise ValueError(f"Manifest mismatch in {name}")
        runs[name] = index(report["Cases"])
        if runs[name].keys() != references.keys():
            raise ValueError(f"Case IDs differ in {name}")
    cases = []
    key = {}
    for case_id, fixture in references.items():
        outputs = {name: results[case_id]["Final"] for name, results in runs.items()}
        if any(not isinstance(value, str) for value in outputs.values()):
            raise ValueError(f"Missing output for {case_id}")
        choices = sorted(set(outputs.values()))
        random.Random(split + case_id).shuffle(choices)
        labels = {text: chr(65 + i) for i, text in enumerate(choices)}
        key[case_id] = {name: labels[text] for name, text in outputs.items()}
        cases.append({"id": case_id, "spoken": fixture["ExpectedRaw"], "intended": fixture["ExpectedFinal"],
                      "candidates": {labels[text]: text for text in choices}})
    return cases, key


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=["prepare", "summarize"])
    parser.add_argument("split", choices=["dev", "heldout"])
    parser.add_argument("challengers", nargs="+")
    args = parser.parse_args()
    variants = ["incumbent", *args.challengers]
    cases, key = grade_packets(args.split, variants)
    dest = QUALITY / f"blind-{args.split}-{'-'.join(args.challengers)}"
    dest.mkdir(exist_ok=True)
    size = 18
    if args.action == "prepare":
        for start in range(0, len(cases), size):
            write(dest / f"packet-{start // size + 1}.json", cases[start:start + size])
        return
    grades = {}
    for path in sorted(dest.glob("grade-*.json")):
        for row in read(path):
            k = (row["id"], row["label"])
            if k in grades or row["meaning"] not in {"preserved", "changed", "uncertain"} or row["ready"] not in {"yes", "no", "uncertain"} or not row["reason"].strip():
                raise ValueError(f"Invalid or duplicate grade: {k}")
            grades[k] = row
    if grades.keys() != {(case["id"], label) for case in cases for label in case["candidates"]}:
        raise ValueError("Incomplete or unexpected grades")
    results = []
    counts = {name: Counter() for name in variants}
    for case in cases:
        row = {"id": case["id"]}
        for name in variants:
            grade = grades[case["id"], key[case["id"]][name]]
            row[name] = grade
            counts[name][grade["meaning"]] += 1
            counts[name]["sendable"] += grade["meaning"] == "preserved" and grade["ready"] == "yes"
        results.append(row)
    write(dest / "joined.json", results)
    print(json.dumps(counts, indent=2))


if __name__ == "__main__":
    main()
