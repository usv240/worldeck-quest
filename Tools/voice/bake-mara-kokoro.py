"""Records every line Mara can say with Kokoro-82M (Apache-2.0) through sherpa-onnx, offline, on this PC.

  1. In Unity: Worldeck > Export Mara's lines   (writes Tools/voice/mara-lines.tsv)
  2. python -I Tools/voice/bake-mara-kokoro.py <kokoro-en-v0_19 folder> [--speaker 1] [--speed 1.05] [--force]

Needs the sherpa-onnx Python package 1.12.x (2.0 dropped espeak for this model) and the Kokoro model (k2-fsa/sherpa-onnx releases, tts-models,
kokoro-en-v0_19). Only the recordings ship: Assets/Worldeck/Resources/MaraVoice/<key>.wav, 16 kHz mono. Clips no
longer in the list are removed. Speakers in v0.19: 0 af, 1 af_bella, 2 af_nicole, 3 af_sarah, 4 af_sky, 7 bf_emma,
8 bf_isabella (5, 6, 9, 10 are male).
"""
import argparse
import os
import struct
import sys
import wave

import sherpa_onnx

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(HERE, "..", "..", "Assets", "Worldeck", "Resources", "MaraVoice"))
RATE = 16000


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("model")
    ap.add_argument("--speaker", type=int, default=1)
    ap.add_argument("--speed", type=float, default=1.05)
    ap.add_argument("--force", action="store_true")
    a = ap.parse_args()
    m = a.model
    cfg = sherpa_onnx.OfflineTtsConfig(
        model=sherpa_onnx.OfflineTtsModelConfig(
            kokoro=sherpa_onnx.OfflineTtsKokoroModelConfig(
                model=os.path.join(m, "model.onnx"), voices=os.path.join(m, "voices.bin"),
                tokens=os.path.join(m, "tokens.txt"), data_dir=os.path.join(m, "espeak-ng-data")),
            num_threads=4),
        max_num_sentences=4)
    if not cfg.validate():
        sys.exit("bad Kokoro model folder: " + m)
    tts = sherpa_onnx.OfflineTts(cfg)
    os.makedirs(OUT, exist_ok=True)
    keys, made = set(), 0
    for row in open(os.path.join(HERE, "mara-lines.tsv"), encoding="utf-8"):
        row = row.rstrip("\n")
        if not row.strip():
            continue
        key, text = row.split("\t", 1)
        keys.add(key)
        path = os.path.join(OUT, key + ".wav")
        if os.path.exists(path) and not a.force:
            continue
        # Pauses written as "..." read better as commas to a voice model.
        say = text.replace("...", ", ").replace(" ,", ",").strip(" ,")
        audio = tts.generate(say, sid=a.speaker, speed=a.speed)
        x = list(audio.samples)
        if audio.sample_rate != RATE and len(x) > 1:
            n = int(len(x) * RATE / audio.sample_rate)
            step = (len(x) - 1) / max(1, n - 1)
            y = []
            for i in range(n):
                f = i * step
                k = int(f)
                k2 = min(k + 1, len(x) - 1)
                y.append(x[k] + (x[k2] - x[k]) * (f - k))
            x = y
        peak = max((abs(v) for v in x), default=0.0)
        g = 0.89 / peak if peak > 0 else 1.0
        pcm = [max(-32768, min(32767, int(v * g * 32767))) for v in x]
        with wave.open(path, "wb") as w:
            w.setnchannels(1)
            w.setsampwidth(2)
            w.setframerate(RATE)
            w.writeframes(struct.pack("<%dh" % len(pcm), *pcm))
        made += 1
    removed = 0
    for f in os.listdir(OUT):
        if f.endswith(".wav") and f[:-4] not in keys:
            os.remove(os.path.join(OUT, f))
            if os.path.exists(os.path.join(OUT, f + ".meta")):
                os.remove(os.path.join(OUT, f + ".meta"))
            removed += 1
    total = sum(os.path.getsize(os.path.join(OUT, f)) for f in os.listdir(OUT) if f.endswith(".wav"))
    print(f"Mara's voice (Kokoro speaker {a.speaker}): {made} recorded, {removed} removed, {len(keys)} lines, {total / 1e6:.1f} MB")


if __name__ == "__main__":
    main()
