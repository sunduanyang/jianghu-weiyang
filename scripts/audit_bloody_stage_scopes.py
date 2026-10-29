"""Read original 20 ordinary + four throne prefab component scopes only."""
import collections,json
from pathlib import Path
import UnityPy
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'output/bloody-full-port-20261028/environment-check/stage-scope-audit.json'


def main():
    catalog=json.loads((ROOT/'output/bloody-full-port-20261028/original-visual-catalog.json').read_text(encoding='utf-8'))
    scopes={}
    for region in [r for r in catalog['scene'] if r['script']=='RegionConfiguration']:
        pairs=[('ordinary',stage['levelType'],'stages['+str(i)+'].graphicsContainer')for i,stage in enumerate(region['data']['stages'])]
        pairs.append(('throne',4,'throneRoomGraphics'))
        for kind,level,key in pairs:
            identity=region['assets'][key];asset=catalog['assets'][identity]
            root=asset['scene_path']
            assert '/'not in root
            scopes[(asset['source']['file'],root)]=dict(region=region['data']['regionId'],kind=kind,level_type=level,source_prefab=identity,root=root,asset_paths=asset['asset_paths'],components=[])
    assert len(scopes)==24
    by_file=collections.defaultdict(dict)
    for (file,root),scope in scopes.items():by_file[file][root]=scope
    env=UnityPy.load(str(ROOT/'output/bloody-reference-20261028/serialized'))
    scripts={}
    for file in env.files.values():
        if file.name not in by_file:continue
        gos={o.path_id:o.read_typetree()for o in file.objects.values()if o.type.name=='GameObject'}
        ts={o.path_id:o.read_typetree()for o in file.objects.values()if o.type.name in ('Transform','RectTransform')}
        by_go={t['m_GameObject']['m_PathID']:k for k,t in ts.items()};memo={}
        def node(t):
            if t in memo:return memo[t]
            tr=ts[t];name=gos[tr['m_GameObject']['m_PathID']]['m_Name'];p=tr['m_Father']['m_PathID']
            if p not in ts:value=(name,[],[])
            else:
                root,indices,names=node(p);index=[c['m_PathID']for c in ts[p]['m_Children']].index(t);value=(root,indices+[index],names+[name])
            memo[t]=value;return value
        for obj in file.objects.values():
            if obj.type.name!='MonoBehaviour':continue
            header=obj.read_typetree(check_read=False);goid=header['m_GameObject']['m_PathID']
            if goid not in by_go:continue
            root,indices,names=node(by_go[goid])
            if root not in by_file[file.name]:continue
            script=obj.read(check_read=False).m_Script
            if not script.m_PathID:continue
            key=(script.m_FileID,script.m_PathID)
            if key not in scripts:scripts[key]=script.read().m_ClassName
            cls=scripts[key];identity=file.name+':'+str(obj.path_id)
            by_file[file.name][root]['components'].append(dict(identity=identity,script=cls,enabled=bool(header['m_Enabled']),node_identity=file.name+':'+str(goid),source_active_self=bool(gos[goid]['m_IsActive']),hierarchy=dict(indices=indices,names=names),catalog_record_available=any(r['source']==dict(file=file.name,id=obj.path_id)for r in catalog['scene'])))
    rows=sorted(scopes.values(),key=lambda r:(r['region'],r['kind'],r['level_type']))
    restored={'BystanderCustomization','BystanderMovement','BystandersController','LightActivator','CustomizeGroundSpriteShapes','CustomizeEnviroProp','BloodOnTheGroundCustomizer','CwPaintableMeshTexture','CwPaintableMesh','CwMaterialCloner','Light2D'}
    for row in rows:
        row['counts']=dict(collections.Counter(c['script']for c in row['components']))
        row['other_scripts']=dict(collections.Counter(c['script']for c in row['components']if c['script']not in restored))
    result=dict(ordinary=20,throne=4,source_apk_modified=False,prefabs_modified=False,stages=rows,
        notes=['Component list is read from all original MonoBehaviours under verified source roots, including scripts absent from the visual catalog.','Restored class availability does not establish applicability of an uninstalled scope or native callback ordering.'])
    OUT.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    for row in rows:print(json.dumps({k:row[k]for k in ('region','kind','level_type','root','counts','other_scripts')},ensure_ascii=False))


if __name__=='__main__':main()
