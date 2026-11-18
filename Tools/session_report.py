"""What happened in a Worldeck session, from its play log: where a player got stuck, what they reached, how it ran.

    python Tools/session_report.py <session.jsonl> [more.jsonl ...]
    python Tools/session_report.py --pull [N]      the newest N sessions from the headset (default 1), via adb

For each session, in plain words:
- Milestones, with the time each was reached: first press, the first world, her call, each landing, the palm
  landing, the ending.
- Friction:
  - silences of 20 s or more with nothing pressed or grabbed;
  - pinches and pokes that missed;
  - the help card opened;
  - a controller picked up;
  - safety fades;
  - errors.
- Health: frame rate (mean, 99th percentile), memory, temperature.

The play log never holds words or voice: only counts, timings and device and setting values.
"""
import json
import os
import subprocess
import sys

ADB = os.environ.get("ADB", "C:/Unity/Editors/6000.0.85f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe")
PKG = "com.ujwal240.worldeck"
HERE = os.path.dirname(os.path.abspath(__file__))

MILESTONES = [
    (("Hands", "pressed"), "first press"),
    (("Director", "state"), "first world"),
    (("Gather", "task done"), "first task done"),
    (("Mission", "title card"), "her call (chapter title)"),
    (("Mission", "landing"), "a storm over"),
    (("Mission", "landed in your hand"), "her ship in your hand"),
    (("Runway", "still dark"), "a piece still dark"),
    (("Runway", "lit again"), "a piece lit again"),
    (("Local", "waves back"), "a wave answered"),
    (("Local", "favour done"), "a favour done"),
    (("Mission", "story ending at home"), "the ending at home"),
]
ACTIVE = {("Hands", "pressed"), ("Gather", "grabbed"), ("Maker", "grabbed"), ("Voice", "handling"), ("Maker", "touched"),
          ("Gather", "collected by looking")}


def load(path):
    rows = []
    for line in open(path, encoding="utf-8", errors="replace"):
        try:
            rows.append(json.loads(line))
        except ValueError:
            pass
    return rows


def pct(values, p):
    if not values:
        return 0.0
    v = sorted(values)
    return v[min(len(v) - 1, int(round(p / 100.0 * (len(v) - 1))))]


