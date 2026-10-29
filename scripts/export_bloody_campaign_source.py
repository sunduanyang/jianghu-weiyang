"""Export original campaign band/name/boss references without guessing IDs."""
from pathlib import Path
import json
import re
import struct
from export_bloody_match_configs import documents

ROOT=Path(__file__).resolve().parents[1]
ASSETS=ROOT/'output/bloody-full-port-20261028/Recovered/ExportedProject/Assets'


def main():
    by_guid={}
    for path in ASSETS.rglob('*.asset.meta'):
        match=re.search(r'^guid: (\w+)',path.read_text(encoding='utf-8-sig'),re.M)
        if match:by_guid[match[1]]=Path(str(path)[:-5])
    cache={}
    def asset(pointer):
        guid=pointer.get('guid')
        if not guid:return None
        if guid not in cache:cache[guid]=documents(by_guid[guid])[0]['MonoBehaviour']
        return cache[guid]
    names={};bosses={};regions={}
    for path in (ASSETS/'MonoBehaviour').glob('RegionConfig_*.asset'):
        data=documents(path)[0]['MonoBehaviour'];bands=[]
        for band in data['enemyBands']:
            bulk=asset(band['config']);name=bulk['m_Name']
            bands.append(dict(min=band['waveMin'],max=band['waveMax'],bulk=name))
            for field in ['EnemyNames_Male','EnemyNames_Female']:
                ref=bulk[field];table=asset(ref)
                names[ref['guid']]={k:table[k]for k in ['m_Name','FirstNames','MiddleNames','FamilyNames']}
            entries=[]
            for entry in bulk.get('SpecialBosses',[]):
                profile=asset(entry['Config'])
                entries.append(dict(level=entry['BossLevel'],profile=profile['m_Name']if profile else None,profile_url=entry.get('ProfileUrl')))
            bosses[name]=entries
        regions[str(data['regionId'])]=dict(region=data['regionId'],name=data['DisplayName'],number_of_levels=data['NumberOfLevels'],bands=bands)
    native_strings=json.loads((ROOT/'output/bloody-full-port-20261028/native-strings.json').read_text(encoding='utf-8'))
    strings={r['getter']:r['text']for r in native_strings['records']}
    binary=(ROOT/'output/bloody-reference-20261028/libil2cpp.so').read_bytes()
    f=lambda va:struct.unpack_from('<f',binary,va)[0]
    constants=dict(beard_color_divider=f(0x120b2a8),clothes_random_ranges=[[f(a),f(b)]for a,b in [(0x120a298,0x120ac60),(0x12094e0,0x1209600),(0x120a2ec,0x120ada0),(0x120a2ec,0x120ada0),(0x12096dc,0x12096dc),(0x120b0ac,0x120b0ac),(0x120b0ac,0x120b0ac),(0x12094e4,0x12094e4)]],boss_offsets=dict(Height=-.25,Weight=f(0x120a97c),ShouldersWidth=f(0x120a514),WaistWidth=.25,VoicePitch=f(0x120b1c4)),name_separator=strings['dy'],female_family_suffix=strings['fj'],female_russian_family_suffix=strings['fk'],constructor_name=strings['gl'])
    result=dict(source_apk_sha256='c9e4a52874f18c58363d976194227d68a699173d72925d22da34a75d0993f2ba',regions=regions,names=names,special_bosses=bosses,constants=constants,native_methods=dict(run_start='0x341cbc0',select_band='0x341caa8',pre_customize='0x341d6e0',select_pool='0x3425984',copy_gradients='0x341f844'))
    dest=ROOT/'recovery/bloody/Runtime/Resources/OriginalCampaignSource.json'
    dest.write_text(json.dumps(result,ensure_ascii=False,separators=(',',':')),encoding='utf-8')
    print(json.dumps(dict(regions=len(regions),bands=sum(len(v['bands'])for v in regions.values()),name_tables=len(names),special_boss_entries=sum(len(v)for v in bosses.values()))))


if __name__=='__main__':main()
