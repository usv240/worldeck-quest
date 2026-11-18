#!/usr/bin/env python3
"""
make_sounds.py - procedural audio pack for Worldeck (Meta Quest 3, mixed reality).

Every sound is synthesised from code with numpy (+ scipy.signal filters). No samples, no downloads.
Writes 16-bit PCM mono WAV files to Assets/Worldeck/Audio/, reads every file back, checks it, prints a
table and writes Assets/Worldeck/Audio/SOUNDS.md (what each sound is and its levels).

    python Tools/make_sounds.py            (WORLDECK_SND_DEBUG=1 also prints per-layer levels)

Formats
  * Ambience beds (amb_*): 24 000 Hz, 24 s seamless loops, RMS -24 .. -20 dBFS.
  * fire_loop: 24 000 Hz, 12 s seamless loop. jukebox_music: 24 000 Hz, 16 s seamless loop.
  * UI / feedback / build flourishes: 48 000 Hz one-shots, sample peak -14 dBFS (ui_hover -18 dBFS),
    raised-cosine fades, so the first and last samples are zero.

Seamless loops
  * Continuous layers (wind, waves, rain, vent, rumble, fire roar, brush swish ...) are synthesised 1 s
    longer than the loop and the extra second is equal-power crossfaded into the head. Sample N-1 then
    runs into sample 0 exactly as sample N-1 ran into sample N of the long render (no click), and since
    head and overshoot are independent noise the equal-power law keeps the level flat through the fade.
  * Every modulation (gusts, wave cycles, flicker, swing pattern, tremolo) is a function of time made of
    whole cycles per loop, so it lines up at the seam.
  * Discrete events (owl, gulls, chimes, beeps, bubbles, crackles, birds) and the music are rendered into
    a circular buffer (whatever rings past the end wraps to the start), and tonal drones use whole cycles
    per loop. Those layers are exactly periodic, so they are not crossfaded (an equal-power fade of
    identical material would bump the level by 3 dB).
  * The finished loop is periodic, so any sample of it can be the first: each file is rotated to start at
    a steady moment (the 50 ms either side match in level, no event nearby). The jukebox starts half a
    beat before bar 1, as a pickup.

Key: C major pentatonic (C D E G A) for every earcon, flourish, chime, beep and bird whistle; hz() refuses
any other pitch class. The jazz (bed, jukebox and build_jukebox) is diatonic C major (ii-V-I), with
chromatic approach notes only in the walking bass.

Deterministic: each sound seeds its RNG from a CRC32 of its name; reverb IRs from a CRC32 of their params.
"""

import math
import os
import wave
import zlib

import numpy as np
from scipy import signal as sps

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(HERE)
OUT_DIR = os.path.join(PROJECT, "Assets", "Worldeck", "Audio")
DEBUG = bool(os.environ.get("WORLDECK_SND_DEBUG"))

SR_LOOP = 24000
SR_UI = 48000
XFADE_SEC = 1.0
UI_PEAK_DB = -14.0
BED_RMS_LO, BED_RMS_HI = -24.0, -20.0
MAX_UI_SEC = 0.4
MAX_BUILD_SEC = 2.0
TOTAL_MB_LIMIT = 25.0
JAZZ_BPM = 90.0  # 1 beat = 16000 samples at 24 kHz; 36 beats = 24 s, 24 beats = 16 s

# ----------------------------------------------------------------------------------------------
# Pitch
# ----------------------------------------------------------------------------------------------
_PC = {"C": 0, "C#": 1, "Db": 1, "D": 2, "D#": 3, "Eb": 3, "E": 4, "F": 5, "F#": 6, "Gb": 6, "G": 7,
       "G#": 8, "Ab": 8, "A": 9, "A#": 10, "Bb": 10, "B": 11}
PENTA = frozenset({0, 2, 4, 7, 9})          # C D E G A
DIATONIC = frozenset({0, 2, 4, 5, 7, 9, 11})  # C major
CHROMATIC = frozenset(range(12))


def hz(note, allowed=PENTA):
    """'E5' -> 659.26. Raises for a pitch class outside `allowed` (C major pentatonic by default)."""
    name, octave = note[:-1], int(note[-1])
    midi = 12 * (octave + 1) + _PC[name]
    if midi % 12 not in allowed:
        raise ValueError(f"{note} is outside the allowed scale")
    return 440.0 * 2.0 ** ((midi - 69) / 12.0)


def hz_c(note):
    return hz(note, DIATONIC)


def hz_any(note):
    return hz(note, CHROMATIC)


# ----------------------------------------------------------------------------------------------
# Small helpers
# ----------------------------------------------------------------------------------------------
def n_of(sec, sr):
    return int(round(sec * sr))


def tl(n, sr):
    return np.arange(n) / sr


def smoothstep(u):
    u = np.clip(u, 0.0, 1.0)
    return u * u * (3.0 - 2.0 * u)


def db(g):
    return 10.0 ** (g / 20.0)


def rms(x):
    return float(np.sqrt(np.mean(np.square(x))))


def rms_db(x):
    return 20.0 * math.log10(rms(x) + 1e-12)


def peak_db(x):
    return 20.0 * math.log10(float(np.max(np.abs(x))) + 1e-12)


def unit_rms(x):
    r = rms(x)
    return x / r if r > 0 else x


def unit_peak(x):
    p = float(np.max(np.abs(x)))
    return x / p if p > 0 else x


def logu(rng, a, b):
    return float(math.exp(rng.uniform(math.log(a), math.log(b))))


def phase_of(freq, sr, n=None):
    """Running phase (radians) of a frequency track; starts at exactly 0."""
    freq = np.broadcast_to(np.asarray(freq, float), (n,) if n else np.shape(freq))
    c = np.cumsum(freq)
    return 2.0 * np.pi * (c - c[0]) / sr


def perc_env(n, sr, attack, tau, hold=0.0):
    """Smooth attack (smoothstep) then exponential decay with time constant tau."""
    t = tl(n, sr)
    a = smoothstep(t / attack) if attack > 0 else np.ones(n)
    return a * np.exp(-np.maximum(t - attack - hold, 0.0) / tau)


def env_pts(n, sr, pts):
    """Breakpoint envelope [(time, value), ...] with smoothstep easing between points."""
    t = tl(n, sr)
    out = np.full(n, float(pts[-1][1]))
    out[t < pts[0][0]] = pts[0][1]
    for (t0, a0), (t1, a1) in zip(pts[:-1], pts[1:]):
        m = (t >= t0) & (t < t1)
        out[m] = a0 + (a1 - a0) * smoothstep((t[m] - t0) / (t1 - t0))
    return out


def glide(n, sr, pts, ease=True):
    """Pitch track through [(time, Hz), ...], interpolated in log-frequency."""
    t = tl(n, sr)
    lf = np.full(n, math.log(pts[-1][1]))
    lf[t < pts[0][0]] = math.log(pts[0][1])
    for (t0, f0), (t1, f1) in zip(pts[:-1], pts[1:]):
        m = (t >= t0) & (t < t1)
        u = (t[m] - t0) / (t1 - t0)
        if ease is True:
            u = smoothstep(u)
        elif callable(ease):
            u = ease(u)
        lf[m] = math.log(f0) + (math.log(f1) - math.log(f0)) * u
    return np.exp(lf)


def vibrato(n, sr, rate, depth, delay):
    t = tl(n, sr)
    return 1.0 + depth * smoothstep((t - delay) / 0.08) * np.sin(2.0 * np.pi * rate * t)


def note_tail(n, frac=0.25):
    """Raised-cosine fade over the last `frac` of a note buffer, so a still-ringing note never cuts."""
    w = np.ones(n)
    b = max(1, int(n * frac))
    w[n - b:] = 0.5 + 0.5 * np.cos(np.pi * (np.arange(b) + 1) / b)
    return w


def add(buf, sig, i0, gain=1.0):
    if i0 < 0:
        sig, i0 = sig[-i0:], 0
    if i0 >= len(buf):
        return
    m = min(len(sig), len(buf) - i0)
    if m > 0:
        buf[i0:i0 + m] += gain * sig[:m]


def add_at(buf, sig, t, sr, gain=1.0):
    add(buf, sig, n_of(t, sr), gain)


def circ_add(buf, sig, i0, gain=1.0):
    """Add sig into a circular buffer starting at sample i0; anything past the end wraps to the start."""
    L = len(buf)
    i, pos = i0 % L, 0
    while pos < len(sig):
        m = min(L - i, len(sig) - pos)
        buf[i:i + m] += gain * sig[pos:pos + m]
        pos += m
        i = 0


def sample_fn(fn, n, sr, step=0.004):
    """Evaluate a (slow) function of time on a coarse grid and interpolate to every sample."""
    tg = np.arange(0.0, n / sr + 2 * step, step)
    return np.interp(tl(n, sr), tg, fn(tg))


def spread_times(rng, period, count, min_gap):
    """count times in [0, period) at least min_gap apart, also across the wrap."""
    for _ in range(4000):
        ts = np.sort(rng.uniform(0.0, period, count))
        gaps = np.diff(np.concatenate([ts, [ts[0] + period]]))
        if gaps.min() >= min_gap:
            return ts
    return (np.arange(count) + rng.uniform(0, 0.3)) * period / count


# ----------------------------------------------------------------------------------------------
# Filters and noise
# ----------------------------------------------------------------------------------------------
def lp_mag(f, fc, order=2):
    return 1.0 / np.sqrt(1.0 + (np.asarray(f, float) / fc) ** (2 * order))


def hp_mag(f, fc, order=2):
    r = (np.maximum(np.asarray(f, float), 0.0) / fc) ** (2 * order)
    return np.sqrt(r / (1.0 + r))


def bp_mag(f, fc, q):
    f = np.maximum(np.asarray(f, float), 1e-3)
    x = q * (f / fc - fc / f)
    return 1.0 / np.sqrt(1.0 + x * x)


def pink_mag(f):
    return 1.0 / np.sqrt(np.maximum(np.asarray(f, float), 20.0))


def brown_mag(f):
    return 1.0 / np.maximum(np.asarray(f, float), 10.0)


