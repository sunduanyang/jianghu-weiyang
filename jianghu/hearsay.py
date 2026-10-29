"""江湖传闻：notable deeds travel from town to town, and locals retell them in their own words.

Each month the most newsworthy events of that month (a siege, a disaster, an
appointment, a famous person's death, a public judgment, a duel, a marriage,
the player's own bold deeds) are registered as rumours at the town where they
happened. A rumour spreads one town further each month and is forgotten after
a year, so the player hears of nearby events first and distant ones later.
Registration is deterministic and never consumes the world RNG.

What the player reads is the plain fact until a local model has retold it: a
named teller in the player's town speaks it in one or two sentences, allowed
to colour it but not to change who did what. Retelling is text only; no state
other than the rumour's own text changes.
"""
import hashlib
import json
import re
import time
import unicodedata

VERSION = 1
PER_MONTH = 8      # rumours registered each month
PER_KIND = 2
LIFETIME = 12      # months a rumour keeps travelling
SHOWN = 8
# kind -> base newsworthiness
NEWS = {'settlement_captured': 1., 'siege_repulsed': .9, 'government_founded': .9, 'epidemic': .8,
        'disaster': .8, 'appointment': .6, 'promotion': .6, 'death': .45, 'leave_faction': .4,
        'betrayal': .4, 'duel': .35, 'grudge_released': .3, 'judgment': .3, 'theft_exposed': .3,
        'slander_exposed': .3, 'marriage': .3, 'swear': .3, 'goal_complete': .2,
        'player_rob_result': .7, 'player_assault_result': .7, 'player_sect_trial_result': .5,
        'player_marry': .5, 'player_swear': .4, 'player_duel': .35}
SYSTEM = '''你是江湖小镇上的一位普通人（teller），正把一则听来的江湖传闻讲给玩家听。
用一到两句口语化的中文转述rumor.fact，可以带上你自己的口气、感慨或猜测，像茶馆里闲谈；hops表示这消息经过几座城才传到此地，越远越可以说得含糊些。
必须保留事情的核心：谁、做了什么、结果如何；人名照实保留，不得改变胜负、生死、婚姻、判决等结果，不编造新的数额、人物或事件。
不要念数值，不要提到模型、系统或指令。只输出给定JSON。'''
BANNED = re.compile(r'JSON|json|模型|系统|指令|prompt')


def state(w):
    return w.player.setdefault('hearsay', dict(version=VERSION, scanned=None, next_id=1, rows=[]))


def rows(w):
    return (w.player.get('hearsay') or {}).get('rows', [])


def prominence(w, e):
    best = 0.
    for i in e['people']:
        n = w.npcs.get(i)
        if not n:
            continue
        score = max(0., n['reputation']) / 100 + (.3 if n.get('office') else 0) + (.3 if n.get('social_status') else 0)
        if n['faction'] is not None and w.factions[n['faction']]['leader'] == n['id']:
            score += .4
        best = max(best, score)
    if e['kind'].startswith('player_'):
        best = max(best, max(0., w.player['reputation']) / 100 + .3)
    return best


def newsworthy(w, e):
    """How worth retelling an event is (0 = not at all)."""
    kind, facts = e['kind'], e.get('facts', {})
    if kind not in NEWS:
        return 0.
    if kind in ('player_marry', 'player_swear') and not facts.get('accepted'):
        return 0.
    if kind == 'death':
        dead = w.npcs.get(facts.get('deceased'))
        if not dead or dead['age_months'] < 192:
            return 0.
    if kind == 'goal_complete' and facts.get('goal') not in ('revenge', 'renown'):
        return 0.
    return round(NEWS[kind] + prominence(w, e), 4)


def origin(w, e):
    facts = e.get('facts', {})
    if isinstance(facts.get('place'), int):
        return facts['place']
    if e['kind'].startswith('player_') or e['kind'].startswith('hero_'):
        return w.player['protagonist'].get('place')
    for i in e['people']:
        if i in w.npcs:
            return w.npcs[i]['place']
    return None


