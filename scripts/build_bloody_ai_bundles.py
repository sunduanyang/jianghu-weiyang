"""Resolve original AI bundle clip references without dropping duplicate weights."""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "output/bloody-full-port-20261028/dependencies.json"
TARGET = ROOT / "recovery/bloody/Runtime/Resources/OriginalAIBundles.json"


def main():
    data = json.loads(SOURCE.read_text(encoding="utf-8-sig"))
    clips = data["clips"]
    result = {
        key: {
            "source": bundle["source"],
            "name": bundle["name"],
            "up": [clips[ref]["m_Name"] for ref in bundle["up"]],
            "down": [clips[ref]["m_Name"] for ref in bundle["down"]],
        }
        for key, bundle in data["ai_bundles"].items()
    }
    assert len(result) == 18
    TARGET.parent.mkdir(parents=True, exist_ok=True)
    TARGET.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(f"Resolved {len(result)} original bundles; duplicate clip entries preserved")


if __name__ == "__main__":
    main()
