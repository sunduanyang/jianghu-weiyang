"""Owned local companion for the native Unity client. Python standard library only."""
import argparse
import ctypes
import json
import os
from pathlib import Path
import shutil
import sqlite3
import threading
import time
from http.server import ThreadingHTTPServer

from jianghu import server as original
from jianghu.engine import World
from jianghu.store import Store


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--port', type=int, required=True)
    p.add_argument('--session', required=True)
    p.add_argument('--save-dir', type=Path, required=True)
    p.add_argument('--parent', type=int, default=0)
    args = p.parse_args()
    args.save_dir.mkdir(parents=True, exist_ok=True)
    (args.save_dir / 'data').mkdir(exist_ok=True)
    original.ROOT = args.save_dir
    active = args.save_dir / 'data' / 'active_world.txt'
    path = Path(active.read_text(encoding='utf-8').strip()) if active.exists() else args.save_dir / 'world.sqlite'
    seed = Path(__file__).with_name('seed-world.sqlite')
    if not path.exists() and seed.exists():
        shutil.copy2(seed, path)
    game = original.Game(path)
    # Explicitly saved model preferences persist; autoplay remains off on startup.
    base = original.handler_for(game)

    class Handler(base):
        def authorized(self):
            if self.headers.get('X-Unity-Session') == args.session:
                return True
            self.json_response({'error': 'Unity session mismatch'}, 403)
            return False

        def do_GET(self):
            if not self.authorized():
                return
            if self.path == '/api/unity/health':
                self.json_response({'session': args.session, 'engine': 'jianghu-unity', 'pid': os.getpid()})
                return
            if not self.path.startswith('/api/'):
                self.json_response({'error': 'Native Unity client only'}, 404)
                return
            super().do_GET()

        def do_POST(self):
            if not self.authorized():
                return
            if self.path == '/api/unity/shutdown':
                with game.lock:
                    game.autoplay = False
                    game.job['remaining'] = 0
                    game.world.checkpoint()
                self.json_response({'saved': True})
                threading.Thread(target=http.shutdown, daemon=True).start()
                return
            if self.path == '/api/unity/import':
                try:
                    body = json.loads(self.rfile.read(int(self.headers.get('Content-Length', '0'))))
                    source = Path(body['path']).resolve(strict=True)
                    with game.lock:
                        if game.ai.pending or game.ai.dialogue_pending or game.autoplay or game.job['remaining']:
                            raise ValueError('请先暂停推演并等待模型结束')
                        target = args.save_dir / 'data' / f'imported-{time.time_ns()}.sqlite'
                        with sqlite3.connect(source.as_uri() + '?mode=ro', uri=True) as src:
                            with sqlite3.connect(target) as dst:
                                src.backup(dst)
                                if dst.execute('pragma integrity_check').fetchone()[0] != 'ok':
                                    raise ValueError('存档完整性检查失败')
                                if not dst.execute("SELECT 1 FROM sqlite_master WHERE name='checkpoint'").fetchone():
                                    raise ValueError('不是江湖存档')
                                row = dst.execute('SELECT data FROM checkpoint WHERE id=1').fetchone()
                                saved = json.loads(row[0]) if row else {}
                                if not all(k in saved for k in ('npcs', 'month', 'player')):
                                    raise ValueError('存档缺少世界检查点')
                        replacement = World(42, 800, Store(target), compact=True, market_economy=True,rank_assessments=True)
                        game.world.checkpoint()
                        game.world.store.close()
                        game.world = replacement
                        from jianghu import player_social
                        player_social.recover(game.world)
                        game.path = target
                        game.ai.reset_stats()
                        game.ai.restore_stats(game.world.store)
                        active.write_text(str(target), encoding='utf-8')
                    self.json_response({'database': str(target), 'copied_from': str(source)})
                except Exception as exc:
                    self.json_response({'error': str(exc)}, 400)
                return
            super().do_POST()

    http = ThreadingHTTPServer(('127.0.0.1', args.port), Handler)
    if args.parent and os.name == 'nt':
        def watch_parent():
            kernel = ctypes.WinDLL('kernel32', use_last_error=True)
            kernel.OpenProcess.restype = ctypes.c_void_p
            handle = kernel.OpenProcess(0x100000, False, args.parent)
            if handle:
                kernel.WaitForSingleObject.argtypes = [ctypes.c_void_p, ctypes.c_ulong]
                kernel.WaitForSingleObject(handle, 0xFFFFFFFF)
                kernel.CloseHandle.argtypes = [ctypes.c_void_p]
                kernel.CloseHandle(handle)
                http.shutdown()
        threading.Thread(target=watch_parent, daemon=True).start()
    print(f'Unity companion ready on {args.port}; save={path}', flush=True)
    try:
        http.serve_forever()
    finally:
        game.closed = True
        game.ai.close()
        game.thread.join(timeout=3)
        with game.lock:
            game.world.checkpoint()
            game.world.store.close()
        http.server_close()


if __name__ == '__main__':
    main()
