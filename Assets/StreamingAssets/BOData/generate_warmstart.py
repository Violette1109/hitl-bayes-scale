import requests
import json
import os
import re
import sys

# ── 從命令行讀取 Likert 上限，默認 7 ──────────────────────
LIKERT_MAX = int(sys.argv[1]) if len(sys.argv) > 1 else 5

# ── 設定路徑 ──────────────────────────────────────────────
OUTPUT_DIR = os.path.expanduser(
    "~/Desktop/Bayesian-Optimization-for-Unity/Assets/StreamingAssets/BOData/InitData"
)
PARAMS_FILE = os.path.join(OUTPUT_DIR, "warmstart_params.csv")
OBJECTIVES_FILE = os.path.join(OUTPUT_DIR, "warmstart_objectives.csv")

# ── Ollama 設定 ───────────────────────────────────────────
OLLAMA_URL = "http://localhost:11434/api/generate"
MODEL = "llama3.2:1b"
NUM_ROWS = 10

# ── Prompt ───────────────────────────────────────────────
PROMPT = f"""You are an expert in human-computer interaction and motor control.
I need warm-start data for a Multi-Objective Bayesian Optimization study on circular movement tasks.

Parameters:
- button_size: integer between 40 and 120 (pixel radius of the target circle)
- button_distance: integer between 220 and 760 (pixel distance to target)
- button_hue: integer between 0 and 1
- button_saturation: integer between 0 and 1

Objectives:
- speed: integer between 0 and 30000 (milliseconds, smaller is better)
- accuracy: integer between 0 and 1300 (percentage, larger is better)
- aesthetics: integer between 1 and {LIKERT_MAX} (1=very low, {LIKERT_MAX}=very high, larger is better)
- usability: integer between 1 and {LIKERT_MAX} (1=very low, {LIKERT_MAX}=very high, larger is better)

Domain rules:
- Larger buttons at shorter distances → faster completion, higher accuracy, lower mental demand
- Smaller buttons at longer distances → slower completion, lower accuracy, higher mental demand
- button_hue and button_saturation have moderate effect on all objectives
- Include diverse trade-off configurations spread across the full design space

IMPORTANT: All values MUST be strictly within the specified ranges.
Do NOT generate values outside these bounds under any circumstances:
- button_size: MUST be between 40 and 120 (inclusive)
- button_distance: MUST be between 220 and 760 (inclusive)  
- button_hue: MUST be between 0 and 1 (inclusive)
- button_saturation: MUST be between 0 and 1 (inclusive)
- speed: MUST be between 0 and 30000 (inclusive)
- accuracy: MUST be between 0 and 1300 (inclusive)
- aesthetics: MUST be between 1 and {LIKERT_MAX} (inclusive)
- usability: MUST be between 1 and {LIKERT_MAX} (inclusive)

Generate exactly {NUM_ROWS} rows of data.

Output ONLY a JSON object in this exact format, no explanation, no markdown:
{{
  "params": [
    {{"button_size": 80, "button_distance": 400, "button_hue": 0.5, "button_saturation": 0.5}},
    ...
  ],
  "objectives": [
    {{"speed": 5000, "accuracy": 85, "aesthetics": 2, "usability": 2}},
    ...
  ]
}}"""


def call_qwen(prompt):
    print(f"🤖 Calling Qwen3.5 via Ollama (Likert max = {LIKERT_MAX})...")
    response = requests.post(
        OLLAMA_URL,
        json={
            "model": MODEL,
            "prompt": prompt,
            "stream": False,
            "options": {
                "temperature": 0.7,
                "top_p": 0.95,
                "top_k": 20,
            },
        },
        timeout=120,
    )
    response.raise_for_status()
    return response.json()["response"]


def extract_json(text):
    text = re.sub(r"<think>.*?</think>", "", text, flags=re.DOTALL)
    match = re.search(r"\{.*\}", text, re.DOTALL)
    if not match:
        raise ValueError("No JSON found in response")
    return json.loads(match.group())


def validate_and_write(data):
    params = data["params"]
    objectives = data["objectives"]

    if len(params) != len(objectives):
        raise ValueError(f"Row count mismatch: params={len(params)}, objectives={len(objectives)}")
    if len(params) < 2:
        raise ValueError("Need at least 2 rows")

    for i, (p, o) in enumerate(zip(params, objectives)):
        assert 40 <= p["button_size"] <= 120, f"Row {i}: button_size out of bounds"
        assert 220 <= p["button_distance"] <= 760, f"Row {i}: button_distance out of bounds"
        assert 0 <= p["button_hue"] <= 1, f"Row {i}: button_hue out of bounds"
        assert 0 <= p["button_saturation"] <= 1, f"Row {i}: button_saturation out of bounds"
        assert 0 <= o["speed"] <= 30000, f"Row {i}: speed out of bounds"
        assert 0 <= o["accuracy"] <= 1300, f"Row {i}: accuracy out of bounds"
        assert 1 <= o["aesthetics"] <= LIKERT_MAX, f"Row {i}: aesthetics out of bounds (max={LIKERT_MAX})"
        assert 1 <= o["usability"] <= LIKERT_MAX, f"Row {i}: usability out of bounds (max={LIKERT_MAX})"

    os.makedirs(OUTPUT_DIR, exist_ok=True)
    with open(PARAMS_FILE, "w") as f:
        f.write("button_size;button_distance;button_hue;button_saturation\n")
        for p in params:
            f.write(f"{p['button_size']};{p['button_distance']};{p['button_hue']};{p['button_saturation']}\n")

    with open(OBJECTIVES_FILE, "w") as f:
        f.write("speed;accuracy;aesthetics;usability\n")
        for o in objectives:
            f.write(f"{o['speed']};{o['accuracy']};{o['aesthetics']};{o['usability']}\n")

    print(f"✅ Written {len(params)} rows to:")
    print(f"   {PARAMS_FILE}")
    print(f"   {OBJECTIVES_FILE}")


def main():
    raw = call_qwen(PROMPT)
    print("📝 Raw response received, parsing...")
    data = extract_json(raw)
    print("📊 Parsed data:", json.dumps(data, indent=2))  # ← 加这行
    validate_and_write(data)
    print(f"🎉 Done! Likert scale: 1-{LIKERT_MAX}")


if __name__ == "__main__":
    main()
