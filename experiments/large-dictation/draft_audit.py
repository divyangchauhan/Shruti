"""Reuse a blind per-case grade only when the candidate text matches exactly."""
from pathlib import Path
import random
from score import OUT,read,write,indexed
from audit import MODELS
source=OUT/"audit-256"; dest=OUT/"draft-audit-256";dest.mkdir(exist_ok=True)
fixtures=read(OUT/"primary-eval.json")[:256]
runs={m:indexed(read(OUT/(m+"-audit-256-finals.json"))) for m in MODELS}
joined=read(source/"joined.json")
known={}
for i,grades in joined.items():
    for m,g in grades.items():known[i,runs[m][i]["Final"]]=g
packet=[];key={}
for row in fixtures:
    i=row["Id"]
    drafts={m:(runs[m][i]["Proposal"] if runs[m][i]["Proposal"] is not None else row["ExpectedRaw"]) for m in MODELS}
    choices=sorted(set(drafts.values()));random.Random(i+"/draft").shuffle(choices)
    labels={v:chr(65+j) for j,v in enumerate(choices)}
    key[i]=drafts
    new={labels[t]:t for t in choices if (i,t) not in known}
    if new:packet.append(dict(id=i,spoken=row["ExpectedRaw"],intended=row["ExpectedFinal"],candidates=new))
for start in range(0,len(packet),16):
    write(dest/f"packet-{start//16+1:02}.json",packet[start:start+16])
print("draft cases",len(packet),"distinct new outputs",sum(len(r["candidates"]) for r in packet))
files=list(dest.glob("grade-*.json"))
if files:
    texts={(r["id"],l):v for r in packet for l,v in r["candidates"].items()}
    grades=[g for f in files for g in read(f)]
    assert len(grades)==len(texts) and {(g["id"],g["label"]) for g in grades}==texts.keys()
    for g in grades:known[g["id"],texts[g["id"],g["label"]]]=g
    out={i:{m:known[i,v] for m,v in drafts.items()} for i,drafts in key.items()}
    write(dest/"joined.json",out)
    for m in MODELS:print(m,sum(g[m]["ready"]=="yes" and g[m]["meaning"]=="preserved" for g in out.values()),"ready")
