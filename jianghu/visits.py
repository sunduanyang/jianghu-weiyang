"""人物来访：people who know the player come to them, for reasons that really exist.

At the end of each month the rules look at the people in the player's town who
have a tie to the player and find real reasons to come by: a friend short of
money asks for help, someone the player helped comes to thank them, a close
friend invites them for a drink, someone who holds a grudge comes to confront
them, a friend warns them about a person who resents them, the player's master
calls for them. At most two visits a month; each waits until the end of the
following month. The local model only writes what the visitor says on arrival,
in their own voice and from their own life; the player answers from fixed
choices whose effects the rules settle. Generation never consumes the RNG.
"""
from copy import deepcopy
import json
import re
import time
import unicodedata

from . import persona, temperament

VERSION = 1
PER_MONTH = 2
PERSON_GAP = 3   # months before the same person comes again
KIND_GAP = 6     # months before the same person comes for the same reason
KINDS = {'ask_help': '登门求助', 'thanks': '登门道谢', 'invite': '相邀叙旧', 'confront': '上门寻衅',
         'warn': '好意示警', 'summon': '师父召见'}
SYSTEM = '''你是武侠世界里的一个具体人物，此刻主动来找玩家。reason写明你来的真实缘由，choices是玩家可以作出的回应。
用一到两句话开口，说明来意，口吻按voice：自称用self_ref，称呼玩家用address_player，按manner说话，偶尔用一句phrases里的口头禅。
可以带上own_life里你自己的处境和心情，让人听得出你是谁；只用own_life与reason里出现的人名，不编造人物、往事和数额。
寻衅时可以不客气，但不得动手或宣称已经发生了什么；求助时说清所求，不要替玩家答应。只输出给定JSON。'''
BANNED = re.compile(r'JSON|json|模型|系统|指令|prompt|好感度|选项|choice')


def state(w):
    return w.player.setdefault('visits', dict(version=VERSION, next_id=1, rows=[], last_month=None))


def rows(w):
    return (w.player.get('visits') or {}).get('rows', [])


def tie(w, n):
    return w.player['ties'].get(str(n['id']), {})


def recent_help(w, n):
    """The player's latest settled kindness toward this person in the last two months, if any."""
    from . import player_social
    for f in reversed(player_social.state(w)['favors']):
        if f['npc_id'] == n['id'] and f['owed_by'] == n['id'] and w.month - f['month'] <= 2:
            return f.get('event_id')
    r = tie(w, n)
    for i in reversed(r.get('encounter_records', [])[-6:]):
        e = w.store.event(i)
        if e and e['kind'] in ('item_gift', 'player_gift') and w.month - e['month'] <= 2:
            return e['id']
    return None


def reasons(w, n):
    """Real reasons this person has to come to the player now: [(priority, kind, reason, causes)]."""
    from . import sect_life
    r = tie(w, n); h = w.player['protagonist']
    out = []
    adult = n['age_months'] >= 192 and h.get('age_months', 0) >= 192
    if not adult or n['health'] < 30:
        return out
    if (sect_life.master(w) or {}).get('id') == n['id'] and w.month % 3 == 0:
        out.append((5, 'summon', f"作为师父，召{w.player['name']}前来考校功课、叮嘱门中事务", [r.get('last_event')]))
    if r.get('affinity', 0) <= -30 and n['traits']['boldness'] >= 55 and n['mood']['anger'] >= 10:
        out.append((4, 'confront', f"对{w.player['name']}怀有旧怨，忍不住上门理论", [r.get('last_event')]))
    helped = recent_help(w, n)
    if helped and r.get('affinity', 0) > 0:
        out.append((3, 'thanks', f"记着{w.player['name']}前些时候的帮忙，特来道谢", [helped]))
    if r.get('affinity', 0) >= 20 and n['wealth'] < 12 and not r.get('help_requested_month') == w.month:
        out.append((3, 'ask_help', '手头拮据，想向熟人借四两银子周转', [n['last_event']]))
    if r.get('affinity', 0) >= 30:
        foes = []
        for key, rel in n['relationships'].items():
            q = w.npcs.get(int(key))
            if (q and q['alive'] and q['place'] == n['place'] and rel.get('affinity', 0) > -25
                    and w.player['ties'].get(str(q['id']), {}).get('affinity', 0) <= -30):
                foes.append(q)
        if foes:
            foe = min(foes, key=lambda q: (w.player['ties'][str(q['id'])]['affinity'], q['id']))
            out.append((2, 'warn', f"听说{foe['name']}对{w.player['name']}怀恨在心，来提个醒", [foe['last_event']],
                        foe['name']))
        if r.get('affinity', 0) >= 40:
            out.append((1, 'invite', f"与{w.player['name']}交好，想约一同小酌叙旧", [r.get('last_event')]))
    return out


