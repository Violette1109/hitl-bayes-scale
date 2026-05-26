import requests
import json
import os
import re
import sys
import pathlib
import random

# ── 從命令行讀取 Likert 上限，默認 5 ──────────────────────
LIKERT_MAX = int(sys.argv[1]) if len(sys.argv) > 1 else 5

# ── 設定路徑（動態向外追溯，確保路徑在任何 Mac 目錄下都正確） ──
current_script_path = pathlib.Path(__file__).resolve()
OUTPUT_DIR = "/Users/violetvwv/Desktop/Bayesian-Optimization-for-Unity-main/Assets/StreamingAssets/BOData/InitData"

PARAMS_FILE = os.path.join(OUTPUT_DIR, "warmstart_params.csv")
OBJECTIVES_FILE = os.path.join(OUTPUT_DIR, "warmstart_objectives.csv")

# ── Ollama 設定 ───────────────────────────────────────────
OLLAMA_URL = "http://localhost:11434/api/generate"
MODEL = "llama3.2:1b"
NUM_ROWS = 10

# ── Prompt：極致壓縮並強制規定格式 ──
PROMPT = f"""You are a strict data generator. Return ONLY a raw JSON object. No explanation. No markdown code blocks.
Likert scale maximum is {LIKERT_MAX}.

Format exactly as follows:
{{"params": [{{"x_font_size": 32, "button_size": 80, "button_distance": 500, "button_hue": 0.5, "button_saturation": 0.5}}], "objectives": [{{"speed": 4500, "accuracy": 25, "aesthetics": 4, "usability": 4}}]}}

Constraints for exactly {NUM_ROWS} entries:
- x_font_size: integer between 18 and 64
- button_size: integer between 40 and 120
- button_distance: integer between 464 and 760
- button_hue: float between 0.0 and 1.0
- button_saturation: float between 0.0 and 1.0
- speed: integer between 2000 and 20000
- accuracy: integer between 0 and 1300
- aesthetics: integer between 1 and {LIKERT_MAX}
- usability: integer between 1 and {LIKERT_MAX}
"""

def generate_backup_data(likert_max, num_rows=10):
    """當 LLM 格式出錯或欄位數量對不齊時，全自動啟動的備用數據合成器 (Fitts' Law 符合)"""
    data = {"params": [], "objectives": []}
    for _ in range(num_rows):
        x_font_size = random.randint(18, 64)
        button_size = random.randint(40, 120)
        button_distance = random.randint(464, 760)
        button_hue = round(random.uniform(0.0, 1.0), 3)
        button_saturation = round(random.uniform(0.0, 1.0), 3)
        
        # Fitts' Law 關係強度計算
        size_factor = (button_size - 40) / (120 - 40)
        dist_factor = (760 - button_distance) / (760 - 464)
        perf = max(0.0, min(1.0, (size_factor * 0.6) + (dist_factor * 0.4) + random.uniform(-0.1, 0.1)))
        
        speed = int(2200 + (1.0 - perf) * 11000)
        accuracy = int(12 + (1.0 - perf) * 55)
        aes = max(1, min(likert_max, int(round(1 + (perf * 0.7 + random.uniform(0, 0.3)) * (likert_max - 1)))))
        usa = max(1, min(likert_max, int(round(1 + (perf * 0.7 + random.uniform(0, 0.3)) * (likert_max - 1)))))
        
        data["params"].append({
            "x_font_size": x_font_size, "button_size": button_size, "button_distance": button_distance,
            "button_hue": button_hue, "button_saturation": button_saturation
        })
        data["objectives"].append({
            "speed": speed, "accuracy": accuracy, "aesthetics": aes, "usability": usa
        })
    return data

def call_qwen(prompt):
    print(f"🤖 Calling {MODEL} via Ollama (Likert max = {LIKERT_MAX}, temperature=0.2)...")
    try:
        response = requests.post(
            OLLAMA_URL,
            json={
                "model": MODEL,
                "prompt": prompt,
                "stream": False,
                "options": {
                    "temperature": 0.2,  # 降低隨機度，強迫它守規矩
                    "top_p": 0.9,
                },
            },
            timeout=15, # 避免本機 Ollama 假死過久
        )
        response.raise_for_status()
        return response.json()["response"]
    except Exception as e:
        print(f"⚠️ Ollama 服務呼叫失敗 ({e})，將自動切換至強健本地數學合成機制。")
        return None

def extract_json(text):
    if not text:
        return None
    # 去除可能包含的 <think> 標籤與 markdown 標記
    text = re.sub(r"<think>.*?</think>", "", text, flags=re.DOTALL)
    text = text.replace("```json", "").replace("```", "").strip()
    match = re.search(r"\{.*\}", text, re.DOTALL)
    if not match:
        return None
    try:
        return json.loads(match.group())
    except json.JSONDecodeError:
        return None

def validate_and_write(data):
    params = data["params"]
    objectives = data["objectives"]

    if len(params) != len(objectives):
        raise ValueError(f"Row count mismatch: params={len(params)}, objectives={len(objectives)}")
    if len(params) < 2:
        raise ValueError("Need at least 2 rows")

    # 進行安全的邊界夾緊限制 (Clamping)，防止 1B 模型數值暴衝導致 Botorch 報錯
    os.makedirs(OUTPUT_DIR, exist_ok=True)
    with open(PARAMS_FILE, "w", encoding="utf-8") as pf, open(OBJECTIVES_FILE, "w", encoding="utf-8") as of:
        pf.write("x_font_size;button_size;button_distance;button_hue;button_saturation\n")
        of.write("speed;accuracy;aesthetics;usability\n")
        
        for p, o in zip(params, objectives):
            x_f = max(18, min(64, int(p["x_font_size"])))
            b_s = max(40, min(120, int(p["button_size"])))
            b_d = max(464, min(760, int(p["button_distance"])))
            b_h = max(0.0, min(1.0, float(p["button_hue"])))
            b_s_at = max(0.0, min(1.0, float(p["button_saturation"])))
            
            spd = max(0, min(30000, int(o["speed"])))
            acc = max(0, min(1300, int(o["accuracy"])))
            aes = max(1, min(LIKERT_MAX, int(o["aesthetics"])))
            usa = max(1, min(LIKERT_MAX, int(o["usability"])))
            
            pf.write(f"{x_f};{b_s};{b_d};{b_h:.3f};{b_s_at:.3f}\n")
            of.write(f"{spd};{acc};{aes};{usa}\n")

    print(f"✅ Written {len(params)} rows to:")
    print(f"   {PARAMS_FILE}")
    print(f"   {OBJECTIVES_FILE}")

def main():
    raw = call_qwen(PROMPT)
    data = extract_json(raw)
    
    # 🟢 核心安全防護：如果模型生成的資料是 Null、或者長度對不齊，直接無縫切換
    if data is None or "params" not in data or "objectives" not in data or len(data["params"]) != len(data["objectives"]):
        print("🎲 [防禦機制啟動] LLM 輸出數據不完整或語法破碎，已即時動態合成全新 Fitts' Law 規律數據！")
        data = generate_backup_data(LIKERT_MAX, NUM_ROWS)
    else:
        print("📊 Successfully parsed LLM raw data.")
        
    validate_and_write(data)
    print(f"🎉 Done! Warmstart is fully generated. Likert scale: 1-{LIKERT_MAX}")

if __name__ == "__main__":
    main()