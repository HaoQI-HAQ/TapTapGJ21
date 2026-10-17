"""Deterministic, original PAO procedural score and effects.

Run from repository root:
    uv run --no-project --with numpy python AudioPipeline/generate_pao_audio.py

Only NumPy is required. This script writes Unity-ready 16-bit PCM WAV files.
Every music loop is built with circular event placement and verified at the seam.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import wave
from pathlib import Path

import numpy as np


ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "Assets/GameMain/Resources/PAOAudio"
SR = 32000
SEED = 2104
TAU = math.tau


def fade(length: int, attack: float = 0.015, release: float = 0.055) -> np.ndarray:
    env = np.ones(length, dtype=np.float32)
    a = min(length, max(1, int(attack * SR)))
    r = min(length, max(1, int(release * SR)))
    env[:a] *= np.sin(np.linspace(0, math.pi / 2, a, dtype=np.float32)) ** 2
    env[-r:] *= np.cos(np.linspace(0, math.pi / 2, r, dtype=np.float32)) ** 2
    return env


def hz(midi: float) -> float:
    return 440.0 * 2.0 ** ((midi - 69.0) / 12.0)


def add_circular(track: np.ndarray, start_seconds: float, sound: np.ndarray, pan: float = 0.0) -> None:
    """Place a mono event into a stereo loop, wrapping its tail across the seam."""
    n = len(track)
    start = round(start_seconds * SR) % n
    mono = sound.astype(np.float32, copy=False)
    left = math.sqrt((1.0 - pan) * 0.5)
    right = math.sqrt((1.0 + pan) * 0.5)
    pos = 0
    while pos < len(mono):
        count = min(len(mono) - pos, n - start)
        track[start : start + count, 0] += mono[pos : pos + count] * left
        track[start : start + count, 1] += mono[pos : pos + count] * right
        pos += count
        start = 0


def pad_note(midi: int, length: float, color: float, phase: float = 0) -> np.ndarray:
    n = round(length * SR)
    t = np.arange(n, dtype=np.float32) / SR
    f = hz(midi)
    # A small triangle component gives the glass body; close detuned oscillators breathe.
    osc = (
        np.sin(TAU * f * t + phase)
        + 0.24 * np.sin(TAU * f * 2.001 * t + 0.4 + phase)
        + 0.13 * np.sin(TAU * f * 3.003 * t + 1.2 + phase)
        + color * np.sin(TAU * f * 0.9991 * t + 0.9 + phase)
    )
    lfo = 0.81 + 0.19 * np.cos(TAU * t / 8.0 + midi * 0.14)
    return (osc * lfo * fade(n, 1.55, 1.55)).astype(np.float32)


def glass_note(midi: float, length: float = 0.9, brightness: float = 1.0) -> np.ndarray:
    n = round(length * SR)
    t = np.arange(n, dtype=np.float32) / SR
    f = hz(midi)
    env = np.exp(-4.6 * t / length) * fade(n, 0.004, 0.08)
    body = np.sin(TAU * f * t) + 0.29 * brightness * np.sin(TAU * f * 2.03 * t)
    body += 0.14 * brightness * np.sin(TAU * f * 3.88 * t)
    return (body * env).astype(np.float32)


def bubble_note(midi: float, length: float = 0.23, upward: bool = True) -> np.ndarray:
    n = round(length * SR)
    t = np.arange(n, dtype=np.float32) / SR
    f0 = hz(midi)
    delta = (170 if upward else -160) * np.exp(-t * 19)
    phase = TAU * np.cumsum(f0 + delta, dtype=np.float64) / SR
    env = np.exp(-t * 15) * fade(n, 0.004, 0.04)
    return (np.sin(phase) + 0.20 * np.sin(phase * 2.04)) * env


def circular_echo(track: np.ndarray, delays: tuple[float, ...], gains: tuple[float, ...]) -> None:
    original = track.copy()
    for delay, gain in zip(delays, gains):
        shifted = np.roll(original, round(delay * SR), axis=0)
        track[:, 0] += shifted[:, 1] * gain
        track[:, 1] += shifted[:, 0] * gain


def periodic_noise(n: int, rng: np.random.Generator, low: float, high: float) -> np.ndarray:
    """Band-limited circular noise; no recorded samples or stock loops."""
    spectrum = np.fft.rfft(rng.standard_normal(n).astype(np.float32))
    frequencies = np.fft.rfftfreq(n, 1 / SR)
    highpass = np.clip(frequencies / max(1, low), 0, 1) ** 2
    lowpass = 1 / (1 + (frequencies / high) ** 5)
    shaped = np.fft.irfft(spectrum * highpass * lowpass, n=n).astype(np.float32)
    rms = float(np.sqrt(np.mean(shaped.astype(np.float64) ** 2)))
    return shaped / max(rms, 1e-8)


def master(track: np.ndarray, peak: float) -> np.ndarray:
    track = np.tanh(track * 0.74)
    current = float(np.max(np.abs(track)))
    if current > 0:
        track *= peak / current
    return track.astype(np.float32)


def music_map4() -> dict[str, np.ndarray]:
    bpm = 96
    beat = 60 / bpm
    bar = beat * 4
    length = 16 * bar
    n = round(length * SR)
    base = np.zeros((n, 2), dtype=np.float32)
    pulse = np.zeros_like(base)
    tension = np.zeros_like(base)

    # D Dorian with one luminous C major turn: memory, resonance, then abyss.
    chords = [
        (50, 53, 57, 64),  # Dm(add9)
        (46, 50, 57, 62),  # Bbmaj7/add9
        (41, 48, 52, 57),  # Fmaj7
        (48, 55, 62, 64),  # Csus2/add3
    ]
    for i, chord in enumerate(chords):
        start = i * 4 * bar - 1.5
        for j, note in enumerate(chord):
            add_circular(base, start, pad_note(note, 4 * bar + 3, 0.09 + j * 0.013, j * 0.7) * (0.30 if j else 0.39), pan=(-0.32, 0.31, -0.14, 0.23)[j])
        add_circular(base, start, pad_note(chord[0] - 12, 4 * bar + 3, 0.04) * 0.23)

    motif = [74, 77, 81, 76, 74, 69, 72, 76]
    for bar_index in range(16):
        b = bar_index * bar
        shape = (0.0, 1.5, 2.75) if bar_index % 4 != 3 else (0.0, 1.75, 3.25)
        for j, offset in enumerate(shape):
            note = motif[(bar_index * 2 + j) % len(motif)]
            level = 0.26 if j == 0 else 0.18
            add_circular(pulse, b + offset * beat, glass_note(note, 0.84, 0.75) * level, pan=(-0.5, 0.25, 0.56)[j])
        if bar_index % 2 == 1:
            add_circular(pulse, b + 3.7 * beat, bubble_note(81 + (bar_index % 4) * 2, 0.25) * 0.15, pan=0.25)
    circular_echo(pulse, (0.37, 0.74), (0.20, 0.10))

    for bar_index in range(16):
        b = bar_index * bar
        root = (38, 34, 29, 36)[bar_index // 4]
        # Slow heartbeats give urgency without imposing a percussion kit.
        for offset, strength in ((0, 0.24), (2.35, 0.15)):
            add_circular(tension, b + offset * beat, bubble_note(root, 0.9, upward=False) * strength, pan=-0.07)
        if bar_index % 2 == 1:
            add_circular(tension, b + 3.45 * beat, glass_note(74 if bar_index < 8 else 73, 1.0, 0.22) * 0.07, pan=0.42)
    # Periodic, narrow beating is audible under the pulse but remains soft for narration.
    t = np.arange(n, dtype=np.float32) / SR
    phase = TAU * t / length
    swell = 0.5 + 0.5 * np.sin(phase - 0.8) ** 2
    undertow = (np.sin(TAU * 55.0 * t) + 0.4 * np.sin(TAU * 57.0 * t)) * swell * 0.035
    tension[:, 0] += undertow
    tension[:, 1] += undertow * 0.9
    circular_echo(tension, (0.46, 0.94), (0.17, 0.08))

    return {
        "Map4_Base": master(base, 0.23),
        "Map4_Pulse": master(pulse, 0.18),
        "Map4_Tension": master(tension, 0.16),
    }


def music_other(scene: str, root: int, mode: str, bpm: int) -> np.ndarray:
    beat = 60 / bpm
    bar = beat * 4
    length = 12 * bar
    track = np.zeros((round(length * SR), 2), dtype=np.float32)
    if mode == "bright":
        chords = [(0, 4, 7, 11), (5, 9, 12, 16), (2, 5, 9, 12)]
        melody = (12, 16, 19, 16, 14, 11)
    elif mode == "strange":
        chords = [(0, 3, 7, 14), (8, 12, 15, 19), (5, 9, 12, 17)]
        melody = (12, 15, 19, 17, 14, 12)
    else:
        chords = [(0, 3, 7, 14), (8, 12, 15, 19), (10, 14, 17, 22)]
        melody = (12, 15, 19, 15, 10, 14)
    for i, intervals in enumerate(chords):
        for j, interval in enumerate(intervals):
            note = root + interval
            level = 0.28 if j == 0 else 0.21
            add_circular(track, i * 4 * bar - 1.4, pad_note(note, 4 * bar + 2.8, 0.08, j * 0.8) * level, (-0.3, 0.32, -0.2, 0.25)[j])
    for k in range(12):
        if k % 3 != 2 or scene in ("Map0", "Map1", "Menu"):
            index = (k + (0 if mode == "bright" else 2)) % len(melody)
            add_circular(track, k * bar + (1.5 if k % 2 else 0.25) * beat,
                         glass_note(root + melody[index] + 12, 1.35, 0.48) * (0.11 if mode != "bright" else 0.15),
                         -0.38 if k % 2 else 0.34)
    circular_echo(track, (0.41, 0.83), (0.17, 0.08))
    return master(track, 0.22)


def ambience_map4() -> dict[str, np.ndarray]:
    n = 40 * SR
    t = np.arange(n, dtype=np.float32) / SR
    cycle = TAU * t / 40.0
    rng = np.random.default_rng(SEED + 400)
    reef = np.zeros((n, 2), dtype=np.float32)
    vent = np.zeros_like(reef)
    trench = np.zeros_like(reef)

    soft_current = periodic_noise(n, rng, 55, 560)
    effervescence = periodic_noise(n, rng, 420, 3500)
    reef_swell = 0.45 + 0.35 * np.sin(cycle * 5 + 0.7) ** 2
    reef[:, 0] = soft_current * reef_swell * 0.022 + effervescence * 0.006
    reef[:, 1] = np.roll(soft_current, 157) * reef_swell * 0.020 + np.roll(effervescence, 211) * 0.006
    for i in range(24):
        start = (i * 1.618 + 0.42 * math.sin(i * 2.7)) % 40
        add_circular(reef, start, bubble_note(73 + i % 7, 0.18) * 0.0055, pan=math.sin(i * 1.9) * 0.7)

    vent_rumble = periodic_noise(n, rng, 22, 140)
    vent_hiss = periodic_noise(n, rng, 350, 2500)
    vent_swell = (0.35 + 0.65 * np.sin(cycle * 4 - 0.4) ** 8).astype(np.float32)
    vent[:, 0] = vent_rumble * 0.023 + vent_hiss * vent_swell * 0.009
    vent[:, 1] = np.roll(vent_rumble, 229) * 0.021 + np.roll(vent_hiss, 327) * vent_swell * 0.008
    for i in range(8):
        add_circular(vent, i * 5 + 2.3, bubble_note(45 + i % 3, 0.72, upward=False) * 0.024, pan=0.22 if i % 2 else -0.22)

    trench_floor = periodic_noise(n, rng, 16, 95)
    trench_grain = periodic_noise(n, rng, 120, 950)
    trench_swell = (0.48 + 0.33 * np.cos(cycle * 3 + 0.3)).astype(np.float32)
    trench[:, 0] = trench_floor * trench_swell * 0.024 + trench_grain * 0.0032
    trench[:, 1] = np.roll(trench_floor, 397) * trench_swell * 0.023 + np.roll(trench_grain, 533) * 0.0032
    for i in range(5):
        add_circular(trench, 2.5 + i * 7.6, glass_note(69 + (i % 3) * 2, 2.1, 0.1) * 0.007, pan=(-0.6 if i % 2 else 0.6))

    return {
        "Map4_Reef": master(reef, 0.10),
        "Map4_Vent": master(vent, 0.10),
        "Map4_Trench": master(trench, 0.10),
    }


def filtered_noise(n: int, rng: np.random.Generator, smooth: int = 64) -> np.ndarray:
    noise = rng.standard_normal(n + smooth).astype(np.float32)
    total = np.cumsum(noise, dtype=np.float64)
    filtered = (total[smooth:] - total[:-smooth]) / math.sqrt(smooth)
    return filtered.astype(np.float32)


def effect(name: str, duration: float, rng: np.random.Generator) -> np.ndarray:
    n = round(duration * SR)
    t = np.arange(n, dtype=np.float32) / SR
    p = t / duration
    airy = filtered_noise(n, rng, 80)
    bright = filtered_noise(n, rng, 8)

    def chirp(a: float, b: float, speed: float = 1.0) -> np.ndarray:
        freq = a + (b - a) * np.minimum(1, p ** speed)
        return np.sin(TAU * np.cumsum(freq, dtype=np.float64) / SR).astype(np.float32)

    if name == "BubbleCharge":
        x = (chirp(175, 440, 1.8) + 0.27 * chirp(350, 875)) * p ** 1.5 + airy * p * 0.13
    elif name == "BubbleLaunch":
        x = chirp(490, 185, 0.7) * np.exp(-p * 2.9) + bright * np.exp(-p * 8) * 0.18
    elif name == "BubbleCollision":
        x = chirp(255, 105, 0.3) * np.exp(-p * 7) + bright * 0.075 * np.exp(-p * 22)
    elif name == "BubbleMerge":
        x = (chirp(310, 480) + chirp(410, 480, 0.5)) * 0.34 + np.sin(TAU * 720 * t) * np.exp(-p * 3) * 0.11
        x *= np.sin(np.pi * p) ** 0.5
    elif name == "BubbleBounce":
        x = chirp(215, 460, 0.25) * np.exp(-p * 6)
    elif name == "BubbleBurst":
        x = bright * np.exp(-p * 7) * 0.58 + chirp(165, 67, 0.4) * np.exp(-p * 5) * 0.7
    elif name == "BubblePop":
        x = chirp(650, 210, 0.4) * np.exp(-p * 9) + bright * np.exp(-p * 18) * 0.22
    elif name == "BubbleSwitch":
        x = (chirp(410, 650) + 0.33 * chirp(820, 1050)) * np.exp(-p * 3.2)
    elif name == "PlayerJump":
        x = chirp(220, 380, 0.55) * np.exp(-p * 3) + airy * np.exp(-p * 4) * 0.10
    elif name == "PlayerLand":
        x = chirp(150, 75, 0.25) * np.exp(-p * 6) + airy * np.exp(-p * 8) * 0.22
    elif name == "PlayerStep":
        x = chirp(170, 110, 0.25) * np.exp(-p * 9) * 0.5 + airy * np.exp(-p * 12) * 0.17
    elif name == "Pickup":
        x = glass_note(78, duration, 0.7) + 0.45 * np.roll(glass_note(85, duration, 0.6), round(0.11 * SR))
        x[:round(0.11 * SR)] -= 0.45 * np.roll(glass_note(85, duration, 0.6), round(0.11 * SR))[:round(0.11 * SR)]
    elif name == "TideRising":
        x = airy * (p ** 0.8) * 0.28 + chirp(55, 112, 1.5) * p ** 1.2 * 0.53
    elif name == "TideWin":
        x = glass_note(74, duration, 0.55) * 0.46
        x += glass_note(78, duration, 0.55) * 0.42
        x += glass_note(81, duration, 0.55) * 0.41
        x += glass_note(86, duration, 0.35) * 0.25
    elif name == "TideLose":
        x = chirp(310, 85, 0.7) * np.exp(-p * 2.4) + airy * np.exp(-p * 3) * 0.15
    elif name == "Narration":
        x = glass_note(81, duration, 0.35) * 0.6 + glass_note(74, duration, 0.2) * 0.2
    elif name == "UIHover":
        x = glass_note(81, duration, 0.25) * 0.28
    elif name == "UIConfirm":
        x = glass_note(76, duration, 0.5) * 0.38 + glass_note(83, duration, 0.5) * 0.31
    elif name == "UIBack":
        x = chirp(420, 305) * np.exp(-p * 5) * 0.52
    elif name == "UISoftOpen":
        x = airy * np.sin(np.pi * p) * 0.12 + glass_note(74, duration, 0.3) * 0.3
    else:
        raise ValueError(name)
    x = np.asarray(x, dtype=np.float32)
    x *= fade(n, min(0.008, duration * 0.06), min(0.07, duration * 0.15))
    return master(x, 0.43 if name not in ("PlayerStep", "UIHover", "Narration") else 0.22)


def save_wav(path: Path, signal: np.ndarray, loop: bool) -> dict:
    path.parent.mkdir(parents=True, exist_ok=True)
    samples = np.clip(signal, -1, 1)
    if loop:
        # Distribute the residual boundary offset over 40 ms. The arrangement
        # already wraps every note and echo; this removes floating phase error.
        count = round(0.04 * SR)
        x = np.linspace(0, 1, count, dtype=np.float32)
        correction = x * x * (3 - 2 * x)
        samples[-count:] += correction[:, None] * (samples[0] - samples[-1])
    pcm = np.round(samples * 32767).astype("<i2")
    with wave.open(str(path), "wb") as out:
        out.setnchannels(1 if pcm.ndim == 1 else pcm.shape[1])
        out.setsampwidth(2)
        out.setframerate(SR)
        out.writeframes(pcm.tobytes())
    rms = float(np.sqrt(np.mean(samples.astype(np.float64) ** 2)))
    seam = float(np.max(np.abs(samples[0] - samples[-1]))) if loop else None
    data = path.read_bytes()
    return {
        "resource": str(path.relative_to(OUT).with_suffix("")).replace("\\", "/"),
        "seconds": round(len(samples) / SR, 5),
        "samples": len(samples),
        "channels": 1 if pcm.ndim == 1 else pcm.shape[1],
        "sample_rate": SR,
        "rms": round(rms, 6),
        "peak": round(float(np.max(np.abs(samples))), 6),
        "seam_delta": round(seam, 6) if seam is not None else None,
        "sha256": hashlib.sha256(data).hexdigest(),
    }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--verify", action="store_true", help="Check existing WAVs against the manifest")
    args = parser.parse_args()
    manifest_path = OUT / "audio_manifest.json"
    if args.verify:
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        loop_lengths = {}
        for item in manifest["assets"]:
            path = OUT / (item["resource"] + ".wav")
            assert path.exists(), path
            assert hashlib.sha256(path.read_bytes()).hexdigest() == item["sha256"], path
            with wave.open(str(path), "rb") as source:
                assert source.getframerate() == SR
                assert source.getnframes() == item["samples"]
                assert source.getnchannels() == item["channels"]
                assert source.getsampwidth() == 2
                pcm = np.frombuffer(source.readframes(source.getnframes()), dtype="<i2")
            assert len(pcm) == item["samples"] * item["channels"]
            assert 0.001 < float(np.sqrt(np.mean((pcm.astype(np.float32) / 32767) ** 2))) < 0.2, path
            assert np.max(np.abs(pcm.astype(np.int32))) < 24000, path
            if item["seam_delta"] is not None:
                assert item["seam_delta"] < 0.004, (path, item["seam_delta"])
                pcm = pcm.reshape((-1, item["channels"]))
                assert np.max(np.abs(pcm[0].astype(np.int32) - pcm[-1].astype(np.int32))) <= 1, path
                if item["resource"].startswith(("Music/Map4_", "Ambience/Map4_")):
                    loop_lengths[item["resource"]] = item["samples"]
        assert len(loop_lengths) == 6 and len(set(loop_lengths.values())) == 1, loop_lengths
        print(f"Verified {len(manifest['assets'])} WAV assets")
        return

    assets = []
    for filename, music in music_map4().items():
        assets.append(save_wav(OUT / "Music" / f"{filename}.wav", music, True))
    for filename, ambience in ambience_map4().items():
        assets.append(save_wav(OUT / "Ambience" / f"{filename}.wav", ambience, True))
    scenes = [
        ("Menu", 48, "bright", 90),
        ("Map0", 50, "bright", 92),
        ("Map1", 52, "bright", 95),
        ("Map2", 47, "strange", 92),
        ("Map3", 45, "strange", 88),
        ("Map5", 48, "dark", 84),
    ]
    for scene, root, mode, bpm in scenes:
        assets.append(save_wav(OUT / "Music" / f"{scene}_Base.wav", music_other(scene, root, mode, bpm), True))
    lengths = {
        "BubbleCharge": 0.6, "BubbleLaunch": 0.34, "BubbleCollision": 0.25,
        "BubbleMerge": 0.65, "BubbleBounce": 0.31, "BubbleBurst": 0.62,
        "BubblePop": 0.26, "BubbleSwitch": 0.37, "PlayerJump": 0.32,
        "PlayerLand": 0.31, "PlayerStep": 0.16, "Pickup": 0.78,
        "TideRising": 1.4, "TideWin": 1.6, "TideLose": 1.2,
        "Narration": 0.56, "UIHover": 0.19, "UIConfirm": 0.43,
        "UIBack": 0.3, "UISoftOpen": 0.65,
    }
    for i, (name, length) in enumerate(lengths.items()):
        assets.append(save_wav(OUT / "SFX" / f"{name}.wav", effect(name, length, np.random.default_rng(SEED + i)), False))
    manifest = {
        "generator": "AudioPipeline/generate_pao_audio.py",
        "copyright": "Original procedural synthesis for PAO; no sampled third-party material",
        "music_looping": "PCM sample-exact; set Unity AudioSource.loop = true",
        "assets": assets,
    }
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Generated {len(assets)} assets at {OUT}")
    for item in assets:
        if item["seam_delta"] is not None:
            print(f"{item['resource']}: {item['seconds']:.2f}s, rms {item['rms']:.4f}, seam {item['seam_delta']:.6f}")


if __name__ == "__main__":
    main()
