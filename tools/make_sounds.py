#!/usr/bin/env python3
"""Synthesises AVASalvo's sound effects and tunes into ../src/AVASalvo/Sounds as 16-bit mono WAV.

Nothing is sampled: every effect is built here from square, triangle, sine and noise voices, and the tunes are new
compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave


RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVASalvo", "Sounds")
RNG = random.Random(1983)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


def echo(samples, delay=0.11, feedback=0.35, repeats=3):
    """The voice in a big hall: quieter copies of itself, later and later."""
    d = int(RATE * delay)
    out = samples + [0.0] * (d * repeats)
    for k in range(1, repeats + 1):
        g = feedback ** k
        for i, v in enumerate(samples):
            out[i + d * k] += v * g
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def line(bpm, notes, kind="pulse", mult=1.0, gain=0.55, release=0.6):
    """A tune as (note, beats), '-' a rest, a list of notes a chord; mult transposes it."""
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        if note == "-":
            out.extend(silence(d))
        elif isinstance(note, list):
            out.extend(mix(*[tone(d, hz(n) * mult, kind=kind, release=release, gain=gain / len(note) ** 0.5) for n in note]))
        else:
            out.extend(tone(d, hz(note) * mult, kind=kind, release=release, gain=gain))
    return out


def kick(seconds, gain=1.0):
    return cat(tone(0.07, 150, 45, "sine", attack=0.001, release=1.0, gain=gain), silence(max(0, seconds - 0.07)))


def hat(seconds, gain=0.3):
    return cat(tone(0.03, 9000, kind="noise", attack=0.001, release=2.0, gain=gain), silence(max(0, seconds - 0.03)))


def snare(seconds, gain=0.6):
    return cat(mix(tone(0.12, 3000, kind="noise", attack=0.001, release=1.6, gain=gain),
                   tone(0.08, 220, 160, "tri", attack=0.001, release=1.0, gain=gain * 0.6)), silence(max(0, seconds - 0.12)))



def boom(seconds, low=70, gain=1.0):
    """A blast: a falling thump under a roar of noise."""
    return mix(tone(seconds, low * 2.2, low * 0.5, "sine", attack=0.002, release=1.3, gain=gain),
               tone(seconds, 1800, 300, "noise", attack=0.002, release=1.6, gain=gain * 0.8))


def march():
    """The title tune: a brisk little march, tune over a walking bass and a snare."""
    bpm = 132
    tune = [("G4", 0.5), ("C5", 0.5), ("E5", 0.5), ("G5", 1), ("E5", 0.5), ("G5", 1.5), ("-", 0.5),
            ("F5", 0.5), ("E5", 0.5), ("D5", 0.5), ("E5", 1), ("C5", 0.5), ("D5", 1.5), ("-", 0.5),
            ("G4", 0.5), ("C5", 0.5), ("E5", 0.5), ("G5", 1), ("A5", 0.5), ("G5", 1), ("E5", 0.5), ("C5", 0.5),
            ("D5", 0.5), ("F5", 0.5), ("E5", 0.5), ("D5", 0.5), ("C5", 2), ("-", 1)]
    bass = [("C3", 1), ("G2", 1), ("C3", 1), ("G2", 1), ("F2", 1), ("C3", 1), ("G2", 1), ("G2", 1),
            ("C3", 1), ("G2", 1), ("A2", 1), ("E2", 1), ("F2", 1), ("G2", 1), ("C3", 2)]
    beat = 60.0 / bpm
    drums = []
    for i in range(16):
        drums += snare(beat * 0.5, 0.25) if i % 2 else kick(beat * 0.5, 0.5)
        drums += silence(beat * 0.5)
    return mix(line(bpm, tune, "pulse", gain=0.5), line(bpm, bass, "tri", gain=0.6), drums)


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))

    # Firing: a crack and a whoosh; the heavy shell, deeper.
    write("Fire.wav", mix(tone(0.25, 300, 90, "noise", attack=0.001, release=1.5), tone(0.2, 180, 60, "square", attack=0.001, release=1.2, gain=0.4)), 0.4)
    write("FireHeavy.wav", mix(tone(0.4, 160, 45, "noise", attack=0.001, release=1.3), tone(0.35, 110, 40, "square", attack=0.001, release=1.0, gain=0.5)), 0.5)
    write("Split.wav", cat(*[tone(0.04, 1400 - k * 180, kind="pulse", release=0.8) for k in range(5)]), 0.2)
    # Blasts.
    write("Boom.wav", boom(0.6, 80), 0.6)
    write("BigBoom.wav", mix(boom(1.1, 55), tone(0.9, 400, 60, "noise", attack=0.05, release=0.8, gain=0.5)), 0.7)
    write("Dirt.wav", mix(tone(0.5, 600, 120, "noise", attack=0.01, release=1.0), tone(0.3, 90, 50, "sine", release=1.2, gain=0.6)), 0.45)
    write("Dig.wav", cat(*[tone(0.05, 220 + (k % 2) * 80, kind="noise", release=0.5) for k in range(14)]), 0.3)
    write("Thud.wav", mix(tone(0.2, 120, 50, "sine", attack=0.001, release=1.4), tone(0.08, 900, kind="noise", attack=0.001, release=2.0, gain=0.4)), 0.4)
    # A tank hit: a clang; a tank destroyed: a clang and a long rumble.
    write("Hit.wav", mix(tone(0.35, 660, 640, "square", attack=0.001, release=1.8, gain=0.5), tone(0.35, 990, 960, "tri", attack=0.001, release=1.8, gain=0.4)), 0.32)
    write("TankDeath.wav", cat(tone(0.08, 880, 600, "square", release=0.8), boom(1.2, 45)), 0.6)
    # A new turn; the menus; changing weapon.
    write("Turn.wav", cat(tone(0.06, hz("C5"), kind="pulse", release=0.6), tone(0.1, hz("G5"), kind="pulse", release=0.8)), 0.2)
    write("Select.wav", tone(0.05, 1200, kind="pulse", release=0.8), 0.16)
    write("Weapon.wav", cat(tone(0.04, 500, kind="square", release=0.6), tone(0.05, 750, kind="square", release=0.8)), 0.16)
    # A round won: a fanfare; the match over: a longer one.
    write("RoundWin.wav", mix(line(150, [("C5", 0.5), ("E5", 0.5), ("G5", 0.5), ("C6", 1.5)], "pulse"), line(150, [("C3", 1), ("G2", 0.5), ("C3", 1.5)], "tri")), 0.4)
    write("MatchOver.wav", mix(line(110, [("G4", 0.5), ("G4", 0.5), ("G4", 0.5), ("C5", 1.5), ("G5", 1), ("E5", 0.5), ("C5", 0.5), ("G5", 0.5), (["C6", "E6"], 3)], "pulse"),
                               line(110, [("C3", 1), ("G2", 1), ("C3", 1), ("E3", 1), ("G2", 1), ("C3", 3)], "tri")), 0.45)
    write("TitleTune.wav", cat(march(), silence(1.0)), 0.32)


if __name__ == "__main__":
    main()