def fft_filter(x, sr, magfn):
    """Zero-phase filter by a magnitude response magfn(freqs); padded so nothing wraps around."""
    n = len(x)
    N = 1 << int(math.ceil(math.log2(n + sr // 2)))
    f = np.fft.rfftfreq(N, 1.0 / sr)
    return np.fft.irfft(np.fft.rfft(x, N) * magfn(f), N)[:n]


def circ_filter(x, sr, magfn):
    """Zero-phase circular filter: keeps a periodic signal exactly periodic."""
    n = len(x)
    f = np.fft.rfftfreq(n, 1.0 / sr)
    return np.fft.irfft(np.fft.rfft(x) * magfn(f), n)


def fft_noise(n, sr, rng, magfn):
    """Stationary coloured noise of length n, unit RMS."""
    f = np.fft.rfftfreq(n, 1.0 / sr)
    X = magfn(f) * (rng.standard_normal(len(f)) + 1j * rng.standard_normal(len(f)))
    X[0] = 0.0
    return unit_rms(np.fft.irfft(X, n))


def shaped_noise(n, sr, rng, magfn, win=1024):
    """Time-varying coloured noise: STFT overlap-add of random spectra shaped by magfn(T, F), T a column
    of frame-centre times (s), F a row of frequencies (Hz)."""
    hop = win // 4
    nfr = (n + win) // hop + 2
    freqs = np.fft.rfftfreq(win, 1.0 / sr)
    times = (np.arange(nfr) * hop - win / 2) / sr
    mag = np.broadcast_to(magfn(times[:, None], freqs[None, :]), (nfr, len(freqs)))
    spec = (rng.standard_normal(mag.shape) + 1j * rng.standard_normal(mag.shape)) * mag
    w = 0.5 - 0.5 * np.cos(2.0 * np.pi * np.arange(win) / win)
    frames = np.fft.irfft(spec, win, axis=1) * w * math.sqrt(win / 2.0) / math.sqrt(1.5)
    out = np.zeros(nfr * hop + 2 * win)
    for i in range(nfr):
        out[i * hop:i * hop + win] += frames[i]
    return out[win:win + n]


def band_noise(n, sr, rng, fc, q, amp, win=512):
    """Pink-ish noise through a band-pass whose centre fc(T) and level amp(T) are functions of time."""
    return shaped_noise(n, sr, rng, lambda T, F: amp(T) * bp_mag(F, fc(T), q), win)


def plfo(rng, period, kmin, kmax, slope=0.7):
    """Smooth random curve in [-1, 1] made only of whole cycles per `period` (k = kmin..kmax), so it is
    exactly periodic. Returns a function of time (any array shape)."""
    ks = np.arange(kmin, kmax + 1)
    amps = rng.uniform(0.4, 1.0, len(ks)) / ks ** slope
    phs = rng.uniform(0.0, 2.0 * np.pi, len(ks))

    def raw(t):
        t = np.asarray(t, float)
        out = np.zeros(np.shape(t))
        for k, a, p in zip(ks, amps, phs):
            out = out + a * np.cos(2.0 * np.pi * k * t / period + p)
        return out

    m = float(np.max(np.abs(raw(np.arange(8192) * period / 8192)))) or 1.0
    return lambda t: raw(t) / m


def bumps(period, centers, widths, heights):
    """Periodic sum of Gaussian bumps (for occasional events such as a desert whistle)."""
    def f(T):
        T = np.asarray(T, float)
        out = np.zeros(np.shape(T))
        for c, w, h in zip(centers, widths, heights):
            d = (T - c + period / 2) % period - period / 2
            out = out + h * np.exp(-0.5 * (d / w) ** 2)
        return out
    return f


def xfade_loop(y, L, X):
    """y has L + X samples. Equal-power crossfade the overshoot y[L:] into the head; returns L samples.
    out[0] = y[L], which is what followed y[L-1] = out[L-1] in the long render: the seam is continuous."""
    assert len(y) == L + X
    u = np.arange(X) / X
    out = y[:L].copy()
    out[:X] = y[L:L + X] * np.cos(0.5 * np.pi * u) + y[:X] * np.sin(0.5 * np.pi * u)
    return out


# ----------------------------------------------------------------------------------------------
# Reverb: synthetic room impulse responses with a flattened spectrum
# ----------------------------------------------------------------------------------------------
_IRS = {}


def room_ir(sr, rt60, lp=4000.0, hp=120.0, predelay=0.012):
    """Exponentially decaying noise (RT60 rt60) with a smooth hp/lp target spectrum, flattened by
    alternating projection so tones of different pitch get a similar wet level. Normalised so the
    in-band gain is 1: `mix` is the wet level of a sustained in-band tone."""
    key = (sr, rt60, lp, hp, predelay)
    if key not in _IRS:
        rng = np.random.default_rng(zlib.crc32(repr(key).encode()))
        n = n_of(rt60 * 1.1, sr)
        t = tl(n, sr)
        env = np.exp(-6.91 * t / rt60) * smoothstep(t / 0.02)
        f = np.fft.rfftfreq(n, 1.0 / sr)
        target = hp_mag(f, hp, 1) * lp_mag(f, lp, 2)
        k = np.ones(max(1, n_of(0.004, sr)))
        k /= len(k)
        h = rng.standard_normal(n) * env
        for _ in range(12):
            h = np.fft.irfft(target * np.exp(1j * np.angle(np.fft.rfft(h))), n)
            h = h / (np.sqrt(np.convolve(h * h, k, mode="same")) + 1e-12) * env
        h = np.concatenate([np.zeros(n_of(predelay, sr)), h])
        N = 1 << 18
        H = np.abs(np.fft.rfft(h, N))
        fb = np.fft.rfftfreq(N, 1.0 / sr)
        band = (fb > max(200.0, 2 * hp)) & (fb < min(3000.0, 0.8 * lp))
        _IRS[key] = h / np.sqrt(np.mean(H[band] ** 2))
    return _IRS[key]


def reverb(x, sr, rt60, mix, lp=4000.0, dry=1.0, predelay=0.012):
    ir = room_ir(sr, rt60, lp, predelay=predelay)
    return dry * x + mix * sps.fftconvolve(x, ir)[:len(x)]


def circ_reverb(x, sr, rt60, mix, lp=4000.0, dry=1.0, predelay=0.012):
    """Circular convolution: the tail that rings past the end wraps into the start (stays periodic)."""
    ir = room_ir(sr, rt60, lp, predelay=predelay)
    n = len(x)
    return dry * x + mix * np.fft.irfft(np.fft.rfft(x) * np.fft.rfft(ir, n), n)


# ----------------------------------------------------------------------------------------------
# Instruments (mono float arrays, amplitude ~1 at the strike)
# ----------------------------------------------------------------------------------------------
def modal(f, n, sr, modes, attack, tau, fmax=9000.0, bend=0.0, bend_tau=0.01):
    """Struck object: sum of decaying sine modes [(ratio, amp, tau_multiplier), ...]."""
    t = tl(n, sr)
    bendc = 1.0 + bend * np.exp(-t / bend_tau) if bend else 1.0
    fmax = min(fmax, 0.45 * sr)
    out = np.zeros(n)
    for ratio, amp, tmul in modes:
        fr = f * ratio
        if fr >= fmax:
            continue
        taper = 1.0 if fr < 0.75 * fmax else float(smoothstep((fmax - fr) / (0.25 * fmax)))
        out += amp * taper * np.sin(phase_of(fr * bendc, sr, n)) * perc_env(n, sr, attack, tau * tmul)
    return out * note_tail(n)


def kalimba(f, sr, dur=0.5, tau=0.2):
    return modal(f, n_of(dur, sr), sr, [(1.0, 1.0, 1.0), (2.0, 0.05, 0.4), (6.27, 0.12, 0.10)],
                 attack=0.0015, tau=tau, bend=0.003, bend_tau=0.015)


def marimba(f, sr, dur=0.6, tau=0.25):
    return modal(f, n_of(dur, sr), sr, [(1.0, 1.0, 1.0), (3.99, 0.25, 0.25), (9.95, 0.04, 0.08)],
                 attack=0.003, tau=tau)


def glock(f, sr, dur=1.0, tau=0.5):
    return modal(f, n_of(dur, sr), sr, [(1.0, 1.0, 1.0), (2.76, 0.16, 0.33), (5.40, 0.05, 0.17),
                                        (8.93, 0.015, 0.09)], attack=0.0012, tau=tau, fmax=12000.0)


def glass(f, sr, dur=0.35, tau=0.1):
    return modal(f, n_of(dur, sr), sr, [(1.0, 1.0, 1.0), (2.61, 0.18, 0.45), (4.90, 0.05, 0.25)],
                 attack=0.0008, tau=tau, fmax=13000.0)


def chime(f, sr, dur=0.8, tau=0.45):
    return modal(f, n_of(dur, sr), sr, [(1.0, 1.0, 1.0), (2.0, 0.10, 0.5), (2.76, 0.07, 0.3),
                                        (4.0, 0.03, 0.25), (5.40, 0.015, 0.15)], attack=0.004, tau=tau)


def rod_chime(f, sr, dur=2.5, tau=1.3):
    """Aluminium wind-chime rod: free-free bar modes 1 : 2.756 : 5.404 : 8.933."""
    return modal(f, n_of(dur, sr), sr, [(1.0, 1.0, 1.0), (2.756, 0.22, 0.45), (5.404, 0.07, 0.25),
                                        (8.933, 0.025, 0.15)], attack=0.0012, tau=tau)


def wood_tock(f, sr, rng, dur=0.12, tau=0.022):
    """Wooden block 'tock': short inharmonic body with a pitch drop + a tiny band-passed knock."""
    n = n_of(dur, sr)
    body = modal(f, n, sr, [(1.0, 1.0, 1.0), (2.32, 0.30, 0.55), (4.25, 0.10, 0.3)], attack=0.0008,
                 tau=tau, bend=0.03, bend_tau=0.004)
    knock = fft_filter(rng.standard_normal(n), sr, lambda F: bp_mag(F, 2200.0, 1.2))
    return body + 0.18 * unit_peak(knock * perc_env(n, sr, 0.0004, 0.003))


def bell(f, n, sr, tau=0.6, attack=0.003):
    """Calm 'major' bell: hum, prime, fifth, octave, major tenth, twelfth + two faint inharmonic
    partials; some partials get a quiet twin a fraction of a Hz away (slow beat)."""
    t = tl(n, sr)
    parts = [(0.5, 0.35, 1.7, 0.0), (1.0, 1.0, 1.0, 0.55), (1.5, 0.16, 0.7, 0.8), (2.0, 0.38, 0.55, 1.1),
             (2.5, 0.09, 0.45, 0.0), (3.0, 0.09, 0.35, 1.4), (4.2, 0.035, 0.22, 0.0), (5.4, 0.015, 0.13, 0.0)]
    out = np.zeros(n)
    for ratio, amp, tmul, beat in parts:
        fr = f * ratio
        s = np.sin(2.0 * np.pi * fr * t)
        if beat:
            s = (s + 0.4 * np.sin(2.0 * np.pi * (fr + beat) * t)) / 1.4
        out += amp * s * perc_env(n, sr, attack, tau * tmul)
    return out


def _taper(fk, hmax):
    return smoothstep((hmax - fk) / (0.3 * hmax))


def soft_tone(f0, sr, cutoff, slope=1.4, vib=None, hmax=7000.0, nh=10):
    """Soft harmonic tone with a (possibly time-varying) low-pass brightness `cutoff`."""
    n = len(f0)
    f = f0 * vibrato(n, sr, *vib) if vib else f0
    ph = phase_of(f, sr)
    out = np.zeros(n)
    for k in range(1, nh + 1):
        fk = k * f
        if np.min(fk) >= hmax:
            break
        out += k ** -slope * lp_mag(fk, cutoff, 2) * _taper(fk, hmax) * np.sin(k * ph)
    return out


def soft_note(f, sr, dur, attack=0.006, tau=0.12, cutoff=1800.0, slope=1.6, nh=6, droop=0.0, vib=None):
    n = n_of(dur, sr)
    t = tl(n, sr)
    f0 = f * (1.0 - droop * smoothstep(t / dur))
    return soft_tone(f0, sr, cutoff, slope=slope, nh=nh, vib=vib) * perc_env(n, sr, attack, tau) * note_tail(n)


def pad_note(f, n, sr, rng, env, harmonics=8, slope=1.7, cutoff=2200.0, detune_cents=5.0):
    """Warm held note: two slightly detuned harmonic stacks with soft roll-off and slow vibrato."""
    t = tl(n, sr)
    out = np.zeros(n)
    for d in (-detune_cents, detune_cents):
        fd = f * 2.0 ** (d / 1200.0) * (1.0 + 0.0015 * np.sin(2 * np.pi * 4.8 * t + rng.uniform(0, 6.28)))
        ph = phase_of(fd, sr)
        for k in range(1, harmonics + 1):
            if k * f > 8000.0:
                break
            out += k ** -slope * lp_mag(k * f, cutoff, 2) * np.sin(k * ph + rng.uniform(0, 6.28))
    return 0.5 * out * env


def bubble(sr, dur, f_from, f_to, glide_tau, attack, tau):
    """Bubble / blip: sine whose pitch flies from f_from to f_to (exponential approach)."""
    n = n_of(dur, sr)
    t = tl(n, sr)
    f = f_to + (f_from - f_to) * np.exp(-t / glide_tau)
    return np.sin(phase_of(f, sr)) * perc_env(n, sr, attack, tau) * note_tail(n)


def pluck(f, sr, dur=0.8, tau=0.45, pos=0.18, fmax=9000.0):
    """Harp-like plucked string: harmonics weighted by the pluck position, upper ones die faster."""
    n = n_of(dur, sr)
    t = tl(n, sr)
    out = np.zeros(n)
    for k in range(1, 16):
        if k * f >= fmax:
            break
        a = abs(math.sin(math.pi * k * pos)) / k ** 1.3
        out += a * np.sin(2.0 * np.pi * k * f * t) * np.exp(-t / (tau / (1.0 + 0.45 * (k - 1))))
    return out * smoothstep(t / 0.0015) * note_tail(n)


def rhodes(f, sr, hold, vel=1.0, bright=1.0, rel=0.09):
    """Electric piano: 1:1 FM (index decays from a 'bark' to a mellow body) + a faint two-octave tine,
    slow decay, released after `hold` seconds."""
    n = n_of(hold + rel + 0.02, sr)
    t = tl(n, sr)
    index = bright * (0.55 + 0.45 * vel) * (1.3 * np.exp(-t / 0.18) + 0.32)
    ph = 2.0 * np.pi * f * t
    tone = np.sin(ph + index * np.sin(ph))
    tone += 0.09 * bright * vel * np.sin(4.0 * ph) * np.exp(-t / 0.06)
    env = smoothstep(t / 0.003) * np.exp(-t / 1.7) * (1.0 - smoothstep((t - hold) / rel))
    return tone * env * (0.6 + 0.4 * vel)


def upright(f, sr, hold, vel=1.0, rel=0.06):
    """Pizzicato upright bass: harmonics shaped by the pluck point, upper ones decaying faster, a small
    pitch drop at the attack, released after `hold` seconds."""
    n = n_of(hold + rel + 0.01, sr)
    t = tl(n, sr)
    ph = phase_of(f * (1.0 + 0.012 * np.exp(-t / 0.025)), sr)
    out = np.zeros(n)
    for k in range(1, 12):
        if k * f > 3000.0:
            break
        a = (abs(math.sin(k * math.pi * 0.22)) + 0.15) / k ** 1.1
        out += a * np.sin(k * ph) * np.exp(-t / (0.9 / (1.0 + 0.45 * (k - 1))))
    env = smoothstep(t / 0.004) * (1.0 - smoothstep((t - hold) / rel))
    return out * env * vel


def brush_tap(sr, rng, bright=1.0):
    n = n_of(0.16, sr)
    t = tl(n, sr)
    x = fft_filter(rng.standard_normal(n), sr, lambda F: bp_mag(F, 3200.0, 0.6) * lp_mag(F, 7000.0 * bright, 2)
                   * hp_mag(F, 600.0, 2))
    return unit_peak(x * smoothstep(t / 0.004) * np.exp(-t / 0.045) * note_tail(n, 0.4))


def crackle(sr, rng, fc, dur):
    """One fire crackle: a 1-12 ms noise burst, band-passed around fc."""
    n = n_of(dur * 3.0 + 0.004, sr)
    t = tl(n, sr)
    x = rng.standard_normal(n) * smoothstep(t / 0.0002) * np.exp(-t / (dur / 3.0))
    sos = sps.butter(1, [fc / 1.5, min(fc * 1.5, 0.45 * sr)], btype="bandpass", fs=sr, output="sos")
    y = sps.sosfilt(sos, x) * note_tail(n, 0.3)
    return unit_peak(y)


def crackle_events(rng, sr, t0, t1, rate_fn, rate_max, pop_prob=0.1):
    """Poisson crackles (thinned to rate_fn(t) per second), 1-6 ms, band 1.5-6 kHz; some are bigger,
    lower 'pops' followed by a few tiny crackles. Returns [(time, gain, signal), ...]."""
    ev = []
    cand = np.sort(rng.uniform(t0, t1, rng.poisson(rate_max * (t1 - t0))))
    keep = rng.uniform(size=len(cand)) < np.asarray(rate_fn(cand)) / rate_max
    for t in cand[keep]:
        if rng.uniform() < pop_prob:
            ev.append((t, rng.uniform(1.0, 1.6), crackle(sr, rng, rng.uniform(700, 1500), rng.uniform(0.006, 0.012))))
            for _ in range(int(rng.integers(2, 5))):
                ev.append((t + rng.uniform(0.003, 0.03), rng.uniform(0.2, 0.5),
                           crackle(sr, rng, logu(rng, 1500, 6000), rng.uniform(0.001, 0.003))))
        else:
            ev.append((t, min(rng.lognormal(-0.6, 0.55), 1.2),
                       crackle(sr, rng, logu(rng, 1500, 6000), rng.uniform(0.001, 0.006))))
    return ev


def bird_note(sr, f_pts, dur, fm_rate=0.0, fm_depth=0.0, attack=0.006, h2=0.06):
    """FM bird chirp: a sine on a pitch track with sinusoidal FM (warble) and a faint 2nd harmonic."""
    n = n_of(dur, sr)
    t = tl(n, sr)
    f = glide(n, sr, f_pts)
    if fm_depth:
        f = f * (1.0 + fm_depth * np.sin(2.0 * np.pi * fm_rate * t))
    ph = phase_of(f, sr)
    rel = max(0.01, 0.35 * dur)
    env = smoothstep(t / attack) * (1.0 - smoothstep((t - (dur - rel)) / rel))
    return (np.sin(ph) + h2 * np.sin(2.0 * ph)) * env


# ----------------------------------------------------------------------------------------------
# Ambience layers. Continuous ones get n = L + X samples (crossfaded later); event ones are circular
# buffers of exactly L samples. `period` is the loop length in seconds.
# ----------------------------------------------------------------------------------------------
def wind(n, sr, rng, period, lo=250.0, hi=900.0, q=1.5, gust_db=9.0, lp=2500.0, win=1024):
    """White noise through a band-pass whose centre wanders lo..hi (and rises with each gust), with a
    ~0.3 Hz gust envelope. Returns (unit-RMS noise, gust function in [-1, 1])."""
    gust = plfo(rng, period, max(1, round(0.12 * period)), max(2, round(0.42 * period)), slope=0.5)
    wander = plfo(rng, period, 1, 5)

    def mag(T, F):
        g = gust(T)
        u = np.clip(0.5 + 0.30 * g + 0.25 * wander(T), 0.0, 1.0)
        return db(0.5 * gust_db * g) * bp_mag(F, lo * (hi / lo) ** u, q) * lp_mag(F, lp, 2)
    return unit_rms(shaped_noise(n, sr, rng, mag, win)), gust


def whistle(n, sr, rng, period, presence, lo=1200.0, hi=2000.0, q=20.0):
    """Narrow (Q 20) band of noise wandering lo..hi: wind whistling past a corner. Scaled so the RMS is
    ~1 where presence(T) = 1."""
    wander = plfo(rng, period, 1, 4)
    x = shaped_noise(n, sr, rng, lambda T, F: presence(T) * bp_mag(F, lo * (hi / lo) ** (0.5 + 0.5 * wander(T)), q),
                     win=4096)
    p = sample_fn(presence, n, sr, 0.02)
    return x / math.sqrt(np.mean(x * x) / max(np.mean(p * p), 1e-12))


def cricket_pulse(sr, f):
    n = n_of(0.015, sr)
    u = np.arange(n) / n
    return np.sin(phase_of(f * (1.01 - 0.02 * u), sr)) * np.sin(np.pi * u) ** 2


def cricket_layer(L, sr, rng, period, voices):
    """Each cricket: chirps of 3-5 x 15 ms sine pulses (15 ms apart) every 0.6-1.1 s."""
    buf = np.zeros(L)
    for f, g in voices:
        t_first = t = rng.uniform(0.0, 1.0)
        while t < period + t_first - 0.6:
            for p in range(int(rng.integers(3, 6))):
                circ_add(buf, cricket_pulse(sr, f * rng.uniform(0.997, 1.003)), n_of(t + 0.03 * p, sr),
                         g * rng.uniform(0.8, 1.0))
            t += rng.uniform(0.6, 1.1)
    return buf


def owl_phrase(sr, rng):
    """'hoo ... hoo-hoo hooooo' around G4: sine + faint harmonics + breathy band noise, last hoot droops."""
    f = hz("G4") * rng.uniform(0.97, 1.0)
    notes = [(0.00, 0.30, 0.85), (0.62, 0.14, 0.60), (0.80, 0.16, 0.70), (1.05, 0.62, 1.00)]
    out = np.zeros(n_of(1.05 + 0.62 + 0.1, sr))
    for t0, d, a in notes:
        m = n_of(d + 0.05, sr)
        tt = tl(m, sr)
        f0 = f * (0.97 + 0.03 * smoothstep(tt / 0.05)) * (1.0 - 0.07 * smoothstep((tt - 0.6 * d) / (0.4 * d)))
        f0 = f0 * (1.0 + 0.004 * np.sin(2.0 * np.pi * 5.5 * tt))
        ph = phase_of(f0, sr)
        tone = np.sin(ph) + 0.10 * np.sin(2 * ph) + 0.03 * np.sin(3 * ph)
        br = 0.12 * unit_rms(fft_filter(rng.standard_normal(m), sr, lambda F: bp_mag(F, f, 3.0)))
        env = smoothstep(tt / 0.06) * (1.0 - smoothstep((tt - d + 0.04) / 0.09))
        add_at(out, (tone + br) * env, t0, sr, a)
    return out


def owl_layer(L, sr, rng, period, count=2):
    buf = np.zeros(L)
    for t0 in spread_times(rng, period, count, 7.0):
        circ_add(buf, owl_phrase(sr, rng), n_of(t0, sr), rng.uniform(0.75, 1.0))
    buf = circ_filter(buf, sr, lambda F: lp_mag(F, 1400.0, 2) * hp_mag(F, 150.0, 2))
    return circ_reverb(buf, sr, 1.8, 0.8, lp=1500.0, dry=0.6)  # far away: lots of wet


def waves_layer(n, sr, rng, period, cycles):
    """Wave cycles (sum = period): 2.5 s rise, 0.3 s crash, 4 s hiss, then calm. Pink wash whose low-pass
    sweeps 400 -> 1.2k -> 3k -> 700 -> 400 Hz, a foam hiss (HP 2.5 kHz) after each crash and a low crash
    body (brown noise LP 250 Hz)."""
    assert abs(sum(cycles) - period) < 1e-9
    grid = np.arange(0.0, period, 0.01)
    lev, cut, foam, body = (np.zeros_like(grid) for _ in range(4))
    t0 = 0.0
    for C in cycles:
        m = (grid >= t0) & (grid < t0 + C)
        u = grid[m] - t0
        k = rng.uniform(0.8, 1.0)  # this wave's size
        lv = np.where(u < 2.5, 0.25 + 0.5 * smoothstep(u / 2.5),
             np.where(u < 2.8, 0.75 + 0.25 * smoothstep((u - 2.5) / 0.3),
             np.where(u < 6.8, 0.3 + 0.7 * (1.0 - smoothstep((u - 2.8) / 4.0)) ** 1.5,
                      0.3 - 0.05 * smoothstep((u - 6.8) / (C - 6.8)))))
        lev[m] = 0.25 + k * (lv - 0.25)
        lc = np.where(u < 2.5, math.log(400) + math.log(3.0) * smoothstep(u / 2.5),
             np.where(u < 2.8, math.log(1200) + math.log(2.5) * smoothstep((u - 2.5) / 0.3),
             np.where(u < 6.8, math.log(3000) - math.log(3000 / 700) * smoothstep((u - 2.8) / 4.0),
                      math.log(700) - math.log(700 / 400) * smoothstep((u - 6.8) / (C - 6.8)))))
        cut[m] = np.exp(lc)
        foam[m] = k * smoothstep((u - 2.6) / 0.3) * (1.0 - smoothstep((u - 3.0) / 3.8))
        body[m] = k * smoothstep((u - 2.35) / 0.35) * np.exp(-np.maximum(u - 2.7, 0.0) / 0.6) * (u < 6.0)
        t0 += C

    def P(arr):
        return lambda T: np.interp(T, grid, arr, period=period)
    Lv, Cu, Fo, Bo = P(lev), P(cut), P(foam), P(body)
    wash = shaped_noise(n, sr, rng, lambda T, F: Lv(T) * lp_mag(F, Cu(T), 2) * pink_mag(F) * hp_mag(F, 50, 2))
    fz = shaped_noise(n, sr, rng, lambda T, F: Fo(T) * hp_mag(F, 2500, 2) * lp_mag(F, 7000, 2))
    bd = shaped_noise(n, sr, rng, lambda T, F: Bo(T) * brown_mag(F) * lp_mag(F, 250, 2) * hp_mag(F, 35, 2), 2048)
    return unit_rms(wash) + 0.30 * unit_rms(fz) + 0.40 * unit_rms(bd)


def gull_call(sr, rng, f_hi, f_lo, dur):
    """Band-limited sawtooth 'kee-ow': scoops up to f_hi, glides down to f_lo, slight throat flutter."""
    n = n_of(dur, sr)
    t = tl(n, sr)
    f = glide(n, sr, [(0.0, 0.8 * f_hi), (0.05, f_hi), (0.85 * dur, f_lo)])
    f = f * (1.0 + 0.01 * np.sin(2.0 * np.pi * rng.uniform(26, 34) * t))
    ph = phase_of(f, sr)
    out = np.zeros(n)
    for k in range(1, 12):
        fk = k * f
        if fk.max() >= 8000.0:
            break
        out += np.sin(k * ph) / k ** 1.3 * lp_mag(fk, 3000.0, 2)
    return out * env_pts(n, sr, [(0.0, 0.0), (0.03, 1.0), (0.55 * dur, 0.85), (dur, 0.0)])


def gull_layer(L, sr, rng, period, count=2):
    buf = np.zeros(L)
    for t0 in spread_times(rng, period, count, 7.0):
        t = t0
        for i in range(int(rng.integers(2, 4))):
            d = rng.uniform(0.32, 0.42)
            circ_add(buf, gull_call(sr, rng, 1400 * rng.uniform(0.95, 1.03), 900 * rng.uniform(0.95, 1.05), d),
                     n_of(t, sr), rng.uniform(0.7, 1.0) * 0.85 ** i)
            t += d + rng.uniform(0.12, 0.25)
    buf = circ_filter(buf, sr, lambda F: lp_mag(F, 3500.0, 2) * hp_mag(F, 400.0, 2))
    return circ_reverb(buf, sr, 1.4, 0.6, lp=3000.0, dry=0.7)


def chime_layer(L, sr, rng, period, clusters=2):
    pool = ["C6", "D6", "E6", "G6", "A6"]
    buf = np.zeros(L)
    for t0 in spread_times(rng, period, clusters, 6.0):
        for t in np.sort(rng.uniform(0.0, 1.6, int(rng.integers(3, 7)))):
            circ_add(buf, rod_chime(hz(pool[rng.integers(len(pool))]), sr, 2.6, tau=rng.uniform(1.0, 1.6)),
                     n_of(t0 + t, sr), rng.uniform(0.35, 1.0))
    buf = circ_filter(buf, sr, lambda F: lp_mag(F, 7000.0, 2))
    return circ_reverb(buf, sr, 1.2, 0.3, lp=5000.0)


def beep_layer(L, sr, rng, period, count=3):
    motifs = [[("G5", 0.0), ("C6", 0.11)], [("A5", 0.0)], [("E6", 0.0), ("C6", 0.10), ("E6", 0.20)],
              [("D6", 0.0), ("G5", 0.12)]]
    buf = np.zeros(L)
    for i, t0 in enumerate(spread_times(rng, period, count, 5.0)):
        for nm, dt in motifs[(i + int(rng.integers(len(motifs)))) % len(motifs)]:
            circ_add(buf, soft_note(hz(nm), sr, 0.14, attack=0.004, tau=0.05, cutoff=3000.0, slope=2.0, nh=3),
                     n_of(t0 + dt, sr))
    return circ_reverb(buf, sr, 0.7, 0.4, lp=4000.0)


def bubble_layer(L, sr, rng, period, streams=5, singles=26):
    """Rising-sine bubbles, 300-1200 Hz, 20-60 ms: a few streams that climb in pitch + scattered singles."""
    buf = np.zeros(L)

    def one(f0, gain, t):
        d = rng.uniform(0.02, 0.06)
        f0 = min(max(f0, 300.0), 900.0)
        circ_add(buf, bubble(sr, d * 2.5, f0, min(f0 * rng.uniform(1.2, 1.5), 1200.0), d / 2.5, 0.0015, d / 2.5),
                 n_of(t, sr), gain)
    for t0 in spread_times(rng, period, streams, 2.5):
        k = int(rng.integers(5, 14))
        span = rng.uniform(0.3, 1.1)
        fb = rng.uniform(320, 600)
        for tt in np.sort(rng.uniform(0.0, span, k)):
            one(fb * (1.0 + 0.6 * tt / span) * rng.uniform(0.9, 1.1), rng.uniform(0.4, 1.0), t0 + tt)
    for tt in rng.uniform(0.0, period, singles):
        one(rng.uniform(300, 900), rng.uniform(0.2, 0.6), tt)
    return buf


def rain_drops(n, sr, rng, gust, rate=220.0):
    """Rain on glass: Poisson drops (density follows the wind gusts) rendered as tiny decaying ticks
    1.9-5 kHz (five kernels), random lognormal sizes (capped, so no single drop pokes out), HP 900 Hz."""
    dur = n / sr
    cand = np.sort(rng.uniform(0.0, dur, rng.poisson(rate * 1.5 * dur)))
    times = cand[rng.uniform(size=len(cand)) < (1.0 + 0.45 * gust(cand)) / 1.5]
    amps = np.minimum(rng.lognormal(0.0, 0.45, len(times)), 2.2)
    idx = np.minimum((times * sr).astype(int), n - 1)
    cls = rng.integers(0, 5, len(times))
    out = np.zeros(n)
    for c, f in enumerate((1900.0, 2500.0, 3200.0, 4100.0, 5000.0)):
        tau = rng.uniform(0.0007, 0.0014) * math.sqrt(2500.0 / f)
        m = n_of(tau * 7 + 0.001, sr)
        tt = tl(m, sr)
        k = np.sin(2.0 * np.pi * f * tt * (1.0 + 0.08 * np.exp(-tt / 0.0005))) * np.exp(-tt / tau) * smoothstep(tt / 0.00025)
        imp = np.zeros(n)
        np.add.at(imp, idx[cls == c], amps[cls == c])
        out += sps.fftconvolve(imp, k)[:n]
    return fft_filter(out, sr, lambda F: hp_mag(F, 900.0, 2) * lp_mag(F, 7000.0, 2))


def drip_layer(L, sr, rng, period):
    """Slow drips off a ledge: small rising 'plip' bubbles 1.3-2.3 kHz every 0.7-2.2 s."""
    buf = np.zeros(L)
    t_first = t = rng.uniform(0.0, 1.0)
    while t < period + t_first - 0.8:
        f0 = rng.uniform(1300, 2300)
        circ_add(buf, bubble(sr, 0.06, f0, f0 * 1.25, 0.008, 0.001, 0.012), n_of(t, sr), rng.uniform(0.3, 1.0))
        t += rng.uniform(0.7, 2.2)
    return buf


def car_pass(sr, rng, dur=7.0):
    """Distant car on a wet road: tyre-hiss band (centre falls ~25% as it passes) + faint engine rumble."""
    n = n_of(dur, sr)
    tc, w = dur / 2, rng.uniform(1.0, 1.4)

    def env(T):
        return np.exp(-0.5 * ((T - tc) / w) ** 2)
    hiss = shaped_noise(n, sr, rng, lambda T, F: env(T) * bp_mag(F, 950.0 * (1 - 0.22 * np.tanh((T - tc) / 0.9)), 0.8)
                        * lp_mag(F, 3500.0, 2) * hp_mag(F, 200.0, 2))
    eng = fft_noise(n, sr, rng, lambda F: brown_mag(F) * lp_mag(F, 110.0, 2) * hp_mag(F, 30.0, 2)) * sample_fn(env, n, sr)
    t = tl(n, sr)
    return (unit_peak(hiss) + 0.25 * unit_peak(eng)) * smoothstep(t / 0.3) * note_tail(n, 0.1)


BIRD_MOTIFS = [["G6", "C7", "E7"], ["E7", "D7", "C7", "G6"], ["C7", "E7", "G7", "E7"], ["A6", "C7", "D7", "C7"],
               ["G7", "E7", "G7"]]


def bird_phrase(sr, rng, species):
    parts = []
    if species == 0:  # warbler: trill of down-slurred FM chirps
        k, ftop, gap = int(rng.integers(5, 9)), rng.uniform(3600, 4400), rng.uniform(0.065, 0.085)
        for i in range(k):
            f0 = ftop * (1.0 - 0.015 * i)
            parts.append((i * gap, bird_note(sr, [(0.0, f0), (0.045, 0.66 * f0)], 0.05, rng.uniform(45, 70), 0.012),
                          1.0 - 0.05 * i))
    elif species == 1:  # whistler: a short tune in C pentatonic
        t = 0.0
        for nm in BIRD_MOTIFS[rng.integers(len(BIRD_MOTIFS))]:
            d, f = rng.uniform(0.10, 0.18), hz(nm)
            parts.append((t, bird_note(sr, [(0.0, 0.94 * f), (0.025, f), (d, 1.01 * f)], d, 9.0, 0.006),
                          rng.uniform(0.8, 1.0)))
            t += d + rng.uniform(0.03, 0.06)
    else:  # 'fee-bee'
        parts.append((0.0, bird_note(sr, [(0.0, hz("E7")), (0.28, 0.99 * hz("E7"))], 0.28), 1.0))
        parts.append((0.36, bird_note(sr, [(0.0, 1.01 * hz("C7")), (0.32, 0.985 * hz("C7"))], 0.32), 0.85))
    out = np.zeros(n_of(max(t0 + len(s) / sr for t0, s, _ in parts), sr) + 1)
    for t0, s, g in parts:
        add_at(out, s, t0, sr, g)
    return out


def bird_layer(L, sr, rng, period, count=7):
    buf = np.zeros(L)
    for i, t0 in enumerate(spread_times(rng, period, count, 2.2)):
        circ_add(buf, bird_phrase(sr, rng, (i + int(rng.integers(0, 2))) % 3), n_of(t0, sr), rng.uniform(0.45, 1.0))
    buf = circ_filter(buf, sr, lambda F: lp_mag(F, 7500.0, 2) * hp_mag(F, 1000.0, 2))
    return circ_reverb(buf, sr, 1.0, 0.3, lp=6000.0)


def bee_flyby(sr, rng, dur):
    """A bee drifting past: ~230 Hz buzz (harmonic stack, 1/k^1.15), pitch jitter, a Doppler dip as it
    passes, Gaussian level envelope, LP 1.8 kHz."""
    n = n_of(dur, sr)
    t = tl(n, sr)
    tc, f0 = dur / 2, rng.uniform(215, 245)
    knots = np.arange(0.0, dur + 0.3, 0.15)
    jit = np.interp(t, knots, rng.normal(0.0, 1.0, len(knots)))
    ph = phase_of(f0 * (1.0 - 0.03 * np.tanh((t - tc) / 0.7)) * (1.0 + 0.012 * jit), sr)
    tone = np.zeros(n)
    for k in range(1, 16):
        if k * f0 > 3500.0:
            break
        tone += np.sin(k * ph) / k ** 1.15
    env = np.exp(-0.5 * ((t - tc) / (dur / 5.5)) ** 2) * smoothstep(t / 0.1) * note_tail(n, 0.15)
    am = 1.0 + 0.12 * np.sin(2.0 * np.pi * rng.uniform(9, 14) * t)
    return fft_filter(tone * env * am, sr, lambda F: lp_mag(F, 1800.0, 2) * hp_mag(F, 150.0, 2))


def bee_layer(L, sr, rng, period, count=3):
    buf = np.zeros(L)
    for t0 in spread_times(rng, period, count, 5.0):
        circ_add(buf, bee_flyby(sr, rng, rng.uniform(3.5, 5.0)), n_of(t0, sr), rng.uniform(0.6, 1.0))
    return buf


# ----------------------------------------------------------------------------------------------
# Jazz trio (Rhodes, upright bass, brushes), 90 BPM, swung 8ths, rendered circularly
# ----------------------------------------------------------------------------------------------
VOICINGS = {  # rootless left-hand voicings, F3..G4
    "Dm9": ["F3", "A3", "C4", "E4"],
    "G13": ["F3", "A3", "B3", "E4"],
    "Cmaj9": ["E3", "G3", "B3", "D4"],
    "Am9": ["G3", "B3", "C4", "E4"],
    "C69": ["E3", "A3", "D4", "G4"],
}
# bar = ([(chord, start beat, beats), ...], 4 walking-bass quarter notes)
BED_BARS = [  # 9 bars = 36 beats = 24 s: ii-V-I in C, three times with different walking lines
    ([("Dm9", 0, 4)], ["D2", "E2", "F2", "F#2"]),
    ([("G13", 0, 4)], ["G2", "F2", "D2", "Db2"]),
    ([("Cmaj9", 0, 4)], ["C2", "E2", "G2", "E2"]),
    ([("Dm9", 0, 4)], ["D2", "F2", "A2", "Ab2"]),
    ([("G13", 0, 4)], ["G1", "B1", "D2", "Db2"]),
    ([("Cmaj9", 0, 4)], ["C2", "G2", "E2", "Eb2"]),
    ([("Dm9", 0, 4)], ["D2", "C3", "A2", "F2"]),
    ([("G13", 0, 4)], ["G2", "A2", "B2", "D3"]),
    ([("Cmaj9", 0, 4)], ["C3", "B2", "G2", "E2"]),
]
JUKE_BARS = [  # 6 bars = 24 beats = 16 s: | Dm9 | G13 | Cmaj9 | Am9 | Dm9 G13 | C6/9 |
    ([("Dm9", 0, 4)], ["D2", "E2", "F2", "F#2"]),
    ([("G13", 0, 4)], ["G2", "B2", "D3", "Db3"]),
    ([("Cmaj9", 0, 4)], ["C3", "B2", "A2", "G2"]),
    ([("Am9", 0, 4)], ["A2", "G2", "E2", "Eb2"]),
    ([("Dm9", 0, 2), ("G13", 2, 2)], ["D2", "A2", "G2", "Db2"]),
    ([("C69", 0, 4)], ["C2", "E2", "G2", "C#2"]),
]
COMP4 = [[(0.0, 1.5, 0.9), (1.667, 1.2, 0.72)],                  # (beat offset, hold beats, velocity)
         [(0.0, 0.9, 0.85), (1.667, 0.7, 0.7), (2.667, 1.2, 0.75)],
         [(-0.333, 1.8, 0.85), (2.667, 1.0, 0.65)],               # anticipation on the swung 'and' of 4
         [(0.0, 3.4, 0.8)]]
COMP2 = [[(0.0, 0.9, 0.8), (1.667, 0.3, 0.6)], [(0.0, 1.8, 0.8)]]


def jazz_trio(sr, rng, bars, bpm, bright):
    """Returns dict of circular stems (bass, keys, taps, swish), each exactly len(bars)*4 beats long."""
    beat = 60.0 / bpm
    nbeats = 4 * len(bars)
    period = nbeats * beat
    L = n_of(period, sr)
    X = n_of(XFADE_SEC, sr)

    def at(b):
        return n_of(b * beat, sr)
    bass, keys, taps = np.zeros(L), np.zeros(L), np.zeros(L)
    for i, (_, line) in enumerate(bars):
        for j, nm in enumerate(line):
            vel = rng.uniform(0.82, 0.95) * (1.0 if j % 2 else 0.95)
            circ_add(bass, upright(hz_any(nm), sr, 0.9 * beat, vel), at(4 * i + j) + n_of(rng.normal(0, 0.006), sr))
    hits = []
    for i, (chords, _) in enumerate(bars):
        for name, s, B in chords:
            pats = COMP4 if B == 4 else COMP2
            for off, hold, vel in pats[int(rng.integers(len(pats)))]:
                hits.append(((4 * i + s + off) % nbeats, hold, vel, name))
    hits.sort()
    for k, (b0, hold, vel, name) in enumerate(hits):
        nxt = hits[(k + 1) % len(hits)][0] + (nbeats if k + 1 == len(hits) else 0)
        hold = max(0.2, min(hold, nxt - b0 - 0.08))
        for j, nm in enumerate(VOICINGS[name]):
            circ_add(keys, rhodes(hz_c(nm), sr, hold * beat, vel * rng.uniform(0.9, 1.0), bright),
                     at(b0) + n_of(0.007 * j + rng.normal(0, 0.003), sr))
    t = tl(L, sr)
    keys *= 1.0 - 0.12 * (0.5 + 0.5 * np.sin(2.0 * np.pi * round(4.6 * period) * t / period))  # tremolo
    for i in range(len(bars)):
        pattern = [(1.0, 0.9), (3.0, 0.95), (3.667, 0.35)] + ([(1.667, 0.22)] if rng.uniform() < 0.5 else [])
        for off, v in pattern:  # taps on 2 and 4 + swung ghost notes
            circ_add(taps, brush_tap(sr, rng, bright) * v, at(4 * i + off) + n_of(rng.normal(0, 0.004), sr))

    def sw_mag(T, F):  # circular stir, swelling into beats 2 and 4
        env = 0.5 + 0.5 * (0.5 - 0.5 * np.cos(np.pi * ((T / beat) % 2.0)))
        return env * bp_mag(F, 3000.0, 0.5) * lp_mag(F, 6500.0 * bright, 2) * hp_mag(F, 500.0, 2)
    swish = unit_rms(xfade_loop(shaped_noise(L + X, sr, rng, sw_mag, 512), L, X))
    return dict(bass=unit_peak(bass), keys=unit_peak(keys), taps=unit_peak(taps), swish=swish, L=L, beat=beat)


# ----------------------------------------------------------------------------------------------
# Loop recipes: fn(rng, sr, period, L, X) -> L samples (or (samples, fixed start sample))
# ----------------------------------------------------------------------------------------------
def a_weight(f):
    f2 = np.maximum(np.asarray(f, float), 1.0) ** 2
    ra = 12194.0 ** 2 * f2 ** 2 / ((f2 + 20.6 ** 2) * np.sqrt((f2 + 107.7 ** 2) * (f2 + 737.9 ** 2)) * (f2 + 12194.0 ** 2))
    return ra / 0.7943  # 0 dB at 1 kHz


def mix_layers(name, layers, sr=SR_LOOP):
    """Sum the layers. With WORLDECK_SND_DEBUG set, print each layer's A-weighted RMS and its loudest
    100 ms (A-weighted) relative to the A-weighted RMS of the whole mix (a rough audibility guide)."""
    total = sum(x for _, x in layers)
    if DEBUG:
        def aw(x):
            return circ_filter(x, sr, a_weight)
        ta, tr = rms(aw(total)), rms(total)
        w = n_of(0.1, sr)
        parts = []
        for lab, x in layers:
            xa = aw(x)
            p = np.convolve(xa * xa, np.ones(w) / w, mode="valid")
            parts.append(f"{lab} {20 * math.log10(rms(xa) / ta + 1e-12):+.1f}/{10 * math.log10(p.max() / ta ** 2 + 1e-20):+.1f}"
                         f"/pk{20 * math.log10(np.max(np.abs(x)) / tr + 1e-12):+.0f}")
        print(f"  [{name}] " + ", ".join(parts) + f"  (dB(A) re mix: RMS/max100ms/peak re mix RMS; mix crest "
              f"{20 * math.log10(np.max(np.abs(total)) / tr):.1f} dB)")
    return total


def loop_forest(rng, sr, period, L, X):
    n = L + X
    # Gentler gusts, and no owl: on the headset the night forest read as creepy (hoots with a long echo in the dark).
    w, gust = wind(n, sr, rng, period, lo=220.0, hi=700.0, q=1.4, gust_db=5.0, lp=1800.0)
    leaves = shaped_noise(n, sr, rng, lambda T, F: (0.25 + 0.75 * smoothstep((gust(T) + 0.2) / 1.2))
                          * hp_mag(F, 2000.0, 2) * lp_mag(F, 6000.0, 2), 512)
    cont = xfade_loop(unit_rms(w) + db(-16) * unit_rms(leaves), L, X)
    cr = cricket_layer(L, sr, rng, period, [(4500.0, 1.0), (4250.0, 0.5), (4750.0, 0.35)])
    cr = circ_reverb(circ_filter(cr, sr, lambda F: lp_mag(F, 7000.0, 2)), sr, 0.9, 0.35, lp=5000.0)
    return mix_layers("forest", [("wind", cont), ("crickets", db(5) * unit_peak(cr))])


def loop_beach(rng, sr, period, L, X):
    n = L + X
    wv = waves_layer(n, sr, rng, period, [7.6, 9.0, 7.4])
    w, _ = wind(n, sr, rng, period, lo=400.0, hi=1200.0, q=1.2, gust_db=6.0, lp=3000.0)
    cont = xfade_loop(unit_rms(wv), L, X)
    wl = xfade_loop(w, L, X)
    gulls = gull_layer(L, sr, rng, period)
    return mix_layers("beach", [("waves", cont), ("wind", db(-12) * wl), ("gulls", db(3) * unit_peak(gulls))])


def loop_desert(rng, sr, period, L, X):
    n = L + X
    w, gust = wind(n, sr, rng, period, lo=250.0, hi=900.0, q=1.5, gust_db=10.0, lp=2200.0)
    sand = shaped_noise(n, sr, rng, lambda T, F: smoothstep((gust(T) + 0.1) / 1.1) ** 1.5
                        * hp_mag(F, 3000.0, 2) * lp_mag(F, 7000.0, 2))
    c = spread_times(rng, period, 2, 8.0)
    wh = whistle(n, sr, rng, period, bumps(period, c, [1.6, 2.2], [1.0, 0.7]))
    cont = xfade_loop(unit_rms(w) + db(-17) * unit_rms(sand), L, X)
    whl = xfade_loop(wh, L, X)
    ch = chime_layer(L, sr, rng, period)
    return mix_layers("desert", [("wind", cont), ("whistle", db(-9) * whl), ("chimes", db(3) * unit_peak(ch))])


def loop_snow(rng, sr, period, L, X):
    n = L + X
    w, gust = wind(n, sr, rng, period, lo=200.0, hi=650.0, q=1.3, gust_db=8.0, lp=1600.0)
    buffet = fft_noise(n, sr, rng, lambda F: brown_mag(F) * lp_mag(F, 120.0, 2) * hp_mag(F, 30.0, 2))
    buffet *= sample_fn(lambda T: 0.3 + 0.7 * smoothstep((gust(T) + 0.3) / 1.2), n, sr)
    wh = whistle(n, sr, rng, period, lambda T: 0.3 + 0.7 * smoothstep((gust(T) + 0.2) / 1.1))
    cont = xfade_loop(unit_rms(w) + db(-8) * unit_rms(buffet), L, X)
    whl = xfade_loop(wh, L, X)
    return mix_layers("snow", [("wind", cont), ("whistle", db(-13) * whl)])


def loop_space(rng, sr, period, L, X):
    n = L + X
    t = tl(L, sr)
    hum = np.zeros(L)
    for f, a, beat_hz in [(55.0, 1.0, 0.125), (110.0, 0.8, 1.0 / 12.0), (165.0, 0.6, 0.25)]:
        f1, f2 = round(f * period) / period, round((f + beat_hz) * period) / period  # whole cycles per loop
        hum += a * (np.sin(2 * np.pi * f1 * t + rng.uniform(0, 6.28)) + 0.35 * np.sin(2 * np.pi * f2 * t + rng.uniform(0, 6.28)))
    swell = plfo(rng, period, 1, 3)
    hum *= sample_fn(lambda T: db(1.5 * swell(T)), L, sr)
    vm = plfo(rng, period, 1, 6)
    vent = shaped_noise(n, sr, rng, lambda T, F: db(1.5 * vm(T)) * pink_mag(F) * hp_mag(F, 150.0, 2) * lp_mag(F, 1800.0, 2))
    air = shaped_noise(n, sr, rng, lambda T, F: db(2.0 * vm(T)) * bp_mag(F, 650.0, 1.6))
    cont = xfade_loop(unit_rms(vent) + db(-8) * unit_rms(air), L, X)
    beeps = beep_layer(L, sr, rng, period)
    return mix_layers("space", [("vent", cont), ("hum", db(-3) * unit_rms(hum)), ("beeps", db(5) * unit_peak(beeps))])


def loop_reef(rng, sr, period, L, X):
    n = L + X
    rumble = fft_noise(n, sr, rng, lambda F: brown_mag(F) * lp_mag(F, 70.0, 2) * hp_mag(F, 18.0, 2))
    surge = plfo(rng, period, 2, 6)
    wash = shaped_noise(n, sr, rng, lambda T, F: db(4.0 * surge(T)) * pink_mag(F) * lp_mag(F, 500.0, 2) * hp_mag(F, 60.0, 2))
    cont = xfade_loop(unit_rms(rumble) + db(-2) * unit_rms(wash), L, X)
    t = tl(L, sr)
    tones = np.sin(2 * np.pi * 41.0 * t) + 0.7 * np.sin(2 * np.pi * 55.0 * t + 1.3)  # 984 / 1320 cycles per loop
    bub = circ_filter(bubble_layer(L, sr, rng, period), sr, lambda F: lp_mag(F, 900.0, 2))
    bub = circ_reverb(bub, sr, 0.6, 0.3, lp=1200.0)
    return mix_layers("reef", [("rumble+wash", cont), ("41/55 Hz", db(-6) * unit_rms(tones)),
                               ("bubbles", db(-1) * unit_peak(bub))])


def wall_mag(F):
    """'Through the wall': LP 1.2 kHz, a little low-mid body from the wall, no deep sub."""
    return lp_mag(F, 1200.0, 2) * hp_mag(F, 45.0, 2) * (1.0 + 0.6 * bp_mag(F, 150.0, 1.0))


def loop_city_jazz(rng, sr, period, L, X):
    n = L + X
    gust = plfo(rng, period, 2, 8, 0.5)
    drops = rain_drops(n, sr, rng, gust)
    wash = fft_noise(n, sr, rng, lambda F: pink_mag(F) * hp_mag(F, 1000.0, 2) * lp_mag(F, 6000.0, 2))
    wash *= sample_fn(lambda T: db(2.0 * gust(T)), n, sr)
    rain = xfade_loop(unit_rms(drops) + db(-7) * wash, L, X)
    drips = drip_layer(L, sr, rng, period)
    cars = np.zeros(L)
    for t0 in spread_times(rng, period, 2, 9.0):
        circ_add(cars, car_pass(sr, rng), n_of(t0, sr), rng.uniform(0.7, 1.0))
    m = jazz_trio(sr, rng, BED_BARS, JAZZ_BPM, bright=1.0)
    assert m["L"] == L
    music = m["bass"] * 1.0 + m["keys"] * 0.55 + m["taps"] * 0.30 + m["swish"] * 0.06
    music = circ_reverb(circ_filter(music, sr, wall_mag), sr, 0.8, 0.35, lp=1500.0)
    return mix_layers("city_jazz", [("rain", rain), ("drips", db(7) * unit_peak(drips)),
                                    ("cars", db(6) * unit_peak(cars)), ("music", db(6) * unit_rms(music))])


def loop_meadow(rng, sr, period, L, X):
    n = L + X
    w, gust = wind(n, sr, rng, period, lo=350.0, hi=1000.0, q=1.2, gust_db=6.0, lp=2600.0)
    flutter = plfo(rng, period, 40, 160, 0.3)
    leaves = shaped_noise(n, sr, rng, lambda T, F: (0.2 + 0.8 * smoothstep((gust(T) + 0.3) / 1.2))
                          * (0.55 + 0.45 * flutter(T)) * hp_mag(F, 1800.0, 2) * lp_mag(F, 6500.0, 2), 512)
    drone = shaped_noise(n, sr, rng, lambda T, F: bp_mag(F, 230.0, 8.0) + 0.5 * bp_mag(F, 460.0, 8.0)
                         + 0.25 * bp_mag(F, 690.0, 8.0) + 0 * T, 4096)
    cont = xfade_loop(unit_rms(w) + db(-12) * unit_rms(leaves) + db(-18) * unit_rms(drone), L, X)
    birds = bird_layer(L, sr, rng, period)
    bees = bee_layer(L, sr, rng, period)
    return mix_layers("meadow", [("breeze", cont), ("birds", db(1) * unit_peak(birds)), ("bees", db(4) * unit_peak(bees))])


def loop_fire(rng, sr, period, L, X):
    """Farnell-style fire from one noise family: roar (brown LP 150 Hz, flickering), lapping (band
    noise ~420 Hz with 1-9 Hz flutter), hiss (HP 3 kHz, -24 dB), crackles (Poisson 4-12/s, 1-6 ms,
    BP 1.5-6 kHz) with occasional bigger pops."""
    n = L + X
    flick = plfo(rng, period, 6, 40, slope=0.4)
    slow = plfo(rng, period, 1, 4)
    roar = fft_noise(n, sr, rng, lambda F: brown_mag(F) * lp_mag(F, 150.0, 2) * hp_mag(F, 28.0, 2))
    roar *= sample_fn(lambda T: db(2.5 * flick(T) + 1.5 * slow(T)), n, sr)
    lm = plfo(rng, period, 15, 110, slope=0.3)
    lap = fft_noise(n, sr, rng, lambda F: bp_mag(F, 420.0, 0.8) * lp_mag(F, 1400.0, 2))
    lap *= sample_fn(lambda T: (0.35 + 0.65 * (0.5 + 0.5 * lm(T))) ** 2 * db(2.0 * slow(T)), n, sr)
    hiss = fft_noise(n, sr, rng, lambda F: hp_mag(F, 3000.0, 2) * lp_mag(F, 9000.0, 2))
    hiss *= sample_fn(lambda T: db(3.0 * flick(T)), n, sr)
    cont = xfade_loop(unit_rms(roar) + db(-5) * unit_rms(lap) + db(-24) * unit_rms(hiss), L, X)
    rate = plfo(rng, period, 1, 5)
    cr = np.zeros(L)
    for t, a, sig in crackle_events(rng, sr, 0.0, period, lambda T: 8.0 + 4.0 * rate(T), 12.0):
        circ_add(cr, sig, n_of(t, sr), a)
    cr = circ_filter(cr, sr, lambda F: lp_mag(F, 8000.0, 2))
    return mix_layers("fire", [("roar+lap+hiss", cont), ("crackles", db(14) * unit_peak(cr))])


def loop_jukebox(rng, sr, period, L, X):
    m = jazz_trio(sr, rng, JUKE_BARS, JAZZ_BPM, bright=1.35)
    assert m["L"] == L
    music = m["bass"] * 0.9 + m["keys"] * 0.62 + m["taps"] * 0.34 + m["swish"] * 0.10
    music = circ_filter(music, sr, lambda F: lp_mag(F, 5500.0, 2) * hp_mag(F, 38.0, 2) * (1.0 + 0.25 * bp_mag(F, 120.0, 1.0)))
    music = circ_reverb(music, sr, 0.7, 0.16, lp=4000.0)
    return music, L - n_of(0.5 * m["beat"], sr)  # file starts half a beat before bar 1 (pickup)



# ----------------------------------------------------------------------------------------------
# Music: "Ember, play some music" (our own tunes; each loops seamlessly in 16 s)
# ----------------------------------------------------------------------------------------------
def _picked(b, sr, rng, notes, t, voice, gain, human=0.01):
    for k, (nm, dt, g) in enumerate(notes):
        circ_add(b, voice(hz_any(nm)), n_of(t + dt + rng.uniform(-human, human), sr), gain * g * rng.uniform(0.85, 1.0))


def loop_music_campfire(rng, sr, period, L, X):
    """Fingerpicked guitar by the fire, 60 BPM, G | Em | C | D, a little melody on the top string."""
    b = np.zeros(L)
    chords = [("G2", "D3", "G3", "B3", "D4"), ("E2", "B2", "E3", "G3", "B3"), ("C3", "G3", "C4", "E4", "G4"), ("D3", "A3", "D4", "F#4", "A4")]
    pattern = [0, 2, 1, 3, 0, 2, 1, 4]   # thumb on the bass, fingers above (eighth notes)
    voice = lambda f: pluck(f, sr, 1.6, tau=0.7, pos=0.2, fmax=6500.0)
    for bar, ch in enumerate(chords):
        t0 = bar * 4.0
        notes = [(ch[i], k * 0.5, 1.0 if k % 2 == 0 else 0.62) for k, i in enumerate(pattern)]
        _picked(b, sr, rng, notes, t0, voice, 0.5)
    melody = [("B4", 0.0), ("A4", 1.5), ("G4", 2.0), ("B4", 4.0), ("D5", 5.5), ("B4", 6.0), ("E5", 8.0), ("D5", 9.5),
              ("C5", 10.0), ("A4", 12.0), ("F#4", 13.5), ("D4", 14.0)]
    for nm, t in melody:
        circ_add(b, pluck(hz_any(nm), sr, 1.4, tau=0.55, pos=0.3, fmax=7000.0), n_of(t + rng.uniform(-0.01, 0.01), sr), 0.34)
    return circ_reverb(b, sr, 1.1, 0.22, lp=3800.0)


def loop_music_beach(rng, sr, period, L, X):
    """Island marimba at sunset, 120 BPM, C | Am | F | G twice, with a soft wood shaker on the off-beats."""
    b = np.zeros(L)
    chords = [("C4", "E4", "G4", "C5"), ("A3", "C4", "E4", "A4"), ("F3", "A3", "C4", "F4"), ("G3", "B3", "D4", "G4")] * 2
    pattern = [0, 2, 1, 2, 3, 2, 1, 2]
    for bar, ch in enumerate(chords):
        t0 = bar * 2.0
        for k, i in enumerate(pattern):
            circ_add(b, marimba(hz_any(ch[i]), sr, 0.7, tau=0.26), n_of(t0 + k * 0.25 + rng.uniform(-0.006, 0.006), sr),
                     0.42 * (1.0 if k % 2 == 0 else 0.7) * rng.uniform(0.85, 1.0))
        circ_add(b, marimba(hz_any(ch[0][:-1] + "2"), sr, 0.9, tau=0.4), n_of(t0, sr), 0.38)
        for k in range(4):
            circ_add(b, wood_tock(hz("G5"), sr, rng), n_of(t0 + k * 0.5 + 0.25, sr), 0.07)
    return circ_reverb(b, sr, 0.9, 0.18, lp=4200.0)


def loop_music_calm(rng, sr, period, L, X):
    """Calm, for any world and your room: 60 BPM in F major, Fmaj9 | Dm9 | Bbmaj9 | C6sus, four times round
    (64 s, so it is not heard repeating). Warm pads that swell in slowly, a soft low root, and a gentle electric-
    piano melody from the major pentatonic that changes each time round and rests in between. Nothing sudden: every
    note is soft at the attack, no drums, no minor turns, long reverb."""
    b = np.zeros(L)
    chords = [("F2", "C3", "A3", "E4", "G4"), ("D2", "A2", "F3", "C4", "E4"), ("Bb1", "F2", "D3", "A3", "C4"), ("C2", "G2", "F3", "A3", "D4")]
    n4 = n_of(4.6, sr)
    for rnd in range(4):
        for bar, ch in enumerate(chords):
            t0 = (rnd * 4 + bar) * 4.0
            env = env_pts(n4, sr, [(0.0, 0.0), (1.6, 1.0), (3.8, 0.9), (4.6, 0.0)])
            for nm in ch[1:]:
                circ_add(b, pad_note(hz_any(nm), n4, sr, rng, env, cutoff=1300.0, detune_cents=4.0), n_of(t0, sr), 0.11)
            # The root, low and soft, a little after the pad (a heartbeat, not a bass line).
            circ_add(b, pad_note(hz_any(ch[0]), n4, sr, rng, env, harmonics=4, slope=2.2, cutoff=400.0), n_of(t0 + 0.05, sr), 0.16)
    # A melody over it: four phrases, the first and third alike, the fourth resolving home; quiet bars between.
    phrases = [
        [("A4", 0.0, 1.5), ("C5", 2.0, 1.0), ("D5", 3.0, 1.0), ("C5", 4.0, 3.5), ("A4", 8.0, 1.5), ("G4", 10.0, 2.0)],
        [("F4", 0.0, 2.0), ("G4", 2.0, 1.0), ("A4", 3.0, 3.0), ("D5", 8.0, 1.5), ("C5", 10.0, 1.0), ("A4", 11.0, 3.0)],
        [("A4", 0.0, 1.5), ("C5", 2.0, 1.0), ("D5", 3.0, 1.0), ("F5", 4.0, 3.0), ("E5", 8.0, 1.5), ("D5", 10.0, 2.0)],
        [("C5", 0.0, 2.0), ("A4", 2.0, 1.0), ("G4", 3.0, 3.0), ("F4", 8.0, 2.5), ("C4", 12.0, 3.0)],
    ]
    for rnd, ph in enumerate(phrases):
        for nm, t, hold in ph:
            vel = rng.uniform(0.35, 0.5)
            circ_add(b, rhodes(hz_any(nm), sr, hold, vel=vel, bright=0.55, rel=0.6), n_of(rnd * 16.0 + t + rng.uniform(-0.015, 0.015), sr), 0.16)
    # Now and then a high, quiet glockenspiel note, like a star (two each time round, never on the beat).
    for rnd in range(4):
        for k in range(2):
            nm = ["C6", "A5", "F5", "G5", "D6"][(rnd * 2 + k) % 5]
            circ_add(b, glock(hz_any(nm), sr, 1.6, tau=0.8), n_of(rnd * 16.0 + 6.5 + k * 7.0 + rng.uniform(-0.05, 0.05), sr), 0.035)
    return circ_reverb(b, sr, 2.8, 0.38, lp=4200.0)


def loop_music_space(rng, sr, period, L, X):
    """Stars: slow pads (Cmaj9, Am9, Fmaj9, G6, four seconds each) and a sparse glockenspiel arpeggio."""
    b = np.zeros(L)
    n4 = n_of(4.4, sr)
    chords = [("C3", "G3", "B3", "D4", "E4"), ("A2", "E3", "G3", "B3", "C4"), ("F2", "C3", "E3", "G3", "A3"), ("G2", "D3", "E3", "B3", "D4")]
    for bar, ch in enumerate(chords):
        env = env_pts(n4, sr, [(0.0, 0.0), (1.2, 1.0), (3.6, 0.85), (4.4, 0.0)])
        for nm in ch:
            circ_add(b, pad_note(hz_any(nm), n4, sr, rng, env, cutoff=1600.0), n_of(bar * 4.0, sr), 0.13)
        for k in range(8):
            if rng.uniform() < 0.62:
                nm = ch[1 + (k * 2 + bar) % (len(ch) - 1)]
                f = hz_any(nm[:-1] + str(int(nm[-1]) + 2))
                circ_add(b, glock(f, sr, 1.4, tau=0.7), n_of(bar * 4.0 + k * 0.5 + rng.uniform(-0.01, 0.01), sr), 0.09)
    return circ_reverb(b, sr, 2.4, 0.35, lp=5200.0)

# ----------------------------------------------------------------------------------------------
# One-shots: fn(sr, rng, n) -> n samples (48 kHz)
# ----------------------------------------------------------------------------------------------
def c_ui_hover(sr, rng, n):
    b = np.zeros(n)
    add_at(b, modal(hz("C8"), n_of(0.010, sr), sr, [(1.0, 1.0, 1.0)], 0.0008, 0.0022, fmax=13000.0), 0.002, sr)
    add_at(b, modal(hz("C7"), n_of(0.012, sr), sr, [(1.0, 1.0, 1.0)], 0.001, 0.003), 0.002, sr, 0.35)
    return b


def c_ui_select(sr, rng, n):
    b = np.zeros(n)
    add_at(b, bubble(sr, 0.12, hz("G5"), hz("C6"), 0.010, 0.003, 0.030), 0.002, sr)
    add_at(b, kalimba(hz("C5"), sr, 0.14, tau=0.035), 0.002, sr, 0.35)
    return b


def c_grab(sr, rng, n):
    t = tl(n, sr)
    ph = phase_of(880.0 + (660.0 - 880.0) * np.exp(-t / 0.018), sr)  # 660 -> 880 Hz (E5 -> A5)
    pl = (np.sin(ph) + 0.28 * np.sin(2 * ph) * np.exp(-t / 0.03) + 0.08 * np.sin(3 * ph) * np.exp(-t / 0.018))
    pl *= perc_env(n, sr, 0.0015, 0.07)
    tp = phase_of(120.0 + 55.0 * np.exp(-t / 0.012), sr)
    th = (np.sin(tp) + 0.15 * np.sin(2 * tp)) * perc_env(n, sr, 0.002, 0.04)
    return (0.75 * pl + th) * note_tail(n, 0.3)


PLACE_NOTES = ["C5", "D5", "E5", "G5", "A5", "C6"]


def make_place(k):
    def fn(sr, rng, n):
        b = np.zeros(n)
        add_at(b, wood_tock(hz("G5"), sr, rng), 0.0, sr, 0.8)
        add_at(b, marimba(hz(PLACE_NOTES[k]), sr, 0.32, tau=0.16), 0.012, sr, 0.85)
        return b
    return fn


def c_build_complete(sr, rng, n):
    b = np.zeros(n)
    for i, nm in enumerate(["C5", "E5", "G5", "C6"]):
        add_at(b, marimba(hz(nm), sr, 0.9, tau=0.28), 0.09 * i, sr, 0.55 + 0.05 * i)
        add_at(b, glock(2 * hz(nm), sr, 0.9, tau=0.35), 0.09 * i, sr, 0.12)
    tc = 0.27
    env = env_pts(n, sr, [(tc, 0.0), (tc + 0.08, 1.0), (0.8, 0.75), (1.62, 0.0)])
    for nm, g in [("C4", 0.30), ("G4", 0.28), ("C5", 0.32), ("E5", 0.26), ("G5", 0.22)]:
        b += g * pad_note(hz(nm), n, sr, rng, env)
    pool = ["C7", "D7", "E7", "G7", "A7"]
    for tt in np.sort(rng.uniform(0.32, 1.35, 16)):
        add_at(b, glass(hz(pool[rng.integers(len(pool))]), sr, 0.35, tau=0.08), tt, sr,
               rng.uniform(0.04, 0.09) * (1.0 - 0.5 * (tt - 0.32) / 1.03))
    air = band_noise(n, sr, rng, lambda T: 7000.0 + 0 * T, 1.0,
                     lambda T: smoothstep((T - 0.27) / 0.3) * (1.0 - smoothstep((T - 0.9) / 0.6)), 1024)
    return b + 0.012 * unit_peak(air)


def earcon_tone(f, sr, dur, tau):
    return modal(f, n_of(dur, sr), sr, [(1.0, 1.0, 1.0), (2.0, 0.10, 0.5), (3.0, 0.025, 0.3)], 0.004, tau)


def c_listen_start(sr, rng, n):
    b = np.zeros(n)
    add_at(b, earcon_tone(hz("G5"), sr, 0.20, 0.07), 0.0, sr, 0.8)
    add_at(b, earcon_tone(hz("C6"), sr, 0.21, 0.10), 0.085, sr, 1.0)
    return b


def c_listen_stop(sr, rng, n):
    b = np.zeros(n)
    add_at(b, earcon_tone(hz("C6"), sr, 0.20, 0.07), 0.0, sr, 0.9)
    add_at(b, earcon_tone(hz("G5"), sr, 0.21, 0.10), 0.085, sr, 0.85)
    return b


def c_ack(sr, rng, n):
    t = tl(n, sr)
    cut = 700.0 + 2000.0 * smoothstep(t / 0.05) * np.exp(-np.maximum(t - 0.05, 0.0) / 0.12)
    env = smoothstep(t / 0.03) * np.exp(-np.maximum(t - 0.05, 0.0) / 0.11)
    x = soft_tone(np.full(n, hz("C5")), sr, cut, 1.6, nh=6) + 0.75 * soft_tone(np.full(n, hz("G5")), sr, cut, 1.6, nh=6)
    return x * env * note_tail(n, 0.3)


def c_not_understood(sr, rng, n):
    b = np.zeros(n)
    add_at(b, soft_note(hz("E4"), sr, 0.20, attack=0.012, tau=0.07, cutoff=1300.0, slope=1.7), 0.0, sr, 0.85)
    add_at(b, soft_note(hz("C4"), sr, 0.24, attack=0.012, tau=0.11, cutoff=1100.0, slope=1.7, droop=0.01), 0.13, sr)
    return b


def c_reveal_whoosh(sr, rng, n):
    def fc(T):
        return 300.0 * (2000.0 / 300.0) ** smoothstep(T / 0.9)

    def amp(T):
        return smoothstep(T / 0.62) * (1.0 - smoothstep((T - 0.64) / 0.34))
    main = shaped_noise(n, sr, rng, lambda T, F: amp(T) * bp_mag(F, fc(T), 1.3) * pink_mag(F) * hp_mag(F, 100.0, 2)
                        * lp_mag(F, 9000.0, 2))
    air = shaped_noise(n, sr, rng, lambda T, F: amp(T) * bp_mag(F, 2.2 * fc(T), 2.0) * pink_mag(F) * lp_mag(F, 9000.0, 2))
    return unit_peak(main) + 0.3 * unit_peak(air)


def c_reveal_thump(sr, rng, n):
    t = tl(n, sr)
    ph = phase_of(hz("E2") + (115.0 - hz("E2")) * np.exp(-t / 0.03), sr)  # settles on E2 (82 Hz)
    body = (np.sin(ph) + 0.18 * np.sin(2 * ph)) * perc_env(n, sr, 0.005, 0.15)
    puff = fft_filter(rng.standard_normal(n), sr, lambda F: lp_mag(F, 260.0, 2) * hp_mag(F, 30.0, 2))
    b = body + 0.3 * unit_peak(puff * perc_env(n, sr, 0.003, 0.035))
    for tt in np.sort(rng.uniform(0.01, 0.32, 9)):
        add_at(b, crackle(sr, rng, logu(rng, 2000, 5000), rng.uniform(0.001, 0.003)), tt, sr, rng.uniform(0.06, 0.16))
    return b * note_tail(n, 0.3)


def c_world_end(sr, rng, n):
    b = np.zeros(n)
    for nm, t0, g, tau in [("C6", 0.00, 0.75, 0.20), ("A5", 0.10, 0.72, 0.20), ("G5", 0.20, 0.70, 0.22),
                           ("E5", 0.30, 0.70, 0.24), ("C5", 0.42, 0.90, 0.40)]:
        add_at(b, chime(hz(nm), sr, 0.58, tau=tau), t0, sr, g)
    t = tl(n, sr)
    bloom = np.sin(2 * np.pi * hz("C4") * t) + 0.25 * np.sin(2 * np.pi * hz("G4") * t)
    return b + 0.22 * bloom * env_pts(n, sr, [(0.40, 0.0), (0.50, 1.0), (0.98, 0.0)])


def c_souvenir_chime(sr, rng, n):
    b = bell(hz("C6"), n, sr, tau=0.5, attack=0.003)
    add_at(b, glass(hz("G7"), sr, 0.4, tau=0.12), 0.0, sr, 0.06)
    return b * note_tail(n, 0.2)


def c_touch(sr, rng, n):
    t = tl(n, sr)
    air = fft_filter(rng.standard_normal(n), sr, lambda F: hp_mag(F, 3500.0, 2) * lp_mag(F, 10000.0, 2))
    air *= perc_env(n, sr, 0.0006, 0.004)
    ph = phase_of(hz("G3") * (1.0 + 0.04 * np.exp(-t / 0.01)), sr)
    body = (np.sin(ph) + 0.2 * np.sin(2 * ph)) * perc_env(n, sr, 0.003, 0.032)
    return body + 0.35 * unit_peak(air)


def c_item_appear(sr, rng, n):
    b = np.zeros(n)
    for i, nm in enumerate(["C7", "E7", "G7", "A7", "C8", "E8"]):
        add_at(b, glass(hz(nm), sr, 0.3, tau=0.07 + 0.01 * i), 0.035 * i, sr, 0.42 + 0.07 * i)
    t = tl(n, sr)
    b += 0.15 * np.sin(2 * np.pi * hz("C6") * t) * env_pts(n, sr, [(0.0, 0.0), (0.03, 1.0), (0.3, 0.0)])
    air = band_noise(n, sr, rng, lambda T: 7500.0 + 0 * T, 1.2,
                     lambda T: smoothstep(T / 0.08) * (1.0 - smoothstep((T - 0.12) / 0.25)))
    return b + 0.05 * unit_peak(air)


def whoomp(n, sr, rng, t0, cut_pts, decay):
    """Burst of flame air: pink noise whose low-pass opens fast then closes."""
    tc, fc = zip(*cut_pts)

    def amp(T):
        return smoothstep((T - t0) / 0.05) * np.exp(-np.maximum(T - t0 - 0.05, 0.0) / decay)
    return unit_peak(shaped_noise(n, sr, rng, lambda T, F: amp(T) * lp_mag(F, np.interp(T, tc, fc), 2) * pink_mag(F)
                                  * hp_mag(F, 40.0, 2), 512))


def c_fire_ignite(sr, rng, n):
    t = tl(n, sr)
    b = whoomp(n, sr, rng, 0.0, [(0.0, 180.0), (0.07, 1600.0), (0.45, 420.0), (1.2, 300.0)], 0.25)
    roar = shaped_noise(n, sr, rng, lambda T, F: smoothstep((T - 0.1) / 0.2) * (1.0 - smoothstep((T - 0.55) / 0.6))
                        * brown_mag(F) * lp_mag(F, 300.0, 2) * hp_mag(F, 35.0, 2), 1024)
    b += 0.25 * unit_peak(roar)
    b += 0.35 * np.sin(phase_of(60.0 + 30.0 * smoothstep(t / 0.08), sr)) * perc_env(n, sr, 0.02, 0.12)
    for tt, a, sig in crackle_events(rng, sr, 0.06, 1.1, lambda T: 45.0 * np.exp(-(T - 0.06) / 0.35) + 5.0, 50.0):
        add_at(b, sig, tt, sr, 0.35 * a * math.exp(-(tt - 0.06) / 0.6))
    return b


def c_wisp_hello(sr, rng, n):
    b = np.zeros(n)

    def soft_glass(f, d, tau):
        return modal(f, n_of(d, sr), sr, [(1.0, 1.0, 1.0), (2.0, 0.12, 0.5), (2.61, 0.10, 0.4), (4.9, 0.02, 0.2)],
                     0.002, tau, fmax=13000.0)
    add_at(b, soft_glass(hz("E6"), 0.30, 0.09), 0.0, sr, 0.75)
    add_at(b, soft_glass(hz("A6"), 0.27, 0.11), 0.11, sr, 0.9)
    return b


# ---- per-world build flourishes ----
def c_build_fire(sr, rng, n):
    b = np.zeros(n)
    for i, nm in enumerate(["C4", "E4", "G4", "C5", "E5"]):
        add_at(b, kalimba(hz(nm), sr, 1.0, tau=0.32), 0.08 * i, sr, 0.5 + 0.07 * i)
    env = env_pts(n, sr, [(0.3, 0.0), (0.45, 1.0), (0.9, 0.6), (1.55, 0.0)])
    for nm, g in [("C4", 0.22), ("G4", 0.20), ("E5", 0.14)]:
        b += g * pad_note(hz(nm), n, sr, rng, env, cutoff=1500.0)
    b += 0.3 * whoomp(n, sr, rng, 0.2, [(0.2, 200.0), (0.3, 1200.0), (0.7, 350.0), (1.6, 250.0)], 0.3)
    for tt, a, sig in crackle_events(rng, sr, 0.3, 1.35, lambda T: 9.0 + 0 * T, 9.0):
        add_at(b, sig, tt, sr, 0.12 * a)
    return b


def c_build_sandcastle(sr, rng, n):
    b = np.zeros(n)
    for i, (nm, t0) in enumerate([("C5", 0.00), ("E5", 0.09), ("G5", 0.18), ("E5", 0.30), ("A5", 0.40), ("C6", 0.52)]):
        add_at(b, marimba(hz(nm), sr, 0.8, tau=0.2 if i < 5 else 0.4), t0, sr, 0.55 + 0.05 * i)
    add_at(b, glock(hz("C7"), sr, 0.8, tau=0.4), 0.52, sr, 0.12)
    for t0 in (0.0, 0.18, 0.40):  # sand swishes
        sw = band_noise(n_of(0.12, sr), sr, rng, lambda T: 2400.0 + 0 * T, 0.8,
                        lambda T: smoothstep(T / 0.03) * (1.0 - smoothstep((T - 0.04) / 0.07)), 256)
        add_at(b, unit_peak(sw), t0, sr, 0.16)
    for t0 in (0.64, 0.76):  # pat pat
        m = n_of(0.08, sr)
        pat = fft_filter(rng.standard_normal(m), sr, lambda F: lp_mag(F, 500.0, 2) * hp_mag(F, 60.0, 2))
        add_at(b, unit_peak(pat * perc_env(m, sr, 0.002, 0.018)), t0, sr, 0.35)
    return b


def c_build_crystal(sr, rng, n):
    b = np.zeros(n)
    for i, nm in enumerate(["C6", "E6", "G6", "C7", "E7", "G7"]):
        add_at(b, glass(hz(nm), sr, 1.1, tau=0.32), 0.055 * i, sr, 0.45 + 0.06 * i)
    t = tl(n, sr)
    c6, g6 = hz("C6"), hz("G6")
    pad = (np.sin(2 * np.pi * c6 * t) + np.sin(2 * np.pi * c6 * 1.0029 * t)
           + 0.7 * (np.sin(2 * np.pi * g6 * t) + np.sin(2 * np.pi * g6 * 0.9971 * t)))
    b += 0.07 * pad * env_pts(n, sr, [(0.2, 0.0), (0.4, 1.0), (0.9, 0.6), (1.55, 0.0)])
    pool = ["G7", "A7", "C8", "D8", "E8"]
    for tt in np.sort(rng.uniform(0.3, 1.2, 10)):
        add_at(b, glass(hz(pool[rng.integers(len(pool))]), sr, 0.3, tau=0.06), tt, sr,
               rng.uniform(0.05, 0.12) * (1.0 - 0.5 * (tt - 0.3) / 0.9))
    return b


def c_build_snowman(sr, rng, n):
    b = np.zeros(n)
    for t0 in (0.0, 0.14, 0.28):  # three soft packed-snow pats
        m = n_of(0.09, sr)
        grains = rng.standard_normal(m) * (rng.uniform(size=m) < 0.25)
        cr = fft_filter(grains, sr, lambda F: bp_mag(F, 2600.0, 0.8) * lp_mag(F, 6000.0, 2)) * perc_env(m, sr, 0.004, 0.025)
        th = fft_filter(rng.standard_normal(m), sr, lambda F: lp_mag(F, 350.0, 2) * hp_mag(F, 60.0, 2)) * perc_env(m, sr, 0.003, 0.02)
        add_at(b, 0.5 * unit_peak(cr) + 0.5 * unit_peak(th), t0, sr, 0.35)

    def celesta(f, d, tau):
        return modal(f, n_of(d, sr), sr, [(1.0, 1.0, 1.0), (3.0, 0.05, 0.3), (4.0, 0.10, 0.25), (9.2, 0.015, 0.08)],
                     0.0015, tau)
    for i, (nm, t0) in enumerate([("E5", 0.42), ("G5", 0.54), ("C6", 0.66)]):
        add_at(b, celesta(hz(nm), 0.94, 0.35 if i < 2 else 0.5), t0, sr, 0.6 + 0.1 * i)
    add_at(b, celesta(hz("E6"), 0.8, 0.45), 0.66, sr, 0.25)
    add_at(b, celesta(hz("G6"), 0.8, 0.45), 0.66, sr, 0.2)
    for tt in np.sort(rng.uniform(0.66, 0.95, 6)):  # tiny sleigh-bell glints
        add_at(b, glass(hz(["C8", "D8", "E8"][rng.integers(3)]), sr, 0.15, tau=0.03), tt, sr, rng.uniform(0.04, 0.08))
    return b


def c_build_starmap(sr, rng, n):
    b = np.zeros(n)
    for i, nm in enumerate(["C5", "G5", "D6", "A6", "E7"]):  # stacked fifths, each with a fading echo
        blip = soft_note(hz(nm), sr, 0.3, attack=0.004, tau=0.08, cutoff=4000.0, slope=2.2, nh=3)
        for r in range(4):
            add_at(b, blip, 0.1 * i + 0.17 * r, sr, (0.6 + 0.06 * i) * 0.42 ** r)
    t = tl(n, sr)
    env = env_pts(n, sr, [(0.1, 0.0), (0.6, 1.0), (1.1, 0.7), (1.75, 0.0)])
    c4, g4, d5 = hz("C4"), hz("G4"), hz("D5")
    pad = (np.sin(2 * np.pi * c4 * t) + np.sin(2 * np.pi * c4 * 1.002 * t) + 0.7 * np.sin(2 * np.pi * g4 * t)
           + 0.5 * np.sin(2 * np.pi * d5 * t))
    b += 0.10 * pad * env + 0.10 * np.sin(2 * np.pi * hz("C3") * t) * env
    for tt in np.sort(rng.uniform(0.5, 1.5, 6)):
        add_at(b, glass(hz(["G7", "C8", "E8"][rng.integers(3)]), sr, 0.3, tau=0.07), tt, sr, rng.uniform(0.03, 0.06))
    return b


def c_build_anemone(sr, rng, n):
    b = np.zeros(n)
    for i, nm in enumerate(["C5", "D5", "E5", "G5", "A5"]):  # bubbly notes that wobble into pitch
        f, m = hz(nm), n_of(0.5, sr)
        tt = tl(m, sr)
        ph = phase_of(f * (1.0 - 0.2 * np.exp(-tt / 0.015)) * (1.0 + 0.012 * np.sin(2 * np.pi * 7.0 * tt) * smoothstep(tt / 0.05)), sr)
        add_at(b, (np.sin(ph) + 0.12 * np.sin(2 * ph)) * perc_env(m, sr, 0.003, 0.17) * note_tail(m), 0.09 * i, sr, 0.55 + 0.06 * i)
    for tt in np.sort(rng.uniform(0.0, 1.2, 14)):
        f0, d = rng.uniform(400, 1000), rng.uniform(0.02, 0.05)
        add_at(b, bubble(sr, d * 2.5, f0, f0 * rng.uniform(1.25, 1.6), d / 2.5, 0.0015, d / 2.5), tt, sr, rng.uniform(0.1, 0.25))
    env = env_pts(n, sr, [(0.4, 0.0), (0.55, 1.0), (1.0, 0.6), (1.55, 0.0)])
    for nm, g in [("C5", 0.16), ("E5", 0.13), ("A5", 0.11)]:
        b += g * pad_note(hz(nm), n, sr, rng, env, cutoff=1200.0)
    return b


def c_build_jukebox(sr, rng, n):
    b = np.zeros(n)
    for nm, t0 in [("G2", 0.0), ("A2", 0.222), ("B2", 0.444)]:  # swung walk-up into the downbeat
        add_at(b, upright(hz_c(nm), sr, 0.2, 0.85), t0, sr, 0.9)
    add_at(b, upright(hz_c("C2"), sr, 0.95, 1.0), 0.667, sr)
    for j, nm in enumerate(["E3", "B3", "D4", "G4"]):  # Cmaj9
        add_at(b, rhodes(hz_c(nm), sr, 0.95, 0.9, 1.3), 0.667 + 0.012 * j, sr, 0.35)
    add_at(b, rhodes(hz_c("D5"), sr, 0.2, 0.7, 1.3), 1.0, sr, 0.25)
    add_at(b, rhodes(hz_c("E5"), sr, 0.5, 0.75, 1.3), 1.111, sr, 0.28)
    for t0, v in [(0.0, 0.2), (0.667, 0.45), (1.111, 0.18)]:
        add_at(b, brush_tap(sr, rng, 1.3), t0, sr, v)
    sw = band_noise(n, sr, rng, lambda T: 3000.0 + 0 * T, 0.5,
                    lambda T: smoothstep(T / 0.4) * (1.0 - smoothstep((T - 0.7) / 0.9)), 512)
    b += 0.05 * unit_peak(sw)
    t = tl(n, sr)
    return b * (1.0 - 0.08 * (0.5 + 0.5 * np.sin(2 * np.pi * 4.6 * t)))


def c_build_blossom(sr, rng, n):
    b = np.zeros(n)
    for i, nm in enumerate(["C5", "D5", "E5", "G5", "A5", "C6", "D6", "E6", "G6"]):  # harp gliss
        add_at(b, pluck(hz(nm), sr, 1.0, tau=0.5), 0.045 * i, sr, 0.42 + 0.04 * i)
    for tt in np.sort(rng.uniform(0.45, 1.2, 6)):
        add_at(b, glass(hz(["C7", "E7", "G7"][rng.integers(3)]), sr, 0.35, tau=0.09), tt, sr, rng.uniform(0.06, 0.12))
    br = band_noise(n, sr, rng, lambda T: 900.0 + 0 * T, 0.9,
                    lambda T: smoothstep((T - 0.05) / 0.4) * (1.0 - smoothstep((T - 0.6) / 0.9)), 1024)
    b += 0.06 * unit_peak(br)
    add_at(b, bird_note(sr, [(0.0, hz("E7")), (0.07, hz("G7"))], 0.08, h2=0.05), 1.12, sr, 0.15)
    add_at(b, bird_note(sr, [(0.0, hz("G7")), (0.09, hz("E7"))], 0.10, h2=0.05), 1.24, sr, 0.13)
    return b


# ----------------------------------------------------------------------------------------------
# Batch 2: table taps, Ember's voice, campfire roasting, others (all 48 kHz)
# ----------------------------------------------------------------------------------------------
def shifted(x, k):
    """x delayed by k samples (same length, zeros in front)."""
    return np.concatenate([np.zeros(k), x[:len(x) - k]]) if k > 0 else x


def lp_noise(n, sr, rng, fc, attack, tau, hp=40.0):
    """Short low-passed noise burst (a thud / puff body), unit peak."""
    x = fft_filter(rng.standard_normal(n), sr, lambda F: lp_mag(F, fc, 2) * hp_mag(F, hp, 2))
    return unit_peak(x * perc_env(n, sr, attack, tau))


def c_tap_campfire(sr, rng, n):
    b = np.zeros(n)
    m = n_of(0.16, sr)
    knock = modal(hz("C4"), m, sr, [(1.0, 1.0, 1.0), (2.4, 0.35, 0.5), (3.9, 0.12, 0.3)], 0.0015, 0.035,
                  bend=0.04, bend_tau=0.005)
    add_at(b, knock + 0.3 * lp_noise(m, sr, rng, 400.0, 0.002, 0.015, 50.0), 0.0, sr)
    for t0, g in [(0.004, 0.45), (rng.uniform(0.02, 0.05), 0.3), (rng.uniform(0.07, 0.14), 0.18),
                  (rng.uniform(0.15, 0.22), 0.1)]:
        add_at(b, crackle(sr, rng, logu(rng, 2000, 5000), rng.uniform(0.0015, 0.004)), t0, sr, g)
    return b


def c_tap_beach(sr, rng, n):
    puff = fft_filter(rng.standard_normal(n), sr, lambda F: pink_mag(F) * lp_mag(F, 1800.0, 2) * hp_mag(F, 150.0, 2))
    puff = unit_peak(puff * perc_env(n, sr, 0.005, 0.05))
    grains = rng.standard_normal(n) * (rng.uniform(size=n) < 0.04)
    grains = fft_filter(grains, sr, lambda F: hp_mag(F, 2500.0, 2) * lp_mag(F, 7000.0, 2)) * perc_env(n, sr, 0.003, 0.04)
    return puff + 0.25 * unit_peak(grains) + 0.5 * lp_noise(n, sr, rng, 220.0, 0.004, 0.025)


def c_tap_desert(sr, rng, n):
    b = np.zeros(n)
    m = n_of(0.02, sr)
    tick = fft_filter(rng.standard_normal(m), sr, lambda F: hp_mag(F, 2500.0, 2) * lp_mag(F, 9000.0, 2))
    add_at(b, unit_peak(tick * perc_env(m, sr, 0.0003, 0.0025)), 0.0, sr, 0.8)
    for tt in np.sort(rng.uniform(0.004, 0.05, 6)):  # a few dry grains trickling
        add_at(b, crackle(sr, rng, logu(rng, 3000, 7000), 0.0008), tt, sr, rng.uniform(0.08, 0.2))
    add_at(b, lp_noise(n_of(0.08, sr), sr, rng, 300.0, 0.002, 0.012), 0.0, sr, 0.25)
    add_at(b, rod_chime(hz("E7"), sr, 0.28, tau=0.11), 0.008, sr, 0.3)
    return b


def c_tap_snow(sr, rng, n):
    b = np.zeros(n)
    for c0, span, k, g in [(0.0, 0.07, 55, 1.0), (0.085, 0.05, 25, 0.5)]:  # crunch, then a smaller settle
        for tt in c0 + span * np.sort(rng.uniform(0.0, 1.0, k)) ** 1.6:
            add_at(b, crackle(sr, rng, logu(rng, 1500, 5000), rng.uniform(0.0005, 0.0015)), tt, sr,
                   g * rng.uniform(0.15, 0.5))
    add_at(b, lp_noise(n_of(0.1, sr), sr, rng, 300.0, 0.003, 0.02), 0.0, sr, 0.45)
    return b


def c_tap_space(sr, rng, n):
    b = np.zeros(n)
    m = n_of(0.2, sr)
    tt = tl(m, sr)
    ph = phase_of(hz("E6") * (1.0 - 0.06 * np.exp(-tt / 0.008)), sr)
    add_at(b, (np.sin(ph) + 0.12 * np.sin(2 * ph)) * perc_env(m, sr, 0.002, 0.045) * note_tail(m), 0.004, sr, 0.8)
    tick = modal(2300.0, n_of(0.04, sr), sr, [(1.0, 1.0, 1.0), (1.47, 0.6, 0.8), (2.09, 0.4, 0.6), (2.83, 0.25, 0.4)],
                 0.0004, 0.006, fmax=13000.0)  # small metal plate under the button
    add_at(b, tick, 0.0, sr, 0.35)
    return b


def c_tap_reef(sr, rng, n):
    b = np.zeros(n)
    add_at(b, bubble(sr, 0.22, hz("C4"), hz("C5"), 0.025, 0.003, 0.05), 0.0, sr)
    add_at(b, bubble(sr, 0.12, hz("E5"), hz("A5"), 0.015, 0.002, 0.025), 0.055, sr, 0.35)
    add_at(b, lp_noise(n_of(0.08, sr), sr, rng, 300.0, 0.003, 0.015), 0.0, sr, 0.25)
    return b


def c_tap_city_1(sr, rng, n):  # feathered (brushed) kick
    t = tl(n, sr)
    ph = phase_of(hz("A1") + (95.0 - hz("A1")) * np.exp(-t / 0.025), sr)
    kick = (np.sin(ph) + 0.3 * np.sin(2 * ph)) * perc_env(n, sr, 0.004, 0.11)
    felt = fft_filter(rng.standard_normal(n), sr, lambda F: lp_mag(F, 600.0, 2) * hp_mag(F, 60.0, 2)) * perc_env(n, sr, 0.002, 0.01)
    brush = fft_filter(rng.standard_normal(n), sr, lambda F: bp_mag(F, 3000.0, 0.6) * lp_mag(F, 7000.0, 2)) * perc_env(n, sr, 0.004, 0.03)
    return (kick + 0.18 * unit_peak(felt) + 0.06 * unit_peak(brush)) * note_tail(n, 0.3)


def c_tap_city_2(sr, rng, n):  # brushed snare
    nz = fft_filter(rng.standard_normal(n), sr, lambda F: bp_mag(F, 3200.0, 0.55) * lp_mag(F, 8000.0, 2) * hp_mag(F, 500.0, 2))
    e = sum(g * shifted(perc_env(n, sr, 0.002, 0.06), n_of(d, sr)) for d, g in [(0.0, 1.0), (0.0025, 0.7), (0.006, 0.5)])
    body = modal(hz("G3"), n, sr, [(1.0, 1.0, 1.0), (1.59, 0.5, 0.7), (2.14, 0.3, 0.5)], 0.002, 0.045)
    wires = fft_filter(rng.standard_normal(n), sr, lambda F: hp_mag(F, 1500.0, 2) * lp_mag(F, 7000.0, 2)) * perc_env(n, sr, 0.004, 0.08)
    return unit_peak(nz * e) + 0.35 * body + 0.3 * unit_peak(wires)


def c_tap_meadow(sr, rng, n):
    b = np.zeros(n)
    add_at(b, wood_tock(hz("C6"), sr, rng, dur=0.08, tau=0.012), 0.0, sr, 0.9)
    add_at(b, bird_note(sr, [(0.0, hz("E7")), (0.04, hz("G7"))], 0.05, fm_rate=50.0, fm_depth=0.01, h2=0.04), 0.05, sr, 0.3)
    return b


# ---- Ember: a small wordless creature voice (additive harmonics through morphing vowel formants) ----
VOWELS = {  # formants scaled up for a tiny vocal tract: [(centre Hz, bandwidth Hz, gain), ...]
    "oo": [(650, 260, 1.0), (1400, 380, 0.55), (2800, 500, 0.12)],
    "ee": [(480, 200, 0.55), (2900, 500, 1.0), (3800, 600, 0.35)],
    "ah": [(1150, 320, 1.0), (1900, 420, 0.8), (3200, 600, 0.25)],
    "eh": [(800, 260, 0.9), (2500, 450, 0.75), (3500, 600, 0.25)],
    "mm": [(380, 220, 1.0), (1100, 400, 0.12), (2600, 600, 0.04)],
}


def formant_gain(f, va, vb, m):
    """Spectral envelope morphing from vowel va (m=0) to vb (m=1); a 0.22 floor keeps it a 'hint'."""
    g = 0.22
    for (fa, ba, ga), (fb, bb, gb) in zip(VOWELS[va], VOWELS[vb]):
        Fc, Bw, G = fa + (fb - fa) * m, ba + (bb - ba) * m, ga + (gb - ga) * m
        g = g + G / (1.0 + ((f - Fc) / Bw) ** 2)
    return g


def voice(f0, sr, va, vb, morph, amp, slope=1.45, vib=None, hmax=6500.0):
    n = len(f0)
    f = f0 * vibrato(n, sr, *vib) if vib else f0
    ph = phase_of(f, sr)
    out = np.zeros(n)
    for k in range(1, 20):
        fk = k * f
        if fk.min() >= hmax:
            break
        out += k ** -slope * formant_gain(fk, va, vb, morph) * _taper(fk, hmax) * np.sin(k * ph)
    return out * amp


def breath(n, sr, rng, va, vb, morph, amp, lo=900.0):
    """Breathy noise shaped by the same vowel formants, following the voice's amplitude."""
    t = tl(n, sr)

    def mag(T, F):
        return (np.interp(T, t, amp) * formant_gain(F, va, vb, np.interp(T, t, morph))
                * hp_mag(F, lo, 2) * lp_mag(F, 6000.0, 2))
    return shaped_noise(n, sr, rng, mag, win=512)


def ember_syl(sr, rng, dur, pts, va, vb, cons=None, vib=None, breath_amt=0.03, slope=1.45, attack=0.012):
    """One syllable: pitch track pts (scale notes at both ends), vowel va -> vb, optional soft consonant
    onset ('p', 't' = tiny band-noise click; 'h' = breath)."""
    n = n_of(dur, sr)
    t = tl(n, sr)
    morph = smoothstep(t / dur)
    amp = env_pts(n, sr, [(0.0, 0.0), (attack, 1.0), (0.6 * dur, 0.85), (dur, 0.0)])
    v = voice(glide(n, sr, pts), sr, va, vb, morph, amp, slope=slope, vib=vib)
    pk = np.max(np.abs(v))
    v = v + breath_amt * unit_peak(breath(n, sr, rng, va, vb, morph, amp)) * pk
    if cons:
        fc, d, tau, g = {"p": (2500.0, 0.006, 0.0015, 0.18), "t": (4200.0, 0.006, 0.0015, 0.18),
                         "h": (1500.0, 0.02, 0.006, 0.10)}[cons]
        m = n_of(d, sr)
        burst = fft_filter(rng.standard_normal(m), sr, lambda F: bp_mag(F, fc, 1.2)) * perc_env(m, sr, 0.0005, tau)
        add(v, unit_peak(burst) * g * pk, 0)
    return v


EMBER_CHIRPS = [  # (seconds, pitch track, vowel from, vowel to, consonant, description)
    (0.075, [(0.0, "E5"), (0.065, "G5")], "ee", "eh", "p", "'pi' rising E5->G5"),
    (0.090, [(0.0, "A5"), (0.080, "E5")], "mm", "oo", None, "'mu' falling A5->E5"),
    (0.100, [(0.0, "D5"), (0.045, "A5"), (0.100, "G5")], "ah", "eh", None, "'ba' arc D5->A5->G5"),
    (0.060, [(0.0, "C6"), (0.060, "A5")], "ee", "ee", "t", "'ti' short high C6->A5"),
    (0.110, [(0.0, "C5"), (0.100, "D5")], "oo", "ah", None, "'wo' low lift C5->D5"),
    (0.080, [(0.0, "G5"), (0.035, "A5"), (0.080, "E5")], "mm", "eh", None, "'ne' tilt G5->A5->E5"),
]


def make_ember_chirp(k):
    dur, pts, va, vb, cons, _ = EMBER_CHIRPS[k]

    def fn(sr, rng, n):
        b = np.zeros(n)
        add(b, ember_syl(sr, rng, dur, [(t, hz(nm)) for t, nm in pts], va, vb, cons=cons), 0)
        return b
    return fn


def c_ember_giggle(sr, rng, n):
    b = np.zeros(n)
    for i, (a, z) in enumerate([("E5", "D5"), ("G5", "E5"), ("A5", "G5")]):  # three rising 'hee's, each flicks down
        s = ember_syl(sr, rng, 0.08, [(0.0, hz(a)), (0.05, hz(a)), (0.08, hz(z))], "ee", "eh", cons="h", breath_amt=0.08)
        add_at(b, s, 0.105 * i, sr, 0.8 + 0.1 * i)
    return b


def c_ember_wave(sr, rng, n):
    b = np.zeros(n)
    add_at(b, ember_syl(sr, rng, 0.10, [(0.0, hz("G5")), (0.09, hz("A5"))], "ee", "ee", cons="h", breath_amt=0.04), 0.0, sr, 0.85)
    add_at(b, ember_syl(sr, rng, 0.22, [(0.0, hz("A5")), (0.04, hz("C6")), (0.22, hz("C6"))], "ee", "ah",
                        vib=(7.0, 0.012, 0.06), attack=0.015), 0.13, sr)
    return b


def c_ember_yawn(sr, rng, n):
    t = tl(n, sr)
    morph = smoothstep((t - 0.1) / 0.55)
    amp = env_pts(n, sr, [(0.0, 0.0), (0.1, 0.7), (0.28, 1.0), (0.55, 0.6), (0.78, 0.0)])
    v = voice(glide(n, sr, [(0.0, hz("G5")), (0.14, hz("A5")), (0.68, hz("C5"))]), sr, "ah", "oo", morph, amp,
              slope=1.6, vib=(5.0, 0.008, 0.15))
    return v + 0.12 * unit_peak(breath(n, sr, rng, "ah", "oo", morph, amp)) * np.max(np.abs(v))


def c_ember_hello(sr, rng, n):
    b = np.zeros(n)
    add_at(b, ember_syl(sr, rng, 0.11, [(0.0, hz("E5")), (0.1, hz("G5"))], "eh", "eh", cons="h", breath_amt=0.05), 0.0, sr, 0.85)
    add_at(b, ember_syl(sr, rng, 0.32, [(0.0, hz("G5")), (0.06, hz("C6")), (0.32, hz("A5"))], "eh", "oo",
                        vib=(7.5, 0.015, 0.08), attack=0.015), 0.12, sr)
    for nm, t0, g in [("C7", 0.20, 0.07), ("E7", 0.25, 0.08), ("G7", 0.30, 0.09)]:
        add_at(b, glass(hz(nm), sr, 0.2, tau=0.06), t0, sr, g)
    return b


# ---- campfire roasting ----
def loop_sizzle(rng, sr, period, L, X):
    """Soft frying sizzle: a 4 kHz noise band (LP 6 kHz, 3rd order) with a fast 5-30 Hz flutter + dense
    micro-pops (Poisson ~140/s, 0.4-1 ms ticks 2.4-5.5 kHz, circular) + a few small pops."""
    n = L + X
    swell = plfo(rng, period, 1, 4)
    flick = plfo(rng, period, 20, 120, 0.3)
    hiss = shaped_noise(n, sr, rng, lambda T, F: db(2.0 * swell(T)) * (0.55 + 0.45 * flick(T)) * bp_mag(F, 4000.0, 0.7)
                        * lp_mag(F, 6000.0, 3) * hp_mag(F, 1500.0, 2), 512)
    cont = xfade_loop(unit_rms(hiss), L, X)
    times = rng.uniform(0.0, period, rng.poisson(140.0 * period))
    amps = np.minimum(rng.lognormal(0.0, 0.5, len(times)), 2.5)
    idx = (times * sr).astype(int) % L
    cls = rng.integers(0, 5, len(times))
    micro = np.zeros(L)
    for c, f in enumerate((2400.0, 3000.0, 3700.0, 4500.0, 5500.0)):
        tau = rng.uniform(0.0004, 0.001)
        m = n_of(tau * 7 + 0.0005, sr)
        tt = tl(m, sr)
        k = np.sin(2.0 * np.pi * f * tt) * np.exp(-tt / tau) * smoothstep(tt / 0.0002)
        imp = np.zeros(L)
        np.add.at(imp, idx[cls == c], amps[cls == c])
        micro += np.fft.irfft(np.fft.rfft(imp) * np.fft.rfft(k, L), L)  # circular convolution
    pops = np.zeros(L)
    for t0 in spread_times(rng, period, 5, 0.4):
        circ_add(pops, crackle(sr, rng, rng.uniform(1500, 3000), rng.uniform(0.003, 0.006)), n_of(t0, sr), rng.uniform(0.5, 1.0))
    soft = lambda F: lp_mag(F, 6000.0, 3)  # noqa: E731
    return mix_layers("sizzle", [("hiss", cont), ("micro", db(4) * unit_peak(circ_filter(micro, sr, soft))),
                                 ("pops", db(5) * unit_peak(circ_filter(pops, sr, soft)))], sr)


def c_golden_chime(sr, rng, n):
    b = np.zeros(n)
    for i, nm in enumerate(["E6", "G6", "C7"]):  # quick bright strum
        add_at(b, glock(hz(nm), sr, 0.85, tau=0.38), 0.03 * i, sr, 0.6 + 0.1 * i)
    add_at(b, kalimba(hz("C6"), sr, 0.7, tau=0.25), 0.0, sr, 0.3)
    for nm, t0 in [("G7", 0.10), ("E8", 0.15)]:
        add_at(b, glass(hz(nm), sr, 0.3, tau=0.07), t0, sr, 0.12)
    return b


def c_marshmallow_ignite(sr, rng, n):
    t = tl(n, sr)
    b = whoomp(n, sr, rng, 0.0, [(0.0, 300.0), (0.05, 2500.0), (0.3, 700.0), (0.6, 500.0)], 0.12)
    b += 0.2 * np.sin(phase_of(110.0 + 40.0 * smoothstep(t / 0.05), sr)) * perc_env(n, sr, 0.01, 0.06)
    for tt in np.sort(rng.uniform(0.03, 0.35, 4)):
        add_at(b, crackle(sr, rng, logu(rng, 2000, 5000), rng.uniform(0.001, 0.003)), tt, sr, 0.25)
    return b


def c_extinguish(sr, rng, n):
    puff = shaped_noise(n, sr, rng, lambda T, F: smoothstep(T / 0.02) * np.exp(-np.maximum(T - 0.02, 0.0) / 0.06)
                        * pink_mag(F) * lp_mag(F, 700.0, 2) * hp_mag(F, 60.0, 2), 512)
    hiss = shaped_noise(n, sr, rng, lambda T, F: smoothstep((T - 0.02) / 0.05) * np.exp(-np.maximum(T - 0.07, 0.0) / 0.14)
                        * bp_mag(F, 5500.0 * (3200.0 / 5500.0) ** smoothstep(T / 0.4), 0.8) * lp_mag(F, 9000.0, 2), 512)
    b = unit_peak(puff) + 0.55 * unit_peak(hiss)
    for tt in np.sort(rng.uniform(0.05, 0.2, 3)):  # last sputters
        add_at(b, crackle(sr, rng, logu(rng, 2000, 4000), 0.0015), tt, sr, 0.1)
    return b


def c_crunch(sr, rng, n):
    b = np.zeros(n)
    for c0, span, k, g in [(0.0, 0.06, 40, 1.0), (0.085, 0.05, 30, 0.75), (0.17, 0.05, 22, 0.5)]:
        for tt in c0 + span * np.sort(rng.uniform(0.0, 1.0, k)) ** 1.5:
            add_at(b, crackle(sr, rng, logu(rng, 1000, 4500), rng.uniform(0.0003, 0.0012)), tt, sr,
                   g * min(rng.lognormal(-1.2, 0.5), 0.8))
        add_at(b, lp_noise(n_of(0.06, sr), sr, rng, 350.0, 0.002, 0.015), c0, sr, 0.4 * g)
    return b


def c_stoke_whoosh(sr, rng, n):
    air = shaped_noise(n, sr, rng, lambda T, F: smoothstep(T / 0.06) * (1.0 - smoothstep((T - 0.12) / 0.25))
                       * pink_mag(F) * bp_mag(F, np.interp(T, [0.0, 0.12, 0.4], [400.0, 1100.0, 600.0]), 1.0)
                       * lp_mag(F, 5000.0, 2), 512)
    roar = shaped_noise(n, sr, rng, lambda T, F: smoothstep((T - 0.08) / 0.1) * (1.0 - smoothstep((T - 0.22) / 0.17))
                        * brown_mag(F) * lp_mag(F, 300.0, 2) * hp_mag(F, 35.0, 2), 1024)
    b = unit_peak(air) + 0.6 * unit_peak(roar)
    for tt in np.sort(rng.uniform(0.12, 0.32, 4)):  # the fire answers
        add_at(b, crackle(sr, rng, logu(rng, 1500, 5000), rng.uniform(0.001, 0.004)), tt, sr, 0.18)
    return b


def c_sparks_rise(sr, rng, n):
    b = np.zeros(n)
    pool = ["C6", "D6", "E6", "G6", "A6", "C7", "D7", "E7", "G7", "A7", "C8"]
    for tt in np.sort(rng.uniform(0.05, 1.7, 26)):  # pings climb the scale as the sparks rise
        u = tt / 1.7
        i = int(np.clip(round(u * (len(pool) - 1) + rng.normal(0.0, 1.2)), 0, len(pool) - 1))
        add_at(b, glass(hz(pool[i]), sr, 0.3, tau=rng.uniform(0.06, 0.09)), tt, sr, 0.35 * (1.0 - 0.4 * u) * rng.uniform(0.6, 1.0))
    air = band_noise(n, sr, rng, lambda T: 2000.0 * 3.5 ** smoothstep(T / 2.0), 1.2,
                     lambda T: smoothstep(T / 0.4) * (1.0 - smoothstep((T - 0.9) / 1.0)), 1024)
    b += 0.05 * unit_peak(air)
    for tt in np.sort(rng.uniform(0.0, 0.25, 5)):  # the pop that sent them up
        add_at(b, crackle(sr, rng, logu(rng, 2000, 5000), rng.uniform(0.001, 0.004)), tt, sr, 0.3)
    return b


def c_constellation(sr, rng, n):
    t = tl(n, sr)
    env = env_pts(n, sr, [(0.0, 0.0), (0.9, 1.0), (2.2, 0.85), (3.45, 0.0)])
    cut = 900.0 * (3000.0 / 900.0) ** smoothstep(t / 1.6)  # the chord blooms open
    b = np.zeros(n)
    for nm, g in [("C3", 0.30), ("G3", 0.26), ("E4", 0.24), ("A4", 0.20), ("D5", 0.16)]:  # C6/9, all pentatonic
        b += g * pad_note(hz(nm), n, sr, rng, env, cutoff=cut)
    b += 0.10 * np.sin(2 * np.pi * hz("C3") * t) * env
    pool = ["C7", "D7", "E7", "G7", "A7"]
    for tt in np.sort(rng.uniform(0.8, 3.0, 8)):  # a few stars
        add_at(b, glass(hz(pool[rng.integers(len(pool))]), sr, 0.4, tau=0.1), tt, sr, rng.uniform(0.03, 0.06))
    return b


# ---- others ----
def c_visitor_land(sr, rng, n):
    b = np.zeros(n)
    t0 = 0.0
    for i, (gap, g) in enumerate([(0.045, 1.0), (0.05, 0.85), (0.058, 0.7), (0.068, 0.5), (0.08, 0.3)]):
        m = n_of(0.04, sr)
        fc = 700.0 * rng.uniform(0.85, 1.15)
        flap = fft_filter(rng.standard_normal(m), sr, lambda F: bp_mag(F, fc, 0.8) * hp_mag(F, 200.0, 2) * lp_mag(F, 3000.0, 2))
        add_at(b, unit_peak(flap * perc_env(m, sr, 0.006, 0.01)), t0, sr, g)
        t0 += gap
    add_at(b, lp_noise(n_of(0.06, sr), sr, rng, 250.0, 0.003, 0.012), t0 + 0.02, sr, 0.25)  # settles
    return b


def c_bloom(sr, rng, n):
    b = np.zeros(n)
    t = tl(n, sr)
    f0 = glide(n, sr, [(0.0, hz("C5")), (0.7, hz("G5"))])  # a soft tone grows upward
    amp = env_pts(n, sr, [(0.0, 0.0), (0.5, 1.0), (1.15, 0.0)])
    b += 0.35 * soft_tone(f0, sr, 600.0 * (2500.0 / 600.0) ** smoothstep(t / 0.8), 1.6, vib=(5.0, 0.006, 0.1), nh=6) * amp
    for i, (nm, t0) in enumerate([("C5", 0.10), ("E5", 0.24), ("G5", 0.38), ("A5", 0.50), ("C6", 0.62), ("E6", 0.72)]):
        add_at(b, kalimba(hz(nm), sr, 0.5, tau=0.18), t0, sr, 0.35 + 0.05 * i)
    air = band_noise(n, sr, rng, lambda T: 500.0 * 8.0 ** smoothstep(T / 0.9), 1.2,
                     lambda T: smoothstep(T / 0.3) * (1.0 - smoothstep((T - 0.6) / 0.5)), 1024)
    b += 0.05 * unit_peak(air)
    for nm, t0, g in [("C7", 0.75, 0.18), ("E7", 0.80, 0.22), ("G7", 0.86, 0.25)]:
        add_at(b, glass(hz(nm), sr, 0.3, tau=0.08), t0, sr, g)
    return b


def c_tide_wave(sr, rng, n):
    T0 = np.array([0.0, 1.4, 1.62, 2.45, 2.5])
    lev = lambda T: np.interp(T, T0, [0.0, 0.85, 1.0, 0.0, 0.0]) ** 1.3  # noqa: E731
    cut = lambda T: np.exp(np.interp(T, [0.0, 1.4, 1.6, 2.4], np.log([300.0, 1500.0, 3500.0, 900.0])))  # noqa: E731
    wash = shaped_noise(n, sr, rng, lambda T, F: lev(T) * lp_mag(F, cut(T), 2) * pink_mag(F) * hp_mag(F, 40.0, 2))
    body = shaped_noise(n, sr, rng, lambda T, F: smoothstep((T - 1.3) / 0.25) * np.exp(-np.maximum(T - 1.55, 0.0) / 0.35)
                        * brown_mag(F) * lp_mag(F, 220.0, 2) * hp_mag(F, 30.0, 2), 2048)
    foam = shaped_noise(n, sr, rng, lambda T, F: smoothstep((T - 1.5) / 0.2) * (1.0 - smoothstep((T - 1.75) / 0.7))
                        * hp_mag(F, 2500.0, 2) * lp_mag(F, 8000.0, 2))
    return unit_peak(wash) + 0.6 * unit_peak(body) + 0.35 * unit_peak(foam)


def c_fish_dart(sr, rng, n):
    fc = lambda T: np.exp(np.interp(T, [0.0, 0.08, 0.2], np.log([700.0, 3200.0, 1500.0])))  # noqa: E731
    sw = shaped_noise(n, sr, rng, lambda T, F: smoothstep(T / 0.025) * (1.0 - smoothstep((T - 0.05) / 0.15))
                      * bp_mag(F, fc(T), 1.8) * pink_mag(F) * lp_mag(F, 6000.0, 2), 256)
    b = unit_peak(sw)
    for t0 in (0.08, 0.13):
        f0 = rng.uniform(650, 800)
        add_at(b, bubble(sr, 0.05, f0, 1.4 * f0, 0.006, 0.0015, 0.01), t0, sr, 0.2)
    return b


def c_power_on(sr, rng, n):
    b = np.zeros(n)
    t = tl(n, sr)
    relay = lambda: modal(2400.0, n_of(0.03, sr), sr, [(1.0, 1.0, 1.0), (1.47, 0.6, 0.8), (2.09, 0.4, 0.6)],  # noqa: E731
                          0.0004, 0.004, fmax=13000.0)
    add_at(b, relay(), 0.0, sr, 0.3)
    add_at(b, relay(), 0.09, sr, 0.22)
    henv = env_pts(n, sr, [(0.1, 0.0), (0.6, 1.0), (1.3, 1.0), (1.48, 0.0)])
    b += 0.18 * henv * (np.sin(2 * np.pi * hz("A1") * t) + 0.7 * np.sin(2 * np.pi * hz("A2") * t)
                        + 0.35 * np.sin(2 * np.pi * hz("E3") * t))
    f0 = glide(n, sr, [(0.1, hz("C3")), (0.85, hz("C5"))])
    cut = 300.0 * (3000.0 / 300.0) ** smoothstep((t - 0.1) / 0.75)
    sweep = soft_tone(f0, sr, cut, 1.5, nh=10) * env_pts(n, sr, [(0.1, 0.0), (0.3, 0.6), (0.85, 1.0), (1.0, 0.0)])
    b += 0.4 * sweep
    add_at(b, earcon_tone(hz("G5"), sr, 0.4, 0.15), 0.90, sr, 0.6)
    add_at(b, earcon_tone(hz("C6"), sr, 0.5, 0.2), 0.98, sr, 0.75)
    return b


SIMON_NOTES = ["C5", "D5", "E5", "G5", "A5"]


def make_simon(k):
    def fn(sr, rng, n):
        t = tl(n, sr)
        ph = 2 * np.pi * hz(SIMON_NOTES[k]) * t
        tone = np.sin(ph) + 0.12 * np.sin(2 * ph) + 0.03 * np.sin(3 * ph)
        return tone * smoothstep(t / 0.006) * np.exp(-t / 0.35) * (1.0 - smoothstep((t - 0.17) / 0.07))
    return fn


def c_wrong_soft(sr, rng, n):
    t = tl(n, sr)
    f0 = glide(n, sr, [(0.0, hz("E5")), (0.04, hz("E5")), (0.2, hz("C5"))])
    wob = 1.0 - 0.25 * (0.5 - 0.5 * np.cos(2 * np.pi * 7.0 * t))  # two gentle wobbles: 'hmm-mm'
    env = env_pts(n, sr, [(0.0, 0.0), (0.015, 1.0), (0.18, 0.7), (0.3, 0.0)])
    return soft_tone(f0, sr, 1500.0, 1.7, nh=6) * wob * env


# ----------------------------------------------------------------------------------------------
# Mastering
# ----------------------------------------------------------------------------------------------
def fade_window(n, sr, fade_in, fade_out):
    w = np.ones(n)
    a, b = max(1, n_of(fade_in, sr)), max(1, n_of(fade_out, sr))
    w[:a] = 0.5 - 0.5 * np.cos(np.pi * np.arange(a) / a)            # w[0] = 0
    w[n - b:] *= 0.5 + 0.5 * np.cos(np.pi * (np.arange(b) + 1) / b)  # w[-1] = 0
    return w


def master_oneshot(x, sr, c):
    if c["mix"] > 0:
        x = reverb(x, sr, c["rt"], c["mix"], lp=min(5000.0, c["lp"]))
    x = fft_filter(x, sr, lambda f: hp_mag(f, c["hp"], 2) * lp_mag(f, c["lp"], 2))
    x = x - x.mean()
    w = fade_window(len(x), sr, c["fi"], c["fo"])
    x = x * w
    x = x - (np.mean(x) / np.mean(w)) * w  # remove DC without lifting the faded ends off zero
    return unit_peak(x) * db(c["peak"])


def best_start(x, sr):
    """Pick the first sample of a periodic loop: the 50 ms either side should match in level, and the
    surrounding 0.5 s should sit at the loop's typical level (no event, no gap)."""
    L, w, W = len(x), n_of(0.05, sr), n_of(0.25, sr)
    c = np.concatenate([[0.0], np.cumsum(np.concatenate([x[-W:], x, x[:W]]) ** 2)])
    kk = np.arange(0, L, n_of(0.005, sr)) + W

    def mp(a, b):
        return (c[b] - c[a]) / (b - a) + 1e-20
    before, after, around = mp(kk - w, kk), mp(kk, kk + w), mp(kk - W, kk + W)
    k = kk - W
    step = np.abs(x[k] - x[k - 1]) / (np.median(np.abs(np.diff(x))) + 1e-12)  # prefer a typical boundary step
    cost = (np.abs(10 * np.log10(after / before)) + 0.5 * np.abs(10 * np.log10(around / np.mean(x * x)))
            + 0.5 * np.minimum(step, 4.0))
    return int(k[np.argmin(cost)])


def finish_loop(x, sr, target_db, hp, start=None, peak=None):
    """Circular high-pass, rotate to the chosen start, then scale to an RMS target (or a peak target)."""
    x = circ_filter(x, sr, lambda f: hp_mag(f, hp, 2))  # circular, so the loop stays periodic
    k = best_start(x, sr) if start is None else start
    x = np.roll(x, -k)
    if peak is not None:
        return unit_peak(x) * db(peak), k
    return x * (db(target_db) / rms(x)), k


# ----------------------------------------------------------------------------------------------
# Tables
# ----------------------------------------------------------------------------------------------
def LP(name, sec, fn, target, desc, hp=28.0, group="bed", sr=SR_LOOP, peak=None):
    return dict(name=name, sec=sec, fn=fn, target=target, desc=desc, hp=hp, group=group, sr=sr, peak=peak)


LOOPS = [
    LP("amb_forest_night", 24.0, loop_forest, -22.0,
       "campfire forest at night: soft wind (band 220-700 Hz, gusts), leaf rustle, three crickets "
       "(4.25/4.5/4.75 kHz, 3-5 x 15 ms pulses every 0.6-1.1 s); no owl (it read as creepy on the headset)"),
    LP("amb_beach", 24.0, loop_beach, -21.0,
       "tropical beach: three wave cycles (7.6/9.0/7.4 s; pink wash LP swept 400-3k Hz, crash body, foam "
       "hiss), light wind, two distant gull series (band-limited saw 1.4k->900 Hz)"),
    LP("amb_desert", 24.0, loop_desert, -22.0,
       "desert dunes: gusty wind (250-900 Hz, Q 1.5), sand hiss on gusts, two occasional whistles "
       "(Q 20, 1.2-2 kHz), two sparse wind-chime clusters (rods C6-A6)"),
    LP("amb_snow", 24.0, loop_snow, -23.5,
       "snowy cabin, very soft: dark wind (200-650 Hz), low buffeting, faint whistle (Q 20) swelling with gusts"),
    LP("amb_space", 24.0, loop_space, -22.0, "space station: 55/110/165 Hz hum with slow beating twins, vent "
       "noise (pink 150 Hz-1.8 kHz) + airflow band, three rare soft beeps (G5/A5/C6/D6/E6)", hp=22.0),
    LP("amb_reef", 24.0, loop_reef, -22.0, "coral reef: brown-noise rumble LP 70 Hz + 41/55 Hz sines, surging "
       "wash LP 500 Hz, bubble streams and singles (rising sines 300-1200 Hz, 20-60 ms, LP 900 Hz)", hp=22.0),
    LP("amb_city_jazz", 24.0, loop_city_jazz, -21.0,
       "jazz club at 2 a.m. in the rain: rain on glass (Poisson ticks HP 900 Hz, gust-driven density) + "
       "drips, two distant wet-road car passes, and a jazz trio through the wall (LP 1.2 kHz): walking "
       "bass, brushes, Rhodes, ii-V-I in C (Dm9 G13 Cmaj9 x3) at 90 BPM"),
    LP("amb_meadow", 24.0, loop_meadow, -22.0,
       "spring meadow: breeze (350-1000 Hz) + fluttering leaves, seven bird phrases (FM chirp trills, "
       "C-pentatonic whistles, 'fee-bee'), three bee fly-bys (~230 Hz) and a faint distant hive hum"),
    LP("fire_loop", 12.0, loop_fire, -20.0,
       "campfire: roar (brown LP 150 Hz, flicker), lapping (~420 Hz band, 1-9 Hz flutter), hiss (HP 3 kHz, "
       "-24 dB), crackles (Poisson 4-12/s, 1-6 ms, BP 1.5-6 kHz) with occasional pops", group="fire"),
    LP("jukebox_music", 16.0, loop_jukebox, -20.0,
       "jazz trio, 90 BPM swung, 6 bars (Dm9, G13, Cmaj9, Am9, Dm9-G13, C6/9): Rhodes (FM), walking "
       "upright bass, brushes; LP 5.5 kHz (brighter than the bed's 1.2 kHz); starts half a beat before bar 1",
       hp=30.0, group="music"),
    LP("music_campfire", 16.0, loop_music_campfire, -20.0,
       "music by the fire: fingerpicked guitar (pluck), 60 BPM, G | Em | C | D with a top-string melody, warm reverb",
       hp=40.0, group="music"),
    LP("music_beach", 16.0, loop_music_beach, -20.0,
       "island marimba, 120 BPM, C | Am | F | G x2, low marimba bass, soft wood shaker on the off-beats",
       hp=40.0, group="music"),
    LP("music_calm", 64.0, loop_music_calm, -24.0,
       "calm, for every world and your room: 60 BPM, F major, Fmaj9 | Dm9 | Bbmaj9 | C6sus x4, slow warm pads, a soft "
       "low root, a gentle electric-piano melody (major pentatonic) that changes each time round, rare high glockenspiel, "
       "long reverb; nothing sudden, no drums",
       hp=40.0, group="music"),
    LP("music_space", 16.0, loop_music_space, -21.0,
       "stars: slow pads Cmaj9 / Am9 / Fmaj9 / G6 (4 s each) and a sparse two-octaves-up glockenspiel, long reverb",
       hp=35.0, group="music"),
    LP("roast_sizzle", 4.0, loop_sizzle, None,
       "soft marshmallow sizzle, 48 kHz 4 s seamless loop: 4 kHz noise band (LP 6 kHz) with a 5-30 Hz frying "
       "flutter, dense micro-pops (~140/s, 2.4-5.5 kHz) and a few small pops; peak -14 dBFS", hp=200.0, group="roast",
       sr=SR_UI, peak=UI_PEAK_DB),
]


def O(name, dur, fn, desc, limit=MAX_UI_SEC, lp=9000.0, hp=40.0, mix=0.18, rt=0.5, fi=0.002, fo=None,
      peak=UI_PEAK_DB, group="ui", dmin=0.0):
    return dict(name=name, dur=dur, fn=fn, desc=desc, limit=limit, lp=lp, hp=hp, mix=mix, rt=rt, fi=fi,
                fo=fo if fo is not None else max(0.008, 0.2 * dur), peak=peak, group=group, sr=SR_UI, dmin=dmin)


def B(name, dur, fn, desc, **kw):
    kw.setdefault("mix", 0.25)
    kw.setdefault("rt", 0.7)
    kw.setdefault("fo", 0.3)
    return O(name, dur, fn, desc, limit=MAX_BUILD_SEC, group="build", **kw)


ONESHOTS = [
    O("ui_hover", 0.035, c_ui_hover, "8 ms C8 (4.2 kHz) sine tick + faint C7 body", peak=-18.0, mix=0.0,
      lp=13000.0, fo=0.012),
    O("ui_select", 0.16, c_ui_select, "bubble blip G5->C6 over a soft C5 kalimba body", mix=0.10),
    O("grab", 0.25, c_grab, "pluck gliding 660->880 Hz (E5->A5) + 120 Hz thump with a short pitch drop",
      mix=0.10, hp=30.0),
] + [O(f"place_{k + 1}", 0.35, make_place(k), f"wooden tock (G5 block) + marimba {PLACE_NOTES[k]} "
       f"(step {k + 1} of C D E G A C)", mix=0.14) for k in range(6)] + [
    O("build_complete", 1.7, c_build_complete, "marimba+glock arpeggio C5 E5 G5 C6 (90 ms apart), then a "
      "1.3 s shimmering tail: C-major pad, 16 random high glass pings, faint 7 kHz air", limit=1.8,
      mix=0.28, rt=0.7, lp=14000.0, fo=0.3),
    O("listen_start", 0.30, c_listen_start, "soft bell tones G5 then C6, 85 ms apart (rising = mic open)", mix=0.15),
    O("listen_stop", 0.30, c_listen_stop, "soft bell tones C6 then G5, 85 ms apart (falling = mic closed)", mix=0.15),
    O("ack", 0.38, c_ack, "C5+G5 soft bloom: 30 ms swell, brightness opens then closes", mix=0.18),
    O("not_understood", 0.38, c_not_understood, "two rounded low notes E4 -> C4, LP 1.1-1.3 kHz, never a buzzer",
      mix=0.15, lp=5000.0),
    O("reveal_whoosh", 1.0, c_reveal_whoosh, "pink noise band-pass (Q 1.3) sweeping 300->2000 Hz + an "
      "airy band an octave up, swell to 0.65 s then away", limit=1.0, mix=0.15, fo=0.15),
    O("reveal_thump", 0.6, c_reveal_thump, "soft thump settling 115 Hz -> E2 (82 Hz), LP noise puff, nine tiny "
      "crackles", limit=0.6, mix=0.15, hp=25.0),
    O("world_end", 1.0, c_world_end, "descending chimes C6 A5 G5 E5 C5 + a C4/G4 bloom under the last note",
      limit=1.0, mix=0.25, rt=0.7),
    O("souvenir_chime", 1.2, c_souvenir_chime, "C6 'major' bell (hum C5, fifth, octave, tenth, twelfth, slow "
      "beating) + faint G7 glass", limit=1.2, mix=0.25, rt=0.8, lp=13000.0, fo=0.35),
    O("touch", 0.15, c_touch, "soft airy tick (HP 3.5 kHz noise, 4 ms) over a warm G3 body", limit=0.15,
      mix=0.10, lp=11000.0),
    O("item_appear", 0.5, c_item_appear, "glass sparkle C7 E7 G7 A7 C8 E8 every 35 ms + soft C6 + 7.5 kHz air",
      limit=0.5, mix=0.22, lp=14000.0),
    O("fire_ignite", 1.2, c_fire_ignite, "whoomp (pink noise LP opening 180->1600 Hz then closing) + low body "
      "+ a crackle burst thinning from 50/s to 5/s", limit=1.2, mix=0.15, lp=9000.0, hp=30.0, fo=0.3),
    O("wisp_hello", 0.39, c_wisp_hello, "gentle two-note glass chime E6 -> A6 (110 ms apart)", mix=0.22,
      lp=13000.0, fo=0.1),
    B("build_fire", 1.6, c_build_fire, "warm kalimba C4 E4 G4 C5 E5 + C-major pad bloom, a soft flame "
      "whoomp and a few crackles"),
    B("build_sandcastle", 1.5, c_build_sandcastle, "playful marimba C5 E5 G5 E5 A5 C6, sand swishes, "
      "two pats and a glock C7 sparkle"),
    B("build_crystal", 1.6, c_build_crystal, "glass arpeggio C6 E6 G6 C7 E7 G7, beating C6/G6 shimmer pad, "
      "ten high glints", lp=14000.0),
    B("build_snowman", 1.6, c_build_snowman, "three soft snow pats, celesta E5 G5 C6 (+E6 G6), tiny "
      "sleigh-bell glints", lp=13000.0),
    B("build_starmap", 1.8, c_build_starmap, "soft blips in stacked fifths C5 G5 D6 A6 E7 with echoes, "
      "C4/G4/D5 pad and C3 sub swell, faint twinkles", lp=12000.0, rt=1.0),
    B("build_anemone", 1.6, c_build_anemone, "watery notes C5 D5 E5 G5 A5 that wobble into pitch, bubbles, "
      "C/E/A pad; LP 3 kHz", lp=3000.0),
    B("build_jukebox", 1.8, c_build_jukebox, "walking-bass pickup G2 A2 B2 -> C2 with a Cmaj9 Rhodes chord, "
      "D5 E5 fill, brush taps and swish"),
    B("build_blossom", 1.8, c_build_blossom, "harp gliss C5..G6 (pentatonic), petal glass pings, a breeze "
      "swell and two little bird chirps", lp=12000.0),
]

TAP_SEC = 0.35
ONESHOTS += [
    # ---- table taps, one per world ----
    O("tap_campfire", 0.30, c_tap_campfire, "soft log knock (C4 wood modes, small pitch drop) + four spark "
      "crackles 2-5 kHz", limit=TAP_SEC, mix=0.12, group="tap"),
    O("tap_beach", 0.30, c_tap_beach, "sand puff: pink noise LP 1.8 kHz (5 ms attack) + sparse grains above "
      "2.5 kHz + a muffled thud", limit=TAP_SEC, mix=0.08, lp=7000.0, group="tap"),
    O("tap_desert", 0.30, c_tap_desert, "dry sand tick (2.5-9 kHz, 2.5 ms) + six trickling grains + a tiny "
      "E7 rod chime", limit=TAP_SEC, mix=0.15, lp=11000.0, group="tap"),
    O("tap_snow", 0.25, c_tap_snow, "snow crunch: 55 tiny grains front-loaded over 70 ms, a 25-grain settle, "
      "packed-snow thud", limit=TAP_SEC, mix=0.10, lp=7000.0, group="tap"),
    O("tap_space", 0.22, c_tap_space, "console blip E6 (flicks up from just below) over a small metal-plate "
      "tick", limit=TAP_SEC, mix=0.20, lp=11000.0, group="tap"),
    O("tap_reef", 0.28, c_tap_reef, "water bloop: bubble C4->C5 + a small E5->A5 bubble + soft plunk",
      limit=TAP_SEC, mix=0.15, lp=3000.0, group="tap"),
    O("tap_city_1", 0.30, c_tap_city_1, "feathered jazz kick settling 95 Hz -> A1 (55 Hz), felt beater, a "
      "breath of brush (alternate with tap_city_2)", limit=TAP_SEC, mix=0.12, hp=30.0, lp=7000.0, group="tap"),
    O("tap_city_2", 0.30, c_tap_city_2, "brushed snare: three-bristle brush slap (BP 3.2 kHz), G3 drum body, "
      "soft wire rattle", limit=TAP_SEC, mix=0.15, lp=8000.0, group="tap"),
    O("tap_meadow", 0.20, c_tap_meadow, "small wooden tick (C6 block) + a tiny E7->G7 bird chirp",
      limit=TAP_SEC, mix=0.12, lp=11000.0, group="tap"),
] + [
    # ---- Ember, the guide light ----
    O(f"ember_chirp_{k + 1}", EMBER_CHIRPS[k][0], make_ember_chirp(k), f"Ember syllable {EMBER_CHIRPS[k][5]} "
      f"(creature voice: harmonics through morphing vowel formants)", limit=0.110, dmin=0.060, mix=0.12,
      rt=0.4, fo=0.012, group="ember") for k in range(6)
] + [
    O("ember_giggle", 0.35, c_ember_giggle, "three breathy 'hee's rising E5, G5, A5 (each flicks down a step), "
      "105 ms apart", limit=0.35, mix=0.12, rt=0.4, fo=0.04, group="ember"),
    O("ember_wave", 0.38, c_ember_wave, "happy 'hi-ya!': G5->A5, then A5 up to a held C6 with vibrato",
      mix=0.14, rt=0.4, fo=0.04, group="ember"),
    O("ember_yawn", 0.80, c_ember_yawn, "slow yawn: lifts G5->A5 then glides down to C5, vowel 'ah'->'oo', "
      "breathy, vibrato", limit=0.80, mix=0.15, rt=0.5, fo=0.1, group="ember"),
    O("ember_hello", 0.50, c_ember_hello, "excited 'he-llo!': E5->G5, then a G5->C6->A5 arc with vibrato and a "
      "C7 E7 G7 sparkle", limit=0.50, mix=0.16, rt=0.5, lp=12000.0, fo=0.06, group="ember"),
    # ---- campfire roasting ----
    O("golden_chime", 0.85, c_golden_chime, "bright strum E6 G6 C7 (glock) over a soft C6 kalimba + G7/E8 "
      "glints: the marshmallow is perfect", limit=0.85, mix=0.22, rt=0.6, lp=14000.0, fo=0.2, group="roast"),
    O("marshmallow_ignite", 0.60, c_marshmallow_ignite, "small whoomp (pink noise LP opening 300->2500 Hz, "
      "closing to 500) + tiny body + four crackles", limit=0.60, mix=0.12, hp=60.0, fo=0.15, group="roast"),
    O("extinguish", 0.50, c_extinguish, "puff (pink LP 700 Hz, 60 ms) + steam hiss sliding 5.5 -> 3.2 kHz + "
      "three last sputters", limit=0.50, mix=0.10, fo=0.12, group="roast"),
    O("crunch", 0.30, c_crunch, "eating: three bites of granular crunch (40/30/22 grains, 1-4.5 kHz) with a "
      "soft low chomp each", limit=0.30, mix=0.06, lp=7000.0, fo=0.05, group="roast"),
    O("stoke_whoosh", 0.40, c_stoke_whoosh, "palm pumps air: breathy band 400->1100->600 Hz + the fire's "
      "roar swelling (brown LP 300 Hz) + four crackles", limit=0.40, mix=0.10, hp=30.0, fo=0.08, group="roast"),
    O("sparks_rise", 2.0, c_sparks_rise, "26 glass pings climbing C6 -> C8 as the sparks rise and fade, an "
      "airy band rising 2 -> 7 kHz, an initial crackle", limit=2.0, mix=0.25, rt=0.8, lp=13000.0, fo=0.4,
      group="roast"),
    O("constellation", 3.5, c_constellation, "warm C6/9 pad (C3 G3 E4 A4 D5) blooming open (LP 900 -> 3000 Hz "
      "over 1.6 s) + C3 sub + eight faint star pings; peak -16 dBFS", limit=3.5, mix=0.3, rt=1.5, lp=12000.0,
      fi=0.02, fo=0.6, peak=-16.0, group="roast"),
    # ---- others ----
    O("visitor_land", 0.40, c_visitor_land, "soft flutter: five wing flaps (band ~700 Hz) slowing and fading, "
      "then a tiny settle", limit=0.40, mix=0.12, lp=6000.0, fo=0.06, group="misc"),
    O("bloom", 1.2, c_bloom, "a soft tone growing C5->G5 as its brightness opens, kalimba plinks C5..E6 "
      "speeding up, rising air, C7 E7 G7 sparkle", limit=1.2, mix=0.25, rt=0.7, lp=13000.0, fo=0.3, group="misc"),
    O("tide_wave", 2.5, c_tide_wave, "big wave arriving: pink wash swelling as its LP opens 300->1.5k Hz, "
      "crash at 1.5 s (3.5 kHz, low body), foam hiss out; peak -12 dBFS", limit=2.5, mix=0.10, rt=0.8,
      hp=30.0, fo=0.2, peak=-12.0, group="misc"),
    O("fish_dart", 0.25, c_fish_dart, "quick swish: band (Q 1.8) flicking 700->3200->1500 Hz + two small "
      "bubbles; LP 5 kHz", limit=0.25, mix=0.10, lp=5000.0, fo=0.05, group="misc"),
    O("power_on", 1.5, c_power_on, "two relay ticks, A1/A2/E3 hum fading in, a soft tone sweeping C3->C5 as "
      "it brightens, 'ready' G5 -> C6 chime", limit=1.5, mix=0.2, rt=0.6, hp=35.0, fo=0.25, group="misc"),
] + [
    O(f"simon_{k + 1}", 0.25, make_simon(k), f"clean {SIMON_NOTES[k]} tone for the memory game (sine + faint "
      f"2nd/3rd harmonics, 6 ms attack, released at 0.2 s)", limit=0.25, mix=0.08, fo=0.04, group="misc")
    for k in range(5)
] + [
    O("wrong_soft", 0.30, c_wrong_soft, "gentle 'hmm-mm' not-quite: rounded tone gliding E5 -> C5 with two "
      "soft wobbles, LP 1.5 kHz", limit=0.30, mix=0.15, lp=5000.0, fo=0.06, group="misc"),
]


# ----------------------------------------------------------------------------------------------
# WAV I/O and checks
# ----------------------------------------------------------------------------------------------
def write_wav(path, x, sr):
    data = np.clip(np.round(x * 32767.0), -32768, 32767).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(sr)
        w.writeframes(data.tobytes())


def read_wav(path):
    with wave.open(path, "rb") as w:
        assert w.getnchannels() == 1 and w.getsampwidth() == 2
        sr = w.getframerate()
        raw = w.readframes(w.getnframes())
    return np.frombuffer(raw, dtype="<i2").astype(np.float64) / 32768.0, sr


def hf_share_db(x, sr, fc=8000.0):
    X = np.abs(np.fft.rfft(x)) ** 2
    f = np.fft.rfftfreq(len(x), 1.0 / sr)
    return 10 * math.log10((X[f > fc].sum() + 1e-30) / (X.sum() + 1e-30))


def loop_check(x, sr):
    """Seam = sample N-1 -> sample 0. jump/second-difference percentiles rank the seam among all the
    interior steps (50 = a typical step); dRMS compares the first and last 50 ms."""
    w = n_of(0.05, sr)
    d = np.abs(np.diff(x))
    d2 = np.abs(x[2:] - 2 * x[1:-1] + x[:-2])
    jump = abs(x[0] - x[-1])
    d2s = max(abs(x[1] - 2 * x[0] + x[-1]), abs(x[0] - 2 * x[-1] + x[-2]))
    r = dict(jump=jump * 32768, jp=100 * float(np.mean(d < jump)), d2p=100 * float(np.mean(d2 < d2s)),
             drms=abs(rms_db(x[:w]) - rms_db(x[-w:])))
    r["ok"] = r["drms"] < 3.0 and r["jp"] < 99.0 and r["d2p"] < 99.0
    return r


def write_sounds_md(rows):
    def table(group):
        out = ["| file | what it is | length | peak dBFS | RMS dBFS |", "|---|---|---|---|---|"]
        for r in rows:
            if r["group"] in group:
                desc = r["desc"].replace("|", "\\|")
                out.append(f"| `{r['name']}.wav` | {desc} | {r['dur']:.3f} s | {r['peak']:.1f} | {r['rms']:.1f} |")
        return "\n".join(out)
    total = sum(r["bytes"] for r in rows) / 1e6
    md = f"""# Worldeck audio

All {len(rows)} files are synthesised by `Tools/make_sounds.py` (numpy + scipy; no samples; fixed seeds, so a rerun
gives identical files). 16-bit PCM mono WAV, {total:.1f} MB in total. Regenerate with `python Tools/make_sounds.py`.

**Levels.** Beds RMS -24 .. -20 dBFS (the spec's bed level; duck them -6 dB under speech). `fire_loop` and
`jukebox_music` RMS -20 dBFS. Every 48 kHz sound (UI, feedback, builds, taps, Ember, roasting, others, and the
`roast_sizzle` loop) peaks at -14 dBFS, except `ui_hover` -18 dBFS (it repeats a lot), `constellation` -16 dBFS
and `tide_wave` -12 dBFS. Key: every musical note (earcons, flourishes, chimes, beeps, bird whistles, the owl, the space hum
A1/A2/E3) is C major pentatonic (C D E G A); the jazz is diatonic C major. Crickets, gulls, bees, rain and fire keep
their natural pitches.

**Loops.** Continuous layers are rendered 1 s long and the overshoot is equal-power crossfaded into the head;
events and music are rendered circularly. Every loop is exactly periodic: the last sample runs into the first
like any two neighbouring samples. Set the AudioSources to loop; no crossfade is needed in Unity.

## Ambience beds (24 kHz, 24 s seamless loops)

{table({"bed"})}

## Fire and jukebox (24 kHz seamless loops)

{table({"fire", "music"})}

## UI and feedback (48 kHz one-shots)

{table({"ui"})}

## Per-world build flourishes (48 kHz one-shots, at most 2 s)

{table({"build"})}

## Table taps, one per world (48 kHz, at most 0.35 s)

Alternate `tap_city_1` (kick) and `tap_city_2` (snare) like a drummer.

{table({"tap"})}

## Ember, the guide light (48 kHz, wordless creature voice)

`ember_chirp_1..6` are single syllables (60-110 ms) to play one per caption syllable; pick them in a shuffled
order so no two neighbours repeat.

{table({"ember"})}

## Campfire roasting (48 kHz)

{table({"roast"})}

## Others (48 kHz)

{table({"misc"})}
"""
    with open(os.path.join(OUT_DIR, "SOUNDS.md"), "w", encoding="utf-8", newline="\n") as f:
        f.write(md)


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    for c in LOOPS:
        rng = np.random.default_rng(zlib.crc32(c["name"].encode()))
        sr = c["sr"]
        L, X = n_of(c["sec"], sr), n_of(XFADE_SEC, sr)
        res = c["fn"](rng, sr, c["sec"], L, X)
        x, start = res if isinstance(res, tuple) else (res, None)
        assert len(x) == L, c["name"]
        y, _ = finish_loop(x, sr, c["target"], c["hp"], start, c["peak"])
        write_wav(os.path.join(OUT_DIR, c["name"] + ".wav"), y, sr)
    for c in ONESHOTS:
        rng = np.random.default_rng(zlib.crc32(c["name"].encode()))
        sr = c["sr"]
        n = n_of(c["dur"], sr)
        dry = c["fn"](sr, rng, n)
        assert len(dry) == n, c["name"]
        write_wav(os.path.join(OUT_DIR, c["name"] + ".wav"), master_oneshot(dry, sr, c), sr)

    # ---- read everything back and check it ----
    rows, fails = [], 0
    hdr = f"{'file':<22}{'Hz':>6}{'dur s':>8}{'peak':>7}{'RMS':>7}{'>8k':>5}  {'loop seam / edges':<44}result"
    print(f"\nWrote {len(LOOPS) + len(ONESHOTS)} files to {OUT_DIR}\n")
    print(hdr)
    print("-" * len(hdr))
    for c in LOOPS + ONESHOTS:
        path = os.path.join(OUT_DIR, c["name"] + ".wav")
        x, sr = read_wav(path)
        r = dict(name=c["name"], desc=c["desc"], group=c["group"], dur=len(x) / sr, sr=sr, peak=peak_db(x),
                 rms=rms_db(x), hf=hf_share_db(x, sr), bytes=os.path.getsize(path))
        flags = []
        if sr != c["sr"]:
            flags.append("SR")
        if r["peak"] > -1.0:
            flags.append("CLIP")
        if "sec" in c:  # loop
            lc = loop_check(x, sr)
            seam = f"jump {lc['jump']:3.0f} LSB (p{lc['jp']:2.0f}) d2 p{lc['d2p']:2.0f} dRMS50 {lc['drms']:.2f} dB"
            if not lc["ok"]:
                flags.append("SEAM")
            if abs(r["dur"] - c["sec"]) > 1e-9:
                flags.append("DUR")
            if r["peak"] > -3.0:  # keep headroom for the mix
                flags.append("HOT")
            if c["group"] == "bed" and not (BED_RMS_LO - 0.01 <= r["rms"] <= BED_RMS_HI + 0.01):
                flags.append("LEVEL")
            if c["peak"] is not None and abs(r["peak"] - c["peak"]) > 0.3:
                flags.append("PEAK")
        else:
            edge = int(round(max(abs(x[0]), abs(x[-1])) * 32768))
            seam = f"edges {edge} LSB, limit {c['dmin']:.2f}-{c['limit']:.2f} s" if c["dmin"] else \
                f"edges {edge} LSB, limit {c['limit']:.2f} s"
            if edge > 2:
                flags.append("EDGE")
            if (r["dur"] > c["limit"] + 1e-9 or r["dur"] < c["dmin"] - 1e-9
                    or (c["group"] == "ui" and c["limit"] == MAX_UI_SEC and r["dur"] >= MAX_UI_SEC)):  # batch 1: < 0.4 s
                flags.append("DUR")
            if abs(r["peak"] - c["peak"]) > 0.3:
                flags.append("PEAK")
            if r["hf"] > -12.0:
                flags.append("HF")
        fails += bool(flags)
        rows.append(r)
        print(f"{c['name'] + '.wav':<22}{sr:>6}{r['dur']:>8.3f}{r['peak']:>7.1f}{r['rms']:>7.1f}{r['hf']:>5.0f}  "
              f"{seam:<44}{' '.join(flags) or 'ok'}")
    total_mb = sum(r["bytes"] for r in rows) / 1e6
    print(f"\npeak / RMS = dBFS. >8k = share of energy above 8 kHz (dB). Loop seam: jump = |x[N-1] - x[0]|, "
          f"(pXX) = its percentile among all interior sample steps, d2 = same for the second difference, "
          f"dRMS50 = RMS difference of the first and last 50 ms. One-shot edges = |first/last sample|.")
    print(f"Total size {total_mb:.2f} MB (limit {TOTAL_MB_LIMIT:.0f} MB): {'ok' if total_mb < TOTAL_MB_LIMIT else 'OVER'}. "
          f"Failures: {fails}.")
    write_sounds_md(rows)
    print(f"Wrote {os.path.join(OUT_DIR, 'SOUNDS.md')}")
    return 1 if fails or total_mb >= TOTAL_MB_LIMIT else 0


if __name__ == "__main__":
    raise SystemExit(main())
