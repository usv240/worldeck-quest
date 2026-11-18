"""Reads one Worldeck play log (or the newest in a folder) and prints it as a timeline: relative seconds,
state, area and message, with the fields. Errors and safety events stand out. Standard library only.

    python -I Tools/timeline.py Tools/sessions            # newest log
    python -I Tools/timeline.py Tools/sessions/x.jsonl --area Voice,AI --errors
"""
import json
import os
import sys
from datetime import datetime


def newest(path):
    if os.path.isfile(path):
        return path
    files = [os.path.join(d, f) for d, _, fs in os.walk(path) for f in fs if f.endswith("-worldeck.jsonl")]
    return max(files, key=os.path.getmtime) if files else None


def main(argv):
    args = [a for a in argv if not a.startswith("--")]
    flags = [a for a in argv if a.startswith("--")]
    areas = None
    for f in flags:
        if f.startswith("--area"):
            areas = set(f.split("=", 1)[1].split(",")) if "=" in f else None
    only_errors = "--errors" in flags
    path = newest(args[0] if args else "Tools/sessions")
    if not path:
        print("no play log found")
        return 1
    recs = []
    with open(path, encoding="utf-8") as fh:
        for line in fh:
            try:
                recs.append(json.loads(line))
            except json.JSONDecodeError:
                pass
    if not recs:
        print("empty log")
        return 1
    t0 = datetime.fromisoformat(recs[0]["utc"].replace("Z", "+00:00"))
    print(f"# {os.path.basename(path)}: {len(recs)} records\n")
    skip = {"type", "utc", "launch", "area", "msg", "level", "state", "t", "frame"}
    for r in recs:
        t = (datetime.fromisoformat(r["utc"].replace("Z", "+00:00")) - t0).total_seconds()
        typ = r.get("type")
        if typ == "trace":
            level = r.get("level", "Log")
            if only_errors and level == "Log" and r.get("area") not in ("Watch",):
                continue
            if areas and r.get("area") not in areas:
                continue
            mark = "!!" if level == "Error" else "! " if level == "Warning" else "  "
            fields = " ".join(f"{k}={v}" for k, v in r.items() if k not in skip)
            print(f"{t:8.1f}s {mark} {r.get('state', ''):9} {r.get('area', ''):10} {r.get('msg', '')}  {fields}")
        elif typ in ("error", "safety", "health") or not only_errors:
            if areas:
                continue
            fields = " ".join(f"{k}={v}" for k, v in r.items() if k not in skip)
            mark = "!!" if typ == "error" else "! " if typ in ("safety",) or r.get("level") not in (None, "Ok") else "  "
            print(f"{t:8.1f}s {mark} {'':9} {typ:10} {fields}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
