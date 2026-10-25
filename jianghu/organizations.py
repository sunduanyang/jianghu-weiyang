"""Local organizations: scoped merit, paid duties, teaching and accountable funds."""
from . import martial, relationships

LABELS = {'faction_task': '门中差事', 'faction_study': '同门请益', 'leave_faction': '辞别组织'}
STATE_FIELDS = ('leader', 'fund', 'opening_fund', 'account_since', 'revenue', 'expenses',
                'tasks_completed', 'leader_since', 'leader_review_since', 'vacancy_previous',
                'vacancy_event', 'last_event', 'records')


def initialize(w):
    for f in w.factions:
        # Legacy funds become an opening balance; no historical income is invented.
        defaults = dict(opening_fund=f['fund'], account_since=w.month, revenue=0., expenses=0.,
                        tasks_completed=0, leader_since=None, leader_review_since=w.month,
                        vacancy_previous=None, vacancy_event=None, last_event=None, records=[])
        for k, v in defaults.items():
            f.setdefault(k, v)


def prepare(n):
    n.setdefault('faction_since', None)
    n.setdefault('faction_rejoin_after', -1)
    n.setdefault('membership_history', [])


def rank(w, n):
    if n['faction'] is None:
        return None
    f = w.factions[n['faction']]
    if f['leader'] == n['id']:
        return {'title': {'sect': '掌门', 'guild': '会首', 'society': '社首'}[f['kind']], 'grade': 5}
    if n['contribution'] >= 35 and n['skill'] >= 45:
        return {'title': '长老' if f['kind'] == 'sect' else '理事', 'grade': 6}
    if n['contribution'] >= 12 and n['skill'] >= 25:
        return {'title': '执事', 'grade': 7}
    return {'title': '弟子' if f['kind'] == 'sect' else '成员', 'grade': 8}


def record(f, e):
    f['last_event'] = e['id']
    f['records'] = (f['records'] + [e['id']])[-24:]


def candidates(w, n, planning=False):
    if n['faction'] is None:
        return []
    f = w.factions[n['faction']]
    options = []
    def add(kind, detail, **extra):
        options.append(dict(id=f"{kind}:{f['id']}", kind=kind, target=f['id'],
                            label=f'{LABELS[kind]} · {detail}', **extra))
    add('leave_faction', f['name'])
    if f['place'] != n['place'] or n['age_months'] < 192:
        return options
    if n['health'] > 45 and f['fund'] >= 3:
        duty = {'sect': '巡山护民', 'guild': '照看货栈', 'society': '义仓助济'}[f['kind']]
        add('faction_task', f'{duty}，两周 / 酬银3 / 贡献2')
    if n['wealth'] >= 6 and n['contribution'] >= 5 and n['health'] > 30:
        tutors = [w.npcs[i] for i in w.residents.get(n['place'], []) if i != n['id']
                  and w.npcs[i]['faction'] == f['id'] and w.npcs[i]['age_months'] >= 192
                  and w.npcs[i]['health'] > 30 and w.npcs[i]['skill'] > n['skill'] + 8
                  and relationships.partner_available(w, w.npcs[i], planning)]
        if tutors:
            tutor = max(tutors, key=lambda q: (q['skill'], -q['id']))
            add('faction_study', f"向{tutor['name']}请益，2银 / 一周", tutor=tutor['id'])
    return options


def score(w, n, a):
    if a['kind'] == 'leave_faction':
        leader = w.factions[n['faction']]['leader']
        resentment = max(0, -n['relationships'].get(str(leader), {}).get('affinity', 0))
        return (-20 + (100 - n['traits']['loyalty']) * .15
                + (35 + min(30, resentment * .3) if leader != n['id'] and resentment > 25 else 0)
                + (15 if n['beliefs']['tradition'] < .3 else 0))
    if a['kind'] == 'faction_study':
        return 16 + n['traits']['ambition'] * .1 + (25 if n['goal']['kind'] == 'mastery' else 0)
    return (16 + n['traits']['loyalty'] * .1 + max(0, 30 - n['wealth']) * .3
            + (20 if n['goal']['kind'] == 'renown' else 0))


