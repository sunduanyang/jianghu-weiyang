"""Export original battle UI and local match settings, retaining source IDs."""
import json
from pathlib import Path
import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
from UnityPy.helpers.TypeTreeHelper import read_value,TypeTreeConfig
from UnityPy.streams.EndianBinaryReader import EndianBinaryReader

ROOT=Path(__file__).resolve().parents[1]
WANTED=set('MatchHUD PlayerStatsBar CharacterHUD CountdownView RoundEndView MatchEndView TouchControls SingleHandControls CharacterPlayerInputHandler OnScreenButton OnScreenNub LocalGameController NetworkedGameController RoundEndHandler MatchEndHandler CanvasAndCamera WalletView AudioAndMusicManager'.split())

def main():
    env=UnityPy.load(str(ROOT/'output/bloody-reference-20261028/serialized'))
    generator=TypeTreeGenerator('6000.4.0f1');generator.load_local_dll_folder(str(ROOT/'output/bloody-reference-20261028/dummy-attributes'))
    records=[];paths={};nodes={}
    for file in env.files.values():
        selected=[]
        for obj in file.objects.values():
            if obj.type.name!='MonoBehaviour':continue
            ptr=obj.read(check_read=False).m_Script
            if not ptr.m_PathID:continue
            script=ptr.read()
            if script.m_ClassName in WANTED:selected.append((obj,script))
        if not selected:continue
        gos={str(o.path_id):o.read_typetree() for o in file.objects.values() if o.type.name=='GameObject'}
        ts={str(o.path_id):o.read_typetree() for o in file.objects.values() if o.type.name in ['Transform','RectTransform']}
        bygo={str(t['m_GameObject']['m_PathID']):k for k,t in ts.items()};memo={}
        def path(t):
            if t in memo:return memo[t]
            tr=ts[t];p=str(tr['m_Father']['m_PathID']);name=gos[str(tr['m_GameObject']['m_PathID'])]['m_Name']
            memo[t]=(path(p)+'/' if p in ts else '')+name
            return memo[t]
        for goid,go in gos.items():
            if goid not in bygo:continue
            p=path(bygo[goid]);paths[file.name+':'+goid]=p
            for c in go['m_Component']:paths[file.name+':'+str(c['component']['m_PathID'])]=p
        for obj,script in selected:
            name=(script.m_Namespace+'.' if script.m_Namespace else '')+script.m_ClassName
            key=(script.m_AssemblyName,name)
            if key not in nodes:
                node=generator.get_nodes_up(*key)
                for child in node.m_Children:
                    if child.m_Name=='m_Enabled':child.m_MetaFlag|=0x4000
                nodes[key]=node
            reader=EndianBinaryReader(obj.get_raw_data(),endian=obj.reader.endian)
            data=read_value(nodes[key],reader,TypeTreeConfig(True,obj.assets_file,False))
            if reader.Position!=obj.byte_size:raise ValueError(f'Bad UI schema {name} {reader.Position}/{obj.byte_size}')
            records.append(dict(source=file.name+':'+str(obj.path_id),script=script.m_ClassName,path=paths.get(file.name+':'+str(obj.path_id)),data=data))
    for record in records:
        file=record['source'].split(':')[0]
        def resolve(v):
            if isinstance(v,list):return [resolve(x) for x in v]
            if not isinstance(v,dict):return v
            if 'm_PathID' in v:
                result=dict(v)
                if v['m_FileID']==0:result['path']=paths.get(file+':'+str(v['m_PathID']))
                return result
            return {k:resolve(x) for k,x in v.items()}
        record['data']=resolve(record['data'])
    out=ROOT/'output/bloody-full-port-20261028/original-ui-source.json'
    out.write_text(json.dumps(dict(records=records,paths=paths),ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps([dict(script=r['script'],path=r['path'],keys=list(r['data']))for r in records if r['path'] and not r['path'].startswith('Character_')],ensure_ascii=False))

if __name__=='__main__':main()
