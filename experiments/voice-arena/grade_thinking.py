import argparse
import random
import statistics
from grade import QUALITY, index, read, write


base = "copy-editor"
old_names = ["incumbent","sendable",base]
parser=argparse.ArgumentParser()
parser.add_argument("--first", action="store_true", help="Grade unseen outputs from the 32-token run first")
first=parser.parse_args().first
names = [base,base+"-thinking-32"]
if not first:
    names.append(base+"-thinking-128")
dest = QUALITY / ("thinking-dev-first" if first else "thinking-dev-review")
dest.mkdir(exist_ok=True)
fixtures = index(read(QUALITY/"dev.json"))
reports = {name:read(QUALITY/("dev-"+name+".json")) for name in set(old_names+names)}
runs = {name:index(report["Cases"]) for name,report in reports.items()}
for name in names:
    for field in ["ManifestSha256","CleanupModelSha256","PromptFileSha256"]:
        assert reports[name][field] == reports[base][field], (name,field)
    cases = list(runs[name].values())
    print(name, "timeouts",sum(r["Failure"]=="TaskCanceledException" for r in cases),
          "fallbacks",sum(r["UsedRawFallback"] for r in cases),
          "median ms",round(statistics.median(r["StopToResultMs"] for r in cases)),
          "nonempty reasoning",sum(r.get("ReasoningReturned") is True for r in cases),
          "parsed responses",sum(r["Proposal"] is not None for r in cases))
assert all(r.keys()==fixtures.keys() for r in runs.values())
old_grades = read(QUALITY/"blind-dev-sendable-copy-editor/joined.json")
known = {}
for row in old_grades:
    for variant in old_names:
        known[row["id"],runs[variant][row["id"]]["Final"]] = row[variant]
first_dir=QUALITY/"thinking-dev-first"
if not first and (first_dir/"verdicts.json").exists():
    first_cases={r["id"]:r for r in read(first_dir/"packet.json")}
    for r in read(first_dir/"verdicts.json"):
        known[r["id"],first_cases[r["id"]]["candidates"][r["label"]]]=r
novel = []
labels = {}
for case_id, fixture in fixtures.items():
    outputs=sorted({runs[name][case_id]["Final"] for name in names})
    random.Random("budget/"+case_id).shuffle(outputs)
    missing={}
    for i, text in enumerate(outputs):
        label=chr(65+i)
        labels[case_id,text]=label
        if (case_id,text) not in known:
            missing[label]=text
    if missing:
        novel.append(dict(id=case_id,spoken=fixture["ExpectedRaw"],intended=fixture["ExpectedFinal"],candidates=missing))
write(dest/"packet.json",novel)
print("Previously unseen case-output pairs:",sum(len(x["candidates"]) for x in novel))
if (dest/"verdicts.json").exists() or not novel:
    fresh={(r["id"],r["label"]):r for r in (read(dest/"verdicts.json") if novel else [])}
    assert fresh.keys()=={(r["id"],label) for r in novel for label in r["candidates"]}
    result=[]
    for case_id in fixtures:
        row={"id":case_id}
        for name in names:
            text=runs[name][case_id]["Final"]
            row[name]=known.get((case_id,text)) or fresh[case_id,labels[case_id,text]]
        result.append(row)
    write(dest/"joined.json",result)
    for name in names:
        ready=lambda r,n:r[n]["meaning"]=="preserved" and r[n]["ready"]=="yes"
        print(name,"ready",sum(ready(r,name) for r in result),"wins",[r["id"] for r in result if ready(r,name) and not ready(r,base)],"losses",[r["id"] for r in result if ready(r,base) and not ready(r,name)],"new meaning errors",[r["id"] for r in result if r[name]["meaning"]=="changed" and r[base]["meaning"]!="changed"])
