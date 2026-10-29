"""Generate the checkpoint ID whitelist from the recovered source graph."""
import json
from pathlib import Path

POINT_REFS = ('feetTransform', 'handLeftPoint', 'handRightPoint',
              'handLeftPointParent', 'handRightPointParent', 'pelvisPoint',
              'pelvisWhenAnyLegSeveredPoint', 'footLeftPoint', 'footRightPoint')


def extract(source):
    output = dict(version=1, source_apk_sha256=source['source_apk_sha256'], templates={})
    for template, rig in source['rigs'].items():
        transforms = {int(k): v for k, v in rig['transforms'].items()}
        go_transforms = {v['m_GameObject']['m_PathID']: k for k, v in transforms.items()}
        bodies = [p for p in rig['physics'] if p['type'] == 'Rigidbody2D']
        body_transforms = {go_transforms[p['data']['m_GameObject']['m_PathID']] for p in bodies}
        mover = rig['controllers']['CharacterMover'][0]['data']
        mover_go = mover['m_GameObject']['m_PathID']
        name = rig['objects'][str(mover_go)]['m_Name']
        ids = set()
        for ref in POINT_REFS:
            point = mover[ref]['m_PathID']; visited = set()
            while point and point not in body_transforms:
                if point in visited:
                    raise ValueError('Cyclic source target hierarchy')
                visited.add(point);ids.add(point)
                transform = transforms[point]
                go = rig['objects'][str(transform['m_GameObject']['m_PathID'])]
                if go['m_Name'] == 'ControlPoints':
                    break
                point = transform['m_Father']['m_PathID']
        output['templates'][template] = dict(name=name,
            role='enemy' if mover['isEnemy'] else 'reserved' if name.endswith('_2') else 'human',
            bodies=sorted(p['id'] for p in bodies),
            joints=sorted(p['id'] for p in rig['physics'] if p['type'].endswith('Joint2D')),
            points=sorted(ids), refs={ref: mover[ref]['m_PathID'] for ref in POINT_REFS})
    return output


if __name__ == '__main__':
    root = Path(__file__).resolve().parents[1]
    source = json.loads((root/'output/bloody-full-port-20261028/source-combat-data.json').read_text(encoding='utf-8'))
    target = root/'jianghu/data/source_rig_schema.json'
    target.parent.mkdir(exist_ok=True)
    data = extract(source)
    target.write_text(json.dumps(data, indent=2)+'\n', encoding='utf-8')
    for template, row in data['templates'].items():
        print(template, row['role'], len(row['bodies']), len(row['joints']), len(row['points']))
