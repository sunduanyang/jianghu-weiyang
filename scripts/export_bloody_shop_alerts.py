"""Extract mounted ItemCatalog/alert categories, including face/hair items.

Reads the original serialized files only. Decrypts source integers by executing
the original tiny SUB/EOR routine under Unicorn; no guest memory is accessed.
"""
import collections,hashlib,json
from pathlib import Path
import UnityPy
from UnityPy.classes.PPtr import PPtr
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
from UnityPy.helpers.TypeTreeHelper import read_value,TypeTreeConfig
from UnityPy.streams.EndianBinaryReader import EndianBinaryReader
from verify_bloody_ai_native import NativeAI
from unicorn.arm64_const import UC_ARM64_REG_X0
from build_bloody_item_catalog import plain_bool
ROOT=Path(__file__).resolve().parents[1]
def main():
    env=UnityPy.load(str(ROOT/'output/bloody-reference-20261028/serialized'))
    generator=TypeTreeGenerator('6000.4.0f1');generator.load_local_dll_folder(str(ROOT/'output/bloody-reference-20261028/dummy-attributes'))
    nodes={};classes={};typed_cache={};native=NativeAI()
    def identity(o):return o.assets_file.name+':'+str(o.path_id)
    def cls(o):
        if o.type.name!='MonoBehaviour':return ''
        p=o.read(check_read=False).m_Script
        if not p.m_PathID:return ''
        s=p.deref();key=identity(s)
        if key not in classes:classes[key]=s.read()
        return classes[key].m_ClassName
    def typed(o):
        key=identity(o)
        if key in typed_cache:return typed_cache[key]
        script=o.read(check_read=False).m_Script.read();node_key=(script.m_AssemblyName,script.m_ClassName)
        if node_key not in nodes:
            node=generator.get_nodes_up(*node_key)
            for child in node.m_Children:
                if child.m_Name=='m_Enabled':child.m_MetaFlag|=0x4000
            for child in node.traverse():
                if child.m_Type=='string' and child.m_Children and child.m_Children[0].m_Type=='Array' and child.m_Children[0].m_Children[-1].m_Type=='string':child.m_Type='vector'
            nodes[node_key]=node
        reader=EndianBinaryReader(o.get_raw_data(),endian=o.reader.endian)
        result=read_value(nodes[node_key],reader,TypeTreeConfig(True,o.assets_file,False))
        if reader.Position!=o.byte_size:raise ValueError(f'Incomplete source schema {key}: {reader.Position}/{o.byte_size}')
        typed_cache[key]=result;return result
    def ref(o,p):return PPtr(m_FileID=p['m_FileID'],m_PathID=p['m_PathID'],assetsfile=o.assets_file).deref()
    def integer(v):
        native.call(0x32edd40,v['hiddenValue']&0xffffffff,v['currentCryptoKey']&0xffffffff)
        n=native.cpu.reg_read(UC_ARM64_REG_X0)&0xffffffff;return n-0x100000000 if n>=0x80000000 else n
    managers=[o for o in env.objects if o.assets_file.name=='level0' and cls(o)=='NewItemsAlertsManager']
    if len(managers)!=1:raise ValueError('Mounted source NewItemsAlertsManager is ambiguous')
    manager=managers[0];settings=typed(manager);catalog=ref(manager,settings['catalog']);entries=typed(catalog)['entries']
    items=[];skipped=[]
    for index,entry in enumerate(entries):
        if not entry['prefab']['m_PathID']:skipped.append(dict(index=index,reason='null prefab'));continue
        prefab=ref(catalog,entry['prefab']);go=prefab.read_typetree()
        candidates=[]
        for component in go['m_Component']:
            obj=ref(prefab,component['component']);kind=cls(obj)
            if kind in ('SourcePrefab_ArmorItem','SourcePrefab_HandItem'):candidates.append((kind,obj))
        armor=[p for p in candidates if p[0]=='SourcePrefab_ArmorItem'];selection=(armor or candidates)
        if not selection:skipped.append(dict(index=index,prefab=identity(prefab),name=go['m_Name'],reason='no source item component'));continue
        kind,obj=selection[0];data=typed(obj)
        item=dict(index=index,id=data['itemId'],kind=data['Type'],region=data['region'],unlock_rule=data['unlockRule'],unlocks_at_level=integer(data['obs_UnlocksAtLevel']),
            seasonal_type=data['seasonalType'],empty_shop_slot=bool(data['EmptyShopSlot']),max_ownable=1 if kind=='SourcePrefab_ArmorItem' or plain_bool(data['obs_DoubleHanded']) else 2,
            name=go['m_Name'],label=data['Name'],source=identity(obj),prefab=identity(prefab),script=kind,protected_unlocks_at_level=data['obs_UnlocksAtLevel'])
        items.append(item)
    metadata=json.loads((ROOT/'output/bloody-code-audit-20261028/game-metadata.json').read_text(encoding='utf-8'))
    def enum(name):return {f['name']:f['constant'] for t in metadata['types'] if t['type']==name for f in t['fields'] if f['static']}
    result=dict(source_apk_sha256='c9e4a52874f18c58363d976194227d68a699173d72925d22da34a75d0993f2ba',manager=identity(manager),catalog=identity(catalog),catalog_serialized_entry_count=len(entries),
        kind_pips=[dict(kind=p['kind']) for p in settings['kindPips']],kinds=enum('ShopList/ListKind'),unlock_rules=enum('UnlockRule'),seasonal_types=enum('BBSeasonalType'),items=items,skipped=skipped,
        inventory_defaults=dict(entries=[],viewed_ids=[],automatic_owned_rules=[1,3,4],evidence=dict(inventory_data_ctor='0x351e830',get_owned_count='0x351ea10',get_max_ownable='0x351ece8',viewed_load='0x3436b28')),
        limits='Shop UI purchase flow and source network inventory synchronization are outside this local alert domain')
    target=ROOT/'recovery/bloody/Runtime/Resources/OriginalShopAlertSource.json';target.write_text(json.dumps(result,ensure_ascii=False,separators=(',',':'))+'\n',encoding='utf-8')
    audit=ROOT/'output/bloody-full-port-20261028/environment-check/shop-alert-source-audit.json'
    ids=collections.Counter(i['id'] for i in items)
    audit.write_text(json.dumps(dict(serialized_entries=len(entries),items=len(items),unique_ids=len(ids),duplicate_ids={str(k):v for k,v in ids.items() if v>1},kind_pips=result['kind_pips'],by_kind=dict(collections.Counter(i['kind'] for i in items)),by_unlock_rule=dict(collections.Counter(i['unlock_rule'] for i in items)),by_region=dict(collections.Counter(i['region'] for i in items)),skipped=skipped,resource_sha256=hashlib.sha256(target.read_bytes()).hexdigest()),ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(audit.read_text(encoding='utf-8'))
if __name__=='__main__':main()
