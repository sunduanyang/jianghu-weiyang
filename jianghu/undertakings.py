"""长期筹划：multi-month undertakings that turn a life goal into a sequence of real deeds.

A goal used to bias single actions. An undertaking is a concrete pursuit with a
named counterpart where one exists: courting a particular person until a
proposal is answered, seeking out a teacher and studying the manual received,
travelling to places not yet seen, building a name through good deeds, or
pressing a grievance against the person who caused it. Each step is an
ordinary legal action settled by the engine; the undertaking only decides
which one to take, at most one step per month, and waits (rather than forcing
anything) when the step is not currently possible. Choosing never consumes the
world RNG beyond what normal decisions already do.
"""
from . import demography, items, martial

KINDS = {'courtship': '求偶成家', 'grudge': '了结旧怨', 'study': '求师学艺',
         'journey': '远游见闻', 'standing': '积德扬名'}
# Which life goal each undertaking serves.
GOAL = {'courtship': 'belonging', 'grudge': 'revenge', 'study': 'mastery',
        'journey': 'freedom', 'standing': 'renown'}
DEEDS = {'aid', 'donate_relief', 'neighbor_care', 'care', 'public_service', 'faction_guide', 'sect_service', 'duty'}
STANDING_DEEDS = 4
JOURNEY_PLACES = 2
JOURNEY_SAVINGS = 45  # a journey is begun only with savings in hand
JOURNEY_RESERVE = 30  # and each leg leaves enough to live on
PATIENCE = 6        # months without a step before an undertaking is given up
REBUFFS = 2         # refusals before the counterpart is given up
DUEL_BOLDNESS = 45  # below this a grudge is pressed only through the courts
MONTHS = 24


def local(w, n):
    return [w.npcs[i] for i in w.residents.get(n['place'], [])
            if i != n['id'] and w.npcs[i]['alive'] and w.npcs[i]['place'] == n['place']]


def single(n):
    return not n['spouse'] and not n.get('player_spouse')


def suitor(w, n):
    """The local person this one would court: mutually attracted, unattached, not kin, not estranged."""
    if not single(n) or not 18*12 <= n['age_months'] <= 50*12:
        return None
    rows = []
    for q in local(w, n):
        if not single(q) or q['age_months'] < 18*12 or abs(q['age_months'] - n['age_months']) > 15*12:
            continue
        r = n['relationships'].get(str(q['id']), {})
        if r.get('affinity', 0) < -5 or w.close_kin(n, q) or not (demography.attracted(n, q) and demography.attracted(q, n)):
            continue
        rows.append((r.get('romance', 0) + r.get('affinity', 0)*.5 - abs(q['age_months'] - n['age_months'])/6, -q['id'], q))
    return max(rows, key=lambda row: row[:2])[2] if rows else None


def settled_family(w, n):
    """A spouse at home or a small child keeps a person from setting out on a long journey."""
    spouse = w.npcs.get(n['spouse'])
    young = any(w.npcs[i]['alive'] and w.npcs[i]['age_months'] < 72 for i in n['children'] if i in w.npcs)
    return young or bool(spouse and spouse['alive'] and spouse['place'] == n['place'])


def known_arts(w, n):
    return set((martial.resolve(w, n) or {}).get('known', []))


def teacher(w, n):
    """A known local who knows an art this person lacks; friends first."""
    mine = known_arts(w, n)
    rows = sorted(((r['affinity'], int(k)) for k, r in n['relationships'].items()
                   if r['affinity'] > -5 and int(k) in w.npcs), reverse=True)
    for affinity, i in rows[:8]:
        q = w.npcs[i]
        if (q['alive'] and q['place'] == n['place'] and q['age_months'] >= 192 and q['health'] > 30
                and known_arts(w, q) - mine):
            return q
    return None


def unlearned_manual(w, n):
    mine = None
    for i in items.objects(w, n['id']):
        art = items.spec(i).get('art')
        if art:
            mine = known_arts(w, n) if mine is None else mine
            if art not in mine:
                return i
    return None


