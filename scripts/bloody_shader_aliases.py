"""Replace every exported alias of a recovered shader, preserving asset GUIDs."""
import re
import shutil
from pathlib import Path

NAME=re.compile(r'\bShader\s+"([^\"]+)"')

def install(source,assets):
    recovered={}
    for path in source.glob('*.shader'):
        match=NAME.search(path.read_text(encoding='utf-8-sig'))
        if match:recovered[match[1]]=path
    aliases={}
    for path in assets.rglob('*.shader'):
        match=NAME.search(path.read_text(encoding='utf-8-sig'))
        if match and match[1] in recovered:
            shutil.copy2(recovered[match[1]],path)
            aliases.setdefault(match[1],[]).append(path.relative_to(assets).as_posix())
    for name,path in recovered.items():
        if name not in aliases:
            target=assets/'Shader'/path.name;target.parent.mkdir(parents=True,exist_ok=True)
            shutil.copy2(path,target);aliases[name]=[target.relative_to(assets).as_posix()]
    return aliases
