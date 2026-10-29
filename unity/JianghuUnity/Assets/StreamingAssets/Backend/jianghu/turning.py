"""人生转折：the moments worth a person's own thought, noted as they happen.

The model cannot think about everyone every week. Instead, when something that
turns a life happens to someone (a death in the family, a marriage, a
betrayal, being robbed or exposed, a judgment, a lost duel, a rejected
proposal, a birth, an appointment, a fulfilled wish), that person is marked
with a pending turning point. The scheduler gives the most significant ones
the next model slots, before ordinary rotation; those nearer the player rank a
little higher. The model then decides how the person faces it (a bounded
stance with small effects), may change their pursuit, wish and creed, and
leaves a line in their memory. Pending points expire after a few months: the
rules simply carry on for those the model never reached.
"""

VERSION = 1
LIFETIME = 3          # months a turning point waits for a thought
# kind -> {role: significance}; roles are worked out from the event's facts.
KINDS = {
    'death': {'bereaved': 1.},
    'marriage': {'self': .9},
    'birth': {'parent': .7},
    'betrayal': {'victim': .9, 'offender': .6},
    'theft_exposed': {'victim': .5, 'offender': .4},
    'extortion': {'victim': .6},
    'slander_exposed': {'victim': .6, 'offender': .5},
    'judgment': {'victim': .6, 'offender': .8},
    'duel': {'loser': .7, 'winner': .5},
    'rejection': {'self': .6},
    'appointment': {'self': .8},
    'promotion': {'self': .8},
    'leave_faction': {'self': .6},
    'goal_complete': {'self': .5},
    'grudge_released': {'self': .5},
}
LABELS = {'bereaved': '痛失亲人', 'self': '亲历', 'parent': '为人父母', 'victim': '受害', 'offender': '恶行败露',
          'loser': '比武落败', 'winner': '比武取胜'}
# How a person may face a turning point; each has a small, fixed effect.
STANCES = {
    'endure': ('咬牙挺过', '振作起来，日子照常过'),
    'grieve': ('消沉一阵', '一时提不起精神，想找人说说话'),
    'resent': ('记恨在心', '这口气咽不下，迟早要讨回来'),
    'forgive': ('放下宽恕', '不再计较，旧账一笔勾销'),
    'rejoice': ('欣喜振奋', '心里高兴，干劲更足'),
    'resolve': ('痛定思痛', '吸取教训，往后换个活法'),
    'shrug': ('随遇而安', '不放在心上'),
}
FITS = {'bereaved': ('grieve', 'endure', 'resolve'),
        'self': ('rejoice', 'endure', 'resolve', 'shrug', 'grieve'),
        'parent': ('rejoice', 'resolve', 'shrug'),
        'victim': ('resent', 'forgive', 'endure', 'resolve'),
        'offender': ('resolve', 'resent', 'shrug', 'grieve'),
        'loser': ('resent', 'resolve', 'endure', 'shrug'),
        'winner': ('rejoice', 'shrug', 'forgive')}


def roles(w, e):
    """[(person id, role)] for whom this event is a turning point."""
    kind, facts, people = e['kind'], e.get('facts', {}), e['people']
    if kind not in KINDS:
        return []
    if kind == 'death':
        return [(i, 'bereaved') for i in facts.get('heirs', []) if i != facts.get('deceased')]
    if kind in ('betrayal', 'theft_exposed', 'extortion', 'extortion_resisted', 'slander_exposed', 'judgment'):
        return [(i, r) for r, i in (('victim', facts.get('victim')), ('offender', facts.get('offender')))
                if isinstance(i, int) and r in KINDS[kind]]
    if kind == 'duel':
        return [(i, r) for r, i in (('loser', facts.get('loser')), ('winner', facts.get('winner'))) if isinstance(i, int)]
    if kind == 'birth':
        return [(i, 'parent') for i in facts.get('parents', [])]
    if kind == 'rejection':
        # A turned-down proposal of marriage, not every rebuffed courtship.
        return [(people[0], 'self')] if people and str(facts.get('action', '')).startswith('propose') else []
    if kind == 'marriage':
        return [(i, 'self') for i in people[:2]]
    return [(people[0], 'self')] if people else []


