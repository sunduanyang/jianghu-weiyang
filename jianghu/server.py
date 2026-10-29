from copy import deepcopy
from http.server import BaseHTTPRequestHandler
import json
from pathlib import Path
import threading
import time
import traceback
from urllib.parse import parse_qs, urlparse

from .ai import AIManager, DEFAULT_CONFIG, continuous_config, participation
from .engine import World
from .store import Store
from . import reactions, visits
from . import items, equipment, territory, city_maps, government, protagonist, economics, rank_progression, llm_policy, npc_attention, player_social, player_interactions

ROOT = Path(__file__).resolve().parents[1]
# Requests where the player acts on a person; new events there may earn an in-character answer.
REACTING_PATHS = ('/api/interaction', '/api/items', '/api/ranks', '/api/battle', '/api/visit')


class Game:
    def __init__(self, path, seed=42, population=1800, prehistory=0, model_config=None):
        self.lock = threading.RLock()
        self.path = Path(path)
        config_path = ROOT / 'data' / 'ai_config.json'
        try:
            config = json.loads(config_path.read_text(encoding='utf-8'))
        except (FileNotFoundError, ValueError):
            config = DEFAULT_CONFIG.copy()
        if model_config is not None:config.update(model_config)
        config=continuous_config(config)
        self.ai = AIManager(config)
        self.world = World(seed, population, Store(path), compact=True, population_profile=True, market_economy=True,rank_assessments=True)
        self.ai.restore_stats(self.world.store)
        if player_social.recover(self.world) | reactions.recover(self.world) | visits.recover(self.world):self.world.checkpoint()
        self.job = {'remaining': 0, 'total': 0, 'completed': 0, 'fast': True, 'label': '已暂停', 'error': ''}
        self.next_step = 0
        self.autoplay = False
        self.closed = False
        self.next_ai_pump=0.
        if self.world.month == 0 and prehistory and not (self.world.battle and self.world.battle['status'] == 'active'):
            self.job = {'remaining': prehistory*12, 'total': prehistory*12, 'completed': 0,
                        'fast': True, 'label': f'{prehistory}年历史预演 · 规则', 'error': ''}
        self.thread = threading.Thread(target=self.loop, daemon=True)
        self.thread.start()

    def loop(self):
        while not self.closed:
            try:
                with self.lock:
                    self.ai.collect(self.world)
                    fast = bool(self.job['remaining'] and self.job['fast'] and not self.autoplay)
                    now=time.monotonic()
                    if not fast and self.ai.config['enabled'] and now>=self.next_ai_pump and not (self.world.battle and self.world.battle['status']=='active'):
                        self.ai.schedule(self.world);self.next_ai_pump=now+1
                    if (self.job['remaining'] > 0 or self.autoplay) and now >= self.next_step:
                        if self.job.pop('hero_turn',False):
                            h=self.world.player['protagonist']
                            protagonist.record(self.world,'hero_end_turn',f"{self.world.player['name']}结束本月行程，江湖推移一月。",
                                               dict(unused_points=h['points'],next_month=self.world.month+1))
                        self.world.month_step()
                        self.ai.refresh_queue(self.world)
                        self.job['completed'] += 1
                        if self.job['remaining']:
                            self.job['remaining'] -= 1
                        self.job['thinking']=False
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

    def defer_hero_turn(self):
        self.control(dict(action='advance',months=1,fast=False))
        self.job['hero_turn']=True
        return protagonist.view(self.world)

    def state(self):
        self.ai.stats['eligible']=sum(npc_attention.eligible(self.world,n) for n in self.world.npcs.values())
        self.ai.stats['coverage']=sum(n['alive'] and n['last_ai_review'] is not None for n in self.world.npcs.values())
        self.ai.stats['policy_active']=sum(n['alive'] and llm_policy.current(self.world,n) is not None for n in self.world.npcs.values())
        self.ai.stats['month_completed']=sum(n.get('last_ai_review')==self.world.month for n in self.world.npcs.values() if n['alive'])
        self.ai.stats['recent_player_contacts']=sum(n['alive'] and npc_attention.profile(self.world,n)['recent'] for n in self.world.npcs.values())
        self.ai.stats['high_status_attention']=sum(n['alive'] and (npc_attention.grade(self.world,n)<=3 or bool(n.get('office'))) for n in self.world.npcs.values())
        self.ai.stats['plan_executions']=self.world.metrics.get('ai_executed_actions',0)
        self.ai.stats['policy_actions']=self.world.metrics.get('ai_policy_actions',0)
        self.ai.stats['policy_ranking_changes']=self.world.metrics.get('ai_policy_ranking_changes',0)
        self.ai.stats.update(participation(self.world))
        return {**self.world.summary(), 'ai': self.ai.view(), 'job': self.job.copy(),
                'autoplay': self.autoplay, 'database': str(self.path),
                'ai_budget_wait': False,
                'world_events': self.world.store.history(limit=25, important=True)}

    def control(self, body):
        action = body.get('action', 'advance')
        if action in ('auto', 'advance') and self.world.battle and self.world.battle['status'] == 'active':
            raise ValueError('切磋尚未结束，请先收招或认输')
        if action == 'pause':
            self.autoplay = False
            self.job['remaining'] = 0
            self.job['label'] = '已暂停'
            self.job['thinking']=False;self.job.pop('hero_turn',None)
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
        for k in ('backend', 'url', 'model', 'enabled', 'auto_start'):
            if k in body:
                config[k] = body[k]
        if config['backend'] not in ('ollama', 'llamacpp'):
            raise ValueError('请选择Ollama或llama.cpp')
        parsed = urlparse(config['url'])
        if parsed.hostname not in ('127.0.0.1', 'localhost', '::1') or parsed.scheme != 'http':
            raise ValueError('仅允许本机HTTP模型服务')
        for k, lo, hi in [('cooldown_months', 1, 24), ('queue_limit', 1, 128)]:
            if k in body:
                config[k] = max(lo, min(hi, int(body[k])))
        self.ai.config = continuous_config(config)
        self.ai.refresh_queue(self.world)
        self.ai.next_retry=0.;self.next_ai_pump=0.
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
                        elif path == '/api/martial':
                            data = game.world.martial_view()
                        elif path == '/api/campaign':
                            target = int(query.get('place', ['-1'])[0])
                            data = {'options': territory.attack_options(game.world, target)}
                        elif path == '/api/city':
                            data = city_maps.view(game.world, int(query.get('place', ['-1'])[0]))
                        elif path == '/api/hero':
                            data = protagonist.view(game.world)
                        elif path == '/api/hero/travel':
                            data = protagonist.travel_plan(game.world, int(query.get('place', ['-1'])[0]))
                            if game.autoplay or game.job['remaining']:
                                data.update(enabled=False, reason='请先暂停世界推演，再安排行程')
                        elif path == '/api/interaction':
                            data = player_interactions.view(game.world, int(query.get('npc', ['-1'])[0]))
                        elif path.startswith('/api/npc/'):
                            data = game.world.npc_detail(int(path.rsplit('/', 1)[1]))
                            data['history'] = game.world.store.history(data['id'], limit=60)
                        elif path.startswith('/api/faction/'):
                            faction_id = int(path.rsplit('/', 1)[1])
                            if not 0 <= faction_id < len(game.world.factions):
                                raise ValueError('组织不存在')
                            data = game.world.faction_detail(faction_id)
                        elif path.startswith('/api/country/'):
                            country_id = int(path.rsplit('/', 1)[1])
                            if not 0 <= country_id < len(game.world.countries):
                                raise ValueError('国家不存在')
                            data = game.world.country_detail(country_id)
                            data['policy_catalog'] = government.POLICIES
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
                        elif path == '/api/equipment':
                            data = equipment.view(game.world, 'player')
                        elif path == '/api/market':
                            data = items.market_view(game.world)
                        elif path == '/api/economy':
                            place = int(query['place'][0]) if 'place' in query else None
                            if place is not None and str(place) not in economics.state(game.world)['markets']:
                                raise ValueError('请选择有效城镇')
                            data = economics.view(game.world, place, 'player')
                        else:
                            self.json_response({'error': '接口不存在'}, 404)
                            return
                    self.json_response(data)
                    return
                self.json_response({'error': '仅提供 Unity 本地接口'}, 404)
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
                    # People the player just dealt with answer in character once the rules have settled it.
                    reacting = self.path in REACTING_PATHS or (self.path == '/api/hero' and isinstance(body, dict)
                                                               and body.get('action') not in ('end_turn', None))
                    since = game.world.store.next_id
                    if self.path == '/api/control':
                        data = game.control(body)
                    elif self.path == '/api/hero':
                        if game.autoplay or game.job['remaining']:
                            raise ValueError('请先暂停世界推演，再安排行程')
                        data = protagonist.perform(game.world, body,game.defer_hero_turn if game.ai.config['enabled'] else None)
                    elif self.path == '/api/interaction':
                        if game.autoplay or game.job['remaining']:
                            raise ValueError('请先暂停世界推演，再与人物往来')
                        data = player_interactions.perform(game.world, body)
                    elif self.path == '/api/visit':
                        if game.autoplay or game.job['remaining']:
                            raise ValueError('请先暂停江湖，再接待来访')
                        data = visits.answer(game.world, body)
                    elif self.path == '/api/intervene':
                        if body.get('action')=='talk' or body.get('text',''):
                            raise ValueError('自由交谈已关闭，请在人物页选择请求或说服选项')
                        if body['action'] == 'challenge' and (game.autoplay or game.job['remaining']):
                            raise ValueError('请先暂停江湖，再进行切磋')
                        data = game.world.intervene(int(body['npc_id']), body['action'], body.get('text', ''))
                        game.ai.schedule(game.world, int(body['npc_id']))
                    elif self.path == '/api/social':
                        if game.autoplay or game.job['remaining']:
                            raise ValueError('请先暂停江湖，再当面商谈')
                        mode=body.get('mode','request')
                        fields={'npc_id','revision','mode'}|({'action','text'} if mode=='request' else {'request_id','option_id'} if mode=='persuade' else {'request_id'} if mode=='options' else set())
                        if set(body)-fields:raise ValueError('交涉只接受既定请求和选项编号，不能提交自定义台词或指令')
                        data = game.ai.interact(game.world,int(body['npc_id']),body.get('action','talk'),
                                                body.get('text',''),body.get('revision'),mode,
                                                body.get('request_id'),body.get('option_id'))
                    elif self.path in ('/api/items', '/api/equipment'):
                        if self.path == '/api/equipment' and (
                                set(body) - {'operation', 'item_id'}
                                or body.get('operation') not in ('equip', 'unequip', 'repair')):
                            raise ValueError('装备操作只接受装备、卸下或修补及物品编号')
                        if game.world.battle and game.world.battle['status'] == 'active':
                            raise ValueError('请先结束切磋，再整理或使用物品')
                        if body['operation']=='gift' and game.world.player['protagonist']['created']:
                            if game.autoplay or game.job['remaining']:
                                raise ValueError('请先暂停江湖，再赠送物品')
                            h=game.world.player['protagonist']
                            data=player_interactions.perform(game.world,dict(action='gift',npc_id=int(body.get('target')),
                                item_id=body.get('item_id'),revision=h['revision'],month=game.world.month))['event']
                        else:
                            data = items.perform(game.world, 'player', body['operation'], body.get('item_id'),
                                                 body.get('target'), body.get('recipe'))
                        if body['operation']=='gift':game.ai.schedule(game.world,int(body['target']))
                        game.world.checkpoint()
                        if self.path == '/api/equipment':
                            data = {**equipment.view(game.world, 'player'), 'last_event': data['id']}
                    elif self.path == '/api/economy':
                        if game.autoplay or game.job['remaining']:
                            raise ValueError('请先暂停世界推演，再安排行程')
                        data = economics.perform(game.world, body)
                    elif self.path == '/api/ranks':
                        if game.autoplay or game.job['remaining']:
                            raise ValueError('请先暂停世界推演，再办理身份事务')
                        data = rank_progression.player_action(game.world,body)
                    elif self.path == '/api/martial':
                        if game.autoplay or game.job['remaining']:
                            raise ValueError('请先暂停江湖，再调养')
                        data = game.world.martial_action(body.get('action'), body.get('art'))
                    elif self.path == '/api/campaign':
                        if game.autoplay or game.job['remaining'] or (game.world.battle and game.world.battle['status'] == 'active'):
                            raise ValueError('请先暂停江湖并结束切磋，再推演战事')
                        data = territory.campaign(game.world, body.get('attacker'), body.get('place'), declare=True)
                        game.world.checkpoint()
                    elif self.path == '/api/battle':
                        data = game.world.battle_action(body.get('battle_id'), body.get('revision'), body.get('action'), body.get('frame'))
                    elif self.path == '/api/ai/config':
                        data = game.configure(body)
                    elif self.path == '/api/ai/review':
                        if not game.ai.config['enabled']:
                            raise ValueError('请先在本地模型设置中启用AI')
                        scheduled = game.ai.schedule(game.world, int(body['npc_id']))
                        data = {'scheduled': scheduled}
                    elif self.path == '/api/ai/focus':
                        npc_id=int(body['npc_id'])
                        if npc_id not in game.world.npcs:raise ValueError('人物不存在')
                        game.ai.focus_id=npc_id;game.next_ai_pump=0.
                        data={'focused':npc_id}
                    elif self.path == '/api/save':
                        game.world.checkpoint()
                        data = {'saved': str(game.path)}
                    elif self.path == '/api/new':
                        if game.world.battle and game.world.battle['status'] == 'active':
                            raise ValueError('请先结束当前切磋')
                        if game.ai.pending or game.ai.dialogue_pending:
                            raise ValueError('请等待当前推理完成后开新江湖')
                        population = int(body.get('population', 1800))
                        years = int(body.get('prehistory', 0))
                        if not 30 <= population <= 3000 or not 0 <= years <= 50:
                            raise ValueError('人数30至3000，预演0至50年')
                        seed = int(body.get('seed', 42))
                        game.autoplay = False
                        game.world.checkpoint()
                        game.world.store.close()
                        # Each new society gets its own file; previous evidence remains intact.
                        game.path = ROOT / 'data' / f'world-{time.time_ns()}.sqlite'
                        (ROOT / 'data' / 'active_world.txt').write_text(str(game.path), encoding='utf-8')
                        game.world = World(seed, population, Store(game.path), compact=True, population_profile=True, market_economy=True,rank_assessments=True)
                        game.ai.reset_stats()
                        game.job = {'remaining': years*12, 'total': years*12, 'completed': 0,
                                    'fast': True, 'label': '历史预演 · 规则', 'error': ''}
                        game.next_step = 0
                        data = {'database': str(game.path)}
                    else:
                        self.json_response({'error': '接口不存在'}, 404)
                        return
                    if reacting and getattr(game, 'ai', None) is not None:
                        game.ai.react(game.world, since)
                self.json_response(data)
            except (ValueError, KeyError, TypeError) as e:
                self.json_response({'error': str(e)}, 400)
    return Handler


def game_ai_wait(ai, month):
    # Kept for older callers: monthly progression never waits for inference.
    return False
