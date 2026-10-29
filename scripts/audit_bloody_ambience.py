"""Read original Clear/day/night component identities and serialized state."""
import json
from pathlib import Path
import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
from UnityPy.helpers.TypeTreeHelper import read_value,TypeTreeConfig
from UnityPy.streams.EndianBinaryReader import EndianBinaryReader
ROOT=Path(__file__).resolve().parents[1]
def main():
    visual=json.loads((ROOT/'output/bloody-full-port-20261028/original-visual-catalog.json').read_text(encoding='utf-8'))
    env=UnityPy.load(str(ROOT/'output/bloody-reference-20261028/serialized'));files={f.name:f for f in env.files.values()}
    generator=TypeTreeGenerator('6000.4.0f1');generator.load_local_dll_folder(str(ROOT/'output/bloody-reference-20261028/dummy-attributes'));nodes={}
    def typed(obj):
        script=obj.read(check_read=False).m_Script.read();key=(script.m_AssemblyName,script.m_ClassName)
        if key not in nodes:
            node=generator.get_nodes_up(*key)
            for child in node.m_Children:
                if child.m_Name=='m_Enabled':child.m_MetaFlag|=0x4000
            nodes[key]=node
        reader=EndianBinaryReader(obj.get_raw_data(),endian=obj.reader.endian)
        data=read_value(nodes[key],reader,TypeTreeConfig(True,obj.assets_file,False))
        if reader.Position!=obj.byte_size:raise ValueError('Incomplete schema '+script.m_ClassName)
        return script.m_ClassName,data
    goids=set()
    for row in visual['scene']:
        if row['script']=='StopAndPlayAmbienceLoop' and '/Clear/' in row.get('scene_path',''):
            goids.add(row['data']['m_GameObject']['m_PathID'])
    file=files['level0'];result=[]
    for goid in list(goids):
        go=file.objects[goid].read_typetree();tr=next(file.objects[p['component']['m_PathID']].read_typetree() for p in go['m_Component'] if file.objects[p['component']['m_PathID']].type.name=='Transform')
        parent=file.objects[tr['m_Father']['m_PathID']].read_typetree()['m_GameObject']['m_PathID'];goids.add(parent)
    for goid in sorted(goids):
        go=file.objects[goid].read_typetree();components=[]
        for p in go['m_Component']:
            obj=file.objects[p['component']['m_PathID']]
            if obj.type.name=='MonoBehaviour':
                script,data=typed(obj);components.append(dict(identity='level0:'+str(obj.path_id),script=script,data=data))
            else:components.append(dict(identity='level0:'+str(obj.path_id),type=obj.type.name))
        result.append(dict(game_object='level0:'+str(goid),name=go['m_Name'],active_self=go['m_IsActive'],components=components))
    out=ROOT/'output/bloody-full-port-20261028/environment-check/ambience-source-audit.json';out.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(out.read_text(encoding='utf-8'))
if __name__=='__main__':main()
