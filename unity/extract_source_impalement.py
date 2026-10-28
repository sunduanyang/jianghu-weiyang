"""Extract the three original organ triggers without shipping the full audit dump."""
import json
import math
from pathlib import Path


def multiply(a, b):
    return (a[0]*b[0]+a[2]*b[1], a[1]*b[0]+a[3]*b[1],
            a[0]*b[2]+a[2]*b[3], a[1]*b[2]+a[3]*b[3],
            a[0]*b[4]+a[2]*b[5]+a[4], a[1]*b[4]+a[3]*b[5]+a[5])


def extract(source):
    result = dict(version=1, source_apk_sha256=source['source_apk_sha256'], rigs={})
    for key, rig in source['rigs'].items():
        transforms = {int(k): v for k, v in rig['transforms'].items()}
        by_go = {v['m_GameObject']['m_PathID']: k for k, v in transforms.items()}
        physics = {v['id']: v for v in rig['physics']}
        parts = {v['id']: v['data'] for v in rig['controllers']['BodyPart']}

        def relative(node, ancestor):
            if node == ancestor:
                return (1, 0, 0, 1, 0, 0)
            t = transforms[node]
            q, p, s = (t[k] for k in ('m_LocalRotation', 'm_LocalPosition', 'm_LocalScale'))
            # Full quaternion projection retains 180-degree Y reflections.
            a, b = 1-2*(q['y']**2+q['z']**2), 2*(q['x']*q['y']+q['z']*q['w'])
            c, d = 2*(q['x']*q['y']-q['z']*q['w']), 1-2*(q['x']**2+q['z']**2)
            local = a*s['x'], b*s['x'], c*s['y'], d*s['y'], p['x'], p['y']
            parent = t['m_Father']['m_PathID']
            if not parent:
                raise ValueError('Organ transform does not descend from linked body')
            return multiply(relative(parent, ancestor), local)

        organs = []
        for row in rig['controllers']['InternalOrgan']:
            data = row['data']; go = data['m_GameObject']['m_PathID']
            linked_go = parts[data['LinkedBodyPart']['m_PathID']]['m_GameObject']['m_PathID']
            linked_name = rig['objects'][str(linked_go)]['m_Name']
            linked = {'Bone_Head': 'head', 'Bone_Belly': 'belly', 'Bone_Chest': 'torso'}[linked_name]
            rb_id = data['Rigidbody2D']['m_PathID']
            body_go = physics[rb_id]['data']['m_GameObject']['m_PathID']
            collider = next(v for v in rig['physics'] if v['data']['m_GameObject']['m_PathID'] == go and v['type'].endswith('Collider2D'))
            matrix = relative(by_go[go], by_go[linked_go])
            # The old rig adapter uses the LINKED body frame. The source path
            # binds the raw collider and separately uses the specified joint RB.
            organs.append(dict(key=rig['objects'][str(go)]['m_Name'].removeprefix('InternalOrgan_'),
                id=row['id'], transform=by_go[go], linked=linked,
                body='head' if rig['objects'][str(body_go)]['m_Name'] == 'Bone_Head' else 'torso',
                rigidbody=rb_id, collider=collider, linked_transform=by_go[linked_go],
                local_matrix=matrix, masks=[by_go[v['m_PathID']] for v in data['ImpaleMasks']]))
        settings = rig['controllers']['CharacterDamageCalculator'][0]['data']
        result['rigs'][key] = dict(organs=organs, settings={k: settings[k] for k in
            ('impaleDamageThreshold', 'impaleDurationBase', 'impaleDurationPerDamage')})
    return result


if __name__ == '__main__':
    root = Path(__file__).resolve().parents[1]
    data = extract(json.loads((root/'output/bloody-full-port-20261028/source-combat-data.json').read_text(encoding='utf-8')))
    target = root/'unity/JianghuUnity/Assets/Resources/CombatSource/organs.json'
    target.write_text(json.dumps(data, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    print(f'{len(data["rigs"])} rigs / {sum(len(r["organs"]) for r in data["rigs"].values())} organs -> {target}')
