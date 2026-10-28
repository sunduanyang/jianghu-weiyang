"""长远心愿：reachable targets measured against a person's own surroundings.

Every target is set relative to where the person starts and what people around
them actually have, so a pursuit can really be fulfilled. A grudge names the
person it is held against and ends through one of several real outcomes: a
court judgment, an accepted apology, a won duel, the other's death, or slowly
letting go. Queries are pure; only monthly updates and events change state.
"""

VERSION = 2
MINIMUM_MONTHS = 12
# How a settled grudge is described.
RESOLUTIONS = {'judgment': '官府判决讨回公道', 'reconcile': '对方赔礼，旧怨得解', 'duel': '亲手讨回颜面',
               'death': '仇人已逝', 'forgiven': '情分回暖，不再记恨'}


def local_values(w, n, key):
    rows = [w.npcs[i][key] for i in w.residents.get(n['place'], [])
            if i != n['id'] and w.npcs[i]['alive'] and w.npcs[i]['age_months'] >= 192]
    return rows


def percentile(rows, q, default):
    if len(rows) < 4:
        return default
    rows = sorted(rows)
    return rows[min(len(rows)-1, int(q*len(rows)))]


def arts_known(w, n):
    from . import martial
    resolved = martial.resolve(w, n) if n['alive'] else None
    return len((resolved or {}).get('known', [])) + 0.


def grudge_target(w, n, cause):
    """The person this grudge is held against: the offender of the cause, else the most resented."""
    event = w.store.event(cause) if cause else None
    facts = (event or {}).get('facts', {})
    for key in ('offender', 'winner'):
        other = facts.get(key)
        if isinstance(other, int) and other != n['id'] and other in w.npcs:
            return other
    hostile = [(r['affinity'], int(k)) for k, r in n['relationships'].items()
               if r['affinity'] < -25 and int(k) in w.npcs and w.npcs[int(k)]['alive']]
    return min(hostile)[1] if hostile else None


def start_value(w, n, key):
    return {'wealth': n['wealth'], 'mastery': arts_known(w, n), 'renown': n['reputation'],
            'freedom': float(len(set(n.get('visited_places', [n['place']])))), 'belonging': 0., 'revenge': 0.}[key]


def target(w, n, key, start):
    if key == 'wealth':
        local = percentile(local_values(w, n, 'wealth'), .75, n['wealth']*1.5+20)
        return round(min(n['wealth']+400, max(n['wealth']*1.5+20, local)), 2)
    if key == 'mastery':
        return start + 2
    if key == 'renown':
        local = percentile(local_values(w, n, 'reputation'), .8, n['reputation']+15)
        return round(min(95., max(n['reputation']+15, local)), 2)
    if key == 'freedom':
        return start + 3
    return 1.


def prepare(w, n, goal, cause):
    """Fill version-2 fields on a new goal (called from World.set_goal)."""
    key = goal['kind']
    start = start_value(w, n, key)
    goal.update(version=VERSION, start=start, target=target(w, n, key, start),
                minimum_months=0 if key == 'revenge' else MINIMUM_MONTHS)
    if key == 'revenge':
        against = grudge_target(w, n, cause)
        goal.update(against=against, against_name=w.npcs[against]['name'] if against in w.npcs else None,
                    title='向'+w.npcs[against]['name']+'讨回公道' if against in w.npcs else goal['title'])
    return goal


def upgrade(w, n):
    """Old saves: re-base an unreachable legacy goal on today's situation, keeping its kind and start month."""
    g = n.get('goal')
    if not g or g.get('version') == VERSION:
        return False
    since, cause = g['since'], g.get('cause')
    prepare(w, n, g, cause)
    g['since'] = since
    g['upgraded'] = True
    return True


def value(w, n, g):
    key = g['kind']
    if key == 'belonging':
        friends = sum(r['affinity'] > 50 or r.get('sworn') for r in n['relationships'].values())
        return 1. if n['spouse'] or n.get('player_spouse') or friends >= 3 else 0.
    if key == 'revenge':
        return 1. if g.get('resolved') else 0.
    return start_value(w, n, key)


def progress(w, n, g):
    v = value(w, n, g)
    if g['kind'] in ('belonging', 'revenge'):
        return max(0., min(1., v))
    span = g['target'] - g.get('start', 0.)
    return max(0., min(1., (v - g.get('start', 0.)) / span)) if span > 0 else 1.


def resolve(w, n, how, event_id):
    g = n.get('goal')
    if g and g['kind'] == 'revenge' and not g.get('resolved'):
        g['resolved'] = dict(how=how, text=RESOLUTIONS[how], event_id=event_id, month=w.month)
        n['desires']['revenge'] = round(max(0., n['desires']['revenge'] - 35), 4)


def on_event(w, e):
    """Settle grudges from real outcomes. Called once per emitted event."""
    k, f = e['kind'], e.get('facts', {})
    def holder(pid, against):
        n = w.npcs.get(pid)
        g = n.get('goal') if n else None
        return n if n and n['alive'] and g and g['kind'] == 'revenge' and g.get('against') == against else None
    if k == 'judgment':
        n = holder(f.get('victim'), f.get('offender'))
        if n:
            resolve(w, n, 'judgment', e['id'])
    elif k == 'reconcile' and f.get('accepted'):
        n = holder(f.get('receiver'), f.get('giver'))
        if n:
            resolve(w, n, 'reconcile', e['id'])
    elif k == 'duel' and f.get('winner') is not None:
        n = holder(f['winner'], f.get('loser'))
        if n:
            resolve(w, n, 'duel', e['id'])
    elif k == 'death':
        dead = f.get('deceased')
        for i in e['people']:
            n = holder(i, dead)
            if n:
                resolve(w, n, 'death', e['id'])


def monthly(w, n):
    """Grudges fade; kind people let go sooner. Returns a forgiveness resolution when it happens."""
    t = n['traits']
    fade = .985 - t['kindness'] * .0002
    n['desires']['revenge'] = round(n['desires']['revenge'] * fade, 4)
    g = n.get('goal')
    if g and g['kind'] == 'revenge' and not g.get('resolved'):
        other = g.get('against')
        if other is not None and other in w.npcs and not w.npcs[other]['alive']:
            resolve(w, n, 'death', w.npcs[other]['last_event'])
        elif other is not None and n['relationships'].get(str(other), {}).get('affinity', 0) > -5:
            resolve(w, n, 'forgiven', n['last_event'])


def letting_go(n):
    """A grudge whose desire has faded well below other wishes can be put down."""
    g = n.get('goal')
    if not g or g['kind'] != 'revenge' or g.get('resolved'):
        return False
    others = max(v for k, v in n['desires'].items() if k != 'revenge')
    return n['desires']['revenge'] + 10 < others


def view(w, n):
    g = n.get('goal')
    if not g:
        return None
    row = dict(g)
    row['progress'] = round(progress(w, n, g), 4)
    if g['kind'] == 'revenge':
        row['note'] = g['resolved']['text'] if g.get('resolved') else (
            '仇怨未了，可经官府判决、对方赔礼、比武取胜、对方亡故或日久淡忘而了结')
    return row
