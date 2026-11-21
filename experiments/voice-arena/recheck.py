"""Prepare a fresh blind review of changed verdicts and uncertainty."""
from grade import QUALITY, read, write, grade_packets

folder=QUALITY/"blind-heldout-copy-editor"
cases,keys=grade_packets("heldout",["incumbent","copy-editor"])
first={r["id"]:r for r in read(folder/"joined.json")}
review=[]
mapping={}
for case in cases:
    case_id=case["id"]
    grades=[first[case_id][name] for name in ["incumbent","copy-editor"]]
    flags={(g["meaning"],g["ready"]) for g in grades}
    if len(flags)==1 and not any("uncertain" in flag for flag in flags):
        continue
    order=list(reversed(case["candidates"]))
    labels={label:f"choice-{i+1}" for i,label in enumerate(order)}
    mapping[case_id]={new:old for old,new in labels.items()}
    review.append(dict(case,candidates={labels[old]:case["candidates"][old] for old in order}))
write(folder/"review-map.json",mapping)
write(folder/"review-packet.json",review)
print(len(review),"cases requiring fresh review")
