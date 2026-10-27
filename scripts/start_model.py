import subprocess
import socket
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
with socket.socket() as probe:
    if probe.connect_ex(('127.0.0.1', 11435)) == 0:
        raise SystemExit('Local model service already running on port 11435')
server = next((ROOT / '.runtime' / 'llama').rglob('llama-server.exe'), None)
model = ROOT / '.runtime' / 'Qwen3-4B-Q4_K_M.gguf'
if not server or not model.exists():
    raise SystemExit('Run python scripts/setup_model.py first')
subprocess.run([str(server), '-m', str(model), '-ngl', '99', '-c', '8192',
                '--parallel', '1', '--host', '127.0.0.1', '--port', '11435',
                '--jinja', '--reasoning-budget', '0'], cwd=server.parent)
