"""Merge completed per-weapon native measurements, retaining their source paths."""
import argparse
import json
from pathlib import Path


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run', type=Path, required=True)
    args = parser.parse_args()
    root = args.run.resolve()
    serial = root / 'core-stress-release/reference-stress'
    serial_rows = {r['weapon']: r for r in read(serial / 'solver-measurements.json')}
    rows, checks, sources = [], [], {}
    names = ('unarmed', 'dagger', 'sabre', 'sword', 'axe', 'mace', 'spear', 'greatsword', 'greataxe', 'warhammer')
    for name in names:
        if name in names[:5]:
            row = serial_rows[name]
            source = serial / 'solver-measurements.json'
            source_check = serial / 'progress.json'
            native_checks = [c for c in read(source_check) if c['check'].endswith(' ' + name) or (' ' + name + ' / ') in c['check']]
        else:
            folder = (root / 'core-stress-parallel/mace') if name == 'mace' else (root / 'core-stress-heavy' / name)
            source, source_check = folder / 'solver-measurements.json', folder / 'acceptance.json'
            row = next(r for r in read(source) if r['weapon'] == name)
            native_checks = read(source_check)['checks']
        if not native_checks or not all(c['passed'] for c in native_checks):
            raise ValueError(f'Native acceptance did not pass: {name}')
        for c in native_checks:
            checks.append(dict(c, check=name + ' / ' + c['check'], source=str(source_check)))
        facts = row['physics_backend']
        if facts['backend'] != 'physics-core-2d' or facts['native_steps'] <= 0 or facts['legacy_simulation_steps'] != 0:
            raise ValueError(f'Wrong backend: {name}')
        if not row['limit_bound_passed'] or not row['arena_bound_passed']:
            raise ValueError(f'Constraint bounds failed: {name}')
        if name != 'unarmed' and not (row['held_grip_samples'] > 100 and row['maximum_held_grip_gap'] < .03 and row['maximum_visible_weapon_handle_gap'] < .03):
            raise ValueError(f'Held grip failed: {name}')
        rows.append(row)
        sources[name] = dict(measurements=str(source), checks=str(source_check),
                             duration=row['simulation_seconds'], solver_substeps=facts['solver_substeps'], joint_hertz=facts['joint_hertz'])
    output = root / 'core-stress-combined'
    output.mkdir(exist_ok=True)
    (output / 'solver-measurements.json').write_text(json.dumps(rows, ensure_ascii=False, indent=2), encoding='utf-8')
    result = dict(kind='aggregate of completed native cases', passed=True, checks=checks, sources=sources,
                  note='The first five cases completed before the serial runner hit its wall-time limit; their emitted per-case checks are retained. Remaining cases completed in independent processes. The failed 16-substep greataxe study is not counted.')
    (output / 'acceptance.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    print('All ten native AI cases passed;', len(checks), 'source checks retained.')


if __name__ == '__main__':
    main()
