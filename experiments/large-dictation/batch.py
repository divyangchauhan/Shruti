"""Offline throughput run. Save each completed case and resume only with matching settings."""
import argparse
import concurrent.futures
import hashlib
import json
import os
from pathlib import Path
import secrets
import socket
import subprocess
import time
import requests

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/"artifacts/large-dictation"
SERVER=OUT/"vulkan/llama-server.exe"
MODELS={"utter":OUT/"utter-mini-v1-q4_0.gguf","qwen":ROOT/"artifacts/local-cleanup/qwen3.5-2b-q4_k_m.gguf"}
UTTER_PROMPT="Turn this dictation into the text the speaker meant to type: punctuated, formatted and corrected the way a careful writer would type it, in the speaker's own words."
SCHEMA={"type":"json_schema","json_schema":{"name":"cleaned_transcript","strict":True,"schema":{"type":"object","properties":{"text":{"type":"string"}},"required":["text"],"additionalProperties":False}}}

def digest(path):
    with path.open("rb") as f:return hashlib.file_digest(f,"sha256").hexdigest()

def main():
    p=argparse.ArgumentParser();p.add_argument("model",choices=MODELS);p.add_argument("--slots",type=int,default=16)
    p.add_argument("--limit",type=int);p.add_argument("--inputs",default="inputs");p.add_argument("--device",choices=["gpu","cpu"],default="gpu")
    args=p.parse_args()
    model=MODELS[args.model];input_path=OUT/(args.inputs+".json")
    rows=json.loads(input_path.read_bytes())
    base=OUT/f"{args.inputs}-{args.model}-{args.device}-{args.slots}"
    prompt=UTTER_PROMPT if args.model=="utter" else (ROOT/"experiments/voice-arena/copy-editor.txt").read_text(encoding="utf-8-sig")
    metadata=dict(model=model.name,model_sha=digest(model),input_sha=digest(input_path),prompt=prompt,server_sha=digest(SERVER),runner_sha=digest(Path(__file__)),
                  slots=args.slots,context_per_slot=2048,temperature=0,reasoning=False,device=args.device)
    meta=base.with_suffix(".meta.json");results=base.with_suffix(".jsonl")
    if meta.exists() and json.loads(meta.read_bytes())!=metadata:raise ValueError("Run settings changed")
    meta.write_text(json.dumps(metadata,indent=2),encoding="utf8")
    done={r["Id"] for r in (json.loads(s) for s in results.read_bytes().splitlines())} if results.exists() else set()
    pending=[r for r in (rows[:args.limit] if args.limit else rows) if r["Id"] not in done]
    if not pending:return
    key=secrets.token_hex(24)
    with socket.socket() as sock:
        sock.bind(("127.0.0.1",0));port=sock.getsockname()[1]
    url=f"http://127.0.0.1:{port}"
    headers={"Authorization":"Bearer "+key}
    command=[str(SERVER),"-m",str(model),"--host","127.0.0.1","--port",str(port),"--api-key",key,"-c",str(args.slots*2048),"-np",str(args.slots),"-ngl","all" if args.device=="gpu" else "0","-t","4","-tb","4","--no-webui","--reasoning-budget","0","--jinja","--log-disable"]
    def infer(row):
        req=row["Request"]; text=req["transcript"] if req else ""
        if not req or len(text)>1800:
            return dict(Id=row["Id"],proposal=text if req else None,status="bypass",tokens=0)
        if args.model=="qwen":
            user=json.dumps(req,ensure_ascii=True,separators=(",",":"))
        else:
            hints=", ".join(row["Vocabulary"])
            user=(f"Dictionary: {hints}\n" if hints else "")+"Dictated: "+text
        body=dict(messages=[dict(role="system",content=prompt),dict(role="user",content=user)],max_tokens=max(256,min(1024,len(text)+64)),temperature=0,
                  seed=42,chat_template_kwargs={"enable_thinking":False},cache_prompt=True)
        if args.model=="qwen":body["response_format"]=SCHEMA
        response=requests.post(url+"/v1/chat/completions",headers=headers,json=body,timeout=300)
        response.raise_for_status()
        data=response.json(); choice=data["choices"][0]
        candidate=None;status=choice["finish_reason"]
        if status=="stop":
            try:
                content=choice["message"]["content"]
                candidate=json.loads(content)["text"] if args.model=="qwen" else content
                if not isinstance(candidate,str):raise ValueError("non-string output")
            except (ValueError,TypeError,KeyError):
                status="invalid";candidate=None
        return dict(Id=row["Id"],proposal=candidate,status=status,tokens=data["usage"]["completion_tokens"])
    with base.with_suffix(".log").open("ab") as log:
        process=subprocess.Popen(command,stdout=log,stderr=log,creationflags=subprocess.CREATE_NO_WINDOW)
        try:
            for _ in range(180):
                if process.poll() is not None:raise RuntimeError(f"Server exited {process.returncode}")
                try:
                    if requests.get(url+"/health",headers=headers,timeout=1).ok:break
                except requests.RequestException:pass
                time.sleep(1)
            else:raise RuntimeError("Server did not become ready")
            start=time.monotonic()
            with results.open("a",encoding="utf8") as f, concurrent.futures.ThreadPoolExecutor(max_workers=args.slots) as pool:
                for i,row in enumerate(pool.map(infer,pending,buffersize=2*args.slots),1):
                    f.write(json.dumps(row,ensure_ascii=True)+"\n");f.flush()
                    if i%100==0 or i==len(pending):print(len(done)+i,"completed,",round(time.monotonic()-start),"s this run",flush=True)
        finally:
            process.terminate();process.wait(timeout=30)

if __name__=="__main__":
    main()
