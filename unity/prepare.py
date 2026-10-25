"""Stage portable Unity source assets; local saves and Python bundles are opt-in."""
import argparse
import os
from pathlib import Path
import hashlib
import json
import shutil
import sqlite3
import re
import sys
import zipfile

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / 'unity' / 'JianghuUnity'


def bundle_runtime(assets):
    if os.name != 'nt' or not (Path(sys.base_prefix) / 'python.exe').exists():
        raise SystemExit('--bundle-python requires an installed Windows CPython 3.12+ interpreter')
    runtime = assets / 'StreamingAssets' / 'Python'
    runtime.mkdir(parents=True, exist_ok=True)
    prefix = Path(sys.base_prefix)
    version = f'{sys.version_info.major}{sys.version_info.minor}'
    for pattern in ('python.exe', f'python{version}.dll', 'python3.dll', 'vcruntime*.dll', 'LICENSE.txt'):
        for source_file in prefix.glob(pattern):
            shutil.copy2(source_file, runtime / source_file.name)
    dlls = runtime / 'DLLs'
    dlls.mkdir(exist_ok=True)
    for source_file in (prefix / 'DLLs').glob('*'):
        if source_file.is_file() and source_file.suffix in ('.dll', '.pyd') and not any(p in source_file.name for p in ('tkinter', '_test', 'tcl', 'tk86')):
            shutil.copy2(source_file, dlls / source_file.name)
    library = prefix / 'Lib'
    stdlib = runtime / f'python{version}.zip'
    with zipfile.ZipFile(stdlib, 'w', zipfile.ZIP_DEFLATED) as archive:
        for source_file in library.rglob('*.py'):
            relative = source_file.relative_to(library)
            if any(part in {'site-packages','__pycache__','test','tests','idlelib','tkinter','ensurepip','turtledemo'} for part in relative.parts):
                continue
            archive.write(source_file, str(relative))
    (runtime / f'python{version}._pth').write_text(f'python{version}.zip\n.\nDLLs\n../Backend\n', encoding='utf-8')


def prepare(backend_only=False, bundle_python=False, include_local_save=False):
    assets = PROJECT / 'Assets'
    backend = assets / 'StreamingAssets' / 'Backend'
    backend.mkdir(parents=True, exist_ok=True)
    for source in sorted((ROOT / 'jianghu').glob('*.py')):
        target = backend / 'jianghu' / source.name
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, target)
    if backend_only:
        print('Shared engine synchronized into Unity.')
        return
    resources = assets / 'Resources' / 'Art'
    resources.mkdir(parents=True, exist_ok=True)
    for name in ('portraits.png', 'river-valley.png'):
        shutil.copy2(ROOT / 'static' / 'assets' / name, resources / name)
    shutil.copy2(ROOT / 'static' / 'geography-compact.json', assets / 'Resources' / 'geography.json')
    # Bake vector geography masks; ownership colours and interaction stay native in Unity.
    from PIL import Image, ImageDraw, ImageChops
    geo = json.loads((ROOT / 'static' / 'geography-compact.json').read_text(encoding='utf-8'))
    maps = assets / 'Resources' / 'Map'
    maps.mkdir(parents=True, exist_ok=True)
    def polygons(path):
        for section in re.split('[Mm]', path):
            nums = [float(n) for n in re.findall(r'-?\d+(?:\.\d+)?', section)]
            if len(nums) >= 6:
                yield list(zip(nums[::2], nums[1::2]))
    land = Image.new('L', (1280, 760))
    draw = ImageDraw.Draw(land)
    for poly in polygons(geo['land']):
        draw.polygon(poly, fill=255)
    def save_mask(mask, path):
        img = Image.new('RGBA', mask.size, 'white')
        img.putalpha(mask)
        img.save(path)
    save_mask(land, maps / 'land.png')
    territories = {}
    for territory in geo['territories']:
        mask = Image.new('L', land.size)
        draw = ImageDraw.Draw(mask)
        for poly in polygons(territory['path']):
            draw.polygon(poly, fill=255)
        territories[territory['country']] = mask
    for cell in geo['cells']:
        mask = Image.new('L', land.size)
        draw = ImageDraw.Draw(mask)
        for poly in polygons(cell['path']):
            draw.polygon(poly, fill=255)
        mask = ImageChops.multiply(mask, territories[cell['native_country']])
        save_mask(ImageChops.multiply(mask, land), maps / f"cell-{cell['place']}.png")
    reference = assets / 'StreamingAssets' / 'OriginalPuppets'
    reference.mkdir(parents=True, exist_ok=True)
    for name in ('puppet.js', 'rig-spec.json'):
        shutil.copy2(ROOT / 'prototypes' / 'shadow-puppets' / name, reference / name)
    if bundle_python:
        bundle_runtime(assets)
    snapshot = backend / 'seed-world.sqlite'
    active = ROOT / 'data' / 'active_world.txt'
    source = Path(active.read_text(encoding='utf-8').strip()) if active.exists() else ROOT / 'data' / 'world.sqlite'
    if include_local_save and source.exists() and not snapshot.exists():
        # SQLite backup includes committed WAL pages and works with the web game open.
        with sqlite3.connect(source.as_uri() + '?mode=ro', uri=True) as src:
            with sqlite3.connect(snapshot) as dst:
                src.backup(dst)
                assert dst.execute('pragma integrity_check').fetchone()[0] == 'ok'
    manifest = {'source': '.', 'source_save': str(source) if include_local_save else None,
                'save_policy': 'New world by default; local save snapshots require --include-local-save',
                'backend_files': {p.name: hashlib.sha256(p.read_bytes()).hexdigest()
                                  for p in sorted((backend / 'jianghu').glob('*.py'))}}
    (PROJECT.parent / 'migration-manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({'project': str(PROJECT), 'snapshot_bytes': snapshot.stat().st_size if snapshot.exists() else 0}, ensure_ascii=False))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--backend-only', action='store_true', help='Only sync the shared Python engine; no art dependencies required')
    parser.add_argument('--bundle-python', action='store_true', help='Bundle this Windows CPython for a self-contained player (ignored by Git)')
    parser.add_argument('--include-local-save', action='store_true', help='Opt in to a local SQLite snapshot; never committed to Git')
    options = parser.parse_args()
    prepare(**vars(options))
