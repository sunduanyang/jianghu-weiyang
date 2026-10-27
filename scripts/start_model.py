import subprocess
import socket
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
from jianghu.model_service import model_command

with socket.socket() as probe:
    if probe.connect_ex(('127.0.0.1', 11435)) == 0:
        raise SystemExit('Local model service already running on port 11435')
command, runtime = model_command(dict(url='http://127.0.0.1:11435', context_size=8192), ROOT / '.runtime')
subprocess.run(command, cwd=Path(command[0]).parent)
