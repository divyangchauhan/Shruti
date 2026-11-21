"""Blindly grade changed public outputs and all fresh cases in the numeric replay."""
import json
import random
from pathlib import Path
from grade import QUALITY, read, write

experiment = Path(__file__).resolve().parent
destination = QUALITY / "numeric-review"
destination.mkdir(exist_ok=True)
packet = []
key = {}
splits = {}
for split in ["dev", "heldout", "fresh"]:
    manifest = experiment/"validator-fresh.json" if split == "fresh" else QUALITY/f"{split}.json"
    fixtures = {row["Id"]: row for row in read(manifest)}
    rows = read(QUALITY/f"{split}-validator-after.json")
    assert len(rows) == len(fixtures) and {row["Id"] for row in rows} == fixtures.keys()
    for row in rows:
        if split != "fresh" and row["Final"] == row["Previous"]:
            continue
        case_id = row["Id"]
        outputs = sorted({row["Previous"], row["Final"]})
        random.Random("numeric-validator/"+case_id).shuffle(outputs)
        labels = {text:chr(65+i) for i,text in enumerate(outputs)}
        packet.append(dict(id=case_id, spoken=row["Raw"], intended=fixtures[case_id]["ExpectedFinal"],
                           candidates={labels[text]:text for text in outputs}))
        key[case_id] = [labels[row["Previous"]], labels[row["Final"]]]
        splits[case_id] = split
write(destination/"packet.json",packet)
if (destination/"verdicts.json").exists():
    grades = read(destination/"verdicts.json")
    indexed = {(r["id"],r["label"]):r for r in grades}
    assert len(indexed)==len(grades) and indexed.keys()=={(r["id"],label) for r in packet for label in r["candidates"]}
    def ready(row):
        return row["meaning"]=="preserved" and row["ready"]=="yes"
    for case_id, labels in key.items():
        before,after=[indexed[case_id,label] for label in labels]
        print(splits[case_id],case_id,ready(before),ready(after),after["meaning"],after["reason"])
