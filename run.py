import argparse
import socket
from pathlib import Path
from jianghu.server import serve

if __name__ == '__main__':
    root = Path(__file__).resolve().parent
    parser = argparse.ArgumentParser(description='江湖 · 本地AI社会沙盒')
    parser.add_argument('--port', type=int, default=8782)
    parser.add_argument('--population', type=int, default=800)
    parser.add_argument('--seed', type=int, default=42)
    parser.add_argument('--prehistory', type=int, default=0)
    parser.add_argument('--db', type=Path)
    args = parser.parse_args()
    with socket.socket() as probe:
        if probe.connect_ex(('127.0.0.1', args.port)) == 0:
            print(f'Local port {args.port} is already running. Open http://127.0.0.1:{args.port}')
            raise SystemExit(0)
    active = root / 'data' / 'active_world.txt'
    path = args.db or (Path(active.read_text(encoding='utf-8').strip()) if active.exists() else root / 'data' / 'world.sqlite')
    serve(path, port=args.port, seed=args.seed, population=args.population, prehistory=args.prehistory)
