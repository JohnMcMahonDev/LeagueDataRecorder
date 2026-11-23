#!/usr/bin/env python3
"""Transcribe League of Legends comms with faster-whisper + NeMo-Speech diarization.

Pipeline:
  1. Decode the source audio with faster-whisper/PyAV to mono 16 kHz float32.
  2. Write a temporary 16 kHz PCM16 WAV for NeMo-Speech.cpp.
  3. Run NeMo-Speech standalone diarization exactly once over the full recording,
     producing a global RTTM file.
  4. Load Whisper once, transcribe overlapping chunks with word timestamps.
  5. Keep words whose midpoint is in the chunk's non-overlap/core interval.
  6. Assign each word the RTTM speaker with the greatest time overlap.
  7. Save JSON containing global word timestamps and speaker IDs.

The script deliberately does not use Nemotron ASR, speech_context_boost, or
boosting_tree_alpha. League terms are passed to Whisper as a compact hotword list.
"""

from __future__ import annotations

import argparse
import json
import math
import os
import shutil
import subprocess
import sys
import tempfile
import time
import wave
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Iterable, Optional

import numpy as np
from faster_whisper import WhisperModel
from faster_whisper.audio import decode_audio

SAMPLE_RATE = 16_000


@dataclass
class DiarizationTurn:
    start: float
    end: float
    speaker: str


@dataclass
class WordResult:
    start: float
    end: float
    word: str
    probability: Optional[float]
    speaker: Optional[str]


def log(message: str) -> None:
    print(message, flush=True)


def resolve_executable(name_or_path: str) -> str:
    """Resolve nemo-speech, accepting either a PATH command or explicit path."""
    candidate = Path(name_or_path).expanduser()
    if candidate.exists():
        return str(candidate.resolve())
    found = shutil.which(name_or_path)
    if found:
        return found
    raise FileNotFoundError(
        f"Could not find '{name_or_path}'. Install NeMo-Speech.cpp and reopen "
        "PowerShell, or pass --nemo-speech with the full path to nemo-speech.exe."
    )


def write_pcm16_mono_wav(path: Path, audio: np.ndarray, sample_rate: int) -> None:
    """Write a mono float32 waveform as 16-bit PCM WAV using only stdlib wave."""
    data = np.asarray(audio, dtype=np.float32)
    if data.ndim != 1:
        raise ValueError(f"Expected mono audio after decoding; got shape {data.shape}")
    data = np.nan_to_num(data, nan=0.0, posinf=0.0, neginf=0.0)
    pcm = (np.clip(data, -1.0, 1.0) * 32767.0).astype("<i2", copy=False)
    path.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(path), "wb") as wav_file:
        wav_file.setnchannels(1)
        wav_file.setsampwidth(2)
        wav_file.setframerate(sample_rate)
        wav_file.writeframes(pcm.tobytes())


def read_rttm(path: Path) -> list[DiarizationTurn]:
    """Parse NIST RTTM SPEAKER rows."""
    turns: list[DiarizationTurn] = []
    with path.open("r", encoding="utf-8", errors="replace") as handle:
        for line_number, line in enumerate(handle, start=1):
            line = line.strip()
            if not line or line.startswith("#"):
                continue
            fields = line.split()
            if fields[0].upper() != "SPEAKER":
                continue
            if len(fields) < 8:
                log(f"Warning: skipping malformed RTTM line {line_number}: {line}")
                continue
            try:
                start = float(fields[3])
                duration = float(fields[4])
                speaker = fields[7]
            except (ValueError, IndexError):
                log(f"Warning: skipping malformed RTTM line {line_number}: {line}")
                continue
            if duration <= 0:
                continue
            turns.append(DiarizationTurn(start, start + duration, speaker))
    turns.sort(key=lambda item: (item.start, item.end, item.speaker))
    return turns


