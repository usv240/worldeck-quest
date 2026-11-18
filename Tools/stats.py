"""Worldeck play-log statistics: the numbers for the proof card.

Reads every *-worldeck.jsonl under the given folders (pulled from headsets with Tools/quest.sh pull) and
prints: launches, worlds opened and how they were asked for, words-to-world latency (median and p90),
voice and AI outcomes and latency, gather tasks (completion and time), frame times per visit, safety
fades, health fallbacks and errors. Standard library only. Run: python -I Tools/stats.py Tools/sessions
"""
import json
import os
import statistics
import sys
from collections import Counter, defaultdict


def load(paths):
    recs = []
    for root in paths:
        files = [root] if os.path.isfile(root) else [
            os.path.join(d, f) for d, _, fs in os.walk(root) for f in fs if f.endswith("-worldeck.jsonl")]
        for f in sorted(files):
            with open(f, encoding="utf-8") as fh:
                for line in fh:
                    line = line.strip()
                    if not line:
                        continue
                    try:
                        recs.append(json.loads(line))
                    except json.JSONDecodeError:
                        pass
    return recs


def pct(values, p):
    if not values:
        return float("nan")
    v = sorted(values)
    k = max(0, min(len(v) - 1, int(round(p * (len(v) - 1)))))
    return v[k]


def main(argv):
    recs = load(argv or ["Tools/sessions"])
    if not recs:
        print("No play logs found. Pull them with: Tools/quest.sh pull")
        return 1
    by = defaultdict(list)
    for r in recs:
        by[r.get("type", "?")].append(r)
    launches = len({r.get("launch") for r in recs})
    print(f"# Worldeck play logs\n\n{len(recs)} records from {launches} launches.\n")

    worlds = by["world"]
    print(f"## Worlds opened: {len(worlds)}")
    print("By world:", ", ".join(f"{k} {v}" for k, v in Counter(w["biome"] for w in worlds).most_common()))
    print("Asked for by:", ", ".join(f"{k} {v}" for k, v in Counter(w["how"] for w in worlds).most_common()))
    spoken = [w["ms"] for w in worlds if str(w.get("how", "")).startswith("voice")]
    tapped = [w["ms"] for w in worlds if not str(w.get("how", "")).startswith("voice")]
    if spoken:
        print(f"Words to world (spoken): median {statistics.median(spoken) / 1000:.1f} s, p90 {pct(spoken, 0.9) / 1000:.1f} s (n={len(spoken)})")
    if tapped:
        print(f"Touch to world: median {statistics.median(tapped) / 1000:.1f} s (n={len(tapped)})")
    per_launch = Counter(w.get("launch") for w in worlds)
    if per_launch:
        print(f"Worlds per launch: median {statistics.median(per_launch.values()):.0f}, max {max(per_launch.values())}")

    edits = by["edit"]
    if edits:
        print(f"\n## Edits: {len(edits)} (median {statistics.median([e['ms'] for e in edits]):.0f} ms)")

    v = by["voice"]
    if v:
        ok = [x for x in v if x.get("ok")]
        print(f"\n## Voice: {len(ok)} of {len(v)} understood; speech-to-text median {statistics.median([x['ms'] for x in v]):.0f} ms")
    a = by["ai"]
    if a:
        ok = [x for x in a if x.get("ok")]
        print(f"## World AI: {len(ok)} of {len(a)} answered (median {statistics.median([x['ms'] for x in a]):.0f} ms); "
              f"sources {dict(Counter(x.get('source', '') for x in ok))}; declined {sum(1 for x in a if x.get('declined'))}")

    t = by["task"]
    if t:
        secs = [x["seconds"] for x in t]
        print(f"\n## Gather tasks finished: {len(t)}; time median {statistics.median(secs):.0f} s, range {min(secs):.0f}-{max(secs):.0f} s")
        print("Souvenirs earned:", sum(1 for x in t if x.get("souvenir")))
    started = len(worlds)
    if started:
        print(f"Tasks finished per world opened: {len(t)}/{started}")

    visits = [x for x in by["visit"] if x.get("frames", 0) > 0]
    if visits:
        p95 = [x["ms_p95"] for x in visits]
        fps = [x["fps_mean"] for x in visits]
        print(f"\n## Frame times over {len(visits)} visits: mean fps median {statistics.median(fps):.1f}; "
              f"p95 frame median {statistics.median(p95):.1f} ms, worst {max(p95):.1f} ms; "
              f"late frames median {statistics.median([x['late_pct'] for x in visits]):.2f} %")
        secs = [x["seconds"] for x in by["visit"]]
        print(f"Time in a world: median {statistics.median(secs):.0f} s, total {sum(secs) / 60:.1f} min")

    if by["safety"]:
        print(f"\n## Safety fades: {len(by['safety'])} ({dict(Counter(x.get('why') for x in by['safety']))})")
    falls = [h for h in by["health"] if h.get("level") != "Ok"]
    if falls:
        print("## Health not ok:", "; ".join(sorted({f"{h['part']}: {h.get('detail', '')}" for h in falls}))[:600])
    errs = by["error"]
    print(f"\n## Errors: {sum(e.get('count', 1) for e in errs)} ({len(errs)} kinds)")
    for e in errs[:10]:
        print(f"- {e.get('level')}: {e.get('message', '')[:120]} @ {e.get('where', '')[:80]}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