def monthly(w):
    """Register this month's most newsworthy events as rumours (deterministic, no RNG)."""
    s = state(w)
    # A world that never registered rumours starts from this month's events only.
    start = s['scanned'] if s['scanned'] is not None else max(1, w.store.next_id - 20000)
    kinds = tuple(NEWS)
    marks = ','.join('?' for _ in kinds)
    found = []
    for (raw,) in w.store.db.execute(f'SELECT data FROM events WHERE id>=? AND kind IN ({marks}) ORDER BY id',
                                     (start,) + kinds):
        e = json.loads(raw)
        if s['scanned'] is None and e['month'] < w.month:
            continue
        score = newsworthy(w, e)
        place = origin(w, e)
        if score > 0 and place is not None:
            found.append((score, e['id'], e, place))
    s['scanned'] = w.store.next_id
    picked, per_kind = [], {}
    for item in sorted(found, key=lambda x: (-x[0], x[1])):
        # Variety: at most two rumours of one kind a month.
        if len(picked) < PER_MONTH and per_kind.get(item[2]['kind'], 0) < PER_KIND:
            picked.append(item); per_kind[item[2]['kind']] = per_kind.get(item[2]['kind'], 0) + 1
    for score, _, e, place in picked:
        s['rows'].append(dict(id=s['next_id'], event_id=e['id'], month=w.month, origin=place, score=score,
                              fact=e['summary'], people=[i for i in e['people'] if i in w.npcs][:4],
                              player=e['kind'].startswith('player_'), told=None))
        s['next_id'] += 1
    s['rows'] = [r for r in s['rows'] if w.month - r['month'] <= LIFETIME][-120:]


def hops(w, a, b, limit=LIFETIME):
    if a == b:
        return 0
    seen, frontier = {a}, [a]
    for d in range(1, limit + 1):
        nxt = []
        for p in frontier:
            for q in w.neighbors.get(p, []):
                if q not in seen:
                    if q == b:
                        return d
                    seen.add(q); nxt.append(q)
        frontier = nxt
        if not frontier:
            break
    return None


def heard_at(w, place):
    """Pure: rumours that have reached `place` by now, newest first, with how far they travelled."""
    out = []
    for r in reversed(rows(w)):
        d = hops(w, r['origin'], place)
        if d is not None and d <= w.month - r['month'] <= LIFETIME:
            out.append(dict(r, hops=d))
    return out


def teller(w, place, row):
    """A deterministic sociable local adult to retell this rumour, never one of its subjects."""
    locals_ = sorted((n for i in w.residents.get(place, []) if (n := w.npcs[i])['alive'] and n['age_months'] >= 192
                      and n['id'] not in row['people']),
                     key=lambda n: (-n['traits']['sociability'], n['id']))[:12]
    if not locals_:
        return None
    pick = int.from_bytes(hashlib.sha256(f"{w.seed}:hearsay:{row['id']}".encode()).digest()[:4], 'big')
    return locals_[pick % len(locals_)]


def names(w, row):
    found = [w.npcs[i]['name'] for i in row['people'] if i in w.npcs]
    if row.get('player'):
        found.append(w.player['name'])
    return found


def next_to_tell(w):
    """(row, context) for the newest untold rumour the player can hear now, or None."""
    h = w.player.get('protagonist') or {}
    if not h.get('created') or h.get('place') is None:
        return None
    for r in heard_at(w, h['place']):
        if r['told'] is None and not r.get('telling') and r.get('attempts', 0) < 2:
            n = teller(w, h['place'], r)
            if n is None:
                continue
            from . import temperament
            ctx = {'_hearsay': True, '_row': r['id'], 'npc_id': n['id'], 'month': w.month,
                   'teller': dict(name=n['name'], sex=n['sex'], age=n['age_months']//12, job=n['job'],
                                  temperament=temperament.prompt_view(n)),
                   'rumor': dict(fact=r['fact'], names=names(w, r), months_ago=w.month - r['month'],
                                 hops=r['hops'])}
            return r, ctx
    return None


