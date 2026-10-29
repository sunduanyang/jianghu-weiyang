"""Export the unchanged ColorizeSprite record on the original Dummy root."""
from pathlib import Path
import json
import re
from export_bloody_match_configs import documents

ROOT=Path(__file__).resolve().parents[1]
ASSETS=ROOT/'output/bloody-full-port-20261028/Recovered/ExportedProject/Assets'


def main():
    source=json.loads((ROOT/'recovery/bloody/Runtime/Resources/OriginalPracticeSource.json').read_text(encoding='utf-8'))
    script=ASSETS/'Scripts/Assembly-CSharp/ColorizeSprite.cs.meta'
    guid=re.search(r'^guid: (\w+)',script.read_text(encoding='utf-8-sig'),re.M)[1]
    rows=[]
    for doc in documents(ASSETS/'GameObject/Dummy.prefab'):
        data=doc.get('MonoBehaviour')
        if not data or data.get('m_Script',{}).get('guid')!=guid:continue
        renderers=[dict(id=str(r['fileID']),**source['source_bindings'][str(r['fileID'])])for r in data['spriteRenderers']]
        rows.append(dict(id=doc['__file_id'],path=source['source_bindings'][doc['__file_id']]['path'],script='ColorizeSprite',data=dict(LightTintType=data['LightTintType'],spriteRenderers=renderers,possibleColors=data['possibleColors'],customizeOnEnable=data['customizeOnEnable'])))
    result=dict(source_apk_sha256=source['source_apk_sha256'],source_guid=source['network_guid'],records=rows,native_methods=dict(on_enable='0x36c2fb4',customize='0x36c308c',move_next='0x36c319c',environment_finished_predicate='0x36c3168'),note='LightTintType is serialized Foreground3 but is not read by the source ordinary coroutine; retain the actual possibleColors gradient and five RNG calls.')
    path=ROOT/'recovery/bloody/Runtime/Resources/OriginalDummyColorSource.json';path.write_text(json.dumps(result,ensure_ascii=False,separators=(',',':')),encoding='utf-8')
    print(json.dumps(dict(records=len(rows),renderers=sum(len(r['data']['spriteRenderers'])for r in rows))))


if __name__=='__main__':main()