def run_diarization(
    executable: str,
    audio_wav: Path,
    diar_model: str,
    rttm_path: Path,
) -> list[DiarizationTurn]:
    """Run NeMo-Speech.cpp standalone diarization on the complete audio file."""
    rttm_path.parent.mkdir(parents=True, exist_ok=True)
    command = [
        executable,
        "diarize",
        str(audio_wav),
        "--model",
        diar_model,
        "--format",
        "rttm",
        "--output",
        str(rttm_path),
    ]
    log("\n[1/3] Running NeMo-Speech diarization on the full recording...")
    log("Command: " + subprocess.list2cmdline(command))
    try:
        subprocess.run(command, check=True)
    except subprocess.CalledProcessError as exc:
        raise RuntimeError(
            "NeMo diarization failed. Check the model path and run "
            "'nemo-speech help diarize' to verify the options supported by your build. "
            f"Exit code: {exc.returncode}"
        ) from exc

    if not rttm_path.exists() or rttm_path.stat().st_size == 0:
        raise RuntimeError(
            f"NeMo completed but did not create a non-empty RTTM file at {rttm_path}. "
            "Run 'nemo-speech help diarize' and confirm that this build supports "
            "--format rttm --output and --model."
        )

    turns = read_rttm(rttm_path)
    if not turns:
        raise RuntimeError(f"RTTM file contains no SPEAKER rows: {rttm_path}")
    speaker_ids = sorted({turn.speaker for turn in turns})
    log(f"Diarization complete: {len(turns)} turns, {len(speaker_ids)} speakers: {', '.join(speaker_ids)}")
    return turns


def load_vocabulary(path: Optional[Path], max_chars: int = 360) -> list[str]:
    """Read one League term/alias per line and keep a compact hotword list."""
    if path is None:
        return []
    if not path.exists():
        raise FileNotFoundError(f"Vocabulary file does not exist: {path}")

    terms: list[str] = []
    seen: set[str] = set()
    for raw_line in path.read_text(encoding="utf-8-sig", errors="replace").splitlines():
        term = raw_line.strip().strip(",;\t ")
        if not term or term.startswith("#"):
            continue
        key = term.casefold()
        if key in seen:
            continue
        seen.add(key)
        terms.append(term)

    # Whisper's prompt has a limited token budget. Keep the first terms that fit
    # rather than constructing an excessively long prompt that could crowd out speech.
    compact: list[str] = []
    current_chars = 0
    for term in terms:
        addition = len(term) + (2 if compact else 0)
        if current_chars + addition > max_chars:
            break
        compact.append(term)
        current_chars += addition
    if len(compact) < len(terms):
        log(
            f"Vocabulary: loaded {len(terms)} terms; using the first {len(compact)} "
            f"terms ({current_chars} characters) as Whisper hotwords to keep the prompt compact."
        )
    else:
        log(f"Vocabulary: using {len(compact)} hotwords.")
    return compact


def assign_speaker(
    word_start: float,
    word_end: float,
    turns: list[DiarizationTurn],
) -> Optional[str]:
    """Return the speaker whose RTTM interval overlaps the word the most."""
    if word_end <= word_start:
        midpoint = word_start
        word_start = midpoint - 0.025
        word_end = midpoint + 0.025

    best_speaker: Optional[str] = None
    best_overlap = 0.0
    for turn in turns:
        if turn.start >= word_end:
            break
        if turn.end <= word_start:
            continue
        overlap = min(word_end, turn.end) - max(word_start, turn.start)
        if overlap > best_overlap:
            best_overlap = overlap
            best_speaker = turn.speaker
    return best_speaker


