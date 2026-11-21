import argparse,json,pathlib,statistics,collections
from grade import QUALITY, read, write

def status(row):
    if row["Failure"]:
        return row["Failure"]
    if row["Proposal"] is None:
        return "no proposal recorded"
    if row["Proposal"] == row["ModelInput"]:
        return "model kept input"
    if any(rule.startswith("local-") for rule in row["EditRules"]):
        return "applied"
    return "rejected"

def ready(row):
    return row["meaning"]=="preserved" and row["ready"]=="yes"

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument("split")
    parser.add_argument("challengers",nargs="+")
    args=parser.parse_args()
    folder=QUALITY/("blind-"+args.split+"-"+"-".join(args.challengers))
    joined=read(folder/"joined.json")
    summary={}
    for variant in ["incumbent",*args.challengers]:
        cases=read(QUALITY/f"{args.split}-{variant}.json")["Cases"]
        summary[variant]={
            "n":len(cases),
            "ready":sum(ready(r[variant]) for r in joined),
            "exact":sum(c["FormattingExactMatch"] for c in cases),
            "raw_fallback":sum(c["UsedRawFallback"] for c in cases),
            "median_ms":round(statistics.median(c["StopToResultMs"] for c in cases)),
            "model":dict(collections.Counter(status(c) for c in cases)),
            "wins":[r["id"] for r in joined if ready(r[variant]) and not ready(r["incumbent"])],
            "losses":[r["id"] for r in joined if not ready(r[variant]) and ready(r["incumbent"])],
            "new_meaning_errors":[r["id"] for r in joined if r[variant]["meaning"]=="changed" and r["incumbent"]["meaning"]!="changed"],
            "uncertain":[r["id"] for r in joined if "uncertain" in {r[variant]["ready"],r[variant]["meaning"]}]
        }
    write(folder/"summary.json",summary)
    print(json.dumps(summary,indent=2))

if __name__=="__main__":
    main()
