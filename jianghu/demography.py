"""Households, pregnancy and physical risks. All outcomes enter the shared journal."""
import math

DEATH_CAUSES = {'old_age': '衰老', 'illness': '疾病', 'injury': '伤重',
                'accident': '意外', 'starvation': '饥饿', 'disaster': '灾害', 'childbirth': '难产'}


def initialize(w, n):
    parents = [w.npcs[i] for i in n['parents']]
    defaults = {'generation': 1 + max((p.get('generation', 0) for p in parents), default=-1),
                'birth_month': w.month-n['age_months'], 'guardian': n['parents'][0] if parents else None,
                'pregnancy': None, 'family_intent_until': -1, 'illness': None, 'wound': None,
                'starvation_months': 0, 'death': None,
                'family_size': 3 + int(n['traits']['kindness'] > 60) + int((n['id']+w.seed) % 5 == 0),
                'orientation': '同性' if (n['id']*37+w.seed) % 100 < 5 else
                               '皆可' if (n['id']*37+w.seed) % 100 < 18 else '异性'}
    for key, value in defaults.items():
        n.setdefault(key, value)


def attracted(n, q):
    return n['orientation'] == '皆可' or (n['sex'] == q['sex']) == (n['orientation'] == '同性')


def fertile_pair(w, n):
    q = w.npcs.get(n['spouse'])
    if not q or not q['alive'] or q['spouse'] != n['id'] or q['sex'] == n['sex'] or q['place'] != n['place']:
        return False
    mother, father = (n, q) if n['sex'] == '女' else (q, n)
    children = sum(w.npcs[i]['alive'] for i in mother['children'])
    return (216 <= mother['age_months'] <= 516 and 216 <= father['age_months'] <= 780
            and not mother['pregnancy'] and mother['health'] > 35 and father['health'] > 35
            and w.month-mother['last_birth'] >= 24 and not w.close_kin(mother, father)
            and children < min(mother['family_size'], father['family_size'])
            and sum(len(ids) for ids in w.residents.values()) < w.population_limit)


def households(w):
    for child in list(w.npcs.values()):
        if not child['alive'] or child['age_months'] >= 192:
            continue
        current = w.npcs.get(child['guardian'])
        if current and current['alive']:
            continue
        relatives = set(child['parents'])
        for p in child['parents']:
            relatives.update(w.npcs[p]['parents'])
            relatives.update(w.npcs[p]['children'])
        candidates = [w.npcs[i] for i in relatives if w.npcs[i]['alive'] and w.npcs[i]['age_months'] >= 216
                      and w.npcs[i]['health'] >= 30
                      and w.npcs[i]['relationships'].get(str(child['id']), {}).get('affinity', 0) >= -25
                      and w.npcs[i]['traits']['kindness'] + w.npcs[i]['traits']['integrity'] >= 60]
        # Adult siblings can continue a household. A parent's sworn friend may
        # volunteer locally, provided there is a real bond and capacity to care.
        if not candidates:
            volunteers = set()
            for p in child['parents']:
                parent = w.npcs[p]
                for key, tie in parent['relationships'].items():
                    q = w.npcs[int(key)]
                    back = q['relationships'].get(str(p), {})
                    if (tie.get('sworn') and back.get('sworn') and back.get('affinity', 0) >= 20
                            and q['alive'] and q['place'] == child['place'] and q['age_months'] >= 216
                            and q['health'] >= 50 and q['wealth'] >= 15
                            and q['traits']['kindness'] >= 55 and q['traits']['integrity'] >= 50
                            and q['relationships'].get(str(child['id']), {}).get('affinity', 0) >= -10):
                        volunteers.add(q['id'])
            candidates = [w.npcs[i] for i in sorted(volunteers)]
        if candidates:
            guardian = min(candidates, key=lambda p: (p['place'] != child['place'], -p['wealth'], p['id']))
            old = child['guardian']
            child['guardian'] = guardian['id']
            origin = child['place']
            if origin != guardian['place']:
                w.residents[origin].remove(child['id'])
                child['place'] = guardian['place']
                w.residents[child['place']].append(child['id'])
            w.link(guardian, child, 8, 5, label='抚养')
            w.emit('guardianship', [child['id'], guardian['id']],
                   f"{guardian['name']}接续照料未成年的{child['name']}，共同生活于{w.place_name(child['place'])}。", .75,
                   [child['last_event'], guardian['last_event']], {'guardian': guardian['id'], 'previous': old})
        else:
            child['guardian'] = None


def follow_guardian(w, guardian, origin):
    children = [n for n in w.npcs.values() if n['alive'] and n['age_months'] < 192
                and n['guardian'] == guardian['id'] and n['place'] == origin]
    for child in children:
        w.residents[origin].remove(child['id'])
        child['place'] = guardian['place']
        w.residents[child['place']].append(child['id'])
    return [c['id'] for c in children]


