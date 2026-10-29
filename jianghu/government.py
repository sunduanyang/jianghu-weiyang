from . import combat_attributes, fame
"""One 府衙 per region with real people, balanced treasuries and evidence-based cases."""
from .demography import infect
from . import social
from .atlas import PLACES, COUNTRIES
from . import territory

OFFICES = ['县令', '捕头', '主簿']
POLICIES = {
    'balanced': {'name': '均衡施政', 'description': '按长官秉性议税，巡防与公医并重。', 'security_share': .5, 'relief': 4},
    'relief': {'name': '安民轻赋', 'description': '税率6%，优先缓解民困，每次救济5银。', 'security_share': .35, 'relief': 5},
    'security': {'name': '巡防肃盗', 'description': '税率11%，七成公共维护经费用于巡防。', 'security_share': .7, 'relief': 4},
    'health': {'name': '医养济民', 'description': '税率9%，七成公共维护经费用于公医。', 'security_share': .3, 'relief': 5},
}


def prepare(g):
    for k, v in dict(policy='balanced', policy_since=None, policy_reason='沿用既有施政，待年度公议。',
                     records=[], budget_security=0., budget_health=0.).items():
        g.setdefault(k, v)
    return g


def record(g, e):
    g['last_event'] = e['id']
    g['records'] = (g['records'] + [e['id']])[-24:]


def new(country=0):
    return prepare({'name': COUNTRIES[country]['government_name'], 'country': country, 'fund': 80., 'opening_fund': 80., 'tax_rate': .09,
            'security': .5, 'sanitation': .45, 'offices': {k: None for k in OFFICES},
            'appointed': {}, 'last_appointments': {}, 'cases': [], 'last_event': None,
            'revenue': 0., 'expenses': 0., 'relief': 0., 'closed_cases': 0, 'succession_count': 0})


def for_npc(w, n):
    return w.governments[territory.owner(w, n['place'])]


def office_government(w, n):
    return w.governments[n.get('office_country', 0)]


def establish(w, g=None):
    if g is None:
        for item in w.governments:
            establish(w, item)
        return
    e = w.emit('government_founded', [], f"{g['name']}立档，辖地库银80银用于治安、救济和公医。", .85,
               facts={'opening_fund': 80, 'jurisdiction': territory.controlled(w, g['country']), 'initial_condition': True})
    record(g, e)
    appoint(w, g)


def appoint(w, g=None):
    g = g if g is not None else w.government
    for office in OFFICES:
        current = w.npcs.get(g['offices'][office])
        if current and current['alive'] and w.month-g['appointed'].get(office, w.month) < 72:
            continue
        occupied = {i for k, i in g['offices'].items() if k != office and i}
        candidates = [n for n in w.npcs.values() if n['alive'] and 252 <= n['age_months'] <= 840 and n['id'] not in occupied
                      and n['faction'] is None and not n.get('social_status')
                      and territory.owner(w, n['place']) == g['country']
                      and (not n['office'] or n.get('office_country', 0) == g['country'])]
        if not candidates:
            g['offices'][office] = None
            if current:
                current['office'] = None
                current['office_country'] = None
                e = w.emit('office_vacancy', [current['id']],
                           f"{current['name']}的{office}任期届满，暂无合资格继任者，此职暂缺。", .75,
                           [g['last_appointments'].get(office)], {'office': office, 'previous': current['id']})
                g['last_appointments'][office] = e['id']
                record(g, e)
                g.setdefault('departed', {})[office] = current['id']
            continue
        def merit(n):
            return fame.local(n) + n['contribution']*.15 + n['traits']['integrity']*.25 + (
                combat_attributes.rating(n)*.5 if office == '捕头' else n['traits']['kindness']*.2 if office == '县令' else (100-n['traits']['boldness'])*.2)
        chosen = max(candidates, key=lambda n: (merit(n), -n['id']))
        old = current['id'] if current else g.get('departed', {}).pop(office, None)
        if current:
            current['office'] = None
        chosen['office'] = office
        chosen['office_country'] = g['country']
        g['offices'][office] = chosen['id']
        g['appointed'][office] = w.month
        people = [chosen['id']] + ([old] if old and w.npcs[old]['alive'] else [])
        e = w.emit('appointment', people, f"{g['name']}选任{chosen['name']}担任{office}，任期六年。", .85,
                   [g['last_appointments'].get(office), chosen['last_event']],
                   {'office': office, 'holder': chosen['id'], 'previous': old, 'merit': round(merit(chosen), 2)})
        g['last_appointments'][office] = e['id']
        record(g, e)
        g['succession_count'] += int(old is not None and old != chosen['id'])


def tax(w, n, income):
    g = for_npc(w, n)
    amount = round(max(0, income)*g['tax_rate'], 4)
    n['wealth'] -= amount
    g['fund'] += amount
    g['revenue'] += amount
    return amount


