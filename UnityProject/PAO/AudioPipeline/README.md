# PAO audio generation

All shipped WAV files in `Assets/GameMain/Resources/PAOAudio/` are original, deterministic synthesis. No recordings, sample packs, licensed tracks, paid API, or placeholder music are used.

From the Unity project root:

```powershell
uv run --no-project --with numpy python AudioPipeline/generate_pao_audio.py
uv run --no-project --with numpy python AudioPipeline/generate_pao_audio.py --verify
```

The generator writes 32 kHz, signed 16-bit PCM WAV and `audio_manifest.json` with SHA-256, duration, channel count, RMS, peak, and loop seam data. `--verify` checks all listed files. Music and ambience use stereo; positional one-shots use mono. The first three music files (`Map4_Base`, `Map4_Pulse`, `Map4_Tension`) and all three Map4 ambience files are exactly 40 seconds and can start at the same DSP sample. Set `AudioSource.loop = true` and schedule the music stems together with `PlayScheduled` for aligned layers. Crossfade their source volumes to move between exploration, resonance, and tension. The `Map4_Base` stem is the foundation; keep `Pulse` and `Tension` quieter. SFX names correspond to `PAO.AV.FeedbackCue`, plus `PlayerStep`, `UIBack`, and `UISoftOpen`.

Ambience routing: `Map4_Reef` for coral/memory gardens, `Map4_Vent` for pressure vents, and `Map4_Trench` for the abyssal/resonance trench. Their RMS is substantially below the score's base stem. Crossfade area sources rather than stopping abruptly.

For new scenes, add a `(scene, root MIDI note, mode, bpm)` tuple in `scenes` and rerun. For new effects, add a `lengths` entry and an `effect` branch. The synthesis seed is fixed. Unity creates `.meta` files on import; commit those along with the WAV files. Voice acting is intentionally absent: `SFX/Narration` is a short cue for subtitle entrance, not synthesized speech.