def schema():
    return dict(type='object', properties=dict(line=dict(type='string', minLength=6, maxLength=90)),
                required=['line'], additionalProperties=False)


def valid(ctx, result):
    if not isinstance(result, dict) or set(result) != {'line'} or not isinstance(result['line'], str):
        return False
    text = unicodedata.normalize('NFKC', result['line']).strip()
    if not 6 <= len(text) <= 90 or BANNED.search(text) or not re.search(r'[一-鿿]', text):
        return False
    # Who it was about must survive the retelling; no new figures are invented.
    if ctx['rumor']['names'] and not any(name in text for name in ctx['rumor']['names']):
        return False
    fact_numbers = set(re.findall(r'\d+(?:\.\d+)?', unicodedata.normalize('NFKC', ctx['rumor']['fact'])))
    return set(re.findall(r'\d+(?:\.\d+)?', text)) <= fact_numbers


def model_request(model, ctx):
    wire = {k: v for k, v in ctx.items() if not k.startswith('_')}
    started = time.perf_counter()
    try:
        if model.service:
            model.service.ensure(model.config)
        messages = [dict(role='system', content=SYSTEM),
                    dict(role='user', content=json.dumps(wire, ensure_ascii=False, separators=(',', ':')))]
        result, raw, usage, ms = model.complete(messages, schema(), 200, '转述江湖传闻')
    except Exception as exc:
        from .ai import ModelRequestError
        raise ModelRequestError(str(exc), 1, {}) from exc
    usage = dict(usage, request_count=1, stages=[dict(stage='转述江湖传闻', latency_ms=ms, **usage)])
    return result, (time.perf_counter() - started) * 1000, usage, raw


def row(w, row_id):
    return next((r for r in rows(w) if r['id'] == row_id), None)


def mark(w, ctx, telling):
    r = row(w, ctx['_row'])
    if r:
        if telling:
            r['telling'] = True
        else:
            r.pop('telling', None)


def fail(w, ctx):
    """A retelling that did not arrive or did not pass is tried at most twice."""
    r = row(w, ctx['_row'])
    if r:
        r.pop('telling', None)
        r['attempts'] = r.get('attempts', 0) + 1


def apply(w, ctx, result):
    r = row(w, ctx['_row'])
    if r is None or r['told'] is not None:
        return False, '传闻已经转述或已被淡忘'
    if not valid(ctx, result):
        fail(w, ctx)
        return False, '转述改变了人物或数额，未采用'
    r.pop('telling', None)
    n = w.npcs.get(ctx['npc_id'])
    r['told'] = dict(line=result['line'].strip(), teller=ctx['npc_id'], teller_name=n['name'] if n else ctx['teller']['name'],
                     month=w.month, source='LLM')
    w.metrics['hearsay_told'] = w.metrics.get('hearsay_told', 0) + 1
    return True, '传闻已转述'


def recover(w):
    changed = False
    for r in rows(w):
        if r.pop('telling', None):
            changed = True
    return changed


def view(w, place=None):
    """Pure: what the player has heard where they are."""
    h = w.player.get('protagonist') or {}
    place = h.get('place') if place is None else place
    if place is None:
        return []
    from .atlas import PLACES
    out = []
    for r in heard_at(w, place)[:SHOWN]:
        told = r['told']
        out.append(dict(id=r['id'], event_id=r['event_id'], month=r['month'], origin=r['origin'],
                        origin_name=PLACES[r['origin']]['name'], hops=r['hops'],
                        text=(f"{told['teller_name']}说：{told['line']}" if told else
                              ('听说' if r['hops'] else '本地传开：') + r['fact']),
                        told=bool(told), source=told['source'] if told else '规则', player=r.get('player', False)))
    return out