def pregnancies(w):
    for mother in list(w.npcs.values()):
        if not mother['alive'] or mother['sex'] != '女':
            continue
        p = mother['pregnancy']
        if p and p['due'] <= w.month:
            deliver(w, mother)
        elif not p and fertile_pair(w, mother):
            father = w.npcs[mother['spouse']]
            # Parenthood is shaped by both people's values and resources, not a population quota.
            readiness = (mother['traits']['integrity']+father['traits']['integrity']+100)/200
            money = min(1., (mother['wealth']+father['wealth']+w.local_government(mother)['fund']*.02)/50)
            intent = 1.6 if max(mother['family_intent_until'], father['family_intent_until']) >= w.month else 1.
            if w.rng.random() < .065 * readiness * money * intent:
                event = w.emit('pregnancy', [mother['id'], father['id']],
                               f"{mother['name']}有孕，与{father['name']}开始安排抚养和家计。", .85,
                               [w.relation(mother, father['id'])['last_event']],
                               {'mother': mother['id'], 'father': father['id'], 'due': w.month+9})
                mother['pregnancy'] = {'father': father['id'], 'due': w.month+9, 'event_id': event['id']}


def deliver(w, mother):
    p = mother['pregnancy']
    father = w.npcs[p['father']]
    child = w.make_npc([mother['id'], father['id']], mother['place'])
    child['guardian'] = mother['id']
    w.residents[child['place']].append(child['id'])
    for parent in [mother, father]:
        parent['children'].append(child['id'])
        if parent['alive']:
            w.link(parent, child, 60, 45, label='亲子')
        parent['last_birth'] = w.month
    people = [mother['id'], child['id']] + ([father['id']] if father['alive'] else [])
    event = w.emit('birth', people,
                   f"{mother['name']}诞下孩子{child['name']}，这是乡籍第{child['generation']+1}代的新生命。", .95,
                   [p['event_id']], {'parents': child['parents'], 'child': child['id'], 'generation': child['generation']})
    mother['pregnancy'] = None
    w.set_goal(child, w.best_goal(child), event['id'], initial=True)
    w.metrics['births'] += 1
    w.government_grant(child, 6, '育幼补助', event['id'])
    if w.rng.random() < .006 * (2-w.local_government(mother)['sanitation']) * (1 + int(mother['health'] < 40)):
        mother['health'] = 0
        w.die(mother, 'childbirth', event['id'])


def infect(w, n, cause=None, severity=None):
    if not n['alive'] or n['illness']:
        return
    severity = severity if severity is not None else w.rng.uniform(15, 85)
    event = w.emit('illness', [n['id']], f"{n['name']}染病，需休养或求医。", .75,
                   [cause, n['last_event']], {'severity': round(severity, 2)})
    n['illness'] = {'severity': severity, 'remaining': w.rng.randint(3, 7), 'event_id': event['id']}


def health_step(w, n):
    age = n['age_months']/12
    hungry = n['monthly_settlement']['food_shortfall'] > 0
    n['starvation_months'] = n['starvation_months']+1 if hungry else max(0, n['starvation_months']-2)
    if n['health'] <= 0 and n['starvation_months'] >= 2:
        w.die(n, 'starvation', n['last_event'])
        return
    illness = n['illness']
    if illness:
        n['health'] = max(0., n['health']-illness['severity']*.1)
        vulnerability = 1 + max(0, age-55)/35 + int(age < 5)
        if w.rng.random() < illness['severity']/100 * .007 * vulnerability:
            n['health'] = 0
            w.die(n, 'illness', illness['event_id'])
            return
        illness['remaining'] -= 1
        if illness['remaining'] <= 0:
            w.emit('recovery', [n['id']], f"{n['name']}病势渐退，恢复日常生活。", .4, [illness['event_id']])
            n['illness'] = None
    elif w.rng.random() < .0015 * (1.6-w.local_government(n)['sanitation']) * (1+int(age < 3 or age > 65)):
        infect(w, n)
    wound = n['wound']
    if wound:
        if n['health'] < 20 and w.rng.random() < .08:
            n['health'] = 0
            w.die(n, 'injury', wound['event_id'])
            return
        wound['remaining'] -= 1
        if wound['remaining'] <= 0:
            n['wound'] = None
    if n['health'] <= 0:
        w.die(n, 'illness' if n['illness'] else 'injury',
              n['illness']['event_id'] if n['illness'] else n['last_event'])
    elif age >= 50 and w.rng.random() < min(.5, .00035 * math.exp((age-50)/10)):
        w.die(n, 'old_age', n['last_event'])


def accident(w, n, event):
    if not n['alive'] or event['kind'] not in ('work', 'travel') or w.rng.random() >= .0012:
        return
    damage = w.rng.uniform(8, 65) * (1.15-w.local_government(n)['security']*.3)
    catastrophic = w.rng.random() < .035*(1-w.local_government(n)['security']*.5)
    if catastrophic:
        damage = max(damage, n['health'])
    n['health'] = max(0., n['health']-damage)
    e = w.emit('accident', [n['id']], f"{n['name']}{'途中遭遇落石' if event['kind']=='travel' else '劳作时意外跌伤'}，体力损失{damage:.1f}。", .8,
               [event['id']], {'damage': damage, 'catastrophic': catastrophic})
    n['wound'] = {'remaining': 4, 'event_id': e['id']}
    if n['health'] <= 0:
        w.die(n, 'accident', e['id'])
