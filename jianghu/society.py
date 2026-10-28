"""Household support and town mutual aid, settled from actual people and silver.

No opening money or invented relationships are added on upgrade. Public views
are read-only; household transfers run once before the monthly food settlement.
"""
from collections import deque
from copy import deepcopy

from . import relationships, settlements, social

LABELS = {'donate_relief': '捐助义仓', 'neighbor_care': '探病照护'}


def prepare(w, n):
    n.setdefault('social_life', {'since': w.month, 'support_given': 0., 'support_received': 0.,
                               'community_given': 0., 'community_received': 0.,
                               'care_received': 0, 'hardship_months': 0,
                               'last_support_event': None, 'last_care_event': None,
                               'care_month': None})


def new_community(w, place):
    return {'place': place, 'since': w.month, 'fund': 0., 'opening_fund': 0.,
            'revenue': 0., 'expenses': 0., 'donations': 0, 'relief_paid': 0.,
            'care_visits': 0, 'records': []}


def initialize(w):
    if not hasattr(w, 'society'):
        w.society = {'version': 1, 'since': w.month, 'settled_month': None,
                     'communities': {}, 'monthly': empty_month(w)}
    # If a large-map save is consolidated later, preserve every town's accounts.
    merged = {}
    for old in w.society['communities'].values():
        place = settlements.canonical(w, old['place'])
        key = str(place)
        if key not in merged:
            merged[key] = deepcopy(old)
            merged[key]['place'] = place
        else:
            target = merged[key]
            for field in ('fund', 'opening_fund', 'revenue', 'expenses', 'donations', 'relief_paid', 'care_visits'):
                target[field] += old[field]
            target['since'] = min(target['since'], old['since'])
            target['records'] = sorted(set(target['records'] + old['records']))[-24:]
    for p in w.active_places:
        merged.setdefault(str(p['id']), new_community(w, p['id']))
    w.society['communities'] = merged


def empty_month(w):
    return {'month': w.month, 'support_silver': 0., 'support_payments': 0,
            'relief_silver': 0., 'relief_people': 0, 'donated_silver': 0., 'care_visits': 0}


def upkeep(n):
    return 2.3 if n['age_months'] >= 144 else 1.2


def vulnerable(n):
    return n['age_months'] < 192 or n['age_months'] >= 720 or bool(n['illness']) or n['health'] < 55


def need(n):
    return max(0., round(max(upkeep(n) * 2, 4 if n['illness'] else 0) - n['wealth'], 4))


def kin_ids(n):
    return set(n['parents'] + n['children'] + [i for i in (n['spouse'], n['guardian']) if i])


def support_network(w):
    network = {i: set() for i in w.npcs}
    for n in w.npcs.values():
        if not n['alive']:
            continue
        for i in kin_ids(n):
            q = w.npcs.get(i)
            if q and q['alive'] and q['place'] == n['place']:
                network[n['id']].add(i)
                network[i].add(n['id'])
        for key, r in n['relationships'].items():
            q = w.npcs.get(int(key))
            if (q and q['alive'] and q['place'] == n['place'] and r.get('sworn')
                    and q['relationships'].get(str(n['id']), {}).get('sworn')):
                network[n['id']].add(q['id'])
    return network


def record(community, event):
    community['records'] = (community['records'] + [event['id']])[-24:]


