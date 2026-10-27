"""Install a verified, project-local Qwen3.5 4B and llama.cpp runtime."""
import argparse
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[1]
RUNTIME = ROOT / '.runtime'
MODEL_FILE = 'Qwen3.5-4B-Q4_K_M.gguf'
MODEL_REVISION = 'e87f176479d0855a907a41277aca2f8ee7a09523'
MODEL_URL = f'https://huggingface.co/unsloth/Qwen3.5-4B-GGUF/resolve/{MODEL_REVISION}/{MODEL_FILE}'
MODEL_SHA256 = '00fe7986ff5f6b463e62455821146049db6f9313603938a70800d1fb69ef11a4'
LLAMA_RELEASE = 'b11933'


def checksum(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def download(url, dest, expected_sha256):
    if dest.exists():
        if checksum(dest) != expected_sha256:
            raise RuntimeError(f'Checksum mismatch: {dest}; existing file preserved')
        print('Verified:', dest.name, flush=True)
        return
    partial = dest.with_suffix(dest.suffix + '.part')
    print('Downloading:', dest.name, flush=True)
    offset = partial.stat().st_size if partial.exists() else 0
    req = urllib.request.Request(url, headers={'Range': f'bytes={offset}-'} if offset else {})
    with urllib.request.urlopen(req, timeout=120) as response:
        if response.status == 206:
            if not response.headers.get('Content-Range', '').startswith(f'bytes {offset}-'):
                raise RuntimeError('Invalid resume offset')
        else:
            offset = 0
        total = int(response.headers.get('Content-Length', 0)) + offset
        done, last = offset, offset // (128 * 1024**2)
        with partial.open('ab' if offset else 'wb') as out:
            while chunk := response.read(4 * 1024**2):
                out.write(chunk)
                done += len(chunk)
                if done // (128 * 1024**2) > last:
                    last = done // (128 * 1024**2)
                    print(f'{dest.name}: {done/1024**2:.0f}/{total/1024**2:.0f} MiB', flush=True)
        if total and done != total:
            raise RuntimeError('Incomplete download; rerun to resume')
    if checksum(partial) != expected_sha256:
        raise RuntimeError(f'Checksum mismatch: {partial}; installation not activated')
    partial.replace(dest)


def main(backend='auto'):
    RUNTIME.mkdir(exist_ok=True)
    if backend == 'auto':
        probe = shutil.which('nvidia-smi')
        backend = 'cuda' if probe and subprocess.run(
            [probe, '--query-gpu=name', '--format=csv,noheader'], capture_output=True,
            timeout=15, creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0)).returncode == 0 else 'vulkan'
    release_url = f'https://api.github.com/repos/ggml-org/llama.cpp/releases/tags/{LLAMA_RELEASE}'
    with urllib.request.urlopen(release_url, timeout=30) as response:
        release = json.load(response)
    suffix = 'cuda-12.4-x64.zip' if backend == 'cuda' else 'vulkan-x64.zip'
    names = [f'llama-{LLAMA_RELEASE}-bin-win-{suffix}']
    if backend == 'cuda':
        names.append('cudart-llama-bin-win-cuda-12.4-x64.zip')
    binaries = RUNTIME / f'llama-{LLAMA_RELEASE}-{backend}'
    binaries.mkdir(exist_ok=True)
    assets = []
    downloads = []
    for name in names:
        asset = next(a for a in release['assets'] if a['name'] == name)
        digest = asset.get('digest', '')
        if not digest.startswith('sha256:'):
            raise RuntimeError(f'Missing upstream checksum: {name}')
        archive = RUNTIME / name
        downloads.append((asset['browser_download_url'], archive, digest[7:]))
        assets.append(dict(url=asset['browser_download_url'], sha256=digest[7:]))
    model = RUNTIME / MODEL_FILE
    downloads.append((MODEL_URL, model, MODEL_SHA256))
    with ThreadPoolExecutor(max_workers=3) as pool:
        futures = [pool.submit(download, *args) for args in downloads]
        for future in futures:
            future.result()
    for _, archive, _ in downloads[:-1]:
        with zipfile.ZipFile(archive) as bundle:
            bundle.extractall(binaries)
    with model.open('rb') as f:
        if f.read(4) != b'GGUF':
            raise RuntimeError('Invalid GGUF header')
    server = next(binaries.rglob('llama-server.exe'))
    subprocess.run([str(server), '--version'], cwd=server.parent, check=True,
                   creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0))
    manifest = dict(model=MODEL_URL, model_file=MODEL_FILE, model_id='qwen3.5:4b',
                    model_bytes=model.stat().st_size, model_sha256=MODEL_SHA256,
                    server=str(server.relative_to(RUNTIME)), backend=backend,
                    llama_release=LLAMA_RELEASE, runtime_assets=assets)
    target = RUNTIME / 'manifest.json'
    if target.exists():
        shutil.copy2(target, RUNTIME / f'manifest-before-{datetime.now():%Y%m%d-%H%M%S}.json')
    pending = target.with_suffix('.json.tmp')
    pending.write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    pending.replace(target)
    print(f'Ready: {MODEL_FILE}, {backend}. Old model and runtime retained.', flush=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--backend', choices=('auto', 'cuda', 'vulkan'), default='auto')
    main(parser.parse_args().backend)
