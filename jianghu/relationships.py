"""Directional relationships, voluntary reconciliation and shared social time."""
from . import social

LABELS = {'reconcile': '赔礼和解', 'swear': '结义'}


def label(w, n, other, r):
    q = w.npcs[other]
    durable = ('夫妻' if n['spouse'] == other else '师徒' if n['mentor'] == other or q['mentor'] == n['id']
               else '亲子' if other in n['parents'] + n['children'] else '义亲' if r.get('sworn') else None)
    return ((durable + '·反目' if r['affinity'] < -25 else durable) if durable else
            '仇怨' if r['affinity'] < -25 else '挚友' if r['affinity'] > 55 else
            '友人' if r['affinity'] > 20 else '相识')


def prepare(w, n):
    for key, r in n['relationships'].items():
        # Older saves start tracking contact now, without inventing a past meeting.
        r.setdefault('last_contact', None)
        r.setdefault('contact_since', w.month)
        r.setdefault('sworn', False)


def touch(w, a, b):
    for p, q in ((a, b), (b, a)):
        r = w.relation(p, q['id'])
        r['last_contact'] = w.month
        r['label'] = label(w, p, q['id'], r)


def monthly(w):
    for n in w.npcs.values():
        if not n['alive']:
            continue
        for key, r in n['relationships'].items():
            other = int(key)
            if other not in w.npcs or not w.npcs[other]['alive']:
                continue
            last_contact = r.get('last_contact')
            if w.month - (last_contact if last_contact is not None else r.get('contact_since', w.month)) > 6:
                # Only positive feelings cool. Absence does not absolve a grievance.
                r['affinity'] = round(max(0, r['affinity'] - .35), 4) if r['affinity'] > 0 else r['affinity']
                if n['spouse'] != other:
                    r['romance'] = round(max(0, r['romance'] - .5), 4)
            r['label'] = label(w, n, other, r)


def partner_available(w, q, planning=False):
    return q['alive'] and (planning or (q['time_left'] >= 1 and social.available(w, q)))


def candidates(w, n, visible, planning=False):
    options = []
    for i in visible:
        q = w.npcs[i]
        r = n['relationships'].get(str(i))
        if not r or min(n['age_months'], q['age_months']) < 192 or not partner_available(w, q, planning):
            continue
        kinds = []
        if n['wealth'] >= 6 and (r['affinity'] < 5 or r['trust'] < 0):
            kinds.append('reconcile')
        if (r['affinity'] >= 55 and r['trust'] >= 35 and not r.get('sworn')
                and not w.close_kin(n, q) and n['spouse'] != i and n['mentor'] != i and q['mentor'] != n['id']):
            kinds.append('swear')
        for kind in kinds:
            options.append({'id': f'{kind}:{i}', 'kind': kind, 'target': i,
                            'label': f"{LABELS[kind]} · {q['name']}" + ('（赠礼2银）' if kind == 'reconcile' else '')})
    return options


def score(w, n, a):
    r = n['relationships'][str(a['target'])]
    if a['kind'] == 'swear':
        return 12 + n['traits']['loyalty'] * .16 + (18 if n['goal']['kind'] == 'belonging' else 0)
    return (n['traits']['honesty'] * .16 + n['traits']['compassion'] * .14
            - n['mood']['anger'] * .25 + (15 if r.get('sworn') or r['label'].startswith(('夫妻', '亲子', '师徒')) else 0))


def reserve_partner(w, q, a, event, role):
    q['time_left'] -= 1
    q['monthly_actions'].append({**a, 'month': w.month, 'week': w.phase, 'event_id': event['id'],
                                 'source': '回应邀约', 'time_cost': 1, 'role': role})
    q['loneliness'] = max(0, q['loneliness'] - 12)


def execute(w, n, a, facts):
    q = w.npcs[a['target']]
    reciprocal = w.relation(q, n['id'])
    before = {key: n[key] for key in ('wealth', 'health', 'skill')}
    causes = [n['last_event'], q['last_event']]
    kind = a['kind']
    if kind == 'reconcile':
        n['wealth'] -= 2
        q['wealth'] += 2
        accepted = reciprocal['affinity'] > -75 and reciprocal['trust'] > -70 and q['mood']['anger'] < 65
        if accepted:
            w.link(n, q, 12, 6)
            q['mood']['anger'] = max(0, q['mood']['anger'] - 8)
        else:
            w.know(n, q)
            w.know(q, n)
        facts.update(transfer=2, giver=n['id'], receiver=q['id'])
        text = f"{n['name']}向{q['name']}赔礼赠2银，" + ('对方愿意缓和旧怨，信任仍待往来重建。' if accepted else '对方收下赔礼，暂不愿和解。')
    else:
        accepted = reciprocal['affinity'] >= 50 and reciprocal['trust'] >= 30
        if accepted:
            for p, other in ((n, q), (q, n)):
                w.relation(p, other['id'])['sworn'] = True
            w.link(n, q, 5, 8)
        text = f"{n['name']}邀{q['name']}结义，" + ('双方自愿结为义亲，相约守望相助。' if accepted else '对方认为交情尚浅，婉拒结义。')
    touch(w, n, q)
    # Each side has a cooldown: the reverse invitation cannot bypass it.
    for p, other in ((n, q), (q, n)):
        p['cooldowns'][f"{kind}:{other['id']}"] = w.month + (6 if kind == 'reconcile' else 12)
    facts.update(accepted=accepted, partner=q['id'], partner_time_cost=1,
                 actor_delta={key: round(n[key] - v, 4) for key, v in before.items()})
    e = w.emit(kind, [n['id'], q['id']], text, .8 if kind == 'swear' and accepted else .5, causes, facts)
    reserve_partner(w, q, a, e, LABELS[kind])
    return e


def counts(w):
    friends, feuds, sworn = set(), set(), set()
    for n in w.npcs.values():
        if not n['alive']:
            continue
        for key, r in n['relationships'].items():
            i = int(key)
            q = w.npcs.get(i)
            if not q or not q['alive'] or i == n['id']:
                continue
            pair = tuple(sorted((n['id'], i)))
            back = q['relationships'].get(str(n['id']), {})
            if r['affinity'] > 25 and back.get('affinity', 0) > 25:
                friends.add(pair)
            if r['affinity'] < -25:
                feuds.add(pair)
            if r.get('sworn') and back.get('sworn'):
                sworn.add(pair)
    return {'friendships': len(friends), 'feuds': len(feuds), 'sworn_bonds': len(sworn)}
