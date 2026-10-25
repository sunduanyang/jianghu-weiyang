"""World-local settlement control and bounded campaigns. Guard values are abstract
readiness points, not extra NPCs. Silver is charged to the actual state treasury.
"""
import math

from .atlas import PLACES, ROUTES, NEIGHBORS

GUARDS = {'capital': 80., 'city': 55., 'port': 45., 'town': 35., 'mountain': 60.}
ROUTE_INDEX = {tuple(sorted((r['a'], r['b']))): r for r in ROUTES}


def new():
    return [dict(owner=p['country'], guard=GUARDS[p['kind']], stability=100.,
                 changed_month=None, last_battle=-6, event_id=None) for p in PLACES]


def owner(w, place):
    return w.territory[place]['owner']


def controlled(w, country):
    return [p['id'] for p in PLACES if owner(w, p['id']) == country]


def refresh_relations(w):
    for r in ROUTES:
        a, b = owner(w, r['a']), owner(w, r['b'])
        if a != b:
            status = w.countries[a]['relations'].get(str(b),
                     w.countries[b]['relations'].get(str(a), '中立'))
            w.countries[a]['relations'][str(b)] = status
            w.countries[b]['relations'][str(a)] = status


def place_view(w, place):
    p, t = PLACES[place], w.territory[place]
    return {**p, **t, 'country': t['owner'], 'native_country': p['country'],
            'color': w.countries[t['owner']]['color'],
            'guard_max': GUARDS[p['kind']], 'occupied': t['owner'] != p['country']}


def attack_options(w, target):
    if type(target) is not int or not 0 <= target < len(PLACES):
        return []
    best = {}
    for origin in NEIGHBORS[target]:
        attacker = owner(w, origin)
        if attacker == owner(w, target):
            continue
        route = ROUTE_INDEX[tuple(sorted((origin, target)))]
        cost = round(12 + min(18, route['km']*.018) + (10 if route['sea'] else 0), 2)
        reasons = []
        if w.governments[attacker]['fund'] < cost + 8:
            reasons.append('公帑不足，须留8银民政余量')
        if w.territory[origin]['guard'] < 25 or w.territory[origin]['stability'] < 60:
            reasons.append('出发地守备或安定不足')
        if w.month - w.campaigns['last_attack'].get(str(attacker), -3) < 3:
            reasons.append('本方出兵后须休整3个月')
        if w.month - w.territory[target]['last_battle'] < 6:
            reasons.append('此地战后休整6个月')
        if w.month - w.territory[origin]['last_battle'] < 3:
            reasons.append('出发地战后须休整3个月')
        option = dict(attacker=attacker, origin=origin, target=target, cost=cost,
                      sea=route['sea'], ready=not reasons, reason='；'.join(reasons))
        rank = (not reasons, w.territory[origin]['guard']-cost*.5, -origin)
        if attacker not in best or rank > best[attacker][0]:
            best[attacker] = rank, option
    return [best[c][1] for c in sorted(best)]


def capture(w, target, attacker, event):
    from . import careers
    t = w.territory[target]
    t.update(owner=attacker, guard=min(30., GUARDS[PLACES[target]['kind']]*.4),
             stability=35., changed_month=w.month, event_id=event['id'])
    # Officials do not silently change allegiance or govern inside enemy territory.
    active = {t['owner'] for t in w.territory}
    for g in w.governments:
        for role, npc_id in g['offices'].items():
            n = w.npcs.get(npc_id)
            if n and (owner(w, n['place']) != g['country'] or g['country'] not in active):
                n['office'] = n['office_country'] = None
                n['plan'] = None
                g['offices'][role] = None
                g['last_appointments'][role] = event['id']
                careers.sync_identity(w, n, event['id'])
    for f in w.factions:
        f['country'] = owner(w, f['place'])
    refresh_relations(w)