def choices(w, n, kind):
    h = w.player['protagonist']; money = w.player['wealth']
    rows_ = {
        'ask_help': [dict(id='give', label='借他四两', effect='你交付4银，对方记下一份人情', silver=-4, affinity=4, trust=3,
                          enabled=money >= 4),
                     dict(id='decline', label='婉言推辞', effect='不借，对方有些失望', affinity=-2, trust=0)],
        'thanks': [dict(id='accept', label='受了这份谢意', effect='交情更进一步', affinity=2, trust=2),
                   dict(id='modest', label='谦让不受', effect='对方更敬重你几分', affinity=1, trust=3)],
        'invite': [dict(id='go', label='应邀前往（1行动力）', effect='同饮叙旧，交情加深', affinity=4, trust=2, points=1,
                        enabled=h['points'] >= 1),
                   dict(id='decline', label='改日再聚', effect='对方略感失落', affinity=-1, trust=0)],
        'confront': [dict(id='apologize', label='赔礼二两', effect='你交付2银赔礼，怨气稍解', silver=-2, affinity=6, trust=3,
                          enabled=money >= 2),
                     dict(id='calm', label='好言相劝', effect='各退一步，怨气略消', affinity=2, trust=0),
                     dict(id='defy', label='针锋相对', effect='不欢而散，怨气更深', affinity=-4, trust=-2, anger=6)],
        'warn': [dict(id='thank', label='谢他提醒', effect='记下这份好意', affinity=2, trust=2),
                 dict(id='shrug', label='不以为意', effect='对方觉得你不领情', affinity=-1, trust=0)],
        'summon': [dict(id='attend', label='恭敬听训（1行动力）', effect='师徒情分加深', affinity=3, trust=2, points=1,
                        enabled=h['points'] >= 1),
                   dict(id='excuse', label='推说有事', effect='师父有些不悦', affinity=-2, trust=-1)]}[kind]
    for c in rows_:
        c.setdefault('enabled', True)
    return rows_


def pending(w, npc_id=None):
    return [r for r in rows(w) if r['status'] == 'pending' and (npc_id is None or r['npc_id'] == npc_id)]


def monthly(w):
    """Register this month's visits for the player's town (deterministic, no RNG)."""
    h = w.player.get('protagonist') or {}
    if not h.get('created') or h.get('place') is None:
        return []
    s = state(w)
    for r in s['rows']:
        if r['status'] == 'pending' and w.month - r['month'] > 1:
            r['status'] = 'expired'
    if s['last_month'] == w.month:
        return []
    s['last_month'] = w.month
    busy = {r['npc_id'] for r in pending(w)} | {r['npc_id'] for r in s['rows'] if w.month - r['month'] < PERSON_GAP}
    recent_kinds = {(r['npc_id'], r['kind']) for r in s['rows'] if w.month - r['month'] < KIND_GAP}
    found = []
    for i in w.residents.get(h['place'], []):
        n = w.npcs.get(i)
        if not n or not n['alive'] or n['id'] in busy or str(n['id']) not in w.player['ties']:
            continue
        for row in reasons(w, n):
            if (n['id'], row[1]) not in recent_kinds:
                found.append((row[0], n['id'], n, row))
    made, seen = [], set()
    for priority, _, n, row in sorted(found, key=lambda x: (-x[0], x[1])):
        if len(made) >= PER_MONTH:
            break
        if n['id'] in seen:
            continue
        seen.add(n['id'])
        _, kind, reason, causes = row[:4]
        names = [row[4]] if len(row) > 4 else []
        e = w.emit('npc_visit', [n['id']], f"{n['name']}{KINDS[kind]}，来找{w.player['name']}：{reason}。", .55,
                   [c for c in causes if c], dict(kind=kind, reason=reason, visitor=n['id']))
        visit = dict(id=s['next_id'], npc_id=n['id'], npc_name=n['name'], kind=kind, label=KINDS[kind],
                     reason=reason, month=w.month, event_id=e['id'], status='pending', names=names,
                     opening=f"{n['name']}{KINDS[kind]}：{reason}。", opening_source='规则')
        s['next_id'] += 1
        s['rows'] = (s['rows'] + [visit])[-60:]
        made.append(visit)
    return made


