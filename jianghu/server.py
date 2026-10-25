from copy import deepcopy
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
from pathlib import Path
import threading
import time
import traceback
from urllib.parse import parse_qs, urlparse

from .ai import AIManager, DEFAULT_CONFIG
from .engine import World
from .store import Store
from . import items

ROOT = Path(__file__).resolve().parents[1]


class Game:
    def __init__(self, path, seed=42, population=100, prehistory=18):
        self.lock = threading.RLock()
        self.path = Path(path)
        config_path = ROOT / 'data' / 'ai_config.json'
        try:
            config = json.loads(config_path.read_text(encoding='utf-8'))
        except (FileNotFoundError, ValueError):
            config = DEFAULT_CONFIG.copy()
        # Upgrade the old 2-person budget only when opening a pre-v3 config; keep a
        # user's subsequent explicit budget setting intact.
        if config.get('config_version',0)<3:
            config={**config,'reviews_per_month':24,'cooldown_months':3,'queue_limit':48,'config_version':3,
                    'max_tokens':360}
        self.ai = AIManager(config)
        self.world = World(seed, population, Store(path))
        self.ai.restore_stats(self.world.store)
        self.job = {'remaining': 0, 'total': 0, 'completed': 0, 'fast': True, 'label': '已暂停', 'error': ''}
        self.next_step = 0
        self.autoplay = False
        self.closed = False
        if self.world.month == 0 and prehistory:
            self.job = {'remaining': prehistory*12, 'total': prehistory*12, 'completed': 0,
                        'fast': True, 'label': f'{prehistory}年历史预演 · 规则', 'error': ''}
        self.thread = threading.Thread(target=self.loop, daemon=True)
        self.thread.start()

    def loop(self):
        while not self.closed:
            try:
                with self.lock:
                    self.ai.collect(self.world)
                    fast = self.job['fast'] and not self.autoplay
                    ai_budget_wait = (not fast and game_ai_wait(self.ai, self.world.month))
                    if (self.job['remaining'] > 0 or self.autoplay) and not ai_budget_wait and time.monotonic() >= self.next_step:
                        self.world.month_step()
                        self.job['completed'] += 1
                        if self.job['remaining']:
                            self.job['remaining'] -= 1
                        if not fast:
                            self.ai.schedule(self.world)
                        delay = 1 if self.ai.config['enabled'] and not fast else (.55 if self.autoplay else 0)
                        self.next_step = time.monotonic() + delay
                        if not self.job['remaining'] and not self.autoplay:
                            self.job['label'] = '已暂停'
                time.sleep(.015)
            except Exception as e:
                traceback.print_exc()
                with self.lock:
                    self.autoplay = False
                    self.job['remaining'] = 0
                    self.job['error'] = str(e)
                    self.job['label'] = '模拟异常 · 已暂停'
                time.sleep(.5)

    def state(self):
        self.ai.stats['eligible']=sum(n['alive'] and n['age_months']>=120 for n in self.world.npcs.values())
        self.ai.stats['coverage']=sum(n['alive'] and n['last_ai_review'] is not None for n in self.world.npcs.values())
        return {**self.world.summary(), 'ai': self.ai.view(), 'job': self.job.copy(),
                'autoplay': self.autoplay, 'database': str(self.path),
                'ai_budget_wait': game_ai_wait(self.ai, self.world.month),
                'world_events': self.world.store.history(limit=25, important=True)}

    def control(self, body):
        action = body.get('action', 'advance')
        if action == 'pause':
            self.autoplay = False
            self.job['remaining'] = 0
            self.job['label'] = '已暂停'
            self.world.checkpoint()
        elif action == 'auto':
            self.autoplay = not self.autoplay
            self.job['fast'] = False
            self.job['label'] = '江湖自行运转' if self.autoplay else '已暂停'
        elif action == 'advance':
            months = int(body.get('months', 1))
            if not 1 <= months <= 600:
                raise ValueError('每次推进1至600个月')
            if self.job['remaining'] or self.autoplay:
                raise ValueError('请先暂停当前推进')
            fast = bool(body.get('fast', False))
            self.job = {'remaining': months, 'total': months, 'completed': 0, 'fast': fast,
                        'label': f'推进{months}个月' + (' · 规则快进' if fast else ''), 'error': ''}
            self.next_step = 0
        else:
            raise ValueError('未知控制指令')
        return self.job

    def configure(self, body):
        config = {**self.ai.config}
        for k in ('backend', 'url', 'model', 'enabled'):
            if k in body:
                config[k] = body[k]
        if config['backend'] not in ('ollama', 'llamacpp'):
            raise ValueError('请选择Ollama或llama.cpp')
        parsed = urlparse(config['url'])
        if parsed.hostname not in ('127.0.0.1', 'localhost', '::1') or parsed.scheme != 'http':
            raise ValueError('仅允许本机HTTP模型服务')
        for k, lo, hi in [('reviews_per_month', 1, 128), ('cooldown_months', 1, 24), ('queue_limit', 1, 128)]:
            if k in body:
                config[k] = max(lo, min(hi, int(body[k])))
        self.ai.config = config
        (ROOT / 'data' / 'ai_config.json').write_text(json.dumps(config, indent=2), encoding='utf-8')
        return self.ai.view()


