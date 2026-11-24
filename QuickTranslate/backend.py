"""Local JSON-lines bridge to the unmodified Argos EN/RU models.

No network, server, telemetry or clipboard access. CTranslate2 and SentencePiece
are used directly so the portable build does not need PyTorch or spaCy.
"""
import json
import os
from pathlib import Path
import re
import sys

os.environ.setdefault("HF_HUB_OFFLINE", "1")
os.environ.setdefault("OMP_NUM_THREADS", "4")
sys.stdin.reconfigure(encoding="utf-8")
sys.stdout.reconfigure(encoding="utf-8", line_buffering=True)

import ctranslate2
import sentencepiece as spm

ROOT = Path(__file__).resolve().parent
CACHE = {}


def load_model(source, target):
    key = (source, target)
    if key not in CACHE:
        root = ROOT / "models" / f"{source}_{target}"
        metadata = json.loads((root / "metadata.json").read_text(encoding="utf-8"))
        # Python handles Unicode Windows paths; native fopen implementations
        # in model libraries may interpret absolute paths using an ANSI page.
        tokenizer = spm.SentencePieceProcessor(model_proto=(root / "sentencepiece.model").read_bytes())
        model_files = {file.name: file.read_bytes() for file in (root / "model").iterdir() if file.is_file()}
        translator = ctranslate2.Translator(
            f"quicktranslate-{source}-{target}", files=model_files, device="cpu", compute_type="int8",
            inter_threads=1, intra_threads=min(4, os.cpu_count() or 1),
        )
        CACHE[key] = (translator, tokenizer, metadata.get("target_prefix", ""))
    return CACHE[key]


def translate(text, source, target):
    if (source, target) not in (("en", "ru"), ("ru", "en")):
        raise ValueError("Unsupported language direction")
    if not text.strip():
        raise ValueError("Empty input")
    if len(text) > 20000:
        raise ValueError("Text is longer than 20000 characters")
    translator, tokenizer, prefix = load_model(source, target)
    result = []
    # Retain paragraphs and blank lines; split long lines before model limits.
    for line in text.split("\n"):
        if not line.strip():
            result.append("")
            continue
        sentences = re.split(r"(?<=[.!?…])\s+(?=\S)", line.strip())
        parts = []
        for sentence in sentences:
            tokens = tokenizer.encode(sentence, out_type=str)
            if not tokens:
                continue
            for offset in range(0, len(tokens), 240):
                batch = translator.translate_batch(
                    [tokens[offset:offset + 240]], beam_size=4,
                    target_prefix=[[prefix]] if prefix else None,
                    replace_unknowns=True, max_decoding_length=512,
                )
                output_tokens = batch[0].hypotheses[0]
                # Same SentencePiece decoding used by Argos Translate.
                decoded = "".join(output_tokens).replace("▁", " ").strip()
                if prefix and decoded.startswith(prefix):
                    decoded = decoded[len(prefix):].strip()
                parts.append(decoded)
        result.append(" ".join(parts))
    return "\n".join(result)


print(json.dumps({"ready": True}), flush=True)
for raw in sys.stdin:
    request = {}
    try:
        request = json.loads(raw)
        value = translate(request["text"], request["source"], request["target"])
        response = {"id": request.get("id"), "text": value}
    except Exception as exc:
        response = {"id": request.get("id"), "error": f"{type(exc).__name__}: {exc}"}
    print(json.dumps(response, ensure_ascii=False), flush=True)