def execute(w, n, a, facts):
    f = w.factions[n['faction']]
    before = {k: n[k] for k in ('wealth', 'health', 'skill')}
    prior_rank = rank(w, n)
    people, causes = [n['id']], [n['last_event'], f['last_event']]
    kind = a['kind']
    facts['faction'] = f['id']
    if kind == 'faction_task':
        f['fund'] -= 3
        f['expenses'] += 3
        f['tasks_completed'] += 1
        n['wealth'] += 3
        n['contribution'] += 2
        n['health'] -= 1
        n['reputation'] = min(100, n['reputation'] + .4)
        g = w.governments[f['country']]
        axis = 'sanitation' if f['kind'] == 'society' else 'security'
        improvement = min(.004, .95 - g[axis])
        g[axis] += improvement
        facts.update(reward=3, contribution=2, public_axis=axis, improvement=improvement, country=f['country'])
        text = f"{n['name']}为{f['name']}办妥差事，获得库银3银、贡献2点，并协助地方{'公医' if axis == 'sanitation' else '治安'}。"
    elif kind == 'faction_study':
        q = w.npcs[a['tutor']]
        people.append(q['id'])
        causes.append(q['last_event'])
        n['wealth'] -= 2
        q['wealth'] += 1
        f['fund'] += 1
        f['revenue'] += 1
        gain = min(1.5, (q['skill'] - n['skill']) * .06)
        n['skill'] = min(100, n['skill'] + gain)
        w.link(n, q, 3, 3)
        facts.update(tutor=q['id'], payment=2, tutor_income=1, treasury_income=1,
                     transmission=martial.transmit(q, n), skill_gain=gain, partner_time_cost=1)
        text = f"{n['name']}向同门{q['name']}请益，付师资1银、入库1银，武学增长{gain:.2f}。"
    else:
        n['membership_history'].append({'faction': f['id'], 'since': n['faction_since'],
                                        'until': w.month, 'contribution': n['contribution']})
        facts.update(previous_contribution=n['contribution'], rejoin_after=w.month + 12)
        n['faction'], n['faction_since'], n['contribution'] = None, None, 0.
        n['faction_rejoin_after'] = w.month + 12
        n['plan'] = None
        if f['leader'] == n['id']:
            f['leader'] = None
            f['vacancy_previous'] = n['id']
        text = f"{n['name']}辞别{f['name']}，旧门贡献留入履历，一年后方可另择组织。"
    facts['actor_delta'] = {k: round(n[k] - v, 4) for k, v in before.items()}
    e = w.emit(kind, people, text, .65 if kind == 'leave_faction' else .45, causes, facts)
    record(f, e)
    if kind == 'faction_study':
        relationships.reserve_partner(w, q, a, e, '同门授业')
    if kind == 'leave_faction':
        n['membership_history'][-1]['event_id'] = e['id']
        if f['vacancy_previous'] == n['id']:
            f['vacancy_event'] = e['id']
        elect(w, f)
    else:
        promote(w, n, prior_rank, e['id'])
    return e


def promote(w, n, before, cause):
    after = rank(w, n)
    if before != after:
        f = w.factions[n['faction']]
        e = w.emit('faction_rank', [n['id']], f"{n['name']}凭门中贡献与本领，由{before['title']}升为{f['name']}{after['title']}。",
                   .7, [cause], {'faction': f['id'], 'before': before, 'after': after})
        record(f, e)


def elect(w, f, annual=False):
    members = [n for n in w.npcs.values() if n['alive'] and n['faction'] == f['id'] and n['age_months'] >= 192]
    old = f['leader'] if f['leader'] is not None else f['vacancy_previous']
    current = w.npcs.get(old)
    valid = current and current['alive'] and current['faction'] == f['id'] and current['age_months'] >= 192
    if valid and not annual:
        return
    if not members:
        f['leader'] = None
        return
    merit = lambda n: n['contribution'] + n['reputation'] * .3 + n['skill'] * .15
    leader = max(members, key=lambda n: (merit(n), -n['id']))
    if valid and (w.month - f['leader_review_since'] < 72
                  and merit(leader) < merit(current) + 15):
        return
    if old == leader['id']:
        return
    f['leader'], f['leader_since'], f['leader_review_since'] = leader['id'], w.month, w.month
    people = [n['id'] for n in members]
    if current and current['alive'] and old not in people:
        people.append(old)
    e = w.emit('promotion', people, f"{f['name']}按贡献、声望与本领推举{leader['name']}主持事务。", .8,
               [f['last_event'], f['vacancy_event'], leader['last_event']], {'faction': f['id'], 'leader': leader['id'], 'previous': old,
                                                       'merit': round(merit(leader), 4)})
    record(f, e)
    f['vacancy_previous'], f['vacancy_event'] = None, None


def monthly(w):
    for f in w.factions:
        elect(w, f)


def annual(w):
    for f in w.factions:
        elect(w, f, annual=True)
        needy = sorted((n for n in w.npcs.values() if n['alive'] and n['faction'] == f['id'] and n['wealth'] < 15),
                       key=lambda n: (n['wealth'], n['id']))
        for n in needy:
            if f['fund'] < 8:
                break
            f['fund'] -= 8
            f['expenses'] += 8
            n['wealth'] += 8
            people = list(dict.fromkeys([n['id']] + ([f['leader']] if f['leader'] else [])))
            e = w.emit('faction_aid', people, f"{f['name']}拨库银8银救助{n['name']}。", .65,
                       [n['last_event'], f['last_event']], {'faction': f['id'], 'receiver': n['id'], 'silver': 8})
            record(f, e)


def view(w, f):
    members = [n for n in w.npcs.values() if n['alive'] and n['faction'] == f['id']]
    return {**f, 'members': len(members), 'local_members': sum(n['place'] == f['place'] for n in members),
            'ranks': {title: sum(rank(w, n)['title'] == title for n in members)
                      for title in sorted({rank(w, n)['title'] for n in members})}}