def handler_for(game):
    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *_):
            pass

        def json_response(self, data, status=200):
            payload = json.dumps(data, ensure_ascii=False).encode('utf-8')
            self.send_response(status)
            self.send_header('Content-Type', 'application/json; charset=utf-8')
            self.send_header('Cache-Control', 'no-store')
            self.send_header('Content-Length', str(len(payload)))
            self.end_headers()
            self.wfile.write(payload)

        def do_GET(self):
            parsed = urlparse(self.path)
            path, query = parsed.path, parse_qs(parsed.query)
            try:
                if path.startswith('/api/'):
                    with game.lock:
                        if path == '/api/state':
                            data = game.state()
                        elif path.startswith('/api/npc/'):
                            data = game.world.npc_detail(int(path.rsplit('/', 1)[1]))
                            data['history'] = game.world.store.history(data['id'], limit=60)
                        elif path == '/api/events':
                            npc = int(query['npc'][0]) if 'npc' in query else None
                            before = int(query['before'][0]) if 'before' in query else None
                            data = game.world.store.history(npc, before, 60, query.get('important') == ['1'])
                        elif path.startswith('/api/event/'):
                            data = game.world.store.event(int(path.rsplit('/', 1)[1]))
                            if data:
                                data['cause_events'] = [game.world.store.event(i) for i in data['causes']]
                                data['perspectives'] = game.world.store.perspectives(data['id'])
                        elif path == '/api/ai/logs':
                            rows = game.world.store.db.execute('SELECT data FROM ai_log ORDER BY id DESC LIMIT 15').fetchall()
                            data = [json.loads(r[0]) for r in rows]
                        elif path == '/api/inventory':
                            data = items.view(game.world, 'player')
                        elif path == '/api/market':
                            data = items.market_view(game.world)
                        else:
                            self.json_response({'error': '接口不存在'}, 404)
                            return
                    self.json_response(data)
                    return
                files = {'/': 'index.html', '/index.html': 'index.html', '/style.css': 'style.css', '/app.js': 'app.js', '/inventory.js': 'inventory.js'}
                filename = files.get(path)
                if not filename:
                    self.send_error(404)
                    return
                payload = (ROOT / 'static' / filename).read_bytes()
                self.send_response(200)
                self.send_header('Content-Type', {'html': 'text/html', 'css': 'text/css', 'js': 'text/javascript'}[filename.split('.')[-1]]+'; charset=utf-8')
                self.send_header('Content-Length', str(len(payload)))
                self.end_headers()
                self.wfile.write(payload)
            except (ValueError, KeyError) as e:
                self.json_response({'error': str(e)}, 400)

        def do_POST(self):
            try:
                origin = self.headers.get('Origin')
                if origin and urlparse(origin).netloc != self.headers.get('Host'):
                    self.json_response({'error': '拒绝跨来源修改'}, 403)
                    return
                length = int(self.headers.get('Content-Length', 0))
                if length > 16000:
                    raise ValueError('请求过长')
                body = json.loads(self.rfile.read(length) or b'{}')
                with game.lock:
                    if self.path == '/api/control':
                        data = game.control(body)
                    elif self.path == '/api/intervene':
                        data = game.world.intervene(int(body['npc_id']), body['action'], body.get('text', ''))
                        game.ai.schedule(game.world, int(body['npc_id']))
                    elif self.path == '/api/items':
                        data = items.perform(game.world, 'player', body['operation'], body.get('item_id'),
                                             body.get('target'), body.get('recipe'))
                        game.world.checkpoint()
                    elif self.path == '/api/ai/config':
                        data = game.configure(body)
                    elif self.path == '/api/ai/review':
                        if not game.ai.config['enabled']:
                            raise ValueError('请先在本地模型设置中启用AI')
                        scheduled = game.ai.schedule(game.world, int(body['npc_id']))
                        data = {'scheduled': scheduled}
                    elif self.path == '/api/save':
                        game.world.checkpoint()
                        data = {'saved': str(game.path)}
                    elif self.path == '/api/new':
                        if game.ai.pending:
                            raise ValueError('请等待当前推理完成后开新江湖')
                        population = int(body.get('population', 100))
                        years = int(body.get('prehistory', 18))
                        if not 30 <= population <= 1000 or not 0 <= years <= 50:
                            raise ValueError('人数30至1000，预演0至50年')
                        seed = int(body.get('seed', 42))
                        game.autoplay = False
                        game.world.checkpoint()
                        game.world.store.close()
                        # Each new society gets its own file; previous evidence remains intact.
                        game.path = ROOT / 'data' / f'world-{time.time_ns()}.sqlite'
                        (ROOT / 'data' / 'active_world.txt').write_text(str(game.path), encoding='utf-8')
                        game.world = World(seed, population, Store(game.path))
                        game.ai.reset_stats()
                        game.job = {'remaining': years*12, 'total': years*12, 'completed': 0,
                                    'fast': True, 'label': '历史预演 · 规则', 'error': ''}
                        game.next_step = 0
                        data = {'database': str(game.path)}
                    else:
                        self.json_response({'error': '接口不存在'}, 404)
                        return
                self.json_response(data)
            except (ValueError, KeyError, TypeError) as e:
                self.json_response({'error': str(e)}, 400)
    return Handler


def game_ai_wait(ai, month):
    # Ordinary actions never call the model. After one whole world-month, let an
    # outstanding important review land before racing decades past its context.
    return bool(ai.config['enabled'] and ai.submitted_months and
                month-min(ai.submitted_months.values()) >= 1)


def serve(path, host='127.0.0.1', port=8765, seed=42, population=100, prehistory=18):
    game = Game(path, seed, population, prehistory)
    server = ThreadingHTTPServer((host, port), handler_for(game))
    print(f'江湖已开启: http://{host}:{port}  存档: {path}', flush=True)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        game.closed = True
        game.ai.closed = True
        with game.lock:
            game.world.checkpoint()
        server.server_close()
