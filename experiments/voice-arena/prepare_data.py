"""Recreate the exact two text fixtures used in the local Qwen experiment."""
import hashlib
import json
import urllib.parse
import urllib.request
from pathlib import Path

EXPERIMENT = Path(__file__).resolve().parent
OUTPUT = EXPERIMENT.parents[1] / "artifacts/quality"


def main():
    split = json.loads((EXPERIMENT / "split.json").read_text(encoding="utf-8-sig"))
    metadata = json.loads((EXPERIMENT / "run-metadata.json").read_text(encoding="utf-8-sig"))
    count = sum(len(ids) for ids in split.values())
    rows = {}
    for offset in range(0, count, 100):
        query = urllib.parse.urlencode(dict(dataset=metadata["dataset"], config="default",
                                            split="test", offset=offset, length=min(100, count-offset)))
        with urllib.request.urlopen("https://datasets-server.huggingface.co/rows?" + query, timeout=60) as response:
            for item in json.load(response)["rows"]:
                row = item["row"]
                rows[row["id"]] = row
    OUTPUT.mkdir(parents=True, exist_ok=True)
    for name, speakers in split.items():
        fixtures = []
        for case_id, speaker in speakers.items():
            row = rows[case_id]
            if row["speaker_id"] != speaker:
                raise ValueError(f"Speaker differs for {case_id}")
            fixtures.append(dict(Id=case_id, ExpectedRaw=row["spoken_text"],
                                 ExpectedFinal=row["intended_text"], Tags=[row["domain"]]))
        data = json.dumps(fixtures, indent=2).replace("\n", "\r\n").encode("utf8")
        expected = metadata["files"][f"artifacts/quality/{name}.json"].lower()
        if hashlib.sha256(data).hexdigest() != expected:
            raise ValueError(f"The public rows no longer match the frozen {name} fixtures.")
        path = OUTPUT / f"{name}.json"
        if path.exists() and path.read_bytes() != data:
            raise ValueError(f"An existing fixture differs: {path}")
        if not path.exists():
            path.write_bytes(data)
        print(f"{name}: {len(fixtures)} cases verified")


if __name__ == "__main__":
    main()