def monthly(w):
    if w.society['settled_month'] == w.month:
        return
    w.society['settled_month'] = w.month
    stats = w.society['monthly'] = empty_month(w)
    network = support_network(w)
    budgets = {}
    # Reserve the donor's own food and the food of all co-resident dependants.
    dependants = {}
    for n in w.npcs.values():
        if n['alive'] and n['age_months'] < 192:
            for i in kin_ids(n):
                dependants.setdefault(i, set()).add(n['id'])
    needy = sorted((n for n in w.npcs.values() if n['alive'] and need(n) > 0),
                   key=lambda n: (n['age_months'] >= 192, not vulnerable(n), n['wealth'], n['id']))
    for n in needy:
        for i in sorted(network[n['id']], key=lambda i: (-w.npcs[i]['wealth'], i)):
            q = w.npcs[i]
            r = q['relationships'].get(str(n['id']), {})
            if (q['age_months'] < 192 or q['health'] < 35 or r.get('affinity', 0) < -25
                    or r.get('trust', 0) < -30 or q['traits']['kindness'] + q['traits']['integrity'] < 75):
                continue
            reserve = 12 + sum(upkeep(w.npcs[j]) * 2 for j in dependants.get(i, ())
                               if w.npcs[j]['place'] == q['place'])
            amount = round(min(need(n), 4., max(0., q['wealth'] - reserve), 8 - budgets.get(i, 0.)), 4)
            if amount <= 0:
                continue
            q['wealth'] = round(q['wealth'] - amount, 4)
            n['wealth'] = round(n['wealth'] + amount, 4)
            budgets[i] = budgets.get(i, 0.) + amount
            q['social_life']['support_given'] += amount
            n['social_life']['support_received'] += amount
            w.link(q, n, 2, 1)
            event = w.emit('household_support', [q['id'], n['id']],
                           f"{q['name']}从自有家资中拿出{amount:.1f}银扶助{n['name']}，留作口粮与病中用度。", .5,
                           [q['last_event'], n['last_event']],
                           {'giver': i, 'receiver': n['id'], 'transfer': amount, 'place': n['place'],
                            'relation': relationships.label(w, q, n['id'], w.relation(q, n['id'])),
                            'donor_reserve': reserve, 'automatic_household_settlement': True})
            q['social_life']['last_support_event'] = n['social_life']['last_support_event'] = event['id']
            stats['support_payments'] += 1
            stats['support_silver'] = round(stats['support_silver'] + amount, 4)
            if need(n) <= 0:
                break
    # Town aid follows family aid; government relief follows both in the engine.
    for n in needy:
        community = w.society['communities'][str(n['place'])]
        target = max(upkeep(n), 4 if n['illness'] else 0)
        amount = round(min(max(0., target - n['wealth']), 3., community['fund']), 4)
        if amount <= 0:
            continue
        community['fund'] = round(community['fund'] - amount, 4)
        community['expenses'] = round(community['expenses'] + amount, 4)
        community['relief_paid'] = round(community['relief_paid'] + amount, 4)
        n['wealth'] = round(n['wealth'] + amount, 4)
        n['social_life']['community_received'] += amount
        event = w.emit('community_relief', [n['id']],
                       f"{w.place_name(n['place'])}义仓拨{amount:.1f}银给{n['name']}，救助眼前衣食病困。", .55,
                       [n['last_event'], community['records'][-1] if community['records'] else None],
                       {'receiver': n['id'], 'silver': amount, 'place': n['place'],
                        'fund_after': community['fund']})
        n['social_life']['last_support_event'] = event['id']
        record(community, event)
        stats['relief_silver'] = round(stats['relief_silver'] + amount, 4)
        stats['relief_people'] += 1


def settle_hardship(w, n):
    life = n['social_life']
    hungry = n.get('monthly_settlement', {}).get('food_shortfall', 0) > 0
    life['hardship_months'] = life['hardship_months'] + 1 if hungry or (n['wealth'] < 4.6 and vulnerable(n)) else 0


def candidates(w, n, visible, planning=False):
    if n['age_months'] < 192:
        return []
    result = []
    community = w.society['communities'][str(n['place'])]
    local = [w.npcs[i] for i in w.residents.get(n['place'], ())]
    if n['wealth'] >= 20 and community['fund'] < max(8, len(local) * 4) and any(need(q) > 0 for q in local):
        result.append({'id': 'donate_relief:self', 'kind': 'donate_relief', 'target': None,
                       'label': '捐助义仓 · 捐4银留作本乡救急'})
    if n['health'] < 55:
        return result
    for i in visible:
        q = w.npcs[i]
        r = n['relationships'].get(str(i), {})
        back = q['relationships'].get(str(n['id']), {})
        if (q['alive'] and q['place'] == n['place'] and r.get('affinity', 0) >= -10
                and back.get('affinity', 0) >= -25 and back.get('trust', 0) >= -30 and q['mood']['anger'] < 80
                and (q['illness'] or q['health'] < 55 or (q['age_months'] < 192 and q['guardian'] is None))
                and q['social_life']['care_month'] != w.month
                and relationships.partner_available(w, q, planning)):
            result.append({'id': f'neighbor_care:{i}', 'kind': 'neighbor_care', 'target': i,
                           'label': f"探病照护 · {q['name']}（双方各一周）"})
    return result


def score(w, n, a):
    t = n['traits']
    if a['kind'] == 'donate_relief':
        return 4 + t['kindness'] * .24 + t['integrity'] * .07 - (100-t['kindness']) * .17
    return 10 + t['kindness'] * .32 - (100-t['kindness']) * .1 + (10 if a['target'] in kin_ids(n) else 0)


