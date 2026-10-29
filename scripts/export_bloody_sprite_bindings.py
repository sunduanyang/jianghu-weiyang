"""Bind catalog Sprites by retained source render-data identity, never names.

The standalone command audits and atomically updates only the JSON catalog.
It does not modify any original or exported Unity asset.
"""
import collections
import json
import re
from pathlib import Path


def bind_sprites(root, files, records):
    project = root / 'unity/BloodyBastardsRecovery'
    keyed = collections.defaultdict(list)
    named = collections.defaultdict(list)
    for path in (project / 'Assets/Sprite').glob('*.asset'):
        relative = path.relative_to(project).as_posix()
        named[path.stem].append(relative)
        match = re.search(r'm_RenderDataKey:\s*\n\s*([0-9a-f]{32}):\s*(-?\d+)', path.read_text(encoding='utf-8-sig'))
        if match:
            keyed[(match[1], int(match[2]))].append(relative)
    rows = []
    for identity, record in records.items():
        if record['type'] != 'Sprite':
            continue
        origin = record['source']
        source = files[origin['file']].objects[origin['id']].read_typetree()
        guid, local_id = source['m_RenderDataKey']
        # Unity's serialized GUID text reverses the hex nibbles of each uint.
        key = ''.join(f'{guid["data[" + str(i) + "]"]:08x}'[::-1] for i in range(4))
        candidates = sorted(keyed[(key, local_id)])
        previous = record.get('asset_paths', [])
        heuristic = sorted(named[source['m_Name']])
        row = dict(identity=identity, name=source['m_Name'], guid=key, local_id=local_id,
                   previous_paths=previous, old_stem_paths=heuristic, exact_paths=candidates,
                   previous_matches_exact=(previous == candidates and len(candidates) == 1),
                   old_stem_matches_exact=(heuristic == candidates and len(candidates) == 1))
        rows.append(row)
        # Explicitly leave unresolved identities unbound. Selecting a name or
        # rectangle here would silently give a different source Sprite.
        record['asset_paths'] = candidates if len(candidates) == 1 else []
        record['source_render_data_key'] = dict(guid=key, local_id=local_id)
        record['sprite_binding_method'] = 'original render data key' if len(candidates) == 1 else 'unresolved original render data key'
    report = dict(sprite_count=len(rows), unique_bindings=sum(len(r['exact_paths']) == 1 for r in rows),
                  unresolved=sum(len(r['exact_paths']) != 1 for r in rows),
                  previous_wrong_or_ambiguous=sum(not r['previous_matches_exact'] for r in rows),
                  old_stem_wrong_or_ambiguous=sum(not r['old_stem_matches_exact'] for r in rows),
                  rows=rows)
    target = root / 'output/bloody-full-port-20261028/environment-check/all-sprite-identity-audit.json'
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    return report


def main():
    import UnityPy
    root = Path(__file__).resolve().parents[1]
    target = root / 'output/bloody-full-port-20261028/original-visual-catalog.json'
    catalog = json.loads(target.read_text(encoding='utf-8'))
    env = UnityPy.load(str(root / 'output/bloody-reference-20261028/serialized'))
    files = {f.name: f for f in env.files.values()}
    report = bind_sprites(root, files, catalog['assets'])
    temporary = target.with_suffix('.json.tmp')
    temporary.write_text(json.dumps(catalog, ensure_ascii=False, indent=2), encoding='utf-8')
    temporary.replace(target)
    print(json.dumps({k: v for k, v in report.items() if k != 'rows'}))
    for row in [r for r in report['rows'] if not r['previous_matches_exact']][:12]:
        print(json.dumps(row, ensure_ascii=False))


if __name__ == '__main__':
    main()
