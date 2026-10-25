"""Workspace-local llama.cpp + Qwen3 4B. Does not install system services."""
import hashlib
import json
from pathlib import Path
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[1]
RUNTIME = ROOT / '.runtime'
MODEL_URL = 'https://huggingface.co/Qwen/Qwen3-4B-GGUF/resolve/main/Qwen3-4B-Q4_K_M.gguf'


def download(url, dest):
    if dest.exists():
        print('Already downloaded:', dest, flush=True)
        return
    partial = dest.with_suffix(dest.suffix + '.part')
    offset = partial.stat().st_size if partial.exists() else 0
    req = urllib.request.Request(url, headers={'Range': f'bytes={offset}-'} if offset else {})
    with urllib.request.urlopen(req, timeout=120) as response:
        if response.status != 206:
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
    partial.replace(dest)


def main():
    RUNTIME.mkdir(exist_ok=True)
    release_url = 'https://api.github.com/repos/ggml-org/llama.cpp/releases?per_page=12'
    releases = json.load(urllib.request.urlopen(release_url, timeout=30))
    asset = next(a for r in releases for a in r['assets'] if a['name'].endswith('bin-win-vulkan-x64.zip'))
    archive = RUNTIME / asset['name']
    download(asset['browser_download_url'], archive)
    binaries = RUNTIME / 'llama'
    binaries.mkdir(exist_ok=True)
    with zipfile.ZipFile(archive) as z:
        z.extractall(binaries)
    model = RUNTIME / 'Qwen3-4B-Q4_K_M.gguf'
    download(MODEL_URL, model)
    with model.open('rb') as f:
        if f.read(4) != b'GGUF':
            raise RuntimeError('Invalid GGUF header')
    manifest = {'runtime': asset['browser_download_url'], 'model': MODEL_URL,
                'model_bytes': model.stat().st_size,
                'model_sha256': hashlib.file_digest(model.open('rb'), 'sha256').hexdigest()}
    (RUNTIME / 'manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    print('Ready. Start with: python scripts/start_model.py', flush=True)


if __name__ == '__main__':
    main()
