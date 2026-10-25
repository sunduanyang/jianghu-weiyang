"""Monthly, seeded social simulation. Only this module settles world consequences."""
from collections import Counter
from copy import deepcopy
import math
import random
import time

from .store import Store
from . import demography, government

PLACES = [
    {'id': 0, 'name': '青溪镇', 'x': 25, 'y': 66, 'trade': 1.05, 'color': '#a1b99c'},
    {'id': 1, 'name': '栖云山', 'x': 39, 'y': 23, 'trade': .8, 'color': '#91abb4'},
    {'id': 2, 'name': '临川城', 'x': 74, 'y': 55, 'trade': 1.3, 'color': '#d1b881'},
    {'id': 3, 'name': '桃花渡', 'x': 50, 'y': 77, 'trade': .95, 'color': '#c59f9f'},
    {'id': 4, 'name': '寒石寨', 'x': 80, 'y': 20, 'trade': .9, 'color': '#a99eaf'},
]
FACTIONS = [
    {'id': 0, 'name': '栖云门', 'place': 1, 'ethos': '传艺修身', 'leader': None, 'fund': 100},
    {'id': 1, 'name': '临川商会', 'place': 2, 'ethos': '互通有无', 'leader': None, 'fund': 100},
    {'id': 2, 'name': '青溪义社', 'place': 0, 'ethos': '扶危济困', 'leader': None, 'fund': 100},
]
TRAITS = {'compassion': '仁厚', 'ambition': '进取', 'caution': '谨慎',
          'sociability': '外向', 'loyalty': '重义'}
BELIEFS = {'trust': '人可相托', 'justice': '善有善报', 'tradition': '传承有益'}
DESIRES = {'wealth': '安家立业', 'mastery': '武学有成', 'belonging': '有所归属',
           'renown': '名动一方', 'freedom': '行遍江湖', 'revenge': '讨回公道'}
ACTIONS = {'work': '谋生', 'train': '习武', 'rest': '休养', 'befriend': '结交',
           'court': '倾诉心意', 'propose': '提亲', 'aid': '接济', 'trade': '合营',
           'betray': '窃取托银', 'duel': '寻仇比武', 'mentor': '拜师', 'teach': '传艺',
           'join': '入会', 'duty': '尽责', 'travel': '远行', 'rumor': '交换见闻',
           'family': '筹划育儿', 'treatment': '求医', 'report': '报官', 'public_service': '公务'}