def propose(w, n):
    """The undertaking this person's goal suggests right now, or None."""
    g = n.get('goal') or {}
    k = g.get('kind')
    if n['age_months'] < 16*12 or n['health'] < 45:
        return None
    if k == 'belonging':
        q = suitor(w, n)
        if q:
            return 'courtship', f"追求{q['name']}，求结连理", dict(target=q['id'], target_name=q['name'], rebuffs=0)
    elif k == 'revenge' and not g.get('resolved') and g.get('against') in w.npcs and w.npcs[g['against']]['alive']:
        q = w.npcs[g['against']]
        return 'grudge', f"向{q['name']}讨回公道", dict(target=q['id'], target_name=q['name'])
    elif k == 'mastery':
        learned = len(n['martial'].get('learned', []))
        if unlearned_manual(w, n):
            return 'study', '研习行囊中的秘籍', dict(target=None, start_learned=learned, rebuffs=0)
        q = teacher(w, n)
        if q:
            return 'study', f"拜访{q['name']}求学武艺", dict(target=q['id'], target_name=q['name'],
                                                        start_learned=learned, rebuffs=0)
    elif k == 'freedom' and n['wealth'] >= JOURNEY_SAVINGS and not n['pregnancy'] and not settled_family(w, n):
        return 'journey', '出门远游，见识未曾到过的地方', dict(
            target=None, start_visited=len(set(n.get('visited_places', [n['place']]))))
    elif k == 'renown':
        return 'standing', f'行善积德，在乡里立下名声（{STANDING_DEEDS}件善举）', dict(target=None, deeds=0)
    return None


def focus(n):
    """The person an undertaking is about; the engine keeps them in view while both are in town."""
    p = n.get('plan') or {}
    return p.get('target') if p.get('intent') in KINDS else None


def achieved(w, n, p):
    k = p['intent']
    if k == 'courtship' and n['spouse'] == p['target']:
        return f"与{p['target_name']}结为夫妻"
    if k == 'grudge' and (n.get('goal') or {}).get('kind') == 'revenge' and n['goal'].get('resolved'):
        return n['goal']['resolved']['text']
    if k == 'study' and len(n['martial'].get('learned', [])) > p['start_learned']:
        return '学成一门新武学'
    if k == 'journey' and len(set(n.get('visited_places', []))) >= p['start_visited'] + JOURNEY_PLACES:
        return f'已游历{JOURNEY_PLACES}处新地方'
    if k == 'standing' and p['deeds'] >= STANDING_DEEDS:
        return f'已行善{STANDING_DEEDS}件'
    return None


