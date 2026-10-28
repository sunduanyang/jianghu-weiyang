"""Resolve all original hand-item component references and protected values.

Native ObscuredBool.Decrypt at ELF VA 0x32e4d58 is (hidden - key) XOR key;
the old V0 XOR-only routine is a different method and must not be substituted.
"""
import collections
import json
import math
import struct
from pathlib import Path


def plain_float(data):
    raw=bytearray(struct.pack('<I',data['hiddenValue']&0xffffffff))
    raw[1],raw[2]=raw[2],raw[1]
    bits=int.from_bytes(raw,'little')^(data['currentCryptoKey']&0xffffffff)
    return struct.unpack('<f',struct.pack('<I',bits))[0]


def plain_bool(data):
    if data==dict(hash=0,hiddenValue=0,currentCryptoKey=0):return False
    key=data['currentCryptoKey']&255
    clear=((data['hiddenValue']-key)^key)&0xffffffff
    if clear not in (181,213):raise ValueError('Unexpected protected boolean encoding')
    return clear!=181


def graph_index(graph):
    objects=graph['objects'];transforms=graph['transforms']
    go_to_t={str(t['m_GameObject']['m_PathID']):i for i,t in transforms.items()}
    component_go={}
    for goid,go in objects.items():
        for c in go['m_Component']:component_go[str(c['component']['m_PathID'])]=goid
    return objects,transforms,go_to_t,component_go


def main():
    root=Path(__file__).resolve().parents[1]
    dependencies=json.loads((root/'output/bloody-full-port-20261028/dependencies.json').read_text())
    graphs={k:graph_index(v) for k,v in dependencies['item_graphs'].items()}
    catalog=[];skipped=[]
    resources=root/'unity/BloodyBastardsRecovery/Assets/Resources/sourceprefabs/handitem'
    for item in dependencies['items']:
        if item['script']!='SourcePrefab_HandItem':continue
        data=item['data'];filename=item['source']['file']
        if data['IsFlailHead']:continue
        objects,transforms,go_to_t,component_go=graphs[filename]
        goid=str(data['m_GameObject']['m_PathID']);root_t=go_to_t[goid]
        name=objects[goid]['m_Name']
        if not (resources/(name+'.prefab')).exists():
            skipped.append(dict(name=name,source=item['source'],reason='No exported root prefab'))
            continue

        def path(t):
            if t==root_t:return ''
            transform=transforms[t];parent=str(transform['m_Father']['m_PathID'])
            if parent=='0':raise ValueError('Reference leaves original hand-item root: '+name)
            return (path(parent)+'/'+objects[str(transform['m_GameObject']['m_PathID'])]['m_Name']).lstrip('/')

        def resolve(value):
            if isinstance(value,list):return [resolve(v) for v in value]
            if not isinstance(value,dict):return value
            if 'm_PathID' not in value:return {k:resolve(v) for k,v in value.items()}
            i=str(value['m_PathID']);ref=dict(id=i,file=value['m_FileID'])
            if value['m_FileID']==0 and i!='0':
                owner=i if i in objects else component_go.get(i)
                if owner:ref['path']=path(go_to_t[owner])
            return ref

        values={k:plain_float(data[k]) for k in ['obs_Damage','obs_Defense','obs_Weight']}
        if not all(math.isfinite(v) for v in values.values()):raise ValueError('Non-finite source item value')
        two=plain_bool(data['obs_DoubleHanded']);blunt=plain_bool(data['isBlunt'])
        catalog.append(dict(name=name,source=item['source'],item_id=data['itemId'],
            resource='sourceprefabs/handitem/'+name,label=data['Name'],category=data['HandItemType'],
            two_handed=two,blunt=blunt,stats=values,fields=resolve(data)))
    ids=collections.Counter(x['item_id'] for x in catalog)
    result=dict(source_apk_sha256=dependencies['source_apk_sha256'],items=catalog,
        bool_decrypt_va='0x32e4d58',float_decrypt_va='0x32ec260',
        skipped=skipped,duplicate_item_ids={str(k):v for k,v in ids.items() if v>1},
        runtime_port_complete=False)
    output=root/'output/bloody-full-port-20261028/original-item-catalog.json'
    output.write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps(dict(items=len(catalog),categories=dict(collections.Counter(x['category'] for x in catalog)),
        two_handed=sum(x['two_handed'] for x in catalog),skipped=len(skipped),runtime_port_complete=False)))


if __name__=='__main__':main()
