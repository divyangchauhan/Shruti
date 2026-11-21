"""Freeze one primary test case per Utter passage after auditing the mixed pool."""
import json
from collections import Counter
from prepare import OUT,SOURCES,rank,key,save,duckdb,hashlib

src=SOURCES["utter"]
assert hashlib.sha256((OUT/src[3]).read_bytes()).hexdigest()==src[4]
cursor=duckdb.connect().execute("select * from read_parquet(?)",[str(OUT/src[3])])
cols=[c[0] for c in cursor.description]
data=[dict(zip(cols,r)) for r in cursor.fetchall()]
def everyday(r):
    return r["domain"].startswith("everyday") or r["domain"] in {"chat","list-everyday"}
# Keep sources inspected during the reference audit in development.
audit={r["id"] for r in json.loads((OUT/"reference-audit.json").read_bytes())}
groups=set();raws=set();refs=set();rows=[];index=[]
for r in sorted(data,key=lambda r:("utter-"+r["id"] not in audit,not everyday(r),r["domain"]=="science",rank(r["id"]))):
    group=r["id"].rsplit("-",1)[0]
    a,b=key(r["dictated"]),key(r["final_text"])
    if not r["lists"] or not a or not b or group in groups or a in raws or b in refs:continue
    groups.add(group);raws.add(a);refs.add(b)
    case_id="utter-"+r["id"]
    rows.append(dict(Id=case_id,ExpectedRaw=r["dictated"],ExpectedFinal=r["final_text"],Tags=["utter",r["domain"],r["category"]],Vocabulary=r["dictionary"]))
    index.append(dict(id=case_id,group=group,source=r["source"],url=r["source_url"],license=r["license"],domain=r["domain"],category=r["category"]))
    if len(rows)==3500:break
assert len(raws)==len(refs)==len(groups)==3500
ordered=sorted(rows,key=lambda r:rank("primary/"+r["Id"]))
dev=set(r["Id"] for r in sorted(rows,key=lambda r:(r["Id"] not in audit,rank("dev/"+r["Id"])))[:500])
for name,v in [("primary",ordered),("primary-dev",[r for r in ordered if r["Id"] in dev]),("primary-eval",[r for r in ordered if r["Id"] not in dev])]:
    save(OUT/(name+".json"),v)
for r in index:r["split"]="dev" if r["id"] in dev else "evaluation"
save(OUT/"primary-sources.json",index)
fluid=[r for r in json.loads((OUT/"all.json").read_bytes()) if r["Tags"][0]=="fluid"]
save(OUT/"fluid-diagnostic.json",fluid)
print("primary",len(rows),"everyday/chat",sum(everyday(r) for r in index),"domain",dict(Counter(r["domain"] for r in index)))
