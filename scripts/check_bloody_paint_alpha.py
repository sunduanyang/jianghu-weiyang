"""Compare fighter paint after combat with a clean GPU capture.

Blood/bruise multiply must preserve alpha and cannot brighten RGB. A GPU
reference accounts for import compression instead of mistaking its alpha
quantization for damage. Ground paint has a separate contract.
"""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np
from PIL import Image


def audit(capture, reference_capture, textures):
    diagnostics = json.loads((capture / 'rendering-diagnostics.json').read_text(encoding='utf-8'))
    reference = json.loads((reference_capture / 'rendering-diagnostics.json').read_text(encoding='utf-8'))
    clean_state = json.loads((reference_capture / 'hierarchy-preview.json').read_text(encoding='utf-8'))
    if clean_state['impacts'] != 0:
        raise ValueError('Reference capture must precede any combat impact')
    key = lambda row: (row['source'], row['tint'], row['current']['width'], row['current']['height'])
    reference_pixels = {}
    for row in reference['paint_textures']:
        if row['name'] == 'BloodOnTheGround_PaintableMesh':
            continue
        pixels = np.array(Image.open(reference_capture / 'paint-textures' / row['file']).convert('RGBA'))
        identity = key(row)
        if identity in reference_pixels and not np.array_equal(reference_pixels[identity], pixels):
            raise ValueError('Ambiguous clean GPU source/tint identity: ' + str(identity))
        reference_pixels[identity] = pixels
    rows, skipped, errors = [], [], []
    for row in diagnostics['paint_textures']:
        if row['name'] == 'BloodOnTheGround_PaintableMesh':
            skipped.append({'name': row['name'], 'reason': 'separate ground paint contract'})
            continue
        source = textures / (row['source'] + '.png')
        saved = capture / 'paint-textures' / row['file']
        if key(row) not in reference_pixels or not source.is_file() or not saved.is_file():
            errors.append({'name': row['name'], 'reference_missing': key(row) not in reference_pixels,
                           'source_missing': not source.is_file(), 'capture_missing': not saved.is_file()})
            continue
        expected = reference_pixels[key(row)]
        actual = np.array(Image.open(saved).convert('RGBA'))
        if actual.shape != expected.shape:
            errors.append({'name': row['name'], 'reason': 'texture dimensions differ'})
            continue
        mismatches = int(np.count_nonzero(actual[:, :, 3] != expected[:, :, 3]))
        change = actual[:, :, :3].astype(int) - expected[:, :, :3].astype(int)
        brighter = int(np.count_nonzero(np.max(change, axis=2) > 2))
        darkened = int(np.count_nonzero(np.min(change, axis=2) < -2))
        rows.append({'name': row['name'], 'source': row['source'], 'file': row['file'],
                     'source_sha256': hashlib.sha256(source.read_bytes()).hexdigest(),
                     'tint': row['tint'], 'pixels': int(actual.shape[0] * actual.shape[1]),
                     'alpha_mismatch': mismatches, 'rgb_increase_over_2': brighter, 'darkened_over_2': darkened,
                     'maximum_alpha_difference': int(np.max(np.abs(actual[:, :, 3].astype(int) - expected[:, :, 3].astype(int)))),
                     'passed': mismatches == 0 and brighter == 0})
    combat_state = json.loads((capture / 'hierarchy-preview.json').read_text(encoding='utf-8'))
    return {'passed': bool(rows) and not errors and all(row['passed'] for row in rows),
            'scope': 'Every fighter alpha texel equals the zero-impact GPU reference with identical source texture and full tint. Multiply may darken RGB but must not brighten it by more than two byte values.',
            'reference_capture': str(reference_capture), 'reference_impacts': clean_state['impacts'],
            'observed_impacts': combat_state['impacts'],
            'checked_textures': len(rows), 'damaged_textures': sum(not row['passed'] for row in rows),
            'alpha_mismatch_total': sum(row['alpha_mismatch'] for row in rows),
            'rgb_increase_over_2_total': sum(row['rgb_increase_over_2'] for row in rows),
            'darkened_over_2_total': sum(row['darkened_over_2'] for row in rows),
            'textures': rows, 'skipped': skipped, 'errors': errors}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--capture', type=Path, required=True)
    parser.add_argument('--reference-capture', type=Path, required=True)
    parser.add_argument('--textures', type=Path, default=Path('unity/BloodyBastardsRecovery/Assets/Texture2D'))
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    report = audit(args.capture, args.reference_capture, args.textures)
    if args.output:
        args.output.write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps({key: value for key, value in report.items() if key not in ('textures', 'scope')}, ensure_ascii=True))
    return 0 if report['passed'] else 2


if __name__ == '__main__':
    raise SystemExit(main())