def grant(w, n, amount, purpose, cause=None):
    g = for_npc(w, n)
    amount = round(min(amount, g['fund']), 4)
    if amount <= 0:
        return 0
    g['fund'] -= amount
    g['expenses'] += amount
    g['relief'] += amount
    n['wealth'] += amount
    e = w.emit('government_relief', [n['id']], f"{g['name']}拨{amount:.1f}银给{n['name']}，用于{purpose}。", .65,
           [cause, g['last_event']], {'receiver': n['id'], 'silver': amount, 'purpose': purpose,
                                   'treasury_after': round(g['fund'], 4)})
    record(g, e)
    return amount


def monthly(w, g=None):
    if g is None:
        for item in w.governments:
            monthly(w, item)
        return
    appoint(w, g)
    g['security'] = max(.1, g['security']-.003)
    g['sanitation'] = max(.1, g['sanitation']-.002)
    needy = []
    for n in w.npcs.values():
        if not n['alive'] or n['wealth'] >= 2.3 or territory.owner(w, n['place']) != g['country']:
            continue
        supporters = set(n['parents'] + ([n['guardian']] if n['guardian'] else []))
        if n['age_months'] < 192 and any(w.npcs[i]['alive'] and w.npcs[i]['place'] == n['place']
                                        and w.npcs[i]['wealth'] >= 2.3 for i in supporters):
            continue
        needy.append(n)
    needy.sort(key=lambda n: (n['age_months'] >= 192, n['wealth'], n['id']))
    for n in needy:
        if g['fund'] < 3:
            break
        grant(w, n, POLICIES[g['policy']]['relief'], '育幼口粮' if n['age_months'] < 192 else '贫困口粮', n['last_event'])
    for case in g['cases']:
        if case['status'] != '待审' or w.month-case['month'] < 2 or g['offices']['捕头'] is None or g['fund'] < 2:
            continue
        if w.rng.random() > .25 + g['security']*.55:
            continue
        offender, victim = w.npcs[case['offender']], w.npcs[case['victim']]
        if not offender['alive'] or not victim['alive']:
            case['status'] = '当事人去世结案'
            continue
        g['fund'] -= 2
        from . import economics
        economics.fee(w, offender['place'], 2)
        g['expenses'] += 2
        restitution = min(offender['wealth'], case['amount'])
        offender['wealth'] -= restitution
        if case.get('public_money'):
            g['fund']+=restitution
            g['revenue']+=restitution
        else:
            victim['wealth'] += restitution
        fine = min(offender['wealth'], 3)
        offender['wealth'] -= fine
        g['fund'] += fine
        g['revenue'] += fine
        offender['reputation'] = max(-100, offender['reputation']-4)
        restraint=3 if w.store.event(case['crime_event'])['kind'] in ('extortion','extortion_resisted') else 1
        offender['probation_until']=max(offender.get('probation_until',-1),w.month+restraint)
        victim['desires']['revenge'] = max(0, victim['desires']['revenge']-40)
        w.relation(victim, offender['id'])['affinity'] = min(100, w.relation(victim, offender['id'])['affinity']+15)
        e = w.emit('judgment', [victim['id'], offender['id'], g['offices']['捕头']],
                   f"{g['name']}据已提交的证据判令{offender['name']}偿还{restitution:.1f}银，另罚{fine:.1f}银，拘束劳役{restraint}个月。", .9,
                   [case['report_event'], case['crime_event']],
                   {'case': case['crime_event'], 'restitution': restitution, 'fine': fine, 'court_cost': 2,
                    'offender':offender['id'],'victim':victim['id'],'public_money':case.get('public_money',False),
                    'restricted_months':restraint})
        case.update({'status': '已判决', 'judgment': e['id'], 'restitution': restitution})
        record(g, e)
        g['closed_cases'] += 1
    public_works(w, g)
    holders = [i for i in g['offices'].values() if i and w.npcs[i]['alive']]
    if holders and g['fund'] >= len(holders):
        for i in holders:
            w.npcs[i]['wealth'] += 1
        g['fund'] -= len(holders)
        g['expenses'] += len(holders)
        w.emit('stipend', holders, f"{g['name']}向在任官吏各发放俸银1银。", .1,
               facts={'payments': {str(i): 1 for i in holders}, 'treasury_after': round(g['fund'], 4)})


WORKS_WAGE = 3.


def treasury_float(w, g):
    """What a court keeps in hand: a base plus a share per resident of its towns."""
    places = {p['id'] for p in w.active_places if territory.owner(w, p['id']) == g['country']}
    people = sum(len(w.residents.get(p, [])) for p in places)
    return 40. + 2. * people, places


