"""Build a fixed English dictation benchmark from two pinned public evaluation splits."""
import hashlib
import json
import sys
from collections import Counter
from pathlib import Path
from urllib.request import urlopen

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "artifacts/large-dictation"
sys.path.insert(0, str(OUT / "python"))
import duckdb

SOURCES = {
    "fluid": ("johnbean393/fluid-2-sft-eval", "3b03c4029cf539a720890867c09ddb8a97b70c49",
              "data/eval-00000-of-00001.parquet", "fluid-eval.parquet", "9e20310982f01a24371f381a27ddd807ec63659678085b4a0b0a9386ededd6e2"),
    "utter": ("ductai199x/dictation-to-text", "f21f954c0092ad88b1195ce557f3ae3f69717fae",
               "data/test-00000-of-00001.parquet", "utter-test.parquet", "d1dbbd25318334ecd8688c368279c5c7bbe76116d789c2d13a8cdbf268cdb9a5"),
}

def key(text):
    return " ".join(text.casefold().split())

def rank(text):
    return hashlib.sha256(("shruti-large-v1/"+text).encode()).hexdigest()

def save(path, value):
    path.write_bytes((json.dumps(value, ensure_ascii=False, indent=2)+"\n").encode())

def build():
    OUT.mkdir(parents=True, exist_ok=True)
    rows, sources = [], []
    used_raw, used_target = set(), set()
    for source, (repo, rev, remote, local, sha) in SOURCES.items():
        path = OUT / local
        if not path.exists():
            with urlopen(f"https://huggingface.co/datasets/{repo}/resolve/{rev}/{remote}", timeout=120) as res:
                path.write_bytes(res.read())
        assert hashlib.sha256(path.read_bytes()).hexdigest() == sha
        cursor = duckdb.connect().execute("select * from read_parquet(?)", [str(path)])
        data = [dict(zip([c[0] for c in cursor.description], row)) for row in cursor.fetchall()]
        id_field = "row_id" if source=="fluid" else "id"
        groups = set()
        for row in sorted(data,key=lambda r:rank(r[id_field])):
            if source=="utter":
                if not (row["domain"].startswith("everyday") or row["domain"] in {"chat","list-everyday"}) or not row["lists"]:
                    continue
                raw, target, group = row["dictated"], row["final_text"], row["id"].rsplit("-",1)[0]
                tags=[row["domain"],row["category"]]
            else:
                raw,target,group=row["asr_text"],row["clean_target"],row["document_id"]
                tags=["with-context" if row["prev_text"] or row["post_text"] else "standalone"]
            if not key(raw) or not key(target) or key(raw) in used_raw or key(target) in used_target or group in groups:
                continue
            groups.add(group);used_raw.add(key(raw));used_target.add(key(target))
            case_id=source+"-"+row[id_field]
            case=dict(Id=case_id,ExpectedRaw=raw,ExpectedFinal=target,Tags=[source,*tags])
            if source=="fluid":
                case.update(BeforeCaret=row["prev_text"],AfterCaret=row["post_text"])
            else:
                case.update(Vocabulary=row["dictionary"])
            rows.append(case)
            sources.append(dict(id=case_id,source=source,group=group,upstream_id=row[id_field],
                                attribution=row.get("source_url"),source_license=row.get("license"),tags=tags))
            if len(groups)==2000:break
        assert len(groups)==2000,(source,len(groups))
    assert len(used_raw)==len(used_target)==len(rows)==4000
    order=sorted(rows,key=lambda r:rank("split/"+r["Id"]))
    dev=set()
    for src in SOURCES:
        dev.update(r["Id"] for r in [r for r in order if r["Tags"][0]==src][:250])
    for name, selection in [("all",order),("dev",[r for r in order if r["Id"] in dev]),("evaluation",[r for r in order if r["Id"] not in dev])]:
        save(OUT/(name+".json"),selection)
        print(name,len(selection),flush=True)
    audit=[]
    for source in SOURCES:
        source_dev=[r for r in rows if r["Id"] in dev and r["Tags"][0]==source]
        source_dev.sort(key=lambda r: hashlib.sha256(("independent-review-v1/"+r["Id"]).encode()).digest())
        audit.extend(dict(id=r["Id"],raw=r["ExpectedRaw"],reference=r["ExpectedFinal"]) for r in source_dev[:20])
    save(OUT/"reference-audit.json", audit)
    for row in sources:row["split"]="dev" if row["id"] in dev else "evaluation"
    save(OUT/"source-index.json",sources)
    save(OUT/"manifest.json",dict(sources=SOURCES,counts=dict(Counter(r["Tags"][0] for r in rows)),
         hashes={n:hashlib.sha256((OUT/n).read_bytes()).hexdigest() for n in ("all.json","dev.json","evaluation.json","source-index.json")}))
if __name__=="__main__":
    build()
