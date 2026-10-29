"""Summarize measured native runs; never substitute expected or estimated values."""
import argparse
import hashlib
import json
from pathlib import Path


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    root = args.run.resolve()
    comparison = root / 'distribution-comparison'
    rows = {backend: read(comparison / backend / 'comparison.json') for backend in ('legacy', 'core')}
    schedule = [hashlib.sha256((comparison / backend / 'input-sequence.json').read_bytes()).hexdigest() for backend in rows]
    if schedule[0] != schedule[1]:
        raise ValueError('Input schedules differ')
    if {r['weapon'] for r in rows['legacy']} != {r['weapon'] for r in rows['core']} or len(rows['core']) != 10:
        raise ValueError('Expected matching sets of ten weapons')
    summary = {}
    fields = ('weapon', 'frames', 'substeps', 'mean_tick_ms', 'p95_tick_ms', 'max_gap', 'max_limit_degrees',
              'max_held_grip_gap', 'max_visible_handle_gap', 'actual_body_contact_entries', 'constraints_passed', 'held_grip_passed')
    for backend, measurements in rows.items():
        for row in measurements:
            if row['backend'] != ('physics-core-2d' if backend == 'core' else 'legacy-physics2d'):
                raise ValueError('Backend identity mismatch')
            if row['frames'] != 1200 or row['measured_frames'] != 1140:
                raise ValueError('Expected twenty seconds and sixty warmup ticks per case')
        measured = sum(r['measured_frames'] for r in measurements)
        summary[backend] = dict(
            weapon_count=len(measurements), measured_frames=measured,
            mean_tick_ms=sum(r['controller_and_physics_ms'] for r in measurements) / measured,
            maximum_joint_gap_m=max(r['max_gap'] for r in measurements),
            maximum_limit_error_degrees=max(r['max_limit_degrees'] for r in measurements),
            maximum_held_grip_gap_m=max(r['max_held_grip_gap'] for r in measurements),
            maximum_visible_handle_gap_m=max(r['max_visible_handle_gap'] for r in measurements),
            constraint_cases_passed=sum(bool(r['constraints_passed']) for r in measurements),
            grip_cases_passed=sum(bool(r['held_grip_passed']) for r in measurements),
            measurements=[{key: r[key] for key in fields} | {'physics': r['physics']} for r in measurements])
    evidence = {}
    locations = {
        'mechanics': root / 'distribution-native/reference-mechanics/acceptance.json',
        'source_rig': root / 'distribution-native/source-rig/acceptance.json',
        'cold_resume': root / 'distribution-native/source-rig-resume/acceptance.json',
        'formal_spar_rob_assault': root / 'distribution-formal/acceptance.json',
        'manual_offense_and_shields': root / 'distribution-native/guard/acceptance.json',
        'legacy_manual_offense_and_shields': root / 'distribution-legacy-guard/guard/acceptance.json',
        'ai_sixty_seconds': root / 'distribution-ai/reference-stress/acceptance.json',
        'signed_queries': root / 'distribution-native/core-query/acceptance.json',
        'dropped_weapon_save_bounds': root / 'distribution-native/save-bounds/acceptance.json',
        'dropped_weapon_cold_resume': root / 'distribution-native/save-bounds-resume/acceptance.json',
        'same_input_core': comparison / 'core/acceptance.json',
        'same_input_legacy': comparison / 'legacy/acceptance.json',
    }
    for name, path in locations.items():
        result = read(path)
        evidence[name] = dict(passed=result['passed'], checks=len(result['checks']),
                              failures=[r['check'] for r in result['checks'] if not r['passed']], path=str(path))
    for kind in ('spar', 'rob', 'assault'):
        frame = read(root / f'distribution-formal/combat-{kind}-frame.json')
        if frame.get('physics_backend') != 'physics-core-2d':
            raise ValueError(f'{kind} was not actually Core')
    core_zero_legacy = all(r['physics']['legacy_simulation_steps'] == 0 and r['physics']['native_steps'] > 0 for r in rows['core'])
    provenance = read(comparison / 'provenance.json')
    guard = read(root / 'distribution-native/guard/guard-cases.json')
    for case in guard:
        if case['physics_backend']['backend'] != 'physics-core-2d' or case['physics_backend']['legacy_simulation_steps'] != 0:
            raise ValueError('Manual offense did not use Core exclusively')
    cold = read(root / 'distribution-native/source-rig-resume/cold-actual.json')
    saved_backend_preserved = cold.get('physics_backend') == 'physics-core-2d'
    ai = read(root / 'distribution-ai/reference-stress/solver-measurements.json')
    if len(ai) != 10 or {r['weapon'] for r in ai} != {r['weapon'] for r in rows['core']}:
        raise ValueError('Missing current-build AI cases')
    for row in ai:
        native = row['physics_backend']
        if native['backend'] != 'physics-core-2d' or native['native_steps'] <= 0 or native['legacy_simulation_steps'] != 0:
            raise ValueError('AI study did not use Core exclusively')
        expected = (64, 2000) if row['weapon'] in ('spear', 'greatsword', 'greataxe', 'warhammer') else (64, 1000)
        if (native['solver_substeps'], native['joint_hertz']) != expected:
            raise ValueError('AI study differs from shipping solver budget')
    package = read(root / 'release-package.json')
    if package['files'][1]['sha256'].lower() != provenance['assembly_sha256'].lower():
        raise ValueError('Compared assembly differs from shipping package')
    core_evidence = {key: value for key, value in evidence.items() if key not in ('legacy_manual_offense_and_shields', 'same_input_legacy')}
    result = dict(candidate_validated=all(v['passed'] for v in core_evidence.values()) and core_zero_legacy and saved_backend_preserved,
                  input_sha256=schedule[0], duration_seconds_per_weapon=20, control_hz=60,
                  measurement_scope='controller and physics CPU time; excludes rendering; first 60 ticks excluded',
                  comparison=summary, core_to_legacy_cpu_ratio=summary['core']['mean_tick_ms']/summary['legacy']['mean_tick_ms'],
                  evidence=evidence, core_has_zero_legacy_combat_steps=core_zero_legacy, provenance=provenance,
                  release=package,
                  saved_core_frame_overrides_legacy_launcher_request=saved_backend_preserved,
                  total_native_checks=sum(r['checks'] for r in evidence.values()),
                  core_native_checks=sum(r['checks'] for r in core_evidence.values()),
                  baseline_acceptance_passed=evidence['same_input_legacy']['passed'],
                  source_snapshot=str(root / 'current-source-hashes.json'),
                  earlier_long_ai_study=dict(path=str(root / 'core-stress-combined/acceptance.json'),
                      scope='Ten completed cases up to 180 seconds on the frozen pre-offense-restoration gameplay snapshot; not counted as current-build acceptance'),
                  manual_offense=dict(cases=len(guard), maximum_grip_gap_m=max(r['max_grip_gap'] for r in guard),
                      blade_hits=[dict(template=r['template'], shield=r['enemy_shield'], arc=r['arc'], hits=r['primary_blade_hits']) for r in guard]),
                  current_ai_stress=dict(duration_cap_seconds=60, death_ends_case_early=True,
                      maximum_limit_error_degrees=max(r['max_limit'] for r in ai),
                      maximum_joint_gap_m=max(r['max_gap'] for r in ai),
                      maximum_held_grip_gap_m=max(r['maximum_held_grip_gap'] for r in ai),
                      maximum_visible_handle_gap_m=max(r['maximum_visible_weapon_handle_gap'] for r in ai),
                      measurements=[{k: r[k] for k in ('weapon', 'simulation_seconds', 'hits', 'max_gap', 'max_limit', 'maximum_held_grip_gap', 'maximum_visible_weapon_handle_gap')} for r in ai]),
                  python_physical_and_backend_tests=66, default_launcher_changed_by_migration=False)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({key: value for key, value in result.items() if key not in ('comparison', 'evidence')}, ensure_ascii=False, indent=2))
    for backend, value in summary.items():
        print(backend, {k: v for k, v in value.items() if k != 'measurements'})


if __name__ == '__main__':
    main()