def transcribe_chunks(
    audio: np.ndarray,
    model: WhisperModel,
    turns: list[DiarizationTurn],
    *,
    chunk_seconds: float,
    overlap_seconds: float,
    beam_size: int,
    language: str,
    hotwords: list[str],
    vad_filter: bool,
) -> list[WordResult]:
    duration = len(audio) / SAMPLE_RATE
    hotword_text = ", ".join(hotwords) if hotwords else None
    initial_prompt = (
        "Competitive League of Legends team voice communications. "
        "Players discuss champions, item builds, lanes, jungle camps, "
        "minion waves, objectives, rotations, and team fights. "
        "Transcribe only speech that is actually audible."
    )

    word_results: list[WordResult] = []
    chunk_count = max(1, math.ceil(duration / chunk_seconds))

    log("\n[2/3] Transcribing chunks with Whisper...")
    for chunk_index in range(chunk_count):
        core_start = chunk_index * chunk_seconds
        core_end = min(duration, core_start + chunk_seconds)
        read_start = max(0.0, core_start - overlap_seconds)
        read_end = min(duration, core_end + overlap_seconds)

        sample_start = max(0, int(round(read_start * SAMPLE_RATE)))
        sample_end = min(len(audio), int(round(read_end * SAMPLE_RATE)))
        chunk_audio = audio[sample_start:sample_end]
        if chunk_audio.size == 0:
            continue

        log(
            f"  Chunk {chunk_index + 1}/{chunk_count}: "
            f"{core_start:.1f}-{core_end:.1f}s "
            f"(Whisper context {read_start:.1f}-{read_end:.1f}s)"
        )

        segments, info = model.transcribe(
            chunk_audio,
            language=language,
            task="transcribe",
            beam_size=beam_size,
            word_timestamps=True,
            vad_filter=vad_filter,
            vad_parameters={"min_silence_duration_ms": 500} if vad_filter else None,
            condition_on_previous_text=False,
            initial_prompt=initial_prompt,
            hotwords=hotword_text,
            temperature=0.0,
        )

        # The faster-whisper API returns a lazy generator; iterating runs inference.
        for segment in segments:
            segment_words = getattr(segment, "words", None) or []
            if segment_words:
                for word in segment_words:
                    if word.start is None or word.end is None:
                        continue
                    global_start = read_start + float(word.start)
                    global_end = read_start + float(word.end)
                    midpoint = (global_start + global_end) / 2.0

                    # Core ownership prevents overlap duplicates at chunk boundaries.
                    if midpoint < core_start or midpoint >= core_end:
                        continue

                    text = str(word.word or "")
                    if not text.strip():
                        continue
                    speaker = assign_speaker(global_start, global_end, turns)
                    probability = getattr(word, "probability", None)
                    word_results.append(
                        WordResult(
                            start=max(0.0, global_start),
                            end=min(duration, max(global_start, global_end)),
                            word=text,
                            probability=(float(probability) if probability is not None else None),
                            speaker=speaker,
                        )
                    )
            else:
                # Rare fallback if Whisper returns a segment without word alignment.
                global_start = read_start + float(segment.start)
                global_end = read_start + float(segment.end)
                midpoint = (global_start + global_end) / 2.0
                if core_start <= midpoint < core_end and str(segment.text).strip():
                    word_results.append(
                        WordResult(
                            start=max(0.0, global_start),
                            end=min(duration, max(global_start, global_end)),
                            word=str(segment.text).strip(),
                            probability=None,
                            speaker=assign_speaker(global_start, global_end, turns),
                        )
                    )

        # Keep output in global chronological order in case a model returns
        # overlapping punctuation/word boundaries.
        word_results.sort(key=lambda item: (item.start, item.end))

    log(f"Whisper complete: {len(word_results)} aligned word entries.")
    return word_results


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="NeMo-Speech RTTM diarization + local Whisper word transcription."
    )
    parser.add_argument("audio", type=Path, help="Input audio file (WAV, MP3, M4A, FLAC, etc.)")
    parser.add_argument(
        "--diar-model",
        default="models/Nemotron-3-Diarization.q8_0.gguf",
        help="NeMo-Speech.cpp diarization GGUF path (default: %(default)s)",
    )
    parser.add_argument(
        "--nemo-speech",
        default="nemo-speech",
        help="NeMo-Speech executable name or full path (default: %(default)s)",
    )
    parser.add_argument(
        "--asr-model",
        default="large-v3-turbo",
        help="faster-whisper model name or local CTranslate2 model directory (default: %(default)s)",
    )
    parser.add_argument(
        "--model-cache",
        type=Path,
        default=Path("models/whisper"),
        help="Directory used to cache the downloaded Whisper model (default: %(default)s)",
    )
    parser.add_argument("--vocabulary", type=Path, default=None, help="Optional one-term-per-line League vocabulary file.")
    parser.add_argument("--output", type=Path, default=None, help="Output JSON path. Default: beside input audio.")
    parser.add_argument("--rttm-output", type=Path, default=None, help="RTTM output path. Default: beside JSON output.")
    parser.add_argument("--device", choices=["cpu", "cuda"], default="cpu", help="Whisper device (default: cpu).")
    parser.add_argument("--compute-type", default="int8", help="Whisper compute type (CPU default: int8).")
    parser.add_argument("--cpu-threads", type=int, default=max(1, (os.cpu_count() or 4) - 1))
    parser.add_argument("--beam-size", type=int, default=5)
    parser.add_argument("--language", default="en")
    parser.add_argument("--chunk-seconds", type=float, default=30.0)
    parser.add_argument("--overlap-seconds", type=float, default=3.0)
    parser.add_argument("--no-vad", action="store_true", help="Disable faster-whisper's built-in VAD filter.")
    parser.add_argument("--keep-work-wav", action="store_true", help="Keep the normalized temporary WAV beside the JSON output.")
    return parser


