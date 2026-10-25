"""Stop only this workspace's default local services; save the world first."""
import json
from pathlib import Path
import subprocess
import sys
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
DRY_RUN = '--dry-run' in sys.argv


def listening_process(port):
    command = f'Get-NetTCPConnection -LocalPort {port} -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty OwningProcess'
    result = subprocess.run(['powershell', '-NoProfile', '-Command', command], capture_output=True, text=True)
    return int(result.stdout.strip()) if result.stdout.strip().isdigit() else None


def stop(pid):
    if DRY_RUN:
        print('Verified workspace service PID:', pid)
        return
    subprocess.run(['powershell', '-NoProfile', '-Command', f'Stop-Process -Id {pid}'], check=True)


try:
    with urllib.request.urlopen('http://127.0.0.1:8765/api/state', timeout=5) as response:
        state = json.load(response)
    if not Path(state['database']).resolve().is_relative_to((ROOT/'data').resolve()):
        raise RuntimeError('Port 8765 belongs to another workspace; left running')
    req = urllib.request.Request('http://127.0.0.1:8765/api/control', b'{"action":"pause"}', {'Content-Type': 'application/json'})
    if not DRY_RUN:
        urllib.request.urlopen(req, timeout=15).close()
    pid = listening_process(8765)
    if pid:
        stop(pid)
        print('Game service verified.' if DRY_RUN else 'World saved; game service stopped.')
except OSError:
    print('Game service is not running.')

pid = listening_process(11435)
if pid:
    command = f'[Console]::OutputEncoding=[System.Text.UTF8Encoding]::new(); Get-CimInstance Win32_Process -Filter "ProcessId={pid}" | Select-Object Name,CommandLine | ConvertTo-Json -Compress'
    result = subprocess.run(['powershell', '-NoProfile', '-Command', command], capture_output=True, text=True, encoding='utf-8')
    info = json.loads(result.stdout)
    if info['Name'].lower() == 'llama-server.exe' and str(ROOT).casefold() in info['CommandLine'].casefold():
        stop(pid)
        print('Model service verified.' if DRY_RUN else 'Workspace model stopped; GPU memory released.')
