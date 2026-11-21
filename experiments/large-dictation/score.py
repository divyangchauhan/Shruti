"""Apply the Shruti validator to frozen model outputs and score every case."""
import argparse
import hashlib
import json
import subprocess
import sys
from collections import Counter
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/"artifacts/large-dictation"
sys.path.insert(0,str(OUT/"python"))
import duckdb

def read(path): return json.loads(path.read_bytes())
def write(path, data): path.write_bytes((json.dumps(data,ensure_ascii=False,indent=2)+"\n").encode("utf8"))
def indexed(rows):
    result={r["Id"]:r for r in rows}
    if len(rows)!=len(result):raise ValueError("Duplicate IDs")
    return result

def replay(name, audit_size=0):
    manifest=OUT/"primary-eval.json"
    inputs=read(OUT/"eval-inputs.json")
    suffix=""
    if audit_size:
        suffix=f"-audit-{audit_size}"
        fixtures=read(manifest)[:audit_size]
        manifest=OUT/("primary-eval"+suffix+".json")
        write(manifest,fixtures)
        inputs=inputs[:audit_size]
    captured=indexed(inputs)
    logs=indexed([json.loads(s) for s in (OUT/(name+".jsonl")).read_bytes().splitlines()])
    if audit_size: logs={i:r for i,r in logs.items() if i in captured}
    if logs.keys()!=captured.keys():raise ValueError(f"Incomplete run: {len(logs)} of {len(captured)}")
    report={"Stage":"refinement","ManifestSha256":hashlib.sha256(manifest.read_bytes()).hexdigest().upper(),"Cases":[]}
    for case_id, row in captured.items():
        case=logs[case_id]; proposal=case["proposal"]; model_input=row["Request"]
        failed=case["status"] not in ("bypass","stop")
        if not failed and model_input and not isinstance(proposal,str):raise ValueError(case_id)
        report["Cases"].append(dict(Id=case_id,Stage="refinement",Tags=[],AudioSource="text",Raw=row["Raw"],
              Final=row["Raw"] if failed else row["RulesFinal"],ModelInput=model_input["transcript"] if model_input else None,
              Proposal=proposal,UsedRawFallback=failed))
    source=OUT/(name+suffix+"-proposals.json");destination=OUT/(name+suffix+"-finals.json");write(source,report)
    subprocess.run(["dotnet","run","--no-build","--project","tools/Shruti.QualityBench","--","--replay-proposals",str(manifest),str(source),str(destination)],cwd=ROOT,check=True)
    return indexed(read(destination)),logs

def main():
    parser=argparse.ArgumentParser();parser.add_argument("names",nargs="*");args=parser.parse_args()
    data=indexed(read(OUT/"primary-eval.json"));held=indexed(read(OUT/"primary-eval.json"))
    inputs=indexed(read(OUT/"eval-inputs.json"))
    db=duckdb.connect()
    stats=[]
    outputs={"rules":{i:r["RulesFinal"] for i,r in inputs.items()}}
    for name in args.names:
        final,records=replay(name)
        outputs[name+" final"]={i:r["Final"] for i,r in final.items()}
        outputs[name+" draft"]={i:(r["Proposal"] if r["Proposal"] is not None else data[i]["ExpectedRaw"]) for i,r in final.items()}
        print(name,dict(Counter(r["status"] for r in records.values())),flush=True)
    for name, results in outputs.items():
        for split,ids in [("evaluation",held.keys())]:
            for source in ["combined",*sorted({r["Tags"][-1] for r in data.values()})]:
                pairs=[(results[i].replace("\r\n","\n"),data[i]["ExpectedFinal"].replace("\r\n","\n"),data[i]["ExpectedRaw"].replace("\r\n","\n")) for i in ids if source=="combined" or data[i]["Tags"][-1]==source]
                predictions,refs,raw=zip(*pairs)
                edits=db.execute("select sum(levenshtein(o,r)),sum(length(r)) from (select unnest(?) o,unnest(?) r)",[list(predictions),list(refs)]).fetchone()
                stats.append(dict(model=name,split=split,source=source,n=len(pairs),exact=sum(o==r for o,r in zip(predictions,refs)),cer=edits[0]/edits[1],
                    clean=sum(t==r for t,r in zip(raw,refs)),clean_kept=sum(o==r==t for o,r,t in pairs)))
    write(OUT/"primary-scores.json",stats)
    for row in stats:
        if row["source"]=="combined" and row["split"]=="evaluation":print(row)

if __name__=="__main__":main()