def note(w, e):
    """Called once per emitted event; marks the people for whom it turns a life."""
    if e['kind'] not in KINDS:
        return
    for i, role in roles(w, e):
        n = w.npcs.get(i)
        if not n or not n['alive'] or n['age_months'] < 192:
            continue
        rows = [t for t in n.get('turning', []) if w.month - t['month'] < LIFETIME and t['event_id'] != e['id']]
        rows.append(dict(event_id=e['id'], kind=e['kind'], role=role, month=w.month,
                         significance=KINDS[e['kind']][role], summary=e['summary']))
        # Keep the few that matter most.
        n['turning'] = sorted(rows, key=lambda t: (-t['significance'], -t['event_id']))[:2]


def pending(w, n):
    return [t for t in n.get('turning', []) if w.month - t['month'] < LIFETIME]


def priority(w, n, t):
    h = w.player.get('protagonist') or {}
    near = 0.
    if h.get('created'):
        if n['place'] == h.get('place'):
            near = .3
        else:
            from . import territory
            if territory.owner(w, n['place']) == territory.owner(w, h['place']):
                near = .15
    return t['significance'] + near - .1 * (w.month - t['month'])


def queue(w, exclude=(), limit=8):
    """Most pressing pending turning points: [(n, point)], one per person."""
    rows = []
    for n in w.npcs.values():
        # One attempt per person per month: an unanswered point waits for next month.
        if not n['alive'] or n['id'] in exclude or not n.get('turning') or n.get('last_review_attempt') == w.month:
            continue
        points = pending(w, n)
        if points:
            t = max(points, key=lambda t: (priority(w, n, t), t['event_id']))
            rows.append((priority(w, n, t), n['id'], n, t))
    return [(n, t) for _, _, n, t in sorted(rows, key=lambda r: (-r[0], r[1]))[:limit]]


def context(w, n, t):
    return dict(summary=t['summary'], kind=t['kind'], role=LABELS[t['role']], months_ago=w.month - t['month'],
                event_id=t['event_id'],
                stances=[dict(id=s, label=STANCES[s][0], meaning=STANCES[s][1]) for s in FITS[t['role']]])


def resolve(w, n, event_id, stance, note_text):
    """Apply a decided turning point: its stance's small effect, and the person's own line in memory."""
    from . import npc_memory
    t = next((t for t in n.get('turning', []) if t['event_id'] == event_id), None)
    if t is None:
        return None
    n['turning'] = [x for x in n['turning'] if x['event_id'] != event_id]
    mood, desires = n['mood'], n['desires']
    e = w.store.event(event_id) or {}
    other = (e.get('facts') or {}).get('offender') if t['role'] == 'victim' else \
        (e.get('facts') or {}).get('winner') if t['role'] == 'loser' else None
    if stance == 'endure':
        mood['joy'] = min(30., mood['joy'] + 2); n['review_urgency'] = min(n['review_urgency'], .5)
    elif stance == 'grieve':
        mood['joy'] = max(-30., mood['joy'] - 4); n['loneliness'] = min(100, n.get('loneliness', 0) + 10)
    elif stance == 'resent':
        desires['revenge'] = round(min(100., desires['revenge'] + 10), 4); mood['anger'] = min(100., mood['anger'] + 6)
    elif stance == 'forgive':
        desires['revenge'] = round(max(0., desires['revenge'] - 15), 4); mood['anger'] = max(0., mood['anger'] - 10)
        if isinstance(other, int) and other in w.npcs:
            r = w.relation(n, other); r['affinity'] = min(100., r['affinity'] + 5)
    elif stance == 'rejoice':
        mood['joy'] = min(30., mood['joy'] + 4)
    elif stance == 'resolve':
        n['review_urgency'] = max(n['review_urgency'], .9)
    memory = npc_memory.find(w, n, event_id)
    if memory and note_text:
        memory = dict(memory, interpretation=note_text[:180], ai=True)
        w.store.experience(n['id'], memory, w.month)
        current = next((m for m in n['memories'] if m['event_id'] == event_id), None)
        if current is not None:
            current.update(memory)
    n['turning_history'] = (n.get('turning_history', []) + [dict(
        event_id=event_id, month=w.month, kind=t['kind'], role=t['role'], summary=t['summary'],
        stance=stance, stance_label=STANCES[stance][0], note=note_text)])[-6:]
    w.metrics['turning_points_decided'] = w.metrics.get('turning_points_decided', 0) + 1
    return t


def view(w, n):
    return dict(pending=[dict(t, role_label=LABELS[t['role']]) for t in pending(w, n)],
                history=list(reversed(n.get('turning_history', []))))