def campaign(w, attacker, target, *, declare=False):
    if (type(attacker) is not int or not 0 <= attacker < len(w.countries)
            or type(target) is not int or not 0 <= target < len(PLACES)):
        raise ValueError('势力或地点不存在')
    option = next((o for o in attack_options(w, target) if o['attacker'] == attacker), None)
    if not option:
        raise ValueError('只能沿相邻驿道或指定海路进攻异国城镇')
    if not option['ready']:
        raise ValueError(option['reason'])
    defender, origin = owner(w, target), option['origin']
    relation = w.countries[attacker]['relations'].get(str(defender), '中立')
    if relation != '交恶' and not declare:
        raise ValueError('只有交恶势力会自动出兵')
    causes = [w.territory[target]['event_id']]
    if relation != '交恶':
        w.countries[attacker]['relations'][str(defender)] = '交恶'
        w.countries[defender]['relations'][str(attacker)] = '交恶'
        war = w.emit('war_declared', [],
                     f"{w.countries[attacker]['name']}向{w.countries[defender]['name']}宣战，边境关税改为10银。", .95,
                     facts={'countries': [attacker, defender], 'previous': relation, 'relation': '交恶'})
        causes.append(war['id'])
    g = w.governments[attacker]
    g['fund'] -= option['cost']
    g['expenses'] += option['cost']
    staging, defense = w.territory[origin], w.territory[target]
    attack = (staging['guard'] + min(18, math.sqrt(max(0, g['fund']))))*w.rng.uniform(.8, 1.2)
    fort = 1.25 if PLACES[target]['kind'] in ('capital', 'mountain') else 1.
    resistance = (defense['guard']*fort + 12)*(.65 + defense['stability']*.0035)*w.rng.uniform(.8, 1.2)
    if option['sea']:
        attack *= .8
    won = attack > resistance
    staging['guard'] = max(10., staging['guard']-18)
    defense['guard'] = max(8., defense['guard']-(25 if won else 14))
    defense['stability'] = max(20., defense['stability']-12)
    staging['last_battle'] = defense['last_battle'] = w.month
    w.campaigns['last_attack'][str(attacker)] = w.month
    result = '攻占' if won else '未能攻下'
    event = w.emit('settlement_captured' if won else 'siege_repulsed', [],
                   f"{w.countries[attacker]['name']}自{PLACES[origin]['name']}"
                   f"{'渡海' if option['sea'] else '出兵'}{result}{PLACES[target]['name']}，"
                   f"耗费公帑{option['cost']:.1f}银。" +
                   (f"当地税收与通行改归{w.countries[attacker]['name']}。" if won else '守方仍控制此地。'),
                   .98, causes,
                   {'attacker': attacker, 'defender': defender, 'origin': origin, 'place': target,
                    'captured': won, 'sea': option['sea'], 'cost': option['cost'],
                    'attack_strength': round(attack, 3), 'defense_strength': round(resistance, 3),
                    'previous_owner': defender, 'owner': attacker if won else defender,
                    'treasury_after': round(g['fund'], 4)})
    if won:
        capture(w, target, attacker, event)
    else:
        defense['event_id'] = event['id']
    w.campaigns['history'].append(dict(event_id=event['id'], month=w.month, attacker=attacker,
                                     defender=defender, place=target, captured=won))
    w.campaigns['history'] = w.campaigns['history'][-80:]
    return event


def monthly(w):
    for p, t in zip(PLACES, w.territory):
        t['guard'] = min(GUARDS[p['kind']], t['guard']+2)
        t['stability'] = min(100., t['stability']+5)
    if w.month % 3:
        return
    fronts = [o for p in PLACES for o in attack_options(w, p['id']) if o['ready'] and
              w.countries[o['attacker']]['relations'].get(str(owner(w, p['id']))) == '交恶']
    w.rng.shuffle(fronts)
    battles = 0
    for o in fronts:
        if battles >= 2:
            break
        if w.rng.random() < .35:
            try:  # A preceding campaign can invalidate staging grounds/cooldowns.
                campaign(w, o['attacker'], o['target'])
            except ValueError:
                continue
            battles += 1
