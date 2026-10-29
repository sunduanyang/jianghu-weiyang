"""Export the actual mounted wallet config and source campaign/progress data."""
import hashlib
import json
from pathlib import Path
import re
import struct
import yaml
from verify_bloody_ai_native import NativeAI
from unicorn.arm64_const import UC_ARM64_REG_X0

ROOT=Path(__file__).resolve().parents[1]
ASSETS=ROOT/'output/bloody-full-port-20261028/Recovered/ExportedProject/Assets'
TARGET=ROOT/'recovery/bloody/Runtime/Resources/OriginalProgressSource.json'


def main():
    file=ASSETS/'MonoBehaviour/WalletConfig.asset';raw=next(iter(yaml.safe_load('\n'.join(file.read_text(encoding='utf-8-sig').splitlines()[3:])).values()))
    native=NativeAI()
    def decode(value):
        # Literal original ObscuredInt.Decrypt, VA 0x32edd40: SUB/EOR/RET.
        native.call(0x32edd40,value['hiddenValue']&0xffffffff,value['currentCryptoKey']&0xffffffff)
        result=native.cpu.reg_read(UC_ARM64_REG_X0)&0xffffffff
        return result-0x100000000 if result>=0x80000000 else result
    keys=['campaignNormalStarRewards','campaignMiniBossRewards','campaignBossRewards','campaignFinaBossRewards','survivalBasePerEnemy','survivalCap','doubledMultiplier']
    wallet={k:[decode(v)for v in raw[k]]if isinstance(raw[k],list)else decode(raw[k])for k in keys}
    rank_file=ASSETS/'Resources/ranked/RankConfigList.asset'
    rank_raw=next(iter(yaml.safe_load('\n'.join(rank_file.read_text(encoding='utf-8-sig').splitlines()[3:])).values()))
    rank_config=[{key:decode(row[key])for key in('RankStartsAt','RPGain','RPLoss')}for row in rank_raw['Ranks']]
    campaign=json.loads((ROOT/'recovery/bloody/Runtime/Resources/OriginalCampaignSource.json').read_text(encoding='utf-8'))
    strings=json.loads((ROOT/'output/bloody-full-port-20261028/native-strings.json').read_text(encoding='utf-8'))['records'];lookup={r['getter']:r for r in strings}
    errors={key:lookup[getter]for key,getter in dict(missing_progress='ri',invalid_stars='rj',invalid_level_index='sh',cannot_skip_level='si',missing_wallet_config='cvk',invalid_reward_stars='cvl',invalid_gold_delta='cvx',invalid_stage='se').items()}
    scene=ASSETS/'Game/Scenes/MainScene.unity';body=scene.read_text(encoding='utf-8-sig')
    blocks=[m.group(1)for m in re.finditer(r'^--- !u!114 &\d+\n(.*?)(?=^--- !u!|\Z)',body,re.M|re.S)]
    controllers={}
    for name in('ProgressController','WalletController'):
        meta=ASSETS/'Scripts/Assembly-CSharp'/f'{name}.cs.meta';guid=re.search(r'^guid: (\w+)',meta.read_text(),re.M).group(1)
        controllers[name]=next(iter(yaml.safe_load(next(b for b in blocks if'guid: '+guid in b)).values()))
    result=dict(source_apk_sha256='c9e4a52874f18c58363d976194227d68a699173d72925d22da34a75d0993f2ba',wallet_source=file.relative_to(ASSETS).as_posix(),wallet_source_sha256=hashlib.sha256(file.read_bytes()).hexdigest(),wallet=wallet,wallet_protected_values={k:raw[k]for k in keys},source_controllers=controllers,
                rank_config=rank_config,rank_source=rank_file.relative_to(ASSETS).as_posix(),rank_source_sha256=hashlib.sha256(rank_file.read_bytes()).hexdigest(),rank_protected_values=[{key:row[key]for key in('RankStartsAt','RPGain','RPLoss')}for row in rank_raw['Ranks']],
                regions={k:dict(number_of_levels=v['number_of_levels'],name=v['name'])for k,v in campaign['regions'].items()},errors=errors,
                defaults=dict(tutorial_passed=False,gold_release_player=0,campaign_regions=[],survival_max_waves=0,ranked=dict(CurrentRankPoints=0,ATHRankPoints=0,Kills=0,Deaths=0),applied_grant_ids=[],selection=dict(region=1,stage=0,level=1,seed=0,level_type=0,shop_region=1)),
                default_evidence=dict(progress_data_ctor='0x365efc0',wallet_data_ctor='0x3445e0c',wallet_load='0x3445b84',wallet_editor_test_gold_ctor='0x3446ef8',hq_static_ctor='0x343e960'),
                prefs=dict(region=lookup['sb']['text'],stage_prefix=lookup['sc']['text']))
    assert [len(wallet[k])for k in keys[:4]]==[3,3,3,3]
    assert wallet['doubledMultiplier']==2
    TARGET.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(dict(wallet=wallet,regions=list(result['regions']),prefs=result['prefs']),ensure_ascii=False))


if __name__=='__main__':main()
