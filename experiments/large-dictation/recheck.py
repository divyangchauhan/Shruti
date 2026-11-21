"""Blind recheck of final-text grading disagreements or uncertainty."""
from score import OUT,read,write
from audit import MODELS
import random
base=OUT/"audit-256"; folder=OUT/"final-review";folder.mkdir(exist_ok=True)
grades=read(base/"joined.json")
cases={r["id"]:r for p in sorted(base.glob("packet-*.json")) for r in read(p)}
review=[];keys={}
for i,models in grades.items():
    flags={(g["meaning"],g["ready"]) for g in models.values()}
    if len(flags)==1 and not any("uncertain" in pair for pair in flags):continue
    row=cases[i];old=list(row["candidates"])
    random.Random("recheck/"+i).shuffle(old)
    new={k:"choice-"+str(n+1) for n,k in enumerate(old)}
    keys[i]={m:new[g["label"]] for m,g in models.items()}
    review.append(dict(row,candidates={new[k]:row["candidates"][k] for k in old}))
for start in range(0,len(review),22):
    write(folder/f"packet-{start//22+1}.json",review[start:start+22])
print(len(review),"cases")
if len(list(folder.glob("grade-*.json")))==3:
    second={(r["id"],r["label"]):r for p in folder.glob("grade-*.json") for r in read(p)}
    assert second.keys()=={(r["id"],l) for r in review for l in r["candidates"]}
    def ready(g):return g["meaning"]=="preserved" and g["ready"]=="yes"
    for m in MODELS:
        agreed=disputed=second_ready=0
        for i,model_grades in grades.items():
            a=model_grades[m];b=second[i,keys[i][m]] if i in keys else a
            agreed+=ready(a) and ready(b);disputed+=ready(a)!=ready(b);second_ready+=ready(b)
        print(m,"ready in original and recheck:",agreed,"ready in one only:",disputed,"using recheck:",second_ready)