def execute(w, n, a, facts):
    community = w.society['communities'][str(n['place'])]
    stats = w.society['monthly']
    before = {key: n[key] for key in ('wealth', 'health')}
    if a['kind'] == 'donate_relief':
        n['wealth'] = round(n['wealth'] - 4, 4)
        community['fund'] = round(community['fund'] + 4, 4)
        community['revenue'] = round(community['revenue'] + 4, 4)
        community['donations'] += 1
        n['social_life']['community_given'] += 4
        n['reputation'] = min(100, n['reputation'] + .3)
        stats['donated_silver'] += 4
        people = [n['id']]
        facts.update(giver=n['id'], silver=4, fund_after=community['fund'])
        text = f"{n['name']}向{w.place_name(n['place'])}义仓捐4银，供乡里下月救急。"
    else:
        q = w.npcs[a['target']]
        q['health'] = min(100, q['health'] + 2)
        q['pressure'] = max(0, q['pressure'] - 4)
        q['social_life']['care_received'] += 1
        q['social_life']['care_month'] = w.month
        n['health'] = max(0, n['health'] - .8)
        w.link(n, q, 4, 3)
        community['care_visits'] += 1
        stats['care_visits'] += 1
        people = [n['id'], q['id']]
        facts.update(receiver=q['id'], health_gain=2, partner=q['id'], partner_time_cost=1)
        text = f"{n['name']}用一周探望照护{q['name']}，对方体力恢复2点，心中不安稍缓。"
    facts.update(place=n['place'], actor_delta={key: round(n[key] - v, 4) for key, v in before.items()})
    event = w.emit(a['kind'], people, text, .4,
                   [n['last_event'], community['records'][-1] if community['records'] else None], facts)
    record(community, event)
    if a['kind'] == 'neighbor_care':
        q['social_life']['last_care_event'] = event['id']
        relationships.reserve_partner(w, q, a, event, '受照护')
    return event


def migration(w, n):
    """A route towards a known host, using the traveller's last observation only."""
    if n['age_months'] < 192 or n['social_life']['hardship_months'] < 2:
        return None
    hosts = kin_ids(n) | {int(k) for k, r in n['relationships'].items() if r.get('sworn')}
    if not hosts:
        return None
    queue = deque([n['place']])
    first = {n['place']: None}
    distance = {n['place']: 0}
    while queue:
        place = queue.popleft()
        for other in sorted(w.neighbors[place]):
            if other not in first:
                first[other] = other if place == n['place'] else first[place]
                distance[other] = distance[place] + 1
                queue.append(other)
    options = []
    for i in sorted(hosts):
        seen = n['knowledge'].get(str(i))
        r = n['relationships'].get(str(i), {})
        if (not seen or w.month - seen['seen_month'] > 24 or seen['wealth_band'] != '有余'
                or r.get('affinity', 0) < -10 or r.get('trust', 0) < -10):
            continue
        place = settlements.canonical(w, seen['place'])
        if place != n['place'] and place in first:
            options.append({'host': i, 'destination': place, 'next_place': first[place],
                            'seen_month': seen['seen_month'], 'steps': distance[place], 'reason': '困顿投亲'})
    return min(options, key=lambda a: (a['steps'], a['host'])) if options else None


def household(w, n):
    from . import local_life
    if not n['alive']:
        return []
    # Sworn friends can send support, but are not automatically cohabiting kin.
    group, pending = set(), [n['id']]
    reverse = {}
    for q in w.npcs.values():
        if q['alive'] and q['place'] == n['place']:
            for i in kin_ids(q):
                reverse.setdefault(i, set()).add(q['id'])
    while pending:
        i = pending.pop()
        q = w.npcs[i]
        if i in group or not q['alive'] or q['place'] != n['place']:
            continue
        a, b = local_life.entry(w, n), local_life.entry(w, q)
        if a and b and a['home']['place'] == n['place'] and b['home']['place'] == q['place'] and a['home']['settlement_id'] != b['home']['settlement_id']:
            continue
        group.add(i)
        pending.extend((kin_ids(q) | reverse.get(i, set())) - group)
    return sorted(group)


def person_view(w, n):
    members = [w.npcs[i] for i in household(w, n)]
    return {**deepcopy(n['social_life']), 'household_members': [
        {'id': q['id'], 'name': q['name'], 'age_months': q['age_months'], 'wealth': q['wealth'],
         'relation': '本人' if q['id'] == n['id'] else
                     relationships.label(w, n, q['id'], n['relationships'].get(str(q['id']), {'affinity': 0}))}
        for q in members], 'household_wealth': round(sum(q['wealth'] for q in members), 4),
        'community_fund': w.society['communities'][str(n['place'])]['fund'],
        'migration': migration(w, n) if n['alive'] else None}


def overview(w):
    living = [n for n in w.npcs.values() if n['alive']]
    rows = []
    for p in w.active_places:
        people = [w.npcs[i] for i in w.residents.get(p['id'], ())]
        row = deepcopy(w.society['communities'][str(p['id'])])
        row.update(name=p['name'], population=len(people), needy=sum(need(n) > 0 for n in people),
                   hardship=sum(n['social_life']['hardship_months'] > 0 for n in people),
                   unsupported_children=sum(n['age_months'] < 192 and n['guardian'] is None and
                                            not any(w.npcs[i]['alive'] and w.npcs[i]['place'] == n['place']
                                                    for i in n['parents']) for n in people))
        rows.append(row)
    return {'since': w.society['since'], 'monthly': deepcopy(w.society['monthly']),
            'fund': round(sum(r['fund'] for r in rows), 4),
            'hardship': sum(n['social_life']['hardship_months'] > 0 for n in living),
            'unsupported_children': sum(r['unsupported_children'] for r in rows), 'communities': rows}