def step(w, n, p, actions):
    """('act', action) | ('wait', why) | ('close', why): the next step of an undertaking."""
    k = p['intent']
    def find(kind, target=None, test=lambda a: True):
        return next((a for a in actions if a['kind'] == kind and (target is None or a['target'] == target) and test(a)), None)
    if k == 'courtship':
        q = w.npcs.get(p['target'])
        if not single(n):
            return 'close', '已另有归宿'
        if not q or not q['alive']:
            return 'close', '对方已经亡故'
        if not single(q):
            return 'close', f"{q['name']}已另有婚配"
        if q['place'] != n['place']:
            return 'wait', f"{q['name']}不在本地"
        r = n['relationships'].get(str(q['id']), {})
        a = None
        if r.get('affinity', 0) >= 30 and r.get('romance', 0) >= 45:
            a = find('propose', q['id'])
        if a is None and r.get('affinity', 0) >= 12:
            a = find('court', q['id'])
        a = a or find('befriend', q['id'])
        return ('act', a) if a else ('wait', f"{q['name']}这周无暇相见")
    if k == 'grudge':
        q = w.npcs.get(p['target'])
        if not q or not q['alive']:
            return 'close', '对方已经亡故'
        a = find('report')
        if a is None and n['traits']['boldness'] >= DUEL_BOLDNESS:
            a = find('duel', q['id'])
        if a:
            return 'act', a
        return 'wait', ('等候官府审理或对方赔礼' if n['traits']['boldness'] < DUEL_BOLDNESS else
                        f"{q['name']}不在眼前，或身体尚不宜比斗")
    if k == 'study':
        manual = unlearned_manual(w, n)
        if manual:
            a = find('use_item', manual['id'])
            return ('act', a) if a else ('wait', '本周无暇研习秘籍')
        q = w.npcs.get(p['target'])
        if p['target'] is None or not q or not q['alive']:
            return 'close', '求学的老师已不在'
        if q['place'] != n['place']:
            return 'wait', f"{q['name']}不在本地"
        a = find('seek_teacher', q['id'])
        if a is None and n['relationships'].get(str(q['id']), {}).get('affinity', 0) < 6:
            a = find('befriend', q['id'])
        return ('act', a) if a else ('wait', f"{q['name']}本月无暇授业")
    if k == 'journey':
        seen = set(n.get('visited_places', []))
        trips = [a for a in actions if a['kind'] == 'travel' and not a.get('migration')
                 and n['wealth'] >= a['cost'] + JOURNEY_RESERVE]
        fresh = [a for a in trips if a['target'] not in seen]
        onward = [a for a in trips if any(d not in seen for d in w.neighbors[a['target']])]
        a = min(fresh or onward, key=lambda a: (a['cost'], a['target']), default=None)
        if a:
            return 'act', a
        if not any(d not in seen for d in w.neighbors[n['place']]) and not any(
                d2 not in seen for d in w.neighbors[n['place']] for d2 in w.neighbors[d]):
            return 'close', '附近已无未曾到过的地方'
        return 'wait', '盘缠不足或身体欠佳，先攒些路费'
    if k == 'standing':
        deeds = [a for a in actions if a['kind'] in DEEDS]
        if deeds:
            return 'act', max(deeds, key=lambda a: (w.score(n, a), a['id']))
        return 'wait', '眼下没有力所能及的善举'
    return 'close', '筹划已不适用'


def after(w, n, p, a, e):
    """Account for one executed step. Returns (status, reason) when the undertaking ends."""
    k = p['intent']
    facts = e.get('facts', {})
    if k == 'courtship':
        if e['kind'] == 'marriage':
            return '完成', f"与{p['target_name']}结为夫妻"
        if a['kind'] == 'propose' and e['kind'] == 'rejection':
            return '中断', f"向{p['target_name']}提亲未果"
        if a['kind'] == 'court' and e['kind'] == 'rejection':
            p['rebuffs'] += 1
            if p['rebuffs'] >= REBUFFS:
                return '中断', f"{p['target_name']}屡次婉拒"
    elif k == 'study':
        if a['kind'] == 'seek_teacher' and not facts.get('accepted'):
            p['rebuffs'] += 1
            if p['rebuffs'] >= REBUFFS:
                return '中断', f"{p['target_name']}未允传授"
    elif k == 'journey':
        p['place'] = n['place']
    elif k == 'standing':
        p['deeds'] += 1
    reason = achieved(w, n, p)
    return ('完成', reason) if reason else None


def describe(w, n, p):
    k = p['intent']
    if k == 'courtship':
        r = n['relationships'].get(str(p['target']), {})
        return ('择机提亲' if r.get('affinity', 0) >= 30 and r.get('romance', 0) >= 45 else
                '倾诉心意，增进情意' if r.get('affinity', 0) >= 12 else '多来往，先结下交情')
    if k == 'grudge':
        return '向官府递交证据' if n.get('grievance_cause') else (
            '寻机比武讨回颜面' if n['traits']['boldness'] >= DUEL_BOLDNESS else '等候判决、赔礼，或日久淡忘')
    if k == 'study':
        return '研习秘籍' if unlearned_manual(w, n) else '向老师求取秘籍'
    if k == 'journey':
        return f"再游历{max(0, p['start_visited'] + JOURNEY_PLACES - len(set(n.get('visited_places', []))))}处新地方"
    if k == 'standing':
        return f"再行善{max(0, STANDING_DEEDS - p['deeds'])}件"
    return ''