def public_works(w, g):
    """以工代赈: a court holding more than its float hires poor able adults for a week of
    roadwork, dykes or patrols, paying real wages from the treasury."""
    reserve, places = treasury_float(w, g)
    budget = round((g['fund'] - reserve) * .15, 4)
    if budget < WORKS_WAGE:
        return None
    workers = sorted((n for p in places for n in (w.npcs[i] for i in w.residents.get(p, []))
                      if n['alive'] and 192 <= n['age_months'] <= 720 and n['health'] > 30 and n['wealth'] < 15
                      and not n.get('office') and n['time_left'] >= 1),
                     key=lambda n: (n['wealth'], n['id']))[:int(budget // WORKS_WAGE)]
    if not workers:
        return None
    paid = WORKS_WAGE * len(workers)
    g['fund'] = round(g['fund'] - paid, 4); g['expenses'] = round(g['expenses'] + paid, 4)
    for n in workers:
        n['wealth'] = round(n['wealth'] + WORKS_WAGE, 4)
        n['time_left'] -= 1
    g['security'] = min(.95, g['security'] + .002 * len(workers) / max(1, len(places)))
    e = w.emit('public_works', [n['id'] for n in workers],
               f"{g['name']}以工代赈，雇{len(workers)}名贫困百姓修路筑堤一周，各付工钱{WORKS_WAGE:g}银。", .4,
               [g['last_event']], {'wage': WORKS_WAGE, 'workers': [n['id'] for n in workers], 'paid': paid,
                                   'reserve': round(reserve, 4), 'treasury_after': round(g['fund'], 4)})
    record(g, e)
    return e


def annual(w, g=None):
    if g is None:
        for item in w.governments:
            annual(w, item)
        return
    places = territory.controlled(w, g['country'])
    if not places:
        return
    residents = [n for n in w.npcs.values() if n['alive'] and territory.owner(w, n['place']) == g['country']]
    population = len(residents)
    magistrate = w.npcs.get(g['offices']['县令'])
    if magistrate:
        t = magistrate['traits']
        poor = sum(n['wealth'] < 10 for n in residents) / max(1, population)
        patients = sum(bool(n['illness']) or n['health'] < 55 for n in residents) / max(1, population)
        if patients >= .2:
            policy, reason = 'health', f'本地病弱占比{patients:.0%}，增拨公医'
        elif poor >= .3:
            policy, reason = 'relief', f'本地家资不足10银者占{poor:.0%}，减赋济困'
        elif g['security'] < .4:
            policy, reason = 'security', '本地治安低于40，增拨巡防'
        else:
            policy, reason = 'balanced', '民生与治安未达紧急阈值，兼顾巡防公医'
        previous = g['policy']
        g['policy'], g['policy_reason'] = policy, reason
        if previous != policy or g['policy_since'] is None:
            g['policy_since'] = w.month
        g['tax_rate'] = ({'relief': .06, 'security': .11, 'health': .09}.get(policy)
                         or round(max(.05, min(.15, .07+(100-t['kindness'])*.0006)), 4))
        budget = min(g['fund']*.2, max(12, population*.15))
        g['fund'] -= budget
        from . import economics
        seats = [p['id'] for p in w.active_places if territory.owner(w, p['id']) == g['country']]
        if seats:
            economics.fee(w, seats[0], budget, 'transport')
        g['expenses'] += budget
        improvement = .12 * min(1, budget/max(12, population*.15))
        share = POLICIES[policy]['security_share']
        g['budget_security'], g['budget_health'] = round(budget * share, 4), round(budget * (1-share), 4)
        g['security'] = min(.95, g['security']+improvement * 2 * share)
        g['sanitation'] = min(.95, g['sanitation']+improvement * 2 * (1-share))
        e = w.emit('public_budget', [magistrate['id']],
               f"{g['name']}议定「{POLICIES[policy]['name']}」，税率{g['tax_rate']*100:.1f}%，支出{budget:.1f}银维护巡防与公医。", .75,
               [g['last_appointments'].get('县令'), g['last_event']],
               {'country': g['country'], 'budget': budget, 'tax_rate': g['tax_rate'], 'policy': policy,
                'previous_policy': previous, 'reason': reason, 'security_budget': g['budget_security'],
                'health_budget': g['budget_health'], 'security': g['security'], 'sanitation': g['sanitation']})
        record(g, e)
    if w.rng.random() < .24:
        place = w.rng.choice(places)
        sick = [w.npcs[i] for i in w.residents[place] if w.rng.random() < .18 and not w.npcs[i]['illness']]
        if sick:
            e = w.emit('epidemic', [n['id'] for n in sick], f"{w.place_name(place)}流行时疫，{len(sick)}人染病。", .9,
                       facts={'place': place, 'patients': [n['id'] for n in sick]})
            for n in sick:
                infect(w, n, e['id'])


def report(w, n, crime_event, extra=None):
    g = for_npc(w, n)
    crime = w.store.event(crime_event)
    if not crime or crime['kind'] not in social.CRIMES or crime['facts'].get('victim') != n['id']:
        raise ValueError('没有此人实际掌握的受害证据')
    if any(c['crime_event'] == crime_event for court in w.governments for c in court['cases']):
        raise ValueError('该证据已经立案，不能在别处府衙重复报案')
    e = w.emit('crime_report', [n['id']], f"{n['name']}携已知的侵吞证据向{g['name']}报案。", .75,
               [crime_event], {'crime_event': crime_event, 'offender': crime['facts']['offender'], **(extra or {})})
    g['cases'].append({'crime_event': crime_event, 'report_event': e['id'], 'month': w.month,
                                  'offender': crime['facts']['offender'], 'victim': n['id'],
                                  'amount': crime['facts']['transfer'], 'status': '待审',
                                  'public_money':crime['facts'].get('public_money',False)})
    record(g, e)
    return e
