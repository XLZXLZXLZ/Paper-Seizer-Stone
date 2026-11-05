"""Create the short card-action variants. Requires numpy, scipy and soundfile.

Run from any directory; original OGG files and existing Unity GUIDs are preserved.
This is an offline asset authoring tool, not an Editor menu or runtime dependency.
"""

import json
import re
import uuid
from pathlib import Path

import numpy as np
import soundfile as sf
from scipy.signal import correlate, resample_poly


ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "Assets/Game/Audio/SFX/Cards"
DESTINATION = SOURCE / "Short"
MANIFEST = ROOT / "Tools/Audio/card_sound_edits.json"
# Maximum delivered duration, energy retained at the start/end, respectively.
EDITS = {
    "card-fan-1": (0.130, 0.010, 0.995),
    "card-fan-2": (0.150, 0.010, 0.995),
    "card-slide-1": (0.090, 0.002, 0.999),
    "card-slide-2": (0.090, 0.002, 0.999),
    "card-place-1": (0.110, 0.002, 0.999),
    "card-place-2": (0.130, 0.002, 0.999),
    "card-place-3": (0.120, 0.002, 0.999),
    "card-shove-1": (0.160, 0.010, 0.960),
    "card-shove-2": (0.160, 0.010, 0.960),
    "card-shuffle": (0.400, 0.005, 0.995),
}


def compress_gesture(data, rate, frames):
    """Waveform-similar overlap/add: shorten a noise gesture without shifting its pitch."""
    if len(data) <= frames:
        return data.copy()
    window = min(round(rate * 0.020), frames // 3)
    hop = max(1, window // 2)
    search = round(rate * 0.003)
    envelope = np.hanning(window + 2)[1:-1]
    output = np.zeros((frames, data.shape[1]), dtype=np.float64)
    weights = np.zeros(frames, dtype=np.float64)
    positions = list(range(0, frames - window + 1, hop))
    if positions[-1] != frames - window:
        positions.append(frames - window)
    for index, position in enumerate(positions):
        desired = round(position * (len(data) - window) / max(1, frames - window))
        chosen = desired
        if 0 < index < len(positions) - 1:
            overlap = min(hop, window)
            reference = np.mean(output[position:position + overlap]
                                / np.maximum(weights[position:position + overlap, None], 1e-12), axis=1)
            left = max(0, desired - search)
            right = min(len(data) - window, desired + search)
            candidates = np.mean(data[left:right + overlap], axis=1)
            numerator = correlate(candidates, reference, mode="valid", method="direct")
            energy = np.convolve(candidates * candidates, np.ones(overlap), mode="valid")
            denominator = np.sqrt(np.maximum(energy * np.dot(reference, reference), 1e-18))
            chosen = left + int(np.argmax(numerator / denominator))
        output[position:position + window] += data[chosen:chosen + window] * envelope[:, None]
        weights[position:position + window] += envelope
    return output / np.maximum(weights[:, None], 1e-12)


def meta_guid(path, template):
    if path.exists():
        return re.search(r"^guid: (\w+)", path.read_text(encoding="utf-8-sig"), re.M).group(1)
    guid = uuid.uuid4().hex
    content = re.sub(r"^guid: \w+", "guid: " + guid, template, count=1, flags=re.M)
    # Levels are authored below; Unity should not normalize them a second time.
    content = content.replace("  normalize: 1", "  normalize: 0")
    path.write_text(content, encoding="utf-8")
    return guid


def main():
    DESTINATION.mkdir(parents=True, exist_ok=True)
    folder_meta = DESTINATION.with_suffix(".meta")
    if not folder_meta.exists():
        folder_meta.write_text("fileFormatVersion: 2\nguid: " + uuid.uuid4().hex
                               + "\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n"
                               + "  userData: \n  assetBundleName: \n  assetBundleVariant: \n", encoding="utf-8")
    entries = []
    for stem, (maximum, start_energy, end_energy) in EDITS.items():
        source = SOURCE / (stem + ".ogg")
        data, rate = sf.read(source, dtype="float64", always_2d=True)
        original_frames = len(data)
        data -= np.mean(data, axis=0, keepdims=True)
        cumulative = np.cumsum(np.mean(data * data, axis=1))
        start = max(0, int(np.searchsorted(cumulative, cumulative[-1] * start_energy)) - round(rate * 0.002))
        end = min(len(data), int(np.searchsorted(cumulative, cumulative[-1] * end_energy)) + round(rate * 0.008))
        gesture = data[start:end]
        # First condense the long gesture while retaining pitch; then add a small, crisp 8% acceleration.
        condensed_frames = max(64, round(maximum * rate * 1.08))
        short = compress_gesture(gesture, rate, condensed_frames)
        short = resample_poly(short, 25, 27, axis=0)
        short = short[:round(maximum * rate)]
        fade_in = min(round(rate * 0.0007), len(short) // 4)
        fade_out = min(round(rate * 0.008), len(short) // 4)
        short[:fade_in] *= np.linspace(0, 1, fade_in)[:, None]
        short[-fade_out:] *= np.linspace(1, 0, fade_out)[:, None]
        gain = min(2.0, 0.75 / max(float(np.max(np.abs(short))), 1e-12))
        short *= gain
        destination = DESTINATION / (stem + ".wav")
        sf.write(destination, short, rate, subtype="PCM_16")
        guid = meta_guid(destination.with_suffix(".wav.meta"), source.with_suffix(".ogg.meta").read_text(encoding="utf-8-sig"))
        entry = {
            "source": source.relative_to(ROOT).as_posix(),
            "output": destination.relative_to(ROOT).as_posix(),
            "guid": guid,
            "source_seconds": round(original_frames / rate, 4),
            "source_selection_seconds": [round(start / rate, 4), round(end / rate, 4)],
            "output_seconds": round(len(short) / rate, 4),
            "gain": round(gain, 4),
        }
        entries.append(entry)
        print(f"{stem}: {entry['source_seconds']:.3f}s -> {entry['output_seconds']:.3f}s")
    MANIFEST.write_text(json.dumps({"method": "energy trim, waveform-similar time compression, 1.08x acceleration, edge fades",
                                    "files": entries}, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