def recover(w):
    changed = False
    for r in rows(w):
        if r.pop('writing', None):
            changed = True
    return changed


def row(w, visit_id):
    return next((r for r in rows(w) if r['id'] == visit_id), None)


def context(w, visit):
    n = w.npcs[visit['npc_id']]
    life = persona.life(w, n)
    allowed = set(life['names']) | set(visit['names']) | {n['name'], w.player['name']}
    return {'_visit': True, '_row': visit['id'], 'npc_id': n['id'], 'month': w.month,
            'self': dict(name=n['name'], sex=n['sex'], age=n['age_months'] // 12, job=n['job'],
                         temperament=temperament.prompt_view(n), mood={k: round(v) for k, v in n['mood'].items()}),
            'player': dict(name=w.player['name'], sex=w.player['protagonist'].get('sex')),
            'visit': dict(kind=visit['label'], reason=visit['reason']),
            'choices': [dict(label=c['label'], effect=c['effect']) for c in choices(w, n, visit['kind'])],
            'own_life': life, 'voice': persona.voice(w, n),
            'impression': w.player['ties'].get(str(n['id']), {}).get('impression'),
            '_name_guard': [x for x in persona.name_guard(w, n, allowed) if x not in visit['reason']]}


def schema():
    return dict(type='object', properties=dict(opening=dict(type='string', minLength=4, maxLength=90)),
                required=['opening'], additionalProperties=False)


def valid(ctx, result):
    if not isinstance(result, dict) or set(result) != {'opening'} or not isinstance(result['opening'], str):
        return False
    text = unicodedata.normalize('NFKC', result['opening']).strip()
    if not 4 <= len(text) <= 90 or BANNED.search(text) or not re.search(r'[一-鿿]', text):
        return False
    if persona.invents(text, ctx.get('_name_guard', [])):
        return False
    allowed = {'2', '4', '二', '四', '两'}
    return all(d in allowed for d in re.findall(r'\d+', text))


def model_request(model, ctx):
    wire = {k: v for k, v in ctx.items() if not k.startswith('_')}
    started = time.perf_counter()
    try:
        if model.service:
            model.service.ensure(model.config)
        messages = [dict(role='system', content=SYSTEM),
                    dict(role='user', content=json.dumps(wire, ensure_ascii=False, separators=(',', ':')))]
        result, raw, usage, ms = model.complete(messages, schema(), 220, '来访开口')
    except Exception as exc:
        from .ai import ModelRequestError
        raise ModelRequestError(str(exc), 1, {}) from exc
    usage = dict(usage, request_count=1, stages=[dict(stage='来访开口', latency_ms=ms, **usage)])
    return result, (time.perf_counter() - started) * 1000, usage, raw


def apply(w, ctx, result):
    visit = row(w, ctx['_row'])
    if not visit or visit['status'] != 'pending':
        return False, '这次来访已经结束'
    visit.pop('writing', None)
    if not valid(ctx, result):
        visit['attempts'] = visit.get('attempts', 0) + 1
        return False, '开口的话不合要求，沿用规则说明'
    visit.update(opening=result['opening'].strip(), opening_source='LLM')
    w.metrics['visit_openings'] = w.metrics.get('visit_openings', 0) + 1
    return True, '来访开口已写好'


def next_to_write(w):
    for visit in pending(w):
        if visit['opening_source'] == '规则' and not visit.get('writing') and visit.get('attempts', 0) < 2:
            n = w.npcs.get(visit['npc_id'])
            if n and n['alive']:
                return visit, context(w, visit)
    return None


