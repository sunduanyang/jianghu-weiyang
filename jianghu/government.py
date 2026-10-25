"""A county government with real people, a balanced treasury and evidence-based cases."""
from .demography import infect

OFFICES = ['县令', '捕头', '主簿']


def new():
    return {'name': '临川县衙', 'fund': 80., 'opening_fund': 80., 'tax_rate': .09,
            'security': .5, 'sanitation': .45, 'offices': {k: None for k in OFFICES},
            'appointed': {}, 'last_appointments': {}, 'cases': [], 'last_event': None,
            'revenue': 0., 'expenses': 0., 'relief': 0., 'closed_cases': 0, 'succession_count': 0}


def establish(w):
    e = w.emit('government_founded', [], '临川县衙立档，统辖五地，初始库银80银用于治安、救济和公医。', .85,
               facts={'opening_fund': 80, 'jurisdiction': list(range(5)), 'initial_condition': True})
    w.government['last_event'] = e['id']
    appoint(w)


def appoint(w):
    g = w.government
    for office in OFFICES:
        current = w.npcs.get(g['offices'][office])
        if current and current['alive'] and w.month-g['appointed'].get(office, w.month) < 72:
            continue
        occupied = {i for k, i in g['offices'].items() if k != office and i}
        candidates = [n for n in w.npcs.values() if n['alive'] and 252 <= n['age_months'] <= 840 and n['id'] not in occupied]
        if not candidates:
            g['offices'][office] = None
            continue
        def merit(n):
            return n['reputation'] + n['contribution']*.15 + n['traits']['loyalty']*.25 + (
                n['skill']*.5 if office == '捕头' else n['traits']['compassion']*.2 if office == '县令' else n['traits']['caution']*.2)
        chosen = max(candidates, key=lambda n: (merit(n), -n['id']))
        old = current['id'] if current else g.get('departed', {}).pop(office, None)
        if current:
            current['office'] = None
        chosen['office'] = office
        g['offices'][office] = chosen['id']
        g['appointed'][office] = w.month
        people = [chosen['id']] + ([old] if old and w.npcs[old]['alive'] else [])
        e = w.emit('appointment', people, f"临川县衙选任{chosen['name']}担任{office}，任期六年。", .85,
                   [g['last_appointments'].get(office), chosen['last_event']],
                   {'office': office, 'holder': chosen['id'], 'previous': old, 'merit': round(merit(chosen), 2)})
        g['last_appointments'][office] = e['id']
        g['last_event'] = e['id']
        g['succession_count'] += int(old is not None and old != chosen['id'])


def tax(w, n, income):
    amount = round(max(0, income)*w.government['tax_rate'], 4)
    n['wealth'] -= amount
    w.government['fund'] += amount
    w.government['revenue'] += amount
    return amount


def grant(w, n, amount, purpose, cause=None):
    g = w.government
    amount = round(min(amount, g['fund']), 4)
    if amount <= 0:
        return 0
    g['fund'] -= amount
    g['expenses'] += amount
    g['relief'] += amount
    n['wealth'] += amount
    w.emit('government_relief', [n['id']], f"临川县衙拨{amount:.1f}银给{n['name']}，用于{purpose}。", .65,
           [cause, g['last_event']], {'receiver': n['id'], 'silver': amount, 'purpose': purpose,
                                   'treasury_after': round(g['fund'], 4)})
    return amount


def monthly(w):
    g = w.government
    appoint(w)
    g['security'] = max(.1, g['security']-.003)
    g['sanitation'] = max(.1, g['sanitation']-.002)
    needy = []
    for n in w.npcs.values():
        if not n['alive'] or n['wealth'] >= 2.3:
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
        grant(w, n, 4, '育幼口粮' if n['age_months'] < 192 else '贫困口粮', n['last_event'])
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
        g['expenses'] += 2
        restitution = min(offender['wealth'], case['amount'])
        offender['wealth'] -= restitution
        victim['wealth'] += restitution
        fine = min(offender['wealth'], 3)
        offender['wealth'] -= fine
        g['fund'] += fine
        g['revenue'] += fine
        offender['reputation'] = max(-100, offender['reputation']-4)
        victim['desires']['revenge'] = max(0, victim['desires']['revenge']-40)
        w.relation(victim, offender['id'])['affinity'] = min(100, w.relation(victim, offender['id'])['affinity']+15)
        e = w.emit('judgment', [victim['id'], offender['id'], g['offices']['捕头']],
                   f"临川县衙据已提交的侵吞证据判令{offender['name']}偿还{victim['name']}{restitution:.1f}银，另罚{fine:.1f}银。", .9,
                   [case['report_event'], case['crime_event']],
                   {'case': case['crime_event'], 'restitution': restitution, 'fine': fine, 'court_cost': 2})
        case.update({'status': '已判决', 'judgment': e['id'], 'restitution': restitution})
        g['closed_cases'] += 1
    holders = [i for i in g['offices'].values() if i and w.npcs[i]['alive']]
    if holders and g['fund'] >= len(holders):
        for i in holders:
            w.npcs[i]['wealth'] += 1
        g['fund'] -= len(holders)
        g['expenses'] += len(holders)
        w.emit('stipend', holders, '临川县衙向在任官吏各发放俸银1银。', .1,
               facts={'payments': {str(i): 1 for i in holders}, 'treasury_after': round(g['fund'], 4)})


def annual(w):
    g = w.government
    magistrate = w.npcs.get(g['offices']['县令'])
    if magistrate:
        t = magistrate['traits']
        g['tax_rate'] = round(max(.05, min(.15, .07+(100-t['compassion'])*.0006)), 4)
        budget = min(g['fund']*.2, max(12, len([n for n in w.npcs.values() if n['alive']])*.15))
        g['fund'] -= budget
        g['expenses'] += budget
        improvement = .12 * min(1, budget/max(12, sum(n['alive'] for n in w.npcs.values())*.15))
        g['security'] = min(.95, g['security']+improvement)
        g['sanitation'] = min(.95, g['sanitation']+improvement)
        w.emit('public_budget', [magistrate['id']],
               f"临川县衙议定来年税率{g['tax_rate']*100:.1f}%，支出{budget:.1f}银维护治安与公医。", .75,
               [g['last_appointments'].get('县令')], {'budget': budget, 'tax_rate': g['tax_rate'],
                                                    'security': g['security'], 'sanitation': g['sanitation']})
    if w.rng.random() < .24:
        place = w.rng.randrange(5)
        sick = [w.npcs[i] for i in w.residents[place] if w.rng.random() < .18 and not w.npcs[i]['illness']]
        if sick:
            e = w.emit('epidemic', [n['id'] for n in sick], f"{w.place_name(place)}流行时疫，{len(sick)}人染病。", .9,
                       facts={'place': place, 'patients': [n['id'] for n in sick]})
            for n in sick:
                infect(w, n, e['id'])


def report(w, n, crime_event):
    crime = w.store.event(crime_event)
    if not crime or crime['kind'] != 'betrayal' or crime['facts'].get('victim') != n['id']:
        raise ValueError('没有此人实际掌握的受害证据')
    e = w.emit('crime_report', [n['id']], f"{n['name']}携已知的侵吞证据向临川县衙报案。", .75,
               [crime_event], {'crime_event': crime_event, 'offender': crime['facts']['offender']})
    w.government['cases'].append({'crime_event': crime_event, 'report_event': e['id'], 'month': w.month,
                                  'offender': crime['facts']['offender'], 'victim': n['id'],
                                  'amount': crime['facts']['transfer'], 'status': '待审'})
    return e
