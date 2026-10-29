"""Resolve original AI bundle clip references without dropping duplicate weights."""
import json
from pathlib import Path
import shutil

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
    clip_resources={}
    destination=TARGET.parent/'OriginalHandClips';destination.mkdir(exist_ok=True)
    required={ref for bundle in data['ai_bundles'].values()for side in ('up','down')for ref in bundle[side]}
    for ref in sorted(required):
        clip=clips[ref];name=clip['m_Name'];resource=ref.replace(':','_')
        source=ROOT/'output/bloody-full-port-20261028/Recovered/ExportedProject/Assets/AnimationClip'/(name+'.anim')
        if not source.is_file():raise FileNotFoundError(f'Original AI clip asset not found: {ref} {name}')
        shutil.copy2(source,destination/(resource+'.anim'))
        clip_resources[name]=dict(source=ref,resource='OriginalHandClips/'+resource)
    (TARGET.parent/'OriginalAIClips.json').write_text(json.dumps(clip_resources,indent=2)+'\n',encoding='utf-8')
    print(f"Resolved {len(result)} original bundles and {len(clip_resources)} explicitly loaded original clips; duplicate entries preserved")


if __name__ == "__main__":
    main()
