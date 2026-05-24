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
- circle_size: integer between 40 and 120 (pixel radius of the target circle)
- circle_distance: integer between 220 and 760 (pixel distance to target)
- movement_direction: integer between 0 and 180 (degrees)

Objectives:
- task_completion_time: integer between 0 and 120000 (milliseconds, smaller is better)
- accuracy: integer between 0 and 100 (percentage, larger is better)
- mental_demand: integer between 1 and {LIKERT_MAX} (1=very low, {LIKERT_MAX}=very high, smaller is better)

Domain rules:
- Larger circles at shorter distances → faster completion, higher accuracy, lower mental demand
- Smaller circles at longer distances → slower completion, lower accuracy, higher mental demand
- movement_direction has moderate effect on all objectives
- Include diverse trade-off configurations spread across the full design space

IMPORTANT: All values MUST be strictly within the specified ranges.
Do NOT generate values outside these bounds under any circumstances:
- circle_size: MUST be between 40 and 120 (inclusive)
- circle_distance: MUST be between 220 and 760 (inclusive)  
- movement_direction: MUST be between 0 and 180 (inclusive)
- task_completion_time: MUST be between 0 and 120000 (inclusive)
- accuracy: MUST be between 0 and 100 (inclusive)
- mental_demand: MUST be between 1 and {LIKERT_MAX} (inclusive)

Generate exactly {NUM_ROWS} rows of data.

Output ONLY a JSON object in this exact format, no explanation, no markdown:
{{
  "params": [
    {{"circle_size": 80, "circle_distance": 400, "movement_direction": 90}},
    ...
  ],
  "objectives": [
    {{"task_completion_time": 5000, "accuracy": 85, "mental_demand": 2}},
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
        assert 40 <= p["circle_size"] <= 120, f"Row {i}: circle_size out of bounds"
        assert 220 <= p["circle_distance"] <= 760, f"Row {i}: circle_distance out of bounds"
        assert 0 <= p["movement_direction"] <= 180, f"Row {i}: movement_direction out of bounds"
        assert 0 <= o["task_completion_time"] <= 120000, f"Row {i}: task_completion_time out of bounds"
        assert 0 <= o["accuracy"] <= 100, f"Row {i}: accuracy out of bounds"
        assert 1 <= o["mental_demand"] <= LIKERT_MAX, f"Row {i}: mental_demand out of bounds (max={LIKERT_MAX})"

    os.makedirs(OUTPUT_DIR, exist_ok=True)
    with open(PARAMS_FILE, "w") as f:
        f.write("circle_size;circle_distance;movement_direction\n")
        for p in params:
            f.write(f"{p['circle_size']};{p['circle_distance']};{p['movement_direction']}\n")

    with open(OBJECTIVES_FILE, "w") as f:
        f.write("task_completion_time;accuracy;mental_demand\n")
        for o in objectives:
            f.write(f"{o['task_completion_time']};{o['accuracy']};{o['mental_demand']}\n")

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