JOBS = ['农人', '行商', '药师', '匠人', '镖师']
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
        self.npcs, self.factions = {}, deepcopy(FACTIONS)
        self.government = government.new()
        self.player = {'name': '过客', 'wealth': 160., 'reputation': 0, 'ties': {}}
        self.metrics = {'steps': 0, 'simulation_ms': 0., 'actions': {}, 'goals_completed': 0,
                        'births': 0, 'deaths': 0, 'invalid_actions': 0,
                        'ai_applied': 0, 'ai_changed_actions': 0, 'ai_stale': 0}
        self.population_limit = max(1200, population * 2)
        self.residents = {}
        self.durable = durable
        state = self.store.load() if load else None
        if state:
            self.restore(state)
            if 'government' not in state:
                government.establish(self)
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
             'alive': True, 'place': place if place is not None else self.rng.randrange(5),
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
        n['office'] = None
        return n

    def place_name(self, place):
        return PLACES[place]['name']

    def government_grant(self, n, amount, purpose, cause=None):
        return government.grant(self, n, amount, purpose, cause)

    def reindex(self):
        self.residents = {i: [] for i in range(5)}
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
        n['knowledge'][str(other['id'])] = {'name': other['name'], 'place': other['place'],
                                            'seen_month': self.month,
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
        event = self.store.append({'month': self.month, 'date': date(self.month), 'kind': kind,
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
            for j in people:
                if str(j) in n['relationships']:
                    n['relationships'][str(j)]['last_event'] = event['id']
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
                amount = .014 if success else .028
                text = ('往来守信，合作比独行更有余地。' if success and b['trust'] > .5 else
                        '这笔买卖有赚有赔，下次须算清风险。' if not success and t['caution'] > 50 else
                        '一时得失不足定终身，还得看长远。')
                b['trust'] = clamp(b['trust'] + (.0015 if success else -.003), 0, 1)
            elif k == 'work':
                axis, amount = ('caution', .008) if t['caution'] > 50 else ('ambition', .008)
                text = '安稳生计是立身之本。' if t['caution'] > 50 else '眼下的积累，是往后选择的底气。'
            elif k == 'train':
                axis, amount = 'ambition', .012
                text = '武学不负日积月累，急不得，也停不得。'
            else:
                axis, amount = 'loyalty', .015
                text = '共同做事，才知道这份组织之约值多少。'
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

    def legal_actions(self, n):
        if not n['alive']:
            return []
        actions = []
        def add(kind, target=None, detail='', **extra):
            actions.append({'id': f'{kind}:{target if target is not None else "self"}',
                            'kind': kind, 'target': target, 'label': ACTIONS[kind] + (f' · {detail}' if detail else ''), **extra})
        add('rest', detail='恢复体力')
        if n['age_months'] < 120:
            if n['age_months'] >= 60:
                add('train', detail='练习基本功')
            return actions
        add('work', detail=n['job'])
        if n['wealth'] >= 3 and n['health'] > 30:
            add('train', detail='花费3银，精进武艺')
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
        for i in own_rank[:6] + visible_new[:2]:
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
            if n['wealth'] >= 10 and q['wealth'] >= 10 and r['trust'] >= 8:
                add('trade', i, q['name'])
            if q['wealth'] >= 20 and r['trust'] >= 18 and n['wealth'] < 80 and n['age_months'] >= 216:
                add('betray', i, q['name'])
            if r['affinity'] < -25 and n['health'] > 55 and q['health'] > 25 and n['age_months'] >= 216:
                add('duel', i, q['name'])
            if n['age_months'] >= 120 and q['skill'] > n['skill'] + 12 and not n['mentor'] and r['affinity'] >= 6:
                add('mentor', i, q['name'])
            if q['mentor'] == n['id'] and q['health'] > 30:
                add('teach', i, q['name'])
            if r['affinity'] > 5 and n['memories']:
                add('rumor', i, q['name'])
            if n['wealth'] >= 4 and q['job'] == '药师' and (n['illness'] or n['wound'] or n['health'] < 55):
                add('treatment', i, q['name'])
        if demography.fertile_pair(self, n) and n['family_intent_until'] < self.month and n['age_months'] >= 216:
            add('family', detail='与伴侣商议养育下一代')
        if n.get('grievance_cause'):
            cause = n['grievance_cause']
            if not any(c['crime_event'] == cause for c in self.government['cases']):
                add('report', cause, '提交已知侵吞证据')
        if n['office'] and self.government['fund'] >= 4:
            add('public_service', detail='支用公帑4银巡防或防疫')
        if n['faction'] is None and n['skill'] >= 12 and n['age_months'] >= 192:
            for f in self.factions:
                if f['place'] == n['place']:
                    add('join', f['id'], f['name'])
        if n['faction'] is not None and n['wealth'] >= 4:
            add('duty', n['faction'], self.factions[n['faction']]['name'])
        if n['wealth'] >= 16 and n['health'] > 40 and n['age_months'] >= 192:
            for p in PLACES:
                if p['id'] != n['place']:
                    add('travel', p['id'], p['name'])
        return [a for a in actions if n.get('cooldowns', {}).get(a['id'], -1) <= self.month]

    def close_kin(self, a, b):
        def ancestors(n):
            direct = set(n['parents'])
            return direct | {g for p in direct for g in self.npcs[p]['parents']}
        aa, bb = ancestors(a), ancestors(b)
        return b['id'] in aa or a['id'] in bb or bool(aa & bb)

    def score(self, n, a):
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
                 'public_service': 15+t['loyalty']*.15}[k]
        boost = {'wealth': {'work': 16, 'trade': 18, 'betray': 12}, 'mastery': {'train': 23, 'mentor': 28, 'join': 15},
                 'belonging': {'befriend': 15, 'court': 25, 'propose': 34, 'aid': 5},
                 'renown': {'duty': 26, 'join': 25, 'teach': 15, 'aid': 10},
                 'freedom': {'travel': 27, 'work': 7}, 'revenge': {'duel': 38, 'train': 18}}
        score += boost[g].get(k, 0)
        if k in ('court', 'befriend'):
            r = n['relationships'].get(str(a['target']), {})
            score += (r.get('romance', 0)*.1 if k == 'court' else -r.get('affinity', 0)*.08)
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
        return score

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
                    return found
        scored = [(self.score(n, a) + self.rng.uniform(-9, 9), a) for a in actions]
        chosen = max(scored, key=lambda x: x[0])[1]
        n['plan'] = {'source': '规则', 'created': self.month, 'until': self.month+5,
                     'reason': f"围绕「{n['goal']['title']}」，兼顾生计与已有关系。",
                     'steps': [chosen['id']] * 2, 'horizon': '未来三个月'}
        return chosen

    def execute(self, n, action):
        # Reject any caller supplied action not in the engine's CURRENT legal set.
        a = next((a for a in self.legal_actions(n) if a['id'] == action.get('id')), None)
        if a is None:
            self.metrics['invalid_actions'] += 1
            return None
        k, i = a['kind'], a['target']
        q = self.npcs.get(i) if k not in ('join', 'duty', 'travel', 'report') else None
        parents = [n['last_event'], n['goal']['cause']]
        people, imp, facts, kind = [n['id']], .15, {'action': a['id']}, k
        settlement = n.get('monthly_settlement', {})
        if settlement.get('month') == self.month:
            facts['monthly_settlement'] = settlement.copy()
            if settlement.get('payer') != n['id'] and settlement.get('paid', 0) > 0:
                people.append(settlement['payer'])
        before = {'wealth': n['wealth'], 'health': n['health'], 'skill': n['skill']}
        name = n['name']
        if k == 'work':
            earn = (9 + n['skill']*.07 + (2 if n['job'] == '行商' else 0))*PLACES[n['place']]['trade']
            n['wealth'] += earn
            tax = government.tax(self, n, earn) if n['age_months'] >= 192 else 0
            facts.update({'gross_income': earn, 'tax': tax})
            n['health'] = clamp(n['health'] - self.rng.uniform(.5, 2))
            text = f"{name}在{PLACES[n['place']]['name']}以{n['job']}谋生，收入{earn:.1f}银，纳税{tax:.1f}银。"
        elif k == 'rest':
            n['health'] = clamp(n['health'] + (1 if settlement.get('food_shortfall', 0) else 5 if n['illness'] else 10))
            text = f'{name}休养一月，恢复体力。'
        elif k == 'train':
            n['wealth'] -= 3 if n['age_months'] >= 120 else 0
            gain = self.rng.uniform(.4, 1.05)*(1-n['skill']/120)
            n['skill'] = clamp(n['skill'] + gain)
            n['health'] = clamp(n['health'] - .7)
            text = f'{name}苦练基本功，武学增长{gain:.2f}。'
        elif k == 'travel':
            old = n['place']
            self.residents[old].remove(n['id'])
            n['place'], n['moves'] = i, n['moves'] + 1
            self.residents[i].append(n['id'])
            people += demography.follow_guardian(self, n, old)
            n['wealth'] -= 9
            n['health'] = clamp(n['health'] - 2)
            text = f"{name}离开{PLACES[old]['name']}，花9银来到{PLACES[i]['name']}。"
            imp = .4
        elif k == 'family':
            partner = self.npcs[n['spouse']]
            n['family_intent_until'] = partner['family_intent_until'] = self.month + 12
            people.append(partner['id'])
            text, imp = f"{name}与{partner['name']}商议育儿，决定为下一代准备家计。", .7
        elif k == 'treatment':
            n['wealth'] -= 4
            q['wealth'] += 4
            n['health'] = clamp(n['health']+15)
            if n['illness']:
                n['illness']['severity'] *= .45
                n['illness']['remaining'] = min(2, n['illness']['remaining'])
            if n['wound']:
                n['wound']['remaining'] = 1
            people.append(q['id'])
            self.link(n, q, 4, 3)
            text, imp = f"{name}花4银请{q['name']}诊治，体力恢复15，病伤得到缓解。", .6
            facts.update({'doctor': q['id'], 'payment': 4})
        elif k == 'report':
            e = government.report(self, n, i)
            n['last_action'] = {**a, 'month': self.month, 'event_id': e['id']}
            self.metrics['actions'][k] = self.metrics['actions'].get(k, 0)+1
            return e
        elif k == 'public_service':
            self.government['fund'] -= 4
            self.government['expenses'] += 4
            self.government['security'] = min(.95, self.government['security']+.015)
            self.government['sanitation'] = min(.95, self.government['sanitation']+.01)
            n['reputation'] = clamp(n['reputation']+.8)
            text, imp = f"{name}以{n['office']}身份拨公帑4银巡防防疫。", .4
            facts.update({'public_cost': 4, 'office': n['office']})
        elif k == 'join':
            n['faction'] = i
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
                ret = self.rng.uniform(6, 11) if success else 1
                n['wealth'] += ret
                q['wealth'] += ret
                facts['taxes'] = {str(p['id']): government.tax(self, p, max(0, ret-3))
                                  if p['age_months'] >= 192 else 0 for p in (n, q)}
                self.link(n, q, 4 if success else -5, 3 if success else -2)
                text, imp = f"{name}与{q['name']}各出3银合营，{'获利' if success else '失利'}，各收回{ret:.1f}银。", .45
                facts.update({'success': success, 'investment_each': 3, 'return_each': ret})
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
                    n['last_action'] = {**a, 'month': self.month, 'event_id': secret['id']}
                    return secret
            elif k == 'duel':
                won = n['skill'] + self.rng.uniform(-10, 10) > q['skill'] + self.rng.uniform(-10, 10)
                winner, loser = (n, q) if won else (q, n)
                loser['health'] = clamp(loser['health'] - self.rng.uniform(14, 30))
                winner['health'] = clamp(winner['health'] - 6)
                winner['reputation'] = clamp(winner['reputation'] + 3)
                self.link(n, q, -4, -3)
                # Winning revenge settles part of a grievance; it does not erase the opponent's memory.
                self.relation(winner, loser['id'])['affinity'] = clamp(self.relation(winner, loser['id'])['affinity'] + 20, -100, 100)
                text, imp = f"{name}因旧怨向{q['name']}挑战，{winner['name']}取胜讨回颜面，{loser['name']}负伤。", .8
                facts.update({'winner': winner['id'], 'loser': loser['id']})
            elif k == 'mentor':
                qloyal = q['traits']['loyalty']
                accepted = qloyal + q['traits']['compassion'] > 65
                if accepted:
                    n['mentor'] = i
                    self.link(n, q, 9, 8, label='师徒')
                    n['skill'] = clamp(n['skill'] + 1)
                else:
                    n.setdefault('cooldowns', {})[a['id']] = self.month + 24
                text, imp = f"{name}向{q['name']}拜师，{'获准入门' if accepted else '未获应允'}。", .7 if accepted else .35
                facts['accepted'] = accepted
            elif k == 'teach':
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
        facts['actor_delta'] = {k: round(n[k]-v, 4) for k, v in before.items()}
        e = self.emit(kind, people, text, imp, parents, facts)
        n['last_action'] = {**a, 'month': self.month, 'event_id': e['id']}
        self.metrics['actions'][k] = self.metrics['actions'].get(k, 0) + 1
        if k == 'duel':
            loser['wound'] = {'remaining': 5, 'event_id': e['id']}
            if loser['health'] <= 0:
                self.die(loser, 'injury', e['id'])
        demography.accident(self, n, e)
        return e

    def month_step(self):
        started = time.perf_counter()
        self.month += 1
        self.reindex()
        for n in self.npcs.values():
            n['drift_month'] = {}
        demography.households(self)
        government.monthly(self)
        ids = list(self.npcs)
        self.rng.shuffle(ids)
        for i in ids:
            n = self.npcs[i]
            if not n['alive']:
                continue
            n['age_months'] += 1
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
            if n['age_months'] in (120, 192):
                previous_job = n['job']
                if n['age_months'] == 120:
                    n['job'] = '学徒'
                else:
                    family_jobs = [self.npcs[p]['job'] for p in n['parents'] if self.npcs[p]['job'] in JOBS]
                    n['job'] = self.rng.choice(family_jobs or JOBS)
                self.emit('career', [n['id']], f"{n['name']}长成，离开{previous_job}身份，开始以{n['job']}立身。", .65,
                          [n['last_event']], {'before': previous_job, 'after': n['job']})
            self.update_goal(n)
            self.execute(n, self.choose(n, self.legal_actions(n)))
            n['wealth'] = round(n['wealth'], 4)
            n['review_urgency'] = max(.1, n['review_urgency'] * .98)
        demography.pregnancies(self)
        demography.households(self)
        if self.month % 12 == 0:
            self.annual()
        self.metrics['steps'] += 1
        self.metrics['simulation_ms'] += (time.perf_counter()-started)*1000
        if self.month % 12 == 0:
            self.player['wealth'] += 24
        if self.durable:
            self.checkpoint()
        else:
            self.store.db.commit()

    def die(self, n, cause=None, cause_event=None):
        if not n['alive']:
            return self.store.event(n['death']['event_id']) if n['death'] else None
        cause = cause or ('starvation' if n['health'] <= 0 else 'old_age')
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
            self.government['fund'] += estate
            self.government['revenue'] += estate
        n['wealth'] = 0
        observers = [int(i) for i in n['relationships'] if self.npcs[int(i)]['alive'] and
                     (self.npcs[int(i)]['place'] == n['place'] or self.npcs[int(i)]['mentor'] == n['id'])]
        office_causes = [self.government['last_appointments'].get(n['office'])] if n['office'] else []
        e = self.emit('death', [n['id']] + [h['id'] for h in heirs],
                      f"{n['name']}于{n['age_months']//12}岁因{demography.DEATH_CAUSES[cause]}离世，遗产{estate:.1f}银"
                      + ('由在世亲人承继。' if heirs else '由县衙代管。'), 1.,
                      [cause_event, n['last_event']] + office_causes, {'deceased': n['id'], 'estate': estate, 'health': n['health'],
                                           'cause': cause, 'cause_label': demography.DEATH_CAUSES[cause],
                                           'monthly_settlement': n.get('monthly_settlement'),
                                           'heirs': [h['id'] for h in heirs]})
        n['death'] = {'event_id': e['id'], 'month': self.month, 'cause': cause,
                      'cause_label': demography.DEATH_CAUSES[cause], 'cause_event': cause_event}
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
        for q in self.npcs.values():
            if q['mentor'] == n['id']:
                q['mentor'] = None
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
            self.government['offices'][n['office']] = None
            self.government['last_appointments'][n['office']] = e['id']
            self.government['departed'] = {**self.government.get('departed', {}), n['office']: n['id']}
            n['office'] = None
        return e

    def annual(self):
        government.annual(self)
        for n in self.npcs.values():
            if n['alive'] and n['spouse']:
                q = self.npcs[n['spouse']]
                if q['alive']:
                    self.know(n, q)
        if self.rng.random() < .55:
            place = self.rng.randrange(5)
            local = [self.npcs[i] for i in self.residents[place] if self.npcs[i]['alive']]
            affected, losses, fatalities = [], {}, []
            for n in local:
                loss = min(n['wealth'], self.rng.uniform(7, 22))
                n['wealth'] -= loss
                n['health'] = clamp(n['health']-self.rng.uniform(3, 10))
                if self.rng.random() < .004*(1-self.government['security']):
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
        elif action == 'challenge':
            if n['health'] <= 30 or n['age_months'] < 216:
                raise ValueError('对方目前不适合切磋')
            n['health'] -= 8
            n['skill'] = clamp(n['skill'] + .6)
            tie['affinity'] += 2
            e = self.emit('player_duel', [npc_id], f"过客与{n['name']}依约切磋，对方体力消耗8，武学增长0.6。", .65,
                          [cause], {'health_cost': 8, 'skill_gain': .6})
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

    def export(self):
        return {'seed': self.seed, 'month': self.month, 'npcs': self.npcs,
                'factions': self.factions, 'government': self.government, 'player': self.player, 'metrics': self.metrics,
                'population_limit': self.population_limit, 'random_state': self.rng.getstate()}

    def restore(self, state):
        self.seed, self.month = state['seed'], state['month']
        self.npcs = {int(i): n for i, n in state['npcs'].items()}
        for n in self.npcs.values():
            n['ai_pending'] = False
            demography.initialize(self, n)
            n.setdefault('office', None)
        self.factions, self.player, self.metrics = state['factions'], state['player'], state['metrics']
        self.government = state.get('government', government.new())
        self.metrics.setdefault('death_causes', {})
        self.population_limit = state['population_limit']
        def tuples(x):
            return tuple(tuples(v) for v in x) if isinstance(x, list) else x
        self.rng.setstate(tuples(state['random_state']))
        self.reindex()

    def checkpoint(self):
        self.store.save(self.export())

    def npc_summary(self, n):
        return {k: n[k] for k in ('id', 'name', 'sex', 'age_months', 'alive', 'place', 'job', 'wealth',
                                  'skill', 'health', 'reputation', 'goal', 'faction', 'spouse', 'last_action',
                                  'generation', 'office', 'pregnancy', 'illness', 'death')}

    def summary(self):
        living = [n for n in self.npcs.values() if n['alive']]
        friends = sum(r['affinity'] > 25 for n in living for r in n['relationships'].values())//2
        feuds = sum(r['affinity'] < -25 for n in living for r in n['relationships'].values())//2
        return {'month': self.month, 'date': date(self.month), 'seed': self.seed, 'population': len(living),
                'total_people': len(self.npcs), 'events': self.store.count(), 'friendships': friends,
                'feuds': feuds, 'marriages': sum(bool(n['spouse']) for n in living)//2,
                'goals': dict(Counter(n['goal']['kind'] for n in living)), 'metrics': self.metrics,
                'places': [{**p, 'population': len(self.residents[p['id']])} for p in PLACES],
                'factions': self.factions, 'government': {**self.government, 'cases': self.government['cases'][-12:]},
                'demography': {'children': sum(n['age_months'] < 192 for n in living),
                               'elders': sum(n['age_months'] >= 720 for n in living),
                               'pregnancies': sum(bool(n['pregnancy']) for n in living),
                               'generations': dict(Counter(n['generation'] for n in living)),
                               'max_generation': max((n['generation'] for n in self.npcs.values()), default=0),
                               'deaths': self.metrics.get('death_causes', {})}, 'player': self.player,
                'npcs': [self.npc_summary(n) for n in self.npcs.values()]}

    def npc_detail(self, npc_id):
        n = deepcopy(self.npcs[npc_id])
        n['legal_actions'] = self.legal_actions(self.npcs[npc_id])
        n['relations_view'] = [dict(other=int(i), name=self.npcs[int(i)]['name'],
                                    alive=self.npcs[int(i)]['alive'], **r)
                                for i, r in n['relationships'].items()]
        return n