def report(path):
    rows = load(path)
    app = next((r for r in rows if r.get("type") == "app"), {})
    traces = [r for r in rows if r.get("type") == "trace" and isinstance(r.get("t"), (int, float))]
    out = ["## " + os.path.basename(path),
           "Build %s (%s), %s, %s" % (app.get("build", "?")[:8], "dev" if app.get("debug") else "release",
                                    app.get("device", "?"), (app.get("utc") or "")[:16].replace("T", " ") + " UTC")]
    if not traces:
        return "\n".join(out + ["(no events)"])
    end = traces[-1]["t"]
    out.append("Length: %d min %02d s" % (end // 60, end % 60))

    # Milestones: the first time each happened ("first world" = the first time a world opened).
    out.append("\n**Reached**")
    seen = set()
    for r in traces:
        k = (r.get("area"), r.get("msg"))
        for key, label in MILESTONES:
            if k != key or label in seen:
                continue
            if label == "first world" and r.get("to") != "World":
                continue
            seen.add(label)
            extra = ""
            if key == ("Mission", "landing"):
                extra = " (%s, %s blocked of %s)" % (r.get("landing"), r.get("blocked"), (r.get("blocked") or 0) + (r.get("taken") or 0))
            out.append("- %6.1f s  %s%s" % (r["t"], label, extra))
    missing = [label for _, label in MILESTONES if label not in seen]
    if missing:
        out.append("- not reached: " + ", ".join(missing))

    # Friction.
    out.append("\n**Friction**")
    last = 0.0
    gaps = []
    for r in traces:
        if (r.get("area"), r.get("msg")) in ACTIVE:
            if r["t"] - last >= 20 and last > 0:
                gaps.append((last, r["t"]))
            last = r["t"]
    if end - last >= 20 and last > 0:
        gaps.append((last, end))

    def count(area, msg):
        return sum(1 for r in traces if r.get("area") == area and r.get("msg") == msg)

    misses = count("Gather", "closed a hand near a piece and missed")
    ignored = sum(1 for r in traces if r.get("area") == "Hands" and str(r.get("msg", "")).startswith("poke ignored"))
    slid = sum(1 for r in traces if r.get("area") == "Hands" and str(r.get("msg", "")).startswith("slid onto"))
    helps = [r["t"] for r in traces if r.get("area") == "Help" and r.get("msg") == "shown"]
    pads = [r["t"] for r in traces if r.get("area") == "Controllers"]
    fades = [r["t"] for r in traces if r.get("area") == "Watch" and r.get("msg") == "safety fade"]
    errors = [r for r in rows if r.get("type") == "error"]
    rays = sum(1 for r in traces if r.get("area") == "Hands" and r.get("msg") == "pressed" and r.get("how") == "ray")
    pokes = sum(1 for r in traces if r.get("area") == "Hands" and r.get("msg") == "pressed" and r.get("how") in ("poke", "look+pinch"))
    out.append("- silences of 20 s or more with nothing done: %s" % (", ".join("%.0f-%.0f s" % g for g in gaps[:8]) or "none"))
    out.append("- presses: %d by hand, %d by controller ray; missed grabs %d; pokes ignored %d; slid onto a button %d"
               % (pokes, rays, misses, ignored, slid))
    if helps:
        out.append("- How it works opened at " + ", ".join("%.0f s" % t for t in helps))
    if pads:
        out.append("- **a controller was picked up** at " + ", ".join("%.0f s" % t for t in pads))
    if fades:
        out.append("- safety fades (stood or moved) at " + ", ".join("%.0f s" % t for t in fades[:6]))
    if errors:
        out.append("- errors: %d (first: %s)" % (len(errors), str(errors[0].get("message", ""))[:100]))

    # Health.
    beats = [r for r in traces if r.get("area") == "Perf" and r.get("msg") == "heartbeat"]
    visits = [r for r in rows if r.get("type") == "visit"]
    out.append("\n**How it ran**")
    if visits:
        fps = [v.get("fps_mean", 0) for v in visits if v.get("fps_mean")]
        p99 = [v.get("ms_p99", 0) for v in visits if v.get("ms_p99")]
        out.append("- %d worlds: mean frame rate %.1f fps (lowest %.1f); 99th-percentile frame %.2f ms (worst %.2f)"
                   % (len(visits), sum(fps) / max(1, len(fps)), min(fps or [0]), sum(p99) / max(1, len(p99)), max(p99 or [0])))
    elif beats:
        fps = [b.get("fps", 0) for b in beats]
        out.append("- heartbeat frame rate: median %.0f, lowest %.0f fps" % (pct(fps, 50), min(fps)))
    if beats:
        mem = [b.get("mem_mb", 0) for b in beats if b.get("mem_mb")]
        therm = sorted({str(b.get("thermal")) for b in beats if b.get("thermal") is not None})
        out.append("- memory up to %d MB; temperature states: %s" % (max(mem or [0]), ", ".join(therm) or "?"))
    return "\n".join(out)


def pull(n):
    env = dict(os.environ, MSYS_NO_PATHCONV="1")
    names = subprocess.run([ADB, "shell", "ls", "-t", "/sdcard/Android/data/%s/files/sessions/" % PKG],
                           capture_output=True, text=True, env=env).stdout.split()
    dest = os.path.join(HERE, "..", "Recordings", "sessions")
    os.makedirs(dest, exist_ok=True)
    got = []
    for name in names[:n]:
        name = name.strip()
        local = os.path.join(dest, name)
        subprocess.run([ADB, "pull", "/sdcard/Android/data/%s/files/sessions/%s" % (PKG, name), local],
                       capture_output=True, env=env)
        if os.path.exists(local):
            got.append(local)
    return got


def main():
    args = sys.argv[1:]
    if not args:
        print(__doc__)
        return 2
    if args[0] == "--pull":
        paths = pull(int(args[1]) if len(args) > 1 else 1)
    else:
        paths = args
    print("\n\n".join(report(p) for p in paths))
    return 0


if __name__ == "__main__":
    sys.exit(main())
