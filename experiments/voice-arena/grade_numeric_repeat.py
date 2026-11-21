"""Reuse grades for identical texts on a fresh-case repeat."""
import random
from grade import QUALITY, read, write

first = QUALITY / "numeric-review"
folder = QUALITY / "numeric-review-repeat"
folder.mkdir(exist_ok=True)
cases = {row["id"]:row for row in read(first/"packet.json")}
grades = {(g["id"], cases[g["id"]]["candidates"][g["label"]]):g for g in read(first/"verdicts.json")}
packet = []
keys = {}
for row in read(QUALITY/"fresh-repeat-validator-after.json"):
    case_id = row["Id"]
    outputs = sorted({row["Previous"],row["Final"]})
    random.Random(case_id).shuffle(outputs)
    unseen = {chr(65+i):s for i,s in enumerate(outputs) if (case_id,s) not in grades}
    if unseen:
        packet.append(dict(cases[case_id], candidates=unseen))
    keys[case_id] = [row["Previous"],row["Final"]]
write(folder/"packet.json",packet)
print("Novel outputs:",sum(len(r["candidates"]) for r in packet))
if not packet or (folder/"verdicts.json").exists():
    candidates = {(c["id"],label):s for c in packet for label,s in c["candidates"].items()}
    fresh = read(folder/"verdicts.json") if packet else []
    assert len(fresh)==len(candidates) and candidates.keys()=={(v["id"],v["label"]) for v in fresh}
    for v in fresh:
        grades[v["id"],candidates[v["id"],v["label"]]]=v
    joined = {k:[grades[k,text] for text in texts] for k,texts in keys.items()}
    write(folder/"joined.json",joined)
    for i in (0,1):
        print(("before","after")[i],sum(r[i]["meaning"]=="preserved" and r[i]["ready"]=="yes" for r in joined.values()),"of",len(joined))
