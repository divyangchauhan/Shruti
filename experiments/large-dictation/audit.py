"""Blind, paired ready-to-send audit on the first 256 IDs of the frozen shuffled evaluation."""
import hashlib
import json
import random
from score import OUT, indexed, read, replay, write

MODELS=["eval-inputs-qwen-gpu-16","eval-inputs-utter-gpu-64"]
def main():
    fixtures=read(OUT/"primary-eval.json")[:256]
    runs={m:replay(m,256)[0] for m in MODELS}
    folder=OUT/"audit-256";folder.mkdir(exist_ok=True)
    packet=[]; keys={}
    for row in fixtures:
        case_id=row["Id"];outputs={m:runs[m][case_id]["Final"] for m in MODELS}
        values=sorted(set(outputs.values()));random.Random("shruti-audit-v2/"+case_id).shuffle(values)
        labels={v:chr(65+i) for i,v in enumerate(values)}
        packet.append(dict(id=case_id,spoken=row["ExpectedRaw"],intended=row["ExpectedFinal"],candidates={labels[v]:v for v in values}))
        keys[case_id]={m:labels[v] for m,v in outputs.items()}
    for start in range(0,len(packet),16):
        write(folder/f"packet-{start//16+1:02}.json",packet[start:start+16])
    files=list(folder.glob("grade-*.json"))
    if not files:return
    grades={}
    for f in files:
        for g in read(f):
            ix=(g["id"],g["label"])
            if ix in grades or g["meaning"] not in {"changed","preserved","uncertain"} or g["ready"] not in {"yes","no","uncertain"}:raise ValueError(ix)
            grades[ix]=g
    assert grades.keys()=={(r["id"],label) for r in packet for label in r["candidates"]}
    joined={i:{m:grades[i,labels[m]] for m in MODELS} for i,labels in keys.items()}
    write(folder/"joined.json",joined)
    for model in MODELS:
        print(model,"ready",sum(g[model]["ready"]=="yes" and g[model]["meaning"]=="preserved" for g in joined.values()),
              "changed",sum(g[model]["meaning"]=="changed" for g in joined.values()),"of",len(joined))
if __name__=="__main__":main()
