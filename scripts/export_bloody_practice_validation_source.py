"""Source-only body/collider constants for the opt-in Practice fixture."""
from pathlib import Path
import json
from export_bloody_match_configs import documents

ROOT=Path(__file__).resolve().parents[1]


def main():
    source=json.loads((ROOT/'recovery/bloody/Runtime/Resources/OriginalPracticeSource.json').read_text(encoding='utf-8'))
    docs=documents(ROOT/'output/bloody-full-port-20261028/Recovered/ExportedProject/Assets/GameObject/Dummy.prefab');by={d['__file_id']:d for d in docs}
    parts=[]
    for row in source['controllers']:
        if row['script']!='DummyBodyPart':continue
        body=by[row['data']['thisRigidbody2D']['id']]['Rigidbody2D'];goid=str(body['m_GameObject']['fileID']);go=by[goid]['GameObject'];colliders=[]
        for ref in go['m_Component']:
            key=str(ref['component']['fileID']);doc=by[key];kind=next(k for k in doc if k!='__file_id')
            if not kind.endswith('Collider2D'):continue
            data=doc[kind];shape=dict(kind=kind,offset=data['m_Offset'])
            if kind in ['BoxCollider2D','CapsuleCollider2D']:shape['size']=data['m_Size']
            if kind=='BoxCollider2D':shape['edge_radius']=data['m_EdgeRadius']
            if kind=='CapsuleCollider2D':shape['direction']=data['m_Direction']
            if kind=='CircleCollider2D':shape['radius']=data['m_Radius']
            if kind=='PolygonCollider2D':shape['paths']=data['m_Points']['m_Paths']
            colliders.append(dict(id=key,enabled=data['m_Enabled'],trigger=data['m_IsTrigger'],layer=go['m_Layer'],shape=shape))
        parts.append(dict(path=row['path'],body_type=body['m_BodyType'],mass=body['m_Mass'],gravity=body['m_GravityScale'],linear_damping=body['m_LinearDamping'],angular_damping=body['m_AngularDamping'],simulated=body['m_Simulated'],colliders=colliders))
    result=dict(source_apk_sha256=source['source_apk_sha256'],source_guid=source['network_guid'],parts=parts,fixture_note='This resource records source expected values. The fixture must inspect, not apply, these body/collider values.')
    target=ROOT/'recovery/bloody/Runtime/Resources/OriginalPracticeValidationSource.json';target.write_text(json.dumps(result,ensure_ascii=False,separators=(',',':')),encoding='utf-8')
    print(json.dumps(dict(parts=len(parts),colliders=sum(len(p['colliders'])for p in parts))))


if __name__=='__main__':main()
