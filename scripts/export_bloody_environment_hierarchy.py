"""Augment the source catalog with verified Transform child-index chains.

Only adds `hierarchy`; existing catalog fields and the supplied APK stay intact.
Duplicated Snow names cannot be resolved with Transform.Find alone.
"""
import json
from pathlib import Path
import UnityPy
ROOT=Path(__file__).resolve().parents[1]
def main(catalog_path=None):
    path=Path(catalog_path) if catalog_path is not None else ROOT/'output/bloody-full-port-20261028/original-visual-catalog.json'
    data=json.loads(path.read_text(encoding='utf-8'))
    wanted=set(data['assets'])|{r['source']['file']+':'+str(r['source']['id']) for r in data['scene']}
    result={}
    env=UnityPy.load(str(ROOT/'output/bloody-reference-20261028/serialized'))
    for file in env.files.values():
        selected=[key for key in wanted if key.startswith(file.name+':')]
        if not selected:continue
        gos={o.path_id:o.read_typetree() for o in file.objects.values() if o.type.name=='GameObject'}
        ts={o.path_id:o.read_typetree() for o in file.objects.values() if o.type.name in ('Transform','RectTransform')}
        by_go={v['m_GameObject']['m_PathID']:k for k,v in ts.items()}
        for key in selected:
            obj=file.objects[int(key.rsplit(':',1)[1])]
            if obj.type.name=='GameObject':goid=obj.path_id
            elif obj.type.name in ('Transform','RectTransform'):goid=ts[obj.path_id]['m_GameObject']['m_PathID']
            else:
                if not data['assets'].get(key,{}).get('scene_path') and not any(r['source']['file']+':'+str(r['source']['id'])==key and r.get('scene_path') for r in data['scene']):continue
                header=obj.read_typetree(check_read=False)
                if 'm_GameObject' not in header:continue
                goid=header['m_GameObject']['m_PathID']
            if goid not in by_go:continue
            transform=by_go[goid];names=[];indices=[]
            while True:
                tr=ts[transform];names.append(gos[tr['m_GameObject']['m_PathID']]['m_Name'])
                parent=tr['m_Father']['m_PathID']
                if parent not in ts:break
                children=[p['m_PathID'] for p in ts[parent]['m_Children']]
                indices.append(children.index(transform));transform=parent
            result[key]=dict(root=names[-1],indices=list(reversed(indices)),names=list(reversed(names[:-1])))
    data['hierarchy']=dict(sorted(result.items()))
    path.write_text(json.dumps(data,ensure_ascii=False,separators=(',',':'))+'\n',encoding='utf-8')
    print(json.dumps(dict(hierarchy_entries=len(result),snow={key:result[key] for key in ('level0:17649','level0:18516','level0:22016')})))
if __name__=='__main__':main()
