"""Monthly, seeded social simulation. Only this module settles world consequences."""
from collections import Counter
from copy import deepcopy
import math
import random
import time

from .store import Store
from . import demography, government, items
from . import social
from . import politics
from .atlas import PLACES, FACTIONS, COUNTRIES, ROUTES, NEIGHBORS, MAP_VERSION, ERA
from . import careers
from . import martial, combat

TRAITS = {'compassion': '仁厚', 'ambition': '进取', 'caution': '谨慎',
          'sociability': '外向', 'loyalty': '忠诚', **social.EXTRA_TRAITS}
BELIEFS = {'trust': '人可相托', 'justice': '善有善报', 'tradition': '传承有益'}
DESIRES = {'wealth': '安家立业', 'mastery': '武学有成', 'belonging': '有所归属',
           'renown': '名动一方', 'freedom': '行遍江湖', 'revenge': '讨回公道'}
ACTIONS = {'work': '谋生', 'train': '习武', 'rest': '休养', 'befriend': '结交',
           'court': '倾诉心意', 'propose': '提亲', 'aid': '接济', 'trade': '合营',
           'betray': '窃取托银', 'duel': '寻仇比武', 'mentor': '拜师', 'teach': '传艺',
           'join': '入会', 'duty': '尽责', 'travel': '远行', 'rumor': '交换见闻',
           'family': '筹划育儿', 'treatment': '求医', 'report': '报官', 'public_service': '公务',
           'use_item': '使用物品', 'equip_item': '整备行装', 'read_item': '研读典籍',
           'buy_item': '购置物品', 'sell_item': '出售物品', 'repair_item': '修补物品', 'learn_trade': '学艺', 'change_job': '转业', **social.LABELS}
JOBS = list(careers.PROFESSIONS)
FAMILY = '沈陆顾苏林萧温谢江柳叶裴唐宋秦孟钟楚傅白'
GIVEN = ['知秋', '望舒', '行舟', '照野', '清和', '若水', '长宁', '归尘', '听澜', '云生',
         '怀瑾', '见山', '含章', '疏影', '停云', '临渊', '景明', '闻溪', '无咎', '星河',
         '映雪', '怀安', '南枝', '逐风', '修竹', '问筠', '明霁', '玉衡', '令仪', '寒松']


def clamp(v, lo=0, hi=100):
    return round(max(lo, min(hi, v)), 4)


def date(month):
    return f'景和{month // 12 + 1}年{month % 12 + 1}月'