def main() -> int:
    args = build_parser().parse_args()

    audio_path = args.audio.expanduser().resolve()
    if not audio_path.is_file():
        raise FileNotFoundError(f"Input audio file does not exist: {audio_path}")
    if args.chunk_seconds <= 0:
        raise ValueError("--chunk-seconds must be greater than zero")
    if args.overlap_seconds < 0 or args.overlap_seconds >= args.chunk_seconds:
        raise ValueError("--overlap-seconds must be >= 0 and smaller than --chunk-seconds")
    if args.beam_size < 1:
        raise ValueError("--beam-size must be at least 1")

    output_path = (args.output or audio_path.with_name(audio_path.stem + "_transcript.json")).expanduser().resolve()
    output_path.parent.mkdir(parents=True, exist_ok=True)
    rttm_path = (args.rttm_output or output_path.with_suffix(".rttm")).expanduser().resolve()
    model_cache = args.model_cache.expanduser().resolve()
    model_cache.mkdir(parents=True, exist_ok=True)

    diar_model_path = Path(args.diar_model).expanduser()
    if not diar_model_path.exists() and not Path(args.diar_model).is_absolute():
        # Also resolve paths relative to the script's current working directory.
        diar_model_path = (Path.cwd() / diar_model_path).resolve()
    if not diar_model_path.exists():
        raise FileNotFoundError(
            f"NeMo diarization model not found: {args.diar_model}\n"
            "Place Nemotron-3-Diarization.q8_0.gguf in the models folder or pass --diar-model."
        )

    nemo_exe = resolve_executable(args.nemo_speech)

    log(f"Input: {audio_path}")
    log(f"Output JSON: {output_path}")
    log(f"RTTM: {rttm_path}")
    log(f"Whisper: {args.asr_model} | device={args.device} | compute_type={args.compute_type}")
    log(f"Diarization model: {diar_model_path}")

    # PyAV (bundled with faster-whisper) decodes supported audio containers;
    # system FFmpeg is not required for this step.
    log("\nDecoding audio to mono 16 kHz for both engines...")
    audio = decode_audio(str(audio_path), sampling_rate=SAMPLE_RATE)
    audio = np.asarray(audio, dtype=np.float32)
    if audio.size == 0:
        raise RuntimeError("Decoded audio is empty.")
    duration = len(audio) / SAMPLE_RATE
    log(f"Decoded {duration:.2f} seconds of audio.")

    hotwords = load_vocabulary(args.vocabulary.expanduser().resolve() if args.vocabulary else None)

    work_wav_path: Optional[Path] = None
    try:
        if args.keep_work_wav:
            work_wav_path = output_path.with_name(output_path.stem + "_16k_mono.wav")
            write_pcm16_mono_wav(work_wav_path, audio, SAMPLE_RATE)
            work_context = None
        else:
            work_context = tempfile.TemporaryDirectory(prefix="nemo_whisper_")
            work_wav_path = Path(work_context.name) / "normalized_16k_mono.wav"
            write_pcm16_mono_wav(work_wav_path, audio, SAMPLE_RATE)

        turns = run_diarization(nemo_exe, work_wav_path, str(diar_model_path.resolve()), rttm_path)

        log("\nLoading Whisper model (download occurs automatically on first run if needed)...")
        whisper = WhisperModel(
            args.asr_model,
            device=args.device,
            compute_type=args.compute_type,
            download_root=str(model_cache),
            cpu_threads=max(1, args.cpu_threads),
            num_workers=1,
        )
        log("Whisper model loaded.")

        words = transcribe_chunks(
            audio,
            whisper,
            turns,
            chunk_seconds=args.chunk_seconds,
            overlap_seconds=args.overlap_seconds,
            beam_size=args.beam_size,
            language=args.language,
            hotwords=hotwords,
            vad_filter=not args.no_vad,
        )

        speaker_ids = sorted({turn.speaker for turn in turns})
        result = {
            "audio_file": str(audio_path),
            "duration_seconds": round(duration, 3),
            "language": args.language,
            "asr_engine": "faster-whisper",
            "asr_model": args.asr_model,
            "diarization_engine": "NeMo-Speech.cpp",
            "diarization_model": str(diar_model_path.resolve()),
            "sample_rate": SAMPLE_RATE,
            "speakers": speaker_ids,
            "word_count": len(words),
            "text": "".join(word.word for word in words).strip(),
            "words": [asdict(word) for word in words],
        }

        output_path.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
        log("\n[3/3] Complete.")
        log(f"Words saved: {len(words)}")
        log(f"JSON saved: {output_path}")
        log(f"RTTM saved: {rttm_path}")

        # Release model memory before returning.
        del whisper
        return 0
    finally:
        if not args.keep_work_wav and "work_context" in locals() and work_context is not None:
            work_context.cleanup()


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except KeyboardInterrupt:
        print("\nCancelled by user.", file=sys.stderr)
        raise SystemExit(130)
    except Exception as exc:
        print(f"\nERROR: {exc}", file=sys.stderr)
        raise SystemExit(1)
