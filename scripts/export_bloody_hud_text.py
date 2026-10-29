"""Export exact MatchHUD format and verdict terms from APK localization."""
import hashlib
import json
from pathlib import Path
import yaml

ROOT=Path(__file__).resolve().parents[1]


def main():
    source=ROOT/'output/bloody-full-port-20261028/Recovered/ExportedProject/Assets/Resources/I2Languages.asset'
    table=next(iter(yaml.safe_load('\n'.join(source.read_text(encoding='utf-8').splitlines()[3:])).values()))['mSource']
    strings=json.loads((ROOT/'output/bloody-full-port-20261028/native-strings.json').read_text(encoding='utf-8'))['records']
    getters={s['getter']:s for s in strings};terms={s['Term']:s for s in table['mTerms']}
    selected={}
    for getter in('bxp','bxl'):
        key=getters[getter]['text'];term=terms[key];assert len(term['Languages'])==len(table['mLanguages'])
        selected[key]=dict(getter=getter,getter_va=getters[getter]['va'],translations=term['Languages'])
    assert getters['bul']['text']=='{0:D2}:{1:D2}'
    result=dict(source='Resources/I2Languages.asset',source_sha256=hashlib.sha256(source.read_bytes()).hexdigest(),languages=table['mLanguages'],terms=selected,timer_format=getters['bul'],empty_string=getters['fj'])
    target=ROOT/'recovery/bloody/Runtime/Resources/OriginalHUDTextSource.json';target.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(dict(languages=len(table['mLanguages']),terms=len(selected),timer=getters['bul']['text'])))


if __name__=='__main__':main()