class World:
    def __init__(self, seed=42, population=100, store=None, load=True, durable=True):
        self.store = store or Store()
        self.rng = random.Random(seed)
        self.seed, self.month = seed, 0
        self.phase = 0
        self.npcs, self.factions = {}, deepcopy(FACTIONS)
        self.atlas_history = []
        self._atlas_migration = None
        self.countries = politics.new()
        self.governments = [government.new(c['id']) for c in COUNTRIES]
        self.government = self.governments[0]
        self.economy = {'gaming_house': 200.}
        self.player = {'name': '过客', 'wealth': 160., 'reputation': 0, 'ties': {}}
        martial.initialize(self.player, player=True)
        self.battle = None
        self.metrics = {'steps': 0, 'simulation_ms': 0., 'actions': {}, 'goals_completed': 0,
                        'births': 0, 'deaths': 0, 'death_causes': {}, 'invalid_actions': 0,
                        'ai_applied': 0, 'ai_changed_actions': 0, 'ai_stale': 0}
        self.population_limit = max(1200, population * 2)
        self.residents = {}
        self.durable = durable
        state = self.store.load() if load else None
        if state:
            self.restore(state)
            if 'government' not in state:
                government.establish(self)
            elif 'governments' not in state:
                for g in self.governments[1:]:
                    government.establish(self, g)
            if state.get('map_version') == 2:
                self.emit('atlas_revised', [], '舆图改依约公元1200年宋金形势重绘；原有公帑按辖地转入各政权，人物财产与旧年谱保留。', .9,
                          facts=self._atlas_migration)
            elif state.get('map_version', 1) < MAP_VERSION:
                self.emit('world_expansion', [], '全国舆图展开：旧五地归入南宋，新增各地府衙与江湖组织；'
                          '原有人物、财产和经历继续保留，新机构库银作为初始条件登记。', .9,
                          facts={'map_version': MAP_VERSION, 'places': len(PLACES),
                                 'countries': len(COUNTRIES), 'factions': len(FACTIONS),
                                 'new_government_opening_funds': 80 * (len(COUNTRIES)-1),
                                 'new_faction_opening_funds': 100 * (len(FACTIONS)-3),
                                 'initial_condition': True})
            if state.get('map_version', 1) < MAP_VERSION or 'government' not in state:
                self.checkpoint()
        else:
            for _ in range(population):
                self.make_npc()
            self.reindex()
            # Census is an honest initial condition, not invented life history.
            for n in self.npcs.values():
                e = self.emit('census', [n['id']], f"{n['name']}以{n['age_months']//12}岁之龄载入乡籍，"
                              f"居于{PLACES[n['place']]['name']}，以{n['job']}为生。", .25,
                              facts={'age_months': n['age_months'], 'wealth': n['wealth'],
                                     'skill': n['skill'], 'initial_condition': True})
                self.set_goal(n, self.best_goal(n), e['id'], initial=True)
            government.establish(self)
            items.initialize(self)
            self.checkpoint()

    def make_npc(self, parents=None, place=None):
        i = len(self.npcs) + 1
        traits = {k: self.rng.uniform(15, 85) for k in TRAITS}
        name = FAMILY[(i - 1) % len(FAMILY)] + GIVEN[((i - 1) // len(FAMILY) + (i - 1) * 7) % len(GIVEN)]
        if parents:
            family_parent = next((self.npcs[p] for p in parents if self.npcs[p]['sex'] == '男'), self.npcs[parents[0]])
            name = family_parent['name'][0] + name[1:]
        used_names = {p['name'] for p in self.npcs.values() if p['alive']}
        if name in used_names:
            first, last = '景云清长怀明若南初承书玄星沐疏听修望怀照', '安宁舟远溪秋山川竹松澜月雪衡瑾章尘霁岚风'
            cursor = i + self.seed
            while True:
                candidate = name[0] + first[(cursor//len(last)) % len(first)] + last[cursor % len(last)]
                if cursor >= len(first)*len(last):
                    candidate += first[(cursor//(len(first)*len(last))-1) % len(first)]
                if candidate not in used_names:
                    name = candidate
                    break
                cursor += 1
        age = 0 if parents else self.rng.randint(7 * 12, 48 * 12)
        n = {'id': i, 'name': name, 'sex': self.rng.choice(['女', '男']), 'age_months': age,
             'alive': True, 'place': place if place is not None else self.rng.choices(PLACES, weights=[p['weight'] for p in PLACES], k=1)[0]['id'],
             'job': self.rng.choice(JOBS), 'wealth': round(self.rng.uniform(20, 95), 2) if not parents else 0.,
             'skill': self.rng.uniform(4, 32) if age > 180 else 2., 'health': 100., 'reputation': 0.,
             'traits': traits, 'initial_traits': traits.copy(), 'mood': {'joy': 0., 'anger': 0., 'fear': 0.},
             'beliefs': {'trust': self.rng.uniform(.35, .8), 'justice': self.rng.uniform(.3, .8), 'tradition': .6},
             'desires': {}, 'relationships': {}, 'knowledge': {}, 'memories': [],
             'parents': parents or [], 'children': [], 'spouse': None, 'mentor': None, 'faction': None,
             'contribution': 0., 'goal': None, 'past_goals': [], 'plan': None, 'last_action': None,
             'last_event': None, 'last_review': -12, 'review_urgency': .4, 'ai_pending': False,
             'reviews': [], 'moves': 0, 'last_birth': -60, 'drift_month': {}, 'reflected': [],
             'last_goal_change': -999, 'personality_changes': []}
        n['cooldowns'] = {}
        if age < 192:
            n['job'] = '幼童' if age < 120 else '学徒'
        n['desires'] = {
            'wealth': clamp(35 + traits['caution'] * .45 + self.rng.uniform(-20, 20)),
            'mastery': clamp(30 + traits['ambition'] * .5 + self.rng.uniform(-30, 30)),
            'belonging': clamp(20 + traits['sociability'] * .65 + self.rng.uniform(-20, 20)),
            'renown': clamp(traits['ambition'] * .85 + self.rng.uniform(-10, 15)),
            'freedom': clamp(85 - traits['caution'] * .65 + self.rng.uniform(-20, 20)), 'revenge': 0.}
        if parents:
            for k in TRAITS:
                n['traits'][k] = clamp(sum(self.npcs[p]['traits'][k] for p in parents) / len(parents)
                                        + self.rng.uniform(-16, 16))
            n['initial_traits'] = n['traits'].copy()
        self.npcs[i] = n
        demography.initialize(self, n)
        social.initialize(self, n)
        martial.initialize(n)
        n['office'] = None
        items.prepare(n)
        n['office_country'] = None
        n['visited_places'] = [n['place']]
        careers.initialize(self, n)
        n['identity'] = careers.identity(self, n)
        return n

    def place_name(self, place):
        return PLACES[place]['name']

    def summary_place(self, place):
        return PLACES[place].copy()

    def local_government(self, n):
        return government.for_npc(self, n)

    def government_grant(self, n, amount, purpose, cause=None):
        return government.grant(self, n, amount, purpose, cause)

    def reindex(self):
        self.residents = {i: [] for i in range(len(PLACES))}
        for n in self.npcs.values():
            if n['alive']:
                self.residents[n['place']].append(n['id'])

    def relation(self, n, other):
        key = str(other)
        if key not in n['relationships']:
            n['relationships'][key] = {'affinity': 0., 'trust': 0., 'romance': 0.,
                                        'last_event': None, 'label': '相识'}
        return n['relationships'][key]

    def know(self, n, other):
        if n['id'] == other['id']:
            return
        public_identity = careers.identity(self, other)
        n['knowledge'][str(other['id'])] = {'name': other['name'], 'place': other['place'],
                                            'seen_month': self.month,
                                            'job': other['job'],
                                            'identity': public_identity['title'],
                                            'identity_grade': public_identity['grade'],
                                            'skill_band': int(other['skill'] // 20),
                                            'wealth_band': '拮据' if other['wealth'] < 15 else '有余'}

    def link(self, a, b, affinity=0, trust=0, romance=0, label=None):
        for p, q in [(a, b), (b, a)]:
            r = self.relation(p, q['id'])
            r['affinity'] = clamp(r['affinity'] + affinity, -100, 100)
            r['trust'] = clamp(r['trust'] + trust, -100, 100)
            r['romance'] = clamp(r['romance'] + romance)
            durable_tie = ('夫妻' if p['spouse'] == q['id'] else '师徒' if
                           p['mentor'] == q['id'] or q['mentor'] == p['id'] else '亲子' if
                           q['id'] in p['parents'] + p['children'] else None)
            r['label'] = (durable_tie + '·反目' if durable_tie and r['affinity'] < -25 else
                          durable_tie or label or ('仇怨' if r['affinity'] < -25 else '挚友' if r['affinity'] > 55
                                                   else '友人' if r['affinity'] > 20 else '相识'))
            self.know(p, q)

    def emit(self, kind, people, summary, importance=.3, parents=None, facts=None, tone=None):
        people = list(dict.fromkeys(people))
        event = self.store.append({'month': self.month, 'week': self.phase,
                                   'date': date(self.month)+(f'·第{self.phase}周' if self.phase else ''), 'kind': kind,
                                   'people': people, 'summary': summary, 'importance': importance,
                                   'causes': list(dict.fromkeys(x for x in (parents or []) if x)),
                                   'facts': facts or {}})
        for i in people:
            n = self.npcs[i]
            if not n['alive']:
                if kind == 'death' and event['facts'].get('deceased') == i:
                    n['last_event'] = event['id']
                continue
            prior_beliefs = n['beliefs'].copy()
            subjective, changes = self.interpret(n, event, tone)
            changes += [{'axis': k, 'domain': 'belief', 'delta': round((v-prior_beliefs[k])*100, 5),
                         'event_id': event['id'], 'source': '经历塑造信念'}
                        for k, v in n['beliefs'].items() if abs(v-prior_beliefs[k]) > .000001]
            memory = {'event_id': event['id'], 'month': self.month, 'fact': summary,
                      'interpretation': subjective, 'salience': importance, 'changes': changes,
                      'source': '亲历', 'ai': False}
            self.store.experience(i, memory, self.month)
            n['memories'].append(memory)
            # Keep recent experience and enduring turning points; full history stays in SQLite.
            if len(n['memories']) > 40:
                old, recent = n['memories'][:-18], n['memories'][-18:]
                old = sorted(old, key=lambda m: (m['salience'], m['month']), reverse=True)[:22]
                n['memories'] = sorted(old + recent, key=lambda m: m['event_id'])
            n['last_event'] = event['id']
            if importance >= .65:
                n['review_urgency'] = max(n['review_urgency'], importance)
                n['trigger_events'] = (n.get('trigger_events',[])+[event['id']])[-8:]
            for j in people:
                if str(j) in n['relationships']:
                    n['relationships'][str(j)]['last_event'] = event['id']
        if kind != 'identity_change':
            for i in people:
                careers.sync_identity(self, self.npcs[i], event['id'])
        return event

    def drift(self, n, axis, requested, major=False, event_id=None, source='经历累积'):
        # Total trait movement is capped per month, with extra plasticity only at turning points.
        cap = 1.2 if major else .28
        remaining = max(0, cap - n['drift_month'].get(axis, 0))
        delta = max(-remaining, min(remaining, requested))
        before = n['traits'][axis]
        n['traits'][axis] = clamp(before + delta)
        actual = n['traits'][axis] - before
        n['drift_month'][axis] = n['drift_month'].get(axis, 0) + abs(actual)
        if abs(actual) > .001:
            change = {'month': self.month, 'axis': axis, 'delta': round(actual, 4),
                      'event_id': event_id, 'source': source}
            n['personality_changes'].append(change)
            n['personality_changes'] = n['personality_changes'][-80:]
            return change
        return None

    def interpret(self, n, e, tone=None):
        k, t, b = e['kind'], n['traits'], n['beliefs']
        mixed = social.interpretation(self,n,e)
        if mixed is not None:
            return mixed
        changes = []
        positive = k in ('aid', 'marriage', 'friendship', 'teaching', 'birth', 'promotion', 'goal_complete',
                         'pregnancy', 'recovery', 'government_relief', 'judgment', 'guardianship')
        negative = k in ('betrayal', 'duel', 'death', 'disaster', 'loss', 'rejection', 'illness', 'accident', 'epidemic')
        if k in ('work', 'trade', 'train', 'duty'):
            # Small repeated feedback has an observable cumulative effect over decades.
            # These proposals depend on this person's existing interpretation/disposition.
            if k == 'trade':
                success = e['facts'].get('success', True)
                axis = 'ambition' if success and t['ambition'] > 45 else 'caution'
                amount = .006 if success else .014
                text = ('各有所求，眼下合作对我有利。' if success and t['greed']>55 else
                        '往来守信，合作比独行更有余地。' if success and b['trust'] > .5 else
                        '这笔买卖有赚有赔，下次须算清风险。' if not success and t['caution'] > 50 else
                        '一时得失不足定终身，还得看长远。')
                b['trust'] = clamp(b['trust'] + (.0015 if success else -.003), 0, 1)
            elif k == 'work':
                axis, amount = ('compassion', -.012) if n['pressure']>35 and t['compassion']<55 else (
                    ('ambition', -.004) if t['ambition']<35 else ('ambition', .004))
                text = '连自家的生计都难顾，哪有余力管旁人。' if n['pressure']>35 else (
                    '够自己过日子就好，不必事事争先。' if t['ambition']<35 else '眼下的积累，是往后选择的底气。')
            elif k == 'train':
                axis, amount = 'ambition', .012
                text = '武学不负日积月累，急不得，也停不得。'
            else:
                axis, amount = ('loyalty', .007) if t['loyalty']>50 else ('honesty', -.004)
                text = '共同做事，才知道这份组织之约值多少。' if t['loyalty']>50 else '出了这份力，总得为自己换些门路。'
            changes.append(self.drift(n, axis, amount, event_id=e['id']))
        elif k == 'aid':
            receiver = e['facts'].get('receiver') == n['id']
            text = ('危难时仍有人相助，我愿再信人一次。' if receiver else
                    '江湖情义需要有人先付出。' if t['compassion'] > 50 else '结一份善缘，往后或有所助。')
            b['trust'] = clamp(b['trust'] + (.025 if receiver else .008), 0, 1)
            changes.append(self.drift(n, 'compassion', .08 if t['compassion'] > 50 else .025, event_id=e['id']))
        elif k in ('betrayal', 'loss'):
            culprit = e['facts'].get('offender') == n['id']
            text = ('银钱到手，却损了情义。' if culprit else
                    '轻信让人付出代价，下回须留一手。' if t['caution'] > 50 else '此事伤人，但不能因此疑尽天下人。')
            if not culprit:
                b['trust'] = clamp(b['trust'] - (.11 if k == 'betrayal' else .04) * (1.3 - t['compassion']/200), 0, 1)
                changes.append(self.drift(n, 'caution', .7 if k == 'betrayal' else .1, k == 'betrayal', e['id']))
                grievance = 30+(100-t['compassion'])*.6+t['ambition']*.12
                n['desires']['revenge'] = clamp(n['desires']['revenge'] + (grievance if k == 'betrayal' else 4))
                if k == 'betrayal':
                    n['grievance_cause'] = e['id']
        elif k == 'death':
            text = '生死无常，更要守住眼前之人。' if t['loyalty'] > 50 else '此后的人生，需另寻支点。'
            changes.append(self.drift(n, 'loyalty', .5 if t['loyalty'] > 50 else -.25, True, e['id']))
            n['desires']['belonging'] = clamp(n['desires']['belonging'] + 6)
        elif k == 'disaster':
            text = '世事难料，先备余粮才安心。' if t['caution'] > 50 else '邻里都受了难，须设法相扶。'
            changes.append(self.drift(n, 'caution' if t['caution'] > 50 else 'compassion', .45, True, e['id']))
            n['desires']['wealth'] = clamp(n['desires']['wealth'] + 3)
        elif k == 'duel':
            won = e['facts'].get('winner') == n['id']
            text = ('苦练终有回响，尚须自持。' if t['loyalty'] > 50 else '强者才有话语权。') if won else (
                '一时之败，须用长久苦练补上。' if t['ambition'] > 50 else '逞强的代价，值得记住。')
            changes.append(self.drift(n, 'ambition' if t['ambition'] > 50 else 'caution', .18, event_id=e['id']))
        elif k in ('teaching', 'mentor'):
            text = '技艺由人传人，前路便不再孤单。'
            b['tradition'] = clamp(b['tradition'] + .008, 0, 1)
        elif k == 'marriage':
            text = '从此有所牵挂，取舍不再只为自己。'
            changes.append(self.drift(n, 'loyalty', .5, True, e['id']))
        elif k == 'birth':
            text = '新生命让未来有了分量。' if n['age_months'] > 0 else '在亲人的照料中开始人生。'
        elif k == 'census':
            text = '这是乡籍所记的人生起点；更早经历未被模拟。'
        elif k == 'goal_complete':
            text = '多年的付出有了回报，也该想想下一程。'
        elif k in ('government_relief', 'judgment'):
            text = '公家若能护住百姓，日子便多一分依靠。' if b['justice'] > .5 else '此事得了帮助，往后还得看官府如何行事。'
            b['justice'] = clamp(b['justice']+.015, 0, 1)
        elif k in ('illness', 'accident', 'epidemic'):
            text = '性命有时比前程更紧要，须调整眼前打算。'
        else:
            text = tone or ('这份善缘值得珍惜。' if positive else '这次挫折需要慢慢消化。' if negative else '日常的积累，也在铺就往后的路。')
        if positive:
            n['mood']['joy'] = clamp(n['mood']['joy'] + e['importance'] * 24, -100, 100)
        if negative:
            n['mood']['anger'] = clamp(n['mood']['anger'] + e['importance'] * (100-t['compassion']) / 4)
            n['mood']['fear'] = clamp(n['mood']['fear'] + e['importance'] * t['caution'] / 5)
            n['mood']['joy'] = clamp(n['mood']['joy'] - e['importance'] * 20, -100, 100)
        return text, [c for c in changes if c]

    def best_goal(self, n):
        options = self.goal_options(n)
        return max(options, key=lambda k: n['desires'][k] + self.rng.uniform(-8, 8))

    def goal_options(self, n):
        keys = ['wealth', 'belonging', 'freedom']
        if n['skill'] < 92:
            keys.append('mastery')
        if n['reputation'] < 85:
            keys.append('renown')
        if any(r['affinity'] < -25 for r in n['relationships'].values()):
            keys.append('revenge')
        return keys

    def set_goal(self, n, key, cause=None, initial=False):
        old = n['goal']
        if old:
            n['past_goals'].append({**old, 'ended': self.month})
            n['past_goals'] = n['past_goals'][-15:]
        targets = {'wealth': n['wealth'] + max(400, n['wealth']*.7), 'mastery': min(95, n['skill'] + 35),
                   'belonging': 1, 'renown': min(90, n['reputation'] + 45), 'freedom': n['moves'] + 9,
                   'revenge': 1}
        n['goal'] = {'kind': key, 'title': DESIRES[key], 'since': self.month,
                     'horizon_years': self.rng.randint(5, 20), 'target': targets[key],
                     'cause': cause, 'progress': 0., 'status': '追求中', 'minimum_months': 48}
        n['last_goal_change'] = self.month
        if not initial:
            self.emit('goal_change', [n['id']], f"{n['name']}将人生重心转向「{DESIRES[key]}」。", .6,
                      [cause], {'previous': old['kind'] if old else None, 'goal': key})
        n['plan'] = None

    def update_goal(self, n):
        g = n['goal']
        if not g:
            self.set_goal(n, self.best_goal(n), n['last_event'])
            return
        k = g['kind']
        value = {'wealth': n['wealth'], 'mastery': n['skill'],
                 'belonging': 1. if n['spouse'] or sum(r['affinity'] > 50 for r in n['relationships'].values()) >= 3 else 0.,
                 'renown': n['reputation'], 'freedom': n['moves'],
                 'revenge': 1. if any(m['event_id'] > (g['cause'] or 0) and '讨回' in m['fact'] for m in n['memories']) else 0.}[k]
        g['progress'] = clamp(value / max(.1, g['target']), 0, 1)
        if g['progress'] >= 1 and self.month - g['since'] >= g.get('minimum_months', 48):
            g['status'] = '已达成'
            e = self.emit('goal_complete', [n['id']], f"{n['name']}经过{(self.month-g['since'])/12:.1f}年追求，"
                          f"达成人生目标「{g['title']}」。", .85, [g['cause'], n['last_event']],
                          {'goal': k, 'target': g['target'], 'actual': round(value, 2)})
            self.metrics['goals_completed'] += 1
            n['desires'][k] = clamp(n['desires'][k] - 14)
            self.set_goal(n, self.best_goal(n), e['id'])
        elif (self.month - n['last_goal_change'] >= 24 and
              n['desires']['revenge'] > n['desires'][k] + 8 and
              'revenge' in self.goal_options(n) and k != 'revenge'):
            self.set_goal(n, 'revenge', n.get('grievance_cause') or n['last_event'])
        elif self.month - n['last_goal_change'] >= 72 and n['review_urgency'] >= .9:
            # A long-held pursuit may change after a real crisis; routine life does not reroll it.
            best = self.best_goal(n)
            if n['desires'][best] > n['desires'][k] + 15:
                self.set_goal(n, best, n['last_event'])

    def legal_actions(self, n, planning=False):
        if not n['alive'] or (not planning and not social.available(self,n)):
            return []
        actions = []
        def add(kind, target=None, detail='', **extra):
            actions.append({'id': f'{kind}:{target if target is not None else "self"}',
                            'kind': kind, 'target': target, 'label': ACTIONS[kind] + (f' · {detail}' if detail else ''), **extra})
        add('rest', detail='恢复体力')
        if n['age_months'] < 120:
            if n['age_months'] >= 60 and n['health'] > 30 and n['martial']['stamina'] >= 12:
                add('train', detail='练习基本功')
            actions += social.actions(self,n,[])
            return self.available_actions(n,actions,planning)
        actions.extend(items.candidates(self, n))
        add('work', detail=n['job'])
        actions.extend(careers.actions(self, n))
        if n['wealth'] >= 1.5 and n['health'] > 30 and n['martial']['stamina'] >= 12:
            add('train', detail='花费1.5银，练功一周')
        local = self.residents.get(n['place'], [])
        known = [int(i) for i in n['relationships'] if int(i) in self.npcs and
                 self.npcs[int(i)]['alive'] and self.npcs[int(i)]['place'] == n['place']]
        # Stable visible shortlist, rotated monthly; never use a hidden stranger's traits to score.
        strangers = [i for i in local if i != n['id'] and i not in known]
        if strangers:
            offset = (self.month + n['id']) % len(strangers)
            known += (strangers[offset:] + strangers[:offset])[:2]
        own_rank = sorted(set(known)-set(strangers), key=lambda i: abs(n['relationships'].get(str(i), {}).get('affinity', 0)), reverse=True)
        # Leave room for new acquaintances as old generations die and young people grow up.
        visible_new = [i for i in known if i in strangers]
        visible = own_rank[:6] + visible_new[:2]
        for i in visible:
            q = self.npcs[i]
            r = n['relationships'].get(str(i), {'affinity': 0, 'trust': 0, 'romance': 0})
            if q['age_months'] >= 120:
                add('befriend', i, q['name'])
            if (n['age_months'] >= 216 and q['age_months'] >= 216 and not n['spouse'] and not q['spouse']
                    and not self.close_kin(n, q) and demography.attracted(n, q)):
                if r['affinity'] >= 12:
                    add('court', i, q['name'])
                if r['romance'] >= 45 and r['affinity'] >= 30:
                    add('propose', i, q['name'])
            if n['wealth'] >= 14 and q['wealth'] < 20 and r['affinity'] > -10:
                add('aid', i, q['name'])
            if (n['age_months']>=192 and q['age_months']>=192 and n['wealth'] >= 10 and q['wealth'] >= 10 and r['trust'] >= 8 and
                    (planning or (q['time_left']>=1 and social.available(self,q) and q['action_counts'].get('trade',0)<1))):
                add('trade', i, q['name'])
            if q['wealth'] >= 20 and r['trust'] >= 18 and n['wealth'] < 80 and n['age_months'] >= 216:
                add('betray', i, q['name'])
            if r['affinity'] < -25 and n['health'] > 55 and q['health'] > 25 and n['age_months'] >= 216 and q['age_months'] >= 216:
                add('duel', i, q['name'])
            if n['age_months'] >= 120 and q['skill'] > n['skill'] + 12 and not n['mentor'] and r['affinity'] >= 6:
                add('mentor', i, q['name'])
            if q['mentor'] == n['id'] and q['health'] > 30:
                add('teach', i, q['name'])
            if r['affinity'] > 5 and n['memories']:
                add('rumor', i, q['name'])
            if (n['wealth'] >= 4 and q['job'] == '药师' and q['age_months'] >= 192 and q['health'] > 30
                    and (n['illness'] or n['wound'] or n['health'] < 55)):
                add('treatment', i, q['name'])
        if demography.fertile_pair(self, n) and n['family_intent_until'] < self.month and n['age_months'] >= 216:
            add('family', detail='与伴侣商议养育下一代')
        if n.get('grievance_cause'):
            cause = n['grievance_cause']
            if not any(c['crime_event'] == cause for g in self.governments for c in g['cases']):
                add('report', cause, '提交已知侵吞证据')
        if n['office'] and government.office_government(self, n)['fund'] >= 4:
            add('public_service', detail='支用公帑4银巡防或防疫')
        if n['faction'] is None and n['skill'] >= 12 and n['age_months'] >= 192:
            for f in self.factions:
                if f['place'] == n['place']:
                    add('join', f['id'], f['name'])
        if n['faction'] is not None and n['wealth'] >= 4:
            add('duty', n['faction'], self.factions[n['faction']]['name'])
        if n['health'] > 40 and n['age_months'] >= 192:
            for destination in NEIGHBORS[n['place']]:
                p = PLACES[destination]
                trip = politics.journey(self, n['place'], destination)
                if n['wealth'] >= trip['cost'] + 7 and (not n['office'] or p['country'] == n['office_country']):
                    add('travel', destination, f"{p['name']} · {trip['km']}公里 / {trip['cost']:.1f}银 / {trip['border']}", **trip)
        actions += social.actions(self,n,visible)
        return self.available_actions(n,actions,planning)

    def available_actions(self,n,actions,planning=False):
        return [{**a,'time_cost':social.cost(a['kind'])} for a in actions
                if n.get('cooldowns',{}).get(a['id'],-1)<=self.month and
                (n['probation_until']<self.month or a['kind'] not in ('steal','extort','smear','embezzle','travel','duel','betray')) and
                (planning or (social.cost(a['kind'])<=n['time_left'] and
                              (not self.phase or self.phase+social.cost(a['kind'])<=5) and
                              n['action_counts'].get(a['kind'],0)<social.LIMITS.get(a['kind'],4)))]

    def close_kin(self, a, b):
        def ancestors(n):
            direct = set(n['parents'])
            return direct | {g for p in direct for g in self.npcs[p]['parents']}
        aa, bb = ancestors(a), ancestors(b)
        return b['id'] in aa or a['id'] in bb or bool(aa & bb)

    def score(self, n, a):
        if 'item_score' in a:
            return a['item_score']
        k, g, t = a['kind'], n['goal']['kind'], n['traits']
        score = {'rest': -2 + (100-n['health'])*.65,
                 'work': 12 + max(0, 45-n['wealth'])*1.4,
                 'train': 9 + t['ambition']*.06,
                 'befriend': t['sociability']*.16,
                 'court': 13 + t['sociability']*.1,
                 'propose': 22, 'aid': t['compassion']*.36-6,
                 'trade': 20, 'betray': t['ambition']*.25-t['loyalty']*.36-t['compassion']*.2
                 + (30 if t['loyalty'] < 38 and t['ambition'] > 55 else 0),
                 'duel': t['ambition']*.2-t['caution']*.16,
                 'mentor': 18, 'teach': t['loyalty']*.18 + t['compassion']*.1,
                 'join': 18+n['beliefs']['tradition']*4,
                 'duty': 12+t['loyalty']*.1, 'travel': (100-t['caution'])*.08,
                 'rumor': 4+t['sociability']*.08,
                 'family': 20+t['loyalty']*.15,
                 'treatment': 45+(100-n['health'])*.5 + (20 if n['illness'] else 0),
                 'report': 15+n['beliefs']['justice']*22+t['caution']*.2,
                 'public_service': 15+t['loyalty']*.15}.get(k, 0)
        if k in ('change_job', 'learn_trade'):
            score = careers.score(self, n, a)
        boost = {'wealth': {'work': 16, 'trade': 18, 'betray': 12}, 'mastery': {'train': 23, 'mentor': 28, 'join': 15},
                 'belonging': {'befriend': 15, 'court': 25, 'propose': 34, 'aid': 5},
                 'renown': {'duty': 26, 'join': 25, 'teach': 15, 'aid': 10},
                 'freedom': {'travel': 27, 'work': 7}, 'revenge': {'duel': 38, 'train': 18}}
        score += boost[g].get(k, 0)
        if k in ('court', 'befriend'):
            r = n['relationships'].get(str(a['target']), {})
            score += (r.get('romance', 0)*.1 if k == 'court' else -r.get('affinity', 0)*.08)
        if k == 'travel':
            score -= a['cost'] * .16
            score += 5 if a['target'] not in n.get('visited_places', []) else -2
            score -= 6 if a['border'] == '交恶' else 0
        if k == 'join':
            score += 5 if self.factions[a['target']]['focus'] == g else 0
        if k == 'befriend':
            observed = n['knowledge'].get(str(a['target']), {})
            score += (9-observed.get('identity_grade', 9))*t['ambition']*.02
            score += 2 if observed.get('job') == n['job'] else 0
        if k == 'travel' and n['spouse']:
            score -= t['loyalty']*.22
        if k == 'travel' and (n['pregnancy'] or any(self.npcs[i]['alive'] and self.npcs[i]['age_months'] < 72 for i in n['children'])):
            score -= 18
        if k == 'befriend' and not n['spouse'] and 216 <= n['age_months'] <= 480:
            q = self.npcs[a['target']]
            if not q['spouse'] and demography.attracted(n, q) and demography.attracted(q, n) and not self.close_kin(n, q):
                score += 18
        if k == 'court' and not n['spouse']:
            score += 20
        if k == 'travel' and n['spouse']:
            known_spouse = n['knowledge'].get(str(n['spouse']))
            if known_spouse and known_spouse['place'] == a['target'] and n['place'] != a['target']:
                score += 28
        score += {'belonging': {'family': 16}, 'renown': {'public_service': 24},
                  'revenge': {'report': 30}}.get(g, {}).get(k, 0)
        if n['wealth'] < 10 and k != 'work':
            score -= 35
        return score + social.adjustment(self,n,a)

    def choose(self, n, actions):
        plan = n['plan']
        if plan and plan['until'] >= self.month:
            for wanted in plan['steps']:
                found = next((a for a in actions if a['id'] == wanted), None)
                if found:
                    if self.score(n, next(a for a in actions if a['kind'] == 'rest')) > 28 or n['wealth'] < 8:
                        break
                    # AI plans and rule plans persist, but still pass current legality and survival needs.
                    plan['steps'].remove(wanted)
                    n['choice_source'] = plan['source']
                    return found
        scored = [(self.score(n, a) + self.rng.uniform(-9, 9), a) for a in actions]
        chosen = max(scored, key=lambda x: x[0])[1]
        n['choice_source'] = '规则'
        n['plan'] = {'source': '规则', 'created': self.month, 'until': self.month+5,
                     'reason': f"围绕「{n['goal']['title']}」，兼顾生计与已有关系。",
                     'steps': [], 'horizon': '本周行动，下一周依反馈调整'}
        return chosen

    def execute(self, n, action):
        # Reject any caller supplied action not in the engine's CURRENT legal set.
        a = next((a for a in self.legal_actions(n) if a['id'] == action.get('id')), None)
        if a is None:
            self.metrics['invalid_actions'] += 1
            return None
        k, i = a['kind'], a['target']
        if k.endswith('_item'):
            e = items.perform(self, n['id'], k.removesuffix('_item'), i, action=a)
            return self.finish_action(n,a,e)
        q = self.npcs.get(i) if k not in ('join', 'duty', 'travel', 'report', 'change_job') else None
        parents = [n['last_event'], n['goal']['cause']]
        people, imp, facts, kind = [n['id']], .15, {'action': a['id'],'actor':n['id'],
                                                  'decision_source':n['choice_source'],'time_cost':a['time_cost']}, k
        settlement = n.get('monthly_settlement', {})
        if settlement.get('month') == self.month and not n['settlement_logged']:
            facts['monthly_settlement'] = settlement.copy()
            if settlement.get('payer') != n['id'] and settlement.get('paid', 0) > 0:
                people.append(settlement['payer'])
        before = {'wealth': n['wealth'], 'health': n['health'], 'skill': n['skill']}
        if k in social.KINDS:
            e=social.execute(self,n,a,facts)
            return self.finish_action(n,a,e)
        name = n['name']
        gear = items.bonuses(self, n['id'])
        facts['equipment_bonuses'] = gear
        if k == 'work':
            earn = (careers.income(n)+gear['income'])*.5
            school = self.factions[n['faction']] if n['faction'] is not None else None
            bonus = 1.12 if school and school['kind'] == 'guild' and school['place'] == n['place'] else 1
            earn *= bonus
            n['wealth'] += earn
            tax = government.tax(self, n, earn) if n['age_months'] >= 192 else 0
            practice = careers.gain(n, n['career']['profession'], .4)
            facts.update({'career_practice': practice, 'gross_income': earn, 'tax': tax, 'guild_multiplier': bonus,
                          'tax_country': PLACES[n['place']]['country']})
            n['health'] = clamp(n['health'] - self.rng.uniform(.5, 2))
            text = f"{name}在{PLACES[n['place']]['name']}以{n['job']}谋生，收入{earn:.1f}银，纳税{tax:.1f}银。"
        elif k == 'change_job':
            previous = n['job']
            cost = careers.PROFESSIONS[i]['cost']
            n['wealth'] -= cost
            n['job'] = i
            n['career'].update({'profession': i, 'since': self.month, 'last_change': self.month, 'source': '自主转业'})
            n['career']['experience'].setdefault(i, 0.)
            n['plan'] = None
            facts.update({'before': previous, 'after': i, 'setup_cost': cost,
                          'experience': careers.experience(n), 'cooldown_months': 24})
            text, imp = f'{name}花{cost}银置办生计，由{previous}转为{i}，旧业本领仍保留。', .7
        elif k == 'learn_trade':
            job = a['profession']
            n['wealth'] -= 2
            q['wealth'] += 2
            practice = careers.gain(n, job, 2+careers.experience(q)*.03)
            self.link(n, q, 2, 2)
            people.append(q['id'])
            facts.update({'teacher': q['id'], 'payment': 2, 'career_practice': practice})
            text, imp = f"{name}花2银向{q['name']}学习{job}手艺，熟练度增长{practice['gain']:.2f}。", .35
        elif k == 'rest':
            martial.recover(n, 10)
            n['health'] = clamp(n['health'] + (1 if settlement.get('food_shortfall', 0) else 3 if n['illness'] else 5) + gear['recovery']*.5)
            text = f'{name}休养一周，恢复体力。'
        elif k == 'train':
            n['wealth'] -= 1.5 if n['age_months'] >= 120 else 0
            school = self.factions[n['faction']] if n['faction'] is not None else None
            bonus = 1.25 if school and school['kind'] == 'sect' and school['place'] == n['place'] else 1
            training = martial.auto_practice(n, self.rng, scale=.5*bonus, equipment=gear)
            facts.update(training=training, school=n['faction'], training_multiplier=bonus)
            if n['career']['profession'] in ('镖师', '猎户'):
                facts['career_practice'] = careers.gain(n, n['career']['profession'], .15)
            n['health'] = clamp(n['health'] - .7)
            text = (f"{name}修习{training['name']}，" +
                    (f"突破至{martial.REALMS[training['after']['realm']]}。" if training['breakthrough'] else
                     f"进度达到{training['after']['practice']:.1f}。")) if not training.get('mastered') else f'{name}温习已臻化境的武学。'
        elif k == 'travel':
            old = n['place']
            self.residents[old].remove(n['id'])
            n['place'], n['moves'] = i, n['moves'] + 1
            self.residents[i].append(n['id'])
            people += demography.follow_guardian(self, n, old)
            n['wealth'] -= a['cost']
            destination_government = self.governments[a['destination_country']]
            destination_government['fund'] += a['toll']
            destination_government['revenue'] += a['toll']
            n['health'] = clamp(n['health'] - min(10, 1+a['km']/300))
            if i not in n['visited_places']:
                n['visited_places'].append(i)
            facts.update({key: a[key] for key in ('km', 'fare', 'toll', 'cost', 'border', 'origin_country', 'destination_country')})
            facts.update({'origin': old, 'destination': i})
            text = f"{name}离开{PLACES[old]['name']}，行经{a['km']}公里来到{PLACES[i]['name']}，花费{a['cost']:.1f}银（关税{a['toll']:.1f}银）。"
            imp = .4
        elif k == 'family':
            partner = self.npcs[n['spouse']]
            n['family_intent_until'] = partner['family_intent_until'] = self.month + 12
            people.append(partner['id'])
            text, imp = f"{name}与{partner['name']}商议育儿，决定为下一代准备家计。", .7
        elif k == 'treatment':
            n['wealth'] -= 4
            q['wealth'] += 4
            healing = round(10+careers.experience(q)*.2, 4)
            n['health'] = clamp(n['health']+healing)
            if n['illness']:
                n['illness']['severity'] *= .45
                n['illness']['remaining'] = min(2, n['illness']['remaining'])
            if n['wound']:
                n['wound']['remaining'] = 1
            people.append(q['id'])
            self.link(n, q, 4, 3)
            text, imp = f"{name}花4银请{q['name']}诊治，体力恢复{healing:.1f}，病伤得到缓解。", .6
            facts.update({'doctor': q['id'], 'payment': 4, 'healing': healing,
                          'career_practice': careers.gain(q, '药师', .6)})
        elif k == 'report':
            e = government.report(self, n, i, facts)
            return self.finish_action(n,a,e)
        elif k == 'public_service':
            g = government.office_government(self, n)
            g['fund'] -= 4
            g['expenses'] += 4
            g['security'] = min(.95, g['security']+.015)
            g['sanitation'] = min(.95, g['sanitation']+.01)
            n['reputation'] = clamp(n['reputation']+.8)
            text, imp = f"{name}以{n['office']}身份拨公帑4银巡防防疫。", .4
            facts.update({'public_cost': 4, 'office': n['office']})
        elif k == 'join':
            n['faction'] = i
            facts.update({'faction': i, 'art': self.factions[i]['art'],
                          'country': self.factions[i]['country']})
            text, imp = f"{name}加入{self.factions[i]['name']}，认同其「{self.factions[i]['ethos']}」之约。", .7
        elif k == 'duty':
            n['wealth'] -= 4
            n['contribution'] += 1.5
            n['reputation'] = clamp(n['reputation'] + .7)
            self.factions[i]['fund'] += 4
            text = f"{name}向{self.factions[i]['name']}出资出力，积累声望。"
        else:
            people.append(i)
            self.know(n, q)
            self.know(q, n)
            r = self.relation(n, i)
            parents.append(r['last_event'])
            if k == 'befriend':
                fit = 1 - abs(n['traits']['loyalty'] - q['traits']['loyalty'])/130
                gain = self.rng.uniform(4, 9)*fit
                self.link(n, q, gain, 2)
                kind, imp = 'friendship', .35
                text = f"{name}与{q['name']}叙谈，因往来增进交情。"
            elif k == 'court':
                # The other person has independent disposition; acceptance is settled here.
                accepted = (self.relation(q, n['id'])['affinity'] > 8 and
                            demography.attracted(q, n) and
                            self.rng.random() < .55 + q['traits']['sociability']/240)
                if accepted:
                    self.link(n, q, 5, 2, 11)
                    text, imp = f"{name}向{q['name']}倾诉心意，得到温柔回应。", .5
                else:
                    self.link(n, q, -3, 0, -2)
                    n.setdefault('cooldowns', {})[a['id']] = self.month + 6
                    text, kind, imp = f"{name}向{q['name']}表露心迹，对方婉拒，尚需相互了解。", 'rejection', .45
                facts['accepted'] = accepted
            elif k == 'propose':
                reciprocal = self.relation(q, n['id'])
                accepted = reciprocal['romance'] >= 40 and reciprocal['trust'] >= 8 and demography.attracted(q, n)
                if accepted:
                    n['spouse'], q['spouse'] = i, n['id']
                    self.link(n, q, 12, 10, 10, '夫妻')
                    text, kind, imp = f"{name}与{q['name']}历经相处，结为夫妻。", 'marriage', .95
                else:
                    text, kind, imp = f"{name}向{q['name']}提亲未果，对方尚未准备托付终身。", 'rejection', .65
                    n.setdefault('cooldowns', {})[a['id']] = self.month + 12
                facts['accepted'] = accepted
            elif k == 'aid':
                n['wealth'] -= 8
                q['wealth'] += 8
                self.link(n, q, 10, 7)
                n['reputation'] = clamp(n['reputation'] + 1)
                text, imp = f"{name}拿出8银接济拮据的{q['name']}。", .75
                facts.update({'giver': n['id'], 'receiver': i, 'transfer': 8})
            elif k == 'trade':
                n['wealth'] -= 3
                q['wealth'] -= 3
                success = self.rng.random() < .78
                merchant_bonus = sum(careers.experience(p) * .012 for p in (n, q)
                                     if p['career']['profession'] == '行商') if success else 0
                ret = (self.rng.uniform(6, 11) if success else 1) + merchant_bonus
                n['wealth'] += ret
                q['wealth'] += ret
                facts['taxes'] = {str(p['id']): government.tax(self, p, max(0, ret-3))
                                  if p['age_months'] >= 192 else 0 for p in (n, q)}
                self.link(n, q, 4 if success else -5, 3 if success else -2)
                text, imp = f"{name}与{q['name']}各出3银合营，{'获利' if success else '失利'}，各收回{ret:.1f}银。", .45
                facts.update({'success': success, 'investment_each': 3, 'return_each': ret,
                              'merchant_bonus_each': merchant_bonus})
            elif k == 'betray':
                silver = min(20, q['wealth'] * .4)
                n['wealth'] += silver
                q['wealth'] -= silver
                caught = self.rng.random() < .68
                if caught:
                    self.link(n, q, -65, -80, -20)
                    n['reputation'] = clamp(n['reputation'] - 10, -100, 100)
                    text, kind, imp = f"{name}窃取{q['name']}托付的{silver:.1f}银，行迹败露，情义反目。", 'betrayal', .95
                    facts.update({'offender': n['id'], 'victim': i, 'transfer': silver})
                else:
                    # Victim knows the loss, not the identity of a secret perpetrator.
                    secret = self.emit('secret_theft', [n['id']], f"{name}暗中取走{q['name']}的{silver:.1f}银，未被发现。", .5,
                                       parents, {'offender': n['id'], 'victim': i, 'transfer': silver, 'secret': True,
                                                 'monthly_settlement': facts.get('monthly_settlement')})
                    self.emit('loss', [i], f"{q['name']}发觉遗失{silver:.1f}银，尚不知缘由。", .6,
                              [secret['id']], {'amount': silver})
                    return self.finish_action(n,a,secret)
            elif k == 'duel':
                bout = combat.autoplay(combat.start(n, q, self.rng.getrandbits(63), spar=False, equipment=(gear, items.bonuses(self, q['id']))))
                facts['combat'] = bout
                for actor, f in zip((n, q), bout['fighters']):
                    loss = (f['vigor_max']-f['vigor'])/f['vigor_max']
                    actor['health'] = clamp(actor['health']-4-loss*24)
                winner = (n, q)[bout['winner']] if bout['winner'] is not None else None
                loser = (q if winner is n else n) if winner else None
                if winner:
                    winner['reputation'] = clamp(winner['reputation'] + 3)
                self.link(n, q, -4, -3)
                # Winning revenge settles part of a grievance; it does not erase the opponent's memory.
                if winner:
                    self.relation(winner, loser['id'])['affinity'] = clamp(self.relation(winner, loser['id'])['affinity'] + 20, -100, 100)
                result = f"{winner['name']}取胜讨回颜面" if winner else '双方六十合未分胜负'
                text, imp = f"{name}因旧怨向{q['name']}挑战，{result}。", .8
                facts.update({'winner': winner['id'] if winner else None, 'loser': loser['id'] if loser else None})
            elif k == 'mentor':
                qloyal = q['traits']['loyalty']
                accepted = qloyal + q['traits']['compassion'] > 65
                if accepted:
                    n['mentor'] = i
                    self.link(n, q, 9, 8, label='师徒')
                    n['skill'] = clamp(n['skill'] + 1)
                    facts['transmission'] = martial.transmit(q, n)
                else:
                    n.setdefault('cooldowns', {})[a['id']] = self.month + 24
                text, imp = f"{name}向{q['name']}拜师，{'获准入门' if accepted else '未获应允'}。", .7 if accepted else .35
                facts['accepted'] = accepted
            elif k == 'teach':
                facts['transmission'] = martial.transmit(n, q)
                gain = min(1.5, max(.1, (n['skill']-q['skill'])*.05))
                q['skill'] = clamp(q['skill'] + gain)
                self.link(n, q, 3, 2, label='师徒')
                n['reputation'] = clamp(n['reputation'] + .5)
                text, kind, imp = f"{name}向弟子{q['name']}传艺，对方武学增长{gain:.2f}。", 'teaching', .45
            elif k == 'rumor':
                public_memory = next((m for m in reversed(n['memories'])
                                      if not self.store.event(m['event_id'])['facts'].get('secret')), None)
                text, imp = f"{name}与{q['name']}交换见闻。", .25
                if public_memory:
                    heard = {'event_id': public_memory['event_id'], 'month': self.month,
                                          'fact': public_memory['fact'], 'interpretation': '转述之事，尚需亲自核实。',
                                          'salience': .3, 'changes': [], 'source': f'听闻于{name}', 'ai': False}
                    q['memories'].append(heard)
                    self.store.experience(q['id'], heard, self.month)
                    parents.append(public_memory['event_id'])
                    q['memories'] = q['memories'][-40:]
            else:
                raise ValueError(k)
        slots = {'work': ['tool'], 'train': ['weapon'], 'duel': ['weapon', 'armor'], 'travel': ['armor']}.get(k, [])
        facts['equipment_wear'] = items.wear(self, n['id'], slots, 3 if k == 'duel' else 1)
        if k == 'duel':
            facts['opponent_equipment_wear'] = items.wear(self, q['id'], ['weapon', 'armor'], 3)
        facts['actor_delta'] = {k: round(n[k]-v, 4) for k, v in before.items()}
        e = self.emit(kind, people, text, imp, parents, facts)
        if k in ('work', 'change_job', 'learn_trade'):
            n['career']['last_event'] = e['id']
        if k == 'work':
            items.produce(self, n, e)
        self.finish_action(n,a,e)
        if k=='trade':
            q['time_left']-=1
            q['action_counts']['trade']=q['action_counts'].get('trade',0)+1
            q['monthly_actions'].append({**a,'month':self.month,'week':self.phase,'event_id':e['id'],
                                         'source':'回应合作','time_cost':1,'role':'合营伙伴'})
        if k == 'duel':
            n['cooldowns'][a['id']]=self.month+max(2,int(n['traits']['caution']/15))
            for actor, f in zip((n, q), bout['fighters']):
                if f['outer_wounds'] + f['inner_wounds'] > 0:
                    actor['wound'] = {'remaining': 5, 'event_id': e['id'],
                                      'external': f['outer_wounds'], 'internal': f['inner_wounds']}
                if actor['health'] <= 0:
                    self.die(actor, 'injury', e['id'])
        demography.accident(self, n, e)
        return e

    def finish_action(self,n,a,e):
        n['time_left']-=a['time_cost']
        n['action_counts'][a['kind']]=n['action_counts'].get(a['kind'],0)+1
        n['last_action']={**a,'month':self.month,'week':self.phase,'event_id':e['id'],'source':n['choice_source']}
        n['monthly_actions'].append(n['last_action'].copy())
        n['settlement_logged']=True
        self.metrics['actions'][a['kind']]=self.metrics['actions'].get(a['kind'],0)+1
        if a['kind'] in ('befriend','court','propose','rumor','care','family'):
            n['loneliness']=max(0,n['loneliness']-18)
        if n['choice_source']=='AI':
            n['last_ai_execution']=self.month
            self.metrics['ai_executed_actions']=self.metrics.get('ai_executed_actions',0)+1
        return e

    def month_step(self):
        if self.battle and self.battle['status'] == 'active':
            raise ValueError('切磋尚未结束，请先收招或认输')
        started = time.perf_counter()
        self.month += 1
        self.reindex()
        for n in self.npcs.values():
            n['drift_month'] = {}
            social.reset_month(self,n)
        demography.households(self)
        government.monthly(self)
        ids = list(self.npcs)
        self.rng.shuffle(ids)
        for i in ids:
            n = self.npcs[i]
            if not n['alive']:
                continue
            n['age_months'] += 1
            martial.recover(n)
            for k in n['mood']:
                n['mood'][k] *= .72
            upkeep = 2.3 if n['age_months'] >= 144 else 1.2
            payer = n['id']
            guardian_payment = 0
            if n['age_months'] < 192:
                guardians = [self.npcs[p] for p in dict.fromkeys(n['parents'] + ([n['guardian']] if n['guardian'] else []))
                             if self.npcs[p]['alive'] and self.npcs[p]['place'] == n['place'] and self.npcs[p]['wealth'] >= upkeep]
                if guardians:
                    guardians[0]['wealth'] -= upkeep
                    payer, guardian_payment = guardians[0]['id'], upkeep
                    upkeep = 0
            paid = min(n['wealth'], upkeep)
            n['wealth'] -= paid
            n['monthly_settlement'] = {'month': self.month, 'payer': payer,
                                       'paid': round(paid + guardian_payment, 4),
                                       'food_shortfall': round(upkeep-paid, 4)}
            if paid < upkeep:
                n['health'] = clamp(n['health'] - 5)
                if n['health'] < 65:
                    n['review_urgency'] = max(n['review_urgency'], .8)
            if n['age_months'] > 660:
                n['health'] = clamp(n['health'] - (n['age_months']-660)/1200)
            demography.health_step(self, n)
            if not n['alive']:
                continue
            careers.grow(self, n)
            self.update_goal(n)
            n['wealth'] = round(n['wealth'], 4)
            n['review_urgency'] = max(.1, n['review_urgency'] * .98)
        for week in range(1,5):
            self.phase=week
            self.rng.shuffle(ids)
            for i in ids:
                n=self.npcs[i]
                if not n['alive'] or n['time_left']<=0 or not social.available(self,n):
                    continue
                actions=self.legal_actions(n)
                if actions:
                    self.execute(n,self.choose(n,actions))
        self.phase=0
        demography.pregnancies(self)
        demography.households(self)
        if self.month % 12 == 0:
            self.annual()
        if self.month % 3 == 0:
            social.audit(self)
        self.metrics['steps'] += 1
        self.metrics['simulation_ms'] += (time.perf_counter()-started)*1000
        if self.month % 12 == 0:
            self.player['wealth'] += 24
        martial.recover(self.player)
        self.player['health'] = clamp(self.player['health']+6+items.bonuses(self, 'player')['recovery'])
        if self.durable:
            self.checkpoint()
        else:
            self.store.db.commit()

    def die(self, n, cause=None, cause_event=None):
        if not n['alive']:
            return self.store.event(n['death']['event_id']) if n['death'] else None
        cause = cause or ('starvation' if n['health'] <= 0 else 'old_age')
        n['final_identity'] = careers.identity(self, n)
        n['alive'] = False
        self.metrics['deaths'] += 1
        relatives = list(dict.fromkeys(n['children'] + ([n['spouse']] if n['spouse'] else [])))
        heirs = [self.npcs[i] for i in relatives if self.npcs[i]['alive']]
        if not heirs:
            heirs = [self.npcs[i] for i in n['parents'] if self.npcs[i]['alive']]
        estate = n['wealth']
        if heirs:
            for h in heirs:
                h['wealth'] += estate / len(heirs)
        else:
            g = government.for_npc(self, n)
            g['fund'] += estate
            g['revenue'] += estate
        n['wealth'] = 0
        observers = [int(i) for i in n['relationships'] if self.npcs[int(i)]['alive'] and
                     (self.npcs[int(i)]['place'] == n['place'] or self.npcs[int(i)]['mentor'] == n['id'])]
        office_g = government.office_government(self, n) if n['office'] else None
        office_causes = [office_g['last_appointments'].get(n['office'])] if office_g else []
        e = self.emit('death', [n['id']] + [h['id'] for h in heirs],
                      f"{n['name']}于{n['age_months']//12}岁因{demography.DEATH_CAUSES[cause]}离世，遗产{estate:.1f}银"
                      + ('由在世亲人承继。' if heirs else '由县衙代管。'), 1.,
                      [cause_event, n['last_event']] + office_causes, {'deceased': n['id'], 'estate': estate, 'health': n['health'],
                                           'cause': cause, 'cause_label': demography.DEATH_CAUSES[cause],
                                           'monthly_settlement': n.get('monthly_settlement'),
                                           'heirs': [h['id'] for h in heirs]})
        n['death'] = {'event_id': e['id'], 'month': self.month, 'cause': cause,
                      'cause_label': demography.DEATH_CAUSES[cause], 'cause_event': cause_event}
        items.inherit(self, n, heirs, e)
        self.metrics.setdefault('death_causes', {})[cause] = self.metrics.setdefault('death_causes', {}).get(cause, 0)+1
        if n['pregnancy']:
            p = n['pregnancy']
            father = self.npcs[p['father']]
            self.emit('pregnancy_loss', [father['id']] if father['alive'] else [],
                      f"{n['name']}离世后，这次未足月妊娠未能继续。", .95, [e['id'], p['event_id']],
                      {'mother': n['id'], 'father': father['id']})
            n['pregnancy'] = None
        for h in heirs:
            if h['spouse'] == n['id']:
                h['spouse'] = None
            if h['mentor'] == n['id']:
                h['mentor'] = None
        n['spouse'] = None
        for q in self.npcs.values():
            if q['mentor'] == n['id']:
                q['mentor'] = None
        # Spousal and mentorship links are removed after the death settlement.
        # Record those resulting identity changes immediately, even while paused.
        for q in self.npcs.values():
            if q['alive'] and (q['id'] in relatives or str(q['id']) in n['relationships']):
                careers.sync_identity(self, q, e['id'])
        for observer in observers:
            if observer not in e['people']:
                self.emit('death_notice', [observer], f"{self.npcs[observer]['name']}得知{n['name']}已离世。", .75,
                          [e['id']], {'deceased': n['id']})
        for f in self.factions:
            if f['leader'] == n['id']:
                f['leader'] = None
        if n['id'] in self.residents.get(n['place'], []):
            self.residents[n['place']].remove(n['id'])
        if n['office']:
            office_g['offices'][n['office']] = None
            office_g['last_appointments'][n['office']] = e['id']
            office_g['departed'] = {**office_g.get('departed', {}), n['office']: n['id']}
            n['office'] = None
        if n['spouse']:
            n['late_spouse']=n['spouse']
            n['spouse']=None
        return e

    def annual(self):
        government.annual(self)
        politics.annual(self)
        for n in self.npcs.values():
            if n['alive'] and n['spouse']:
                q = self.npcs[n['spouse']]
                if q['alive']:
                    self.know(n, q)
        if self.rng.random() < .55:
            place = self.rng.randrange(len(PLACES))
            local = [self.npcs[i] for i in self.residents[place] if self.npcs[i]['alive']]
            affected, losses, fatalities = [], {}, []
            for n in local:
                loss = min(n['wealth'], self.rng.uniform(7, 22))
                n['wealth'] -= loss
                n['health'] = clamp(n['health']-self.rng.uniform(3, 10))
                if self.rng.random() < .004*(1-self.governments[PLACES[place]['country']]['security']):
                    fatalities.append(n['id'])
                affected.append(n['id'])
                losses[str(n['id'])] = round(loss, 2)
            if affected:
                e = self.emit('disaster', affected, f"{PLACES[place]['name']}遇到水患，{len(affected)}人财物受损，邻里开始互相求援。",
                              .92, facts={'place': place, 'silver_losses': losses, 'fatalities': fatalities})
                for i in fatalities:
                    self.npcs[i]['health'] = 0
                    self.die(self.npcs[i], 'disaster', e['id'])
        demography.households(self)
        for f in self.factions:
            members = [n for n in self.npcs.values() if n['alive'] and n['faction'] == f['id']]
            if not members:
                continue
            leader = max(members, key=lambda n: n['contribution']+n['reputation']*.3)
            old = f['leader']
            if old != leader['id']:
                f['leader'] = leader['id']
                participants = [n['id'] for n in members]
                self.emit('promotion', participants,
                          f"{f['name']}推举贡献最高的{leader['name']}主持事务。", .8,
                          [leader['last_event']], {'leader': leader['id'], 'previous': old, 'faction': f['id']})
                if old and self.npcs[old]['alive']:
                    self.link(leader, self.npcs[old], -18, -8, label='竞争')
            needy = sorted([n for n in members if n['wealth'] < 15], key=lambda n: n['wealth'])
            for n in needy:
                if f['fund'] >= 8:
                    f['fund'] -= 8
                    n['wealth'] += 8
                    self.emit('faction_aid', [n['id'], leader['id']],
                              f"{f['name']}用公帑8银救助{n['name']}。", .65,
                              [n['last_event']], {'faction': f['id'], 'silver': 8})

    def intervene(self, npc_id, action, text=''):
        n = self.npcs.get(npc_id)
        if not n or not n['alive']:
            raise ValueError('人物不在世或不存在')
        if action == 'challenge':
            return self.start_battle(npc_id)
        tie = self.player['ties'].setdefault(str(npc_id), {'affinity': 0., 'trust': 0.})
        cause = n['last_event']
        if action == 'gift':
            if self.player['wealth'] < 10:
                raise ValueError('银两不足；每年可以通过行脚收入补充')
            self.player['wealth'] -= 10
            n['wealth'] += 10
            tie['affinity'] += 8
            tie['trust'] += 3
            e = self.emit('player_gift', [npc_id], f"过客赠给{n['name']}10银，银两已实际交付。", .75,
                          [cause], {'player_cost': 10, 'npc_gain': 10}, '有人在意我的处境，这份情我会记住。')
        elif action == 'talk':
            # Player text is a recorded utterance, never a command to mutate state.
            text = text.strip()[:280]
            if not text:
                raise ValueError('请先写下一句话')
            tie['affinity'] += .5
            e = self.emit('player_talk', [npc_id], f"过客对{n['name']}说：「{text}」", .65,
                          [cause], {'utterance': text, 'untrusted_player_text': True},
                          '来者的话是一种意见；往后的取舍仍须依自己的处境。')
        elif action == 'earn':
            raise ValueError('请使用行脚收入接口')
        else:
            raise ValueError('未知介入方式')
        n['review_urgency'] = 1.
        self.checkpoint()
        return e

    def martial_view(self):
        known = self.player['martial']['arts']
        return {'player': {**deepcopy(self.player), 'martial': martial.view(self.player, items.bonuses(self, 'player'))},
                'catalog': [{**deepcopy(art), 'id': key} for key, art in martial.ARTS.items() if key not in known],
                'battle': combat.view(self.battle)}

    def martial_action(self, action, art_id=None):
        if self.battle and self.battle['status'] == 'active':
            raise ValueError('切磋期间无法修习或换装')
        p = self.player
        facts = {'action': action}
        if action == 'equip':
            martial.equip(p, art_id)
        elif action == 'learn':
            if p['wealth'] < 12:
                raise ValueError('研读手抄谱需要12银')
            martial.learn(p, art_id)
            p['wealth'] -= 12
            facts.update(art=art_id, cost=12)
        elif action in ('practice', 'breakthrough'):
            if p['wealth'] < 3:
                raise ValueError('闭关一月需要3银')
            facts['training'] = martial.practice(p, self.rng, art_id, action == 'breakthrough', equipment=items.bonuses(self, 'player'))
            p['wealth'] -= 3
            facts['cost'] = 3
            facts['equipment_wear'] = items.wear(self, 'player', ['weapon'], 2)
            self.month_step()
        elif action == 'rest':
            martial.recover(p, 30)
            p['health'] = clamp(p['health'] + 18)
            self.month_step()
        else:
            raise ValueError('未知练武指令')
        self.store.append({'month': self.month, 'date': date(self.month), 'kind': 'player_training',
                           'people': [], 'summary': '过客' + {'equip': '调整了功法装配。', 'learn': '研读手抄武谱。',
                           'practice': '闭关修习一月。', 'breakthrough': '闭关突破武学关隘。', 'rest': '调养一月。'}[action],
                           'importance': .3, 'causes': [], 'facts': facts})
        self.checkpoint()
        return self.martial_view()

    def start_battle(self, npc_id):
        if self.battle and self.battle['status'] == 'active':
            raise ValueError('已有切磋正在进行')
        n = self.npcs.get(npc_id)
        if not n or not n['alive'] or n['health'] <= 30 or n['age_months'] < 216:
            raise ValueError('对方目前不适合切磋')
        if self.player['health'] <= 30 or self.player['martial']['stamina'] < 12:
            raise ValueError('你伤疲过重，请先调养')
        battle_id = f'{self.seed}-{self.month}-{self.store.next_id}'
        self.battle = combat.start(self.player, n, self.rng.getrandbits(63), battle_id, equipment=(items.bonuses(self, 'player'), items.bonuses(self, npc_id)))
        e = self.emit('spar_start', [npc_id], f"过客与{n['name']}约定点到为止，摆开切磋架势。", .3,
                      [n['last_event']], {'battle_id': battle_id})
        self.battle['cause'] = e['id']
        self.checkpoint()
        return self.martial_view()

    def battle_action(self, battle_id, round_number, action):
        b = self.battle
        if not b or b['status'] != 'active':
            raise ValueError('目前没有进行中的切磋')
        if battle_id != b['id'] or round_number != b['round']:
            raise ValueError('战局已变化，请刷新后再出招')
        combat.step(b, action)
        if b['status'] == 'finished':
            n = self.npcs[b['fighters'][1]['id']]
            costs = []
            rewards = self.player.setdefault('spar_rewards', {})
            practiced = (b['round'] >= 3 and any(row.get('damage', 0) > 0 and row['side'] == 0 for row in b['log']))
            gain = .3 if practiced and rewards.get(str(n['id'])) != self.month else 0.
            if gain:
                rewards[str(n['id'])] = self.month
            for actor, f in zip((self.player, n), b['fighters']):
                cost = round(2 + 6*(1-f['vigor']/f['vigor_max']), 4)
                actor['health'] = max(1., actor['health']-cost)
                actor['skill'] = clamp(actor['skill']+gain)
                actor['martial']['stamina'] = clamp(actor['martial']['stamina']-12)
                costs.append(cost)
            tie = self.player['ties'].setdefault(str(n['id']), {'affinity': 0., 'trust': 0.})
            tie['affinity'] = clamp(tie['affinity']+2, -100, 100)
            result = '双方平手' if b['winner'] is None else b['fighters'][b['winner']]['name']+'胜出'
            gear_wear = {'player': items.wear(self, 'player', ['weapon', 'armor'], 2),
                         'npc': items.wear(self, n['id'], ['weapon', 'armor'], 2)}
            b['settled'] = True
            e = self.emit('player_duel', [n['id']], f"过客与{n['name']}切磋，{result}，点到为止。", .7,
                          [b['cause']], {'combat': deepcopy(b), 'player_health_cost': costs[0],
                                         'npc_health_cost': costs[1], 'skill_gain': gain, 'equipment_wear': gear_wear})
            b['event_id'] = e['id']
        self.checkpoint()
        return self.martial_view()

    def export(self):
        return {'seed': self.seed, 'month': self.month, 'npcs': self.npcs,
                'factions': self.factions, 'government': self.government, 'governments': self.governments,
                'countries': self.countries, 'map_version': MAP_VERSION, 'atlas_history': self.atlas_history, 'player': self.player, 'items': self.items, 'economy': self.economy, 'metrics': self.metrics,
                'population_limit': self.population_limit, 'random_state': self.rng.getstate(), 'battle': self.battle}

    def restore(self, state):
        self.seed, self.month = state['seed'], state['month']
        self.npcs = {int(i): n for i, n in state['npcs'].items()}
        for n in self.npcs.values():
            n['ai_pending'] = False
            demography.initialize(self, n)
            social.initialize(self,n)
            martial.initialize(n)
            n.setdefault('office', None)
            careers.initialize(self, n, legacy=True)
            n.setdefault('office_country', 0 if n['office'] else None)
            n.setdefault('visited_places', [n['place']])
        self.factions, self.player, self.metrics = state['factions'], state['player'], state['metrics']
        self.countries = state.get('countries', politics.new())
        self.governments = state.get('governments', [government.new(c['id']) for c in COUNTRIES])
        if 'governments' not in state:
            self.governments[0] = state.get('government', government.new())
            self.governments[0]['country'] = 0
        self.atlas_history = state.get('atlas_history', [])
        self._atlas_migration = None
        if state.get('map_version') == 2:
            from .atlas_migration import from_v2
            self._atlas_migration = from_v2(self, state)
        self.government = self.governments[0]
        self.government['name'] = COUNTRIES[0]['government_name']
        # Add metadata and new organizations without resetting funds, leaders or membership.
        existing = {f['id']: f for f in self.factions}
        self.factions = [{**deepcopy(f), **{k: v for k, v in existing.get(f['id'], {}).items()
                                              if k in ('leader', 'fund')}} for f in FACTIONS]
        self.economy=state.get('economy',{'gaming_house':200.})
        for n in self.npcs.values():
            n.setdefault('identity', careers.identity(self, n))
            if not n['alive']:
                n.setdefault('final_identity', deepcopy(n['identity']))
        martial.initialize(self.player, player=True)
        self.battle = state.get('battle')
        self.metrics.setdefault('death_causes', {})
        self.population_limit = state['population_limit']
        def tuples(x):
            return tuple(tuples(v) for v in x) if isinstance(x, list) else x
        self.rng.setstate(tuples(state['random_state']))
        self.reindex()
        self.items = state.get('items')
        if items.initialize(self, legacy=True):
            self.checkpoint()

    def checkpoint(self):
        self.store.save(self.export())

    def npc_summary(self, n):
        return {**{k: n[k] for k in ('id', 'name', 'sex', 'age_months', 'alive', 'place', 'job', 'wealth',
                                  'skill', 'health', 'reputation', 'goal', 'faction', 'spouse', 'last_action',
                                  'generation', 'office', 'office_country', 'pregnancy', 'illness', 'death')},
                'profession': n['career']['profession'], 'career_level': careers.level(n)[1],
                'identity': careers.identity(self, n)}

    def summary(self):
        living = [n for n in self.npcs.values() if n['alive']]
        friends = sum(r['affinity'] > 25 for n in living for r in n['relationships'].values())//2
        feuds = sum(r['affinity'] < -25 for n in living for r in n['relationships'].values())//2
        return {'month': self.month, 'date': date(self.month), 'seed': self.seed, 'population': len(living),
                'total_people': len(self.npcs), 'events': self.store.count(), 'friendships': friends,
                'feuds': feuds, 'marriages': sum(bool(n['spouse']) for n in living)//2,
                'goals': dict(Counter(n['goal']['kind'] for n in living)), 'metrics': self.metrics,
                'professions': dict(Counter(n['job'] for n in living)),
                'identity_grades': dict(Counter(careers.identity(self, n)['grade_label'] for n in living)),
                'places': [{**p, 'population': len(self.residents[p['id']])} for p in PLACES],
                'factions': [{**f, 'members': sum(n['faction'] == f['id'] for n in living)} for f in self.factions],
                'map_version': MAP_VERSION, 'era': ERA, 'routes': ROUTES,
                'countries': [{**c, 'population': sum(PLACES[n['place']]['country'] == c['id'] for n in living),
                               'government': {**self.governments[c['id']], 'cases': self.governments[c['id']]['cases'][-12:]}}
                              for c in self.countries],
                'government': {**self.government, 'cases': self.government['cases'][-12:]},
                'activity': {'weeks_per_month':4,'mean_actions':sum(len(n['monthly_actions']) for n in living)/max(1,len(living)),
                             'active_ai_people':sum(n['last_ai_execution'] is not None and self.month-n['last_ai_execution']<12 for n in living),
                             'adult_people':sum(n['age_months']>=120 for n in living)},
                'demography': {'children': sum(n['age_months'] < 192 for n in living),
                               'elders': sum(n['age_months'] >= 720 for n in living),
                               'pregnancies': sum(bool(n['pregnancy']) for n in living),
                               'generations': dict(Counter(n['generation'] for n in living)),
                               'max_generation': max((n['generation'] for n in self.npcs.values()), default=0),
                               'deaths': self.metrics.get('death_causes', {})}, 'player': self.player,
                'battle_active': bool(self.battle and self.battle['status'] == 'active'),
                'npcs': [self.npc_summary(n) for n in self.npcs.values()]}

    def npc_detail(self, npc_id):
        n = deepcopy(self.npcs[npc_id])
        n['career_view'] = careers.view(self, self.npcs[npc_id])
        n['identity'] = careers.identity(self, self.npcs[npc_id])
        n['career_options'] = careers.catalog(self, self.npcs[npc_id])
        n['martial_view'] = martial.view(n, items.bonuses(self, npc_id))
        n['legal_actions'] = self.legal_actions(self.npcs[npc_id])
        n['possessions'] = items.view(self, npc_id)
        n['relations_view'] = [dict(other=int(i), name=self.npcs[int(i)]['name'],
                                    alive=self.npcs[int(i)]['alive'], **r)
                                for i, r in n['relationships'].items()]
        return n