def answer(w, body):
    """Settle the player's answer to a visit. Validates everything before changing anything."""
    from . import protagonist
    if not isinstance(body, dict) or set(body) - {'visit_id', 'choice', 'revision', 'month'}:
        raise ValueError('来访只接受既定回应')
    h = w.player['protagonist']
    if (protagonist.integer(body.get('revision'), '行程版本') != h['revision'] or
            protagonist.integer(body.get('month'), '月份') != w.month):
        raise ValueError('处境已变化，请刷新后再回应')
    visit = row(w, body.get('visit_id'))
    if not visit or visit['status'] != 'pending' or w.month - visit['month'] > 1:
        raise ValueError('这次来访已经结束')
    n = w.npcs.get(visit['npc_id'])
    if not n or not n['alive']:
        raise ValueError('来访之人已不在人世')
    if n['place'] != h['place']:
        raise ValueError('来访之人已不在此地')
    choice = next((c for c in choices(w, n, visit['kind']) if c['id'] == body.get('choice')), None)
    if choice is None:
        raise ValueError('未知回应')
    if not choice['enabled']:
        raise ValueError('银两或行动力不足')
    r = w.player['ties'].setdefault(str(n['id']), dict(affinity=0., trust=0.))
    silver = choice.get('silver', 0)
    if silver:
        w.player['wealth'] = round(w.player['wealth'] + silver, 4)
        n['wealth'] = round(n['wealth'] - silver, 4)
    h['points'] -= choice.get('points', 0)
    for key in ('affinity', 'trust'):
        r[key] = round(max(-100., min(100., r.get(key, 0.) + choice.get(key, 0))), 4)
    if choice.get('anger'):
        n['mood']['anger'] = min(100., n['mood']['anger'] + choice['anger'])
    if visit['kind'] == 'ask_help' and choice['id'] == 'give':
        from . import player_social
        player_social.state(w, True)['favors'].append(dict(npc_id=n['id'], owed_by=n['id'], kind='来访求助',
                                                           amount=4, status='open', month=w.month))
    if visit['kind'] == 'confront' and choice['id'] in ('apologize', 'calm') and (n.get('goal') or {}).get('against') is None:
        n['desires']['revenge'] = round(max(0., n['desires']['revenge'] - 4), 4)
    h['revision'] += 1
    e = w.emit('visit_answer', [n['id']], f"{w.player['name']}回应{n['name']}的来访（{visit['label']}）：{choice['label']}。{choice['effect']}。",
               .5, [visit['event_id'], r.get('last_event')],
               dict(visit=visit['id'], kind=visit['kind'], choice=choice['id'], silver=-silver if silver else 0,
                    affinity=choice.get('affinity', 0), trust=choice.get('trust', 0), favourable=choice.get('affinity', 0) > 0))
    if visit['kind'] == 'ask_help' and choice['id'] == 'give':
        from . import player_social
        player_social.state(w)['favors'][-1]['event_id'] = e['id']
    r.update(last_event=e['id'], last_contact=w.month)
    r['encounter_records'] = (r.get('encounter_records', []) + [e['id']])[-24:]
    h['records'].append(e['id'])
    visit.update(status='answered', choice=choice['id'], choice_label=choice['label'], answer_event=e['id'])
    w.checkpoint()
    return dict(event=e, visits=view(w))


def view(w):
    """Pure: visits waiting for the player, with the answers they can give."""
    out = []
    for visit in rows(w):
        if visit['status'] != 'pending' or w.month - visit['month'] > 1:
            continue
        n = w.npcs.get(visit['npc_id'])
        if not n or not n['alive']:
            continue
        here = n['place'] == (w.player.get('protagonist') or {}).get('place')
        out.append(dict({k: deepcopy(visit[k]) for k in ('id', 'npc_id', 'npc_name', 'kind', 'label', 'reason',
                                                        'opening', 'opening_source', 'month', 'event_id')},
                        here=here, choices=[{k: c[k] for k in ('id', 'label', 'effect', 'enabled')}
                                            for c in choices(w, n, visit['kind'])]))
    return out
