"""Split blind review across batches without grading an identical message twice."""
import argparse
from pathlib import Path
from grade import QUALITY, grade_packets, read, write

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument("phase",choices=["first","remaining"])
    args=parser.parse_args()
    first=QUALITY/"blind-round-one"
    first.mkdir(exist_ok=True)
    cases,_=grade_packets("heldout",["incumbent"])
    if args.phase=="first":
        for start in range(0,len(cases),20):
            write(first/f"packet-{start//20+1}.json",cases[start:start+20])
        return
    grades={}
    for path in first.glob("grade-*.json"):
        for row in read(path):
            assert row["label"]=="A" and row["id"] not in grades
            grades[row["id"]]=row
    assert set(grades)=={r["id"] for r in cases}
    seen={r["id"]:r["candidates"]["A"] for r in cases}
    paired,_=grade_packets("heldout",["incumbent","copy-editor"])
    dest=QUALITY/"blind-heldout-copy-editor"
    dest.mkdir(exist_ok=True)
    reused=[]
    novel=[]
    for row in paired:
        remaining={}
        for label,text in row["candidates"].items():
            if seen[row["id"]]==text:
                reused.append(dict(grades[row["id"]],label=label))
            else:
                remaining[label]=text
        if remaining:
            novel.append(dict(row,candidates=remaining))
    write(dest/"grade-reused.json",reused)
    for start in range(0,len(novel),16):
        write(dest/f"novel-{start//16+1}.json",novel[start:start+16])
    print(len(reused),"previously graded,",len(novel),"cases with a new output")

if __name__=="__main__":
    main()
