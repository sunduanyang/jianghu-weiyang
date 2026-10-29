"""人物近况与口吻：what a person can talk about from their own life, and a voice they keep.

`life` gathers, from the world as it stands, what this person is busy with,
what troubles them, the few people they feel strongly about and what has
happened to them lately. A conversation can then be about their own days, not
only about the player, and every name the model may use is listed.

`voice` is a short, stable way of speaking: how they refer to themselves,
how they talk, one or two habitual phrases. A rule card derived from age, sex,
livelihood, tradition and temperament is always available; the local model may
replace it once with its own card, which is then kept and reused in every
conversation and reaction, so a person sounds the same from one month to the
next. How they address the player follows the current relationship.
"""
from copy import deepcopy
import json
import re
import time
import unicodedata

from . import temperament

VERSION = 1
TRIVIAL = {'household_consumption', 'work', 'rest', 'play', 'leisure', 'item_produce', 'census', 'identity_change',
           'goal_change', 'stipend', 'household_support', 'sect_service', 'duty', 'learn_trade', 'item_use'}
MANNERS = {'upright': '说话直截了当，爱讲道理', 'kind': '语气温和，常替旁人着想', 'balanced': '说话圆融，凡事留余地',
           'defiant': '言语带刺，不服管束', 'self': '开口先算自己的得失'}
VOICE_SYSTEM = '''你要为武侠世界里的一个人物定下说话的口吻，今后他每次开口都会照此说话。
依据他的年龄、性别、营生、门派传承、处世立场和性格，给出：
self_ref：他平常的自称，一到四个字（如我、在下、老夫、俺、贫道、小女子）；
manner：一句话描述他的说话习惯（语速、腔调、爱用的说法），不超过三十字；
phrases：一到两句他常挂在嘴边的口头禅，每句不超过十二字。
要有个人特色，但不要夸张滑稽，也不要提到具体人名、数额和往事。只输出给定JSON。'''
BANNED = re.compile(r'JSON|json|模型|系统|指令|prompt|好感度')


# ------------------------------------------------------------------ life

def people(w, n, limit=3):
    """The few living people this person feels most strongly about, with their own view of each."""
    rows = []
    for key, r in n['relationships'].items():
        q = w.npcs.get(int(key))
        if not q or not q['alive'] or abs(r.get('affinity', 0)) < 20:
            continue
        family = ('配偶' if n['spouse'] == q['id'] else '子女' if q['id'] in n['children'] else
                  '父母' if q['id'] in n['parents'] else None)
        feeling = ('情深' if r['affinity'] >= 55 else '亲近' if r['affinity'] >= 20 else
                   '怨恨' if r['affinity'] <= -50 else '不和')
        rows.append((abs(r['affinity']) + (30 if family else 0), q['id'],
                     dict(name=q['name'], relation=family or r.get('label') or '相识', feeling=feeling,
                          job=q['job'], same_town=q['place'] == n['place'])))
    return [row[2] for row in sorted(rows, key=lambda x: (-x[0], x[1]))[:limit]]


def troubles(w, n):
    out = []
    if n['wealth'] < 12:
        out.append('手头拮据')
    if n.get('starvation_months', 0):
        out.append('家里断了粮')
    if n.get('illness'):
        out.append('身染疾病')
    if n.get('wound'):
        out.append('身上有伤未愈')
    if n['health'] < 50:
        out.append('身子虚弱')
    g = n.get('goal') or {}
    if g.get('kind') == 'revenge' and not g.get('resolved') and g.get('against_name'):
        out.append('与' + g['against_name'] + '有未了的旧怨')
    for i in n['parents'] + n['children'] + ([n['spouse']] if n['spouse'] else []):
        q = w.npcs.get(i)
        if q and not q['alive'] and (q.get('death') or {}).get('month', -99) >= w.month - 6:
            out.append('不久前失去了' + q['name'])
    return out[:4]


def doing(w, n):
    from . import npc_plans, undertakings
    p = n.get('plan') or {}
    if p.get('intent') in undertakings.KINDS:
        return dict(pursuit=undertakings.KINDS[p['intent']], title=p['title'], status=p.get('status'),
                    with_whom=p.get('target_name'))
    role = npc_plans.role(w, n)
    return dict(pursuit=None, title=role['label'], status=None, with_whom=None)


def recent(w, n, limit=3):
    rows = []
    for m in reversed(n['memories']):
        e = w.store.event(m['event_id'])
        if not e or e['kind'] in TRIVIAL or e['kind'].startswith(('player_', 'hero_')) or m.get('salience', 0) < .45:
            continue
        rows.append(dict(fact=m['fact'], months_ago=w.month - m['month'], my_view=m.get('interpretation', '')))
        if len(rows) >= limit:
            break
    return rows


def life(w, n):
    """Pure: what this person can talk about from their own life, with every name it contains."""
    spouse = w.npcs.get(n['spouse']) if n['spouse'] else None
    children = [w.npcs[i] for i in n['children'] if i in w.npcs and w.npcs[i]['alive']]
    out = dict(busy_with=doing(w, n), troubles=troubles(w, n), people=people(w, n), recent=recent(w, n),
               family=dict(spouse=spouse['name'] if spouse and spouse['alive'] else None,
                           children=[c['name'] for c in children][:4]),
               goal=(n.get('goal') or {}).get('title'))
    names = {p['name'] for p in out['people']} | set(out['family']['children'])
    if out['family']['spouse']:
        names.add(out['family']['spouse'])
    if out['busy_with'].get('with_whom'):
        names.add(out['busy_with']['with_whom'])
    g = n.get('goal') or {}
    if g.get('kind') == 'revenge' and g.get('against_name'):
        names.add(g['against_name'])
    for i in n['parents'] + n['children'] + ([n['spouse']] if n['spouse'] else []):
        if i in w.npcs and any(w.npcs[i]['name'] in t for t in out['troubles']):
            names.add(w.npcs[i]['name'])
    out['names'] = sorted(names)
    return out


def name_guard(w, n, allowed, limit=300):
    """Names of people this person might plausibly mention but that this conversation was not given."""
    pool = [w.npcs[i] for i in w.residents.get(n['place'], []) if i in w.npcs]
    pool += [w.npcs[int(k)] for k in n['relationships'] if int(k) in w.npcs]
    out = []
    for q in pool:
        if q['id'] != n['id'] and q['name'] not in allowed and q['name'] not in out and len(q['name']) >= 2:
            out.append(q['name'])
        if len(out) >= limit:
            break
    return out


def invents(text, guard):
    return any(name in text for name in guard)


# ------------------------------------------------------------------ voice

def rule_voice(w, n):
    """Deterministic fallback voice from age, sex, livelihood, tradition and temperament."""
    age = n['age_months'] / 12
    tradition = w.factions[n['faction']]['tradition'] if n['faction'] is not None else None
    female = n['sex'] == '女'
    if age < 16:
        ref = '我'
    elif tradition == '佛门':
        ref = '贫尼' if female else '贫僧'
    elif tradition == '道门':
        ref = '贫道'
    elif n.get('office'):
        ref = '本官'
    elif age >= 60:
        ref = '老身' if female else ('老夫' if n['job'] in ('书生', '药师') or n.get('social_status') else '老汉')
    elif n['job'] == '书生':
        ref = '小女子' if female else '在下'
    elif not female and (n['job'] in ('镖师', '行商') or n['traits']['sociability'] > 60):
        ref = '在下'
    else:
        ref = '我'
    t = n['traits']
    manner = MANNERS[temperament.stance(n)]
    if t['sociability'] < 30:
        manner += '，话不多'
    elif t['sociability'] > 70:
        manner += '，健谈'
    if t['boldness'] > 72:
        manner += '，嗓门大'
    return dict(version=VERSION, self_ref=ref, manner=manner, phrases=[], source='规则')


def address(w, n):
    """How this person addresses the player now; follows the relationship, not the card."""
    h = w.player['protagonist']
    r = w.player['ties'].get(str(n['id']), {})
    female = h.get('sex') == '女'
    older = h.get('age_months', 0) > n['age_months']
    if r.get('married'):
        return '娘子' if female else '夫君'
    if r.get('sworn'):
        return ('大姐' if female else '大哥') if older else ('贤妹' if female else '贤弟')
    if r.get('affinity', 0) >= 30:
        return w.player['name']
    if n['age_months'] - h.get('age_months', 0) > 25 * 12:
        return '姑娘' if female else '小兄弟'
    return '姑娘' if female else '兄台'


def voice(w, n):
    """Pure: the card in use (the person's own, else the rule card) plus the current form of address."""
    card = deepcopy(n.get('voice')) or rule_voice(w, n)
    card['address_player'] = address(w, n)
    return card


def needs_voice(n):
    return n['alive'] and n['age_months'] >= 120 and not n.get('voice')


def voice_context(w, n):
    return {'_voice': True, 'npc_id': n['id'], 'month': w.month,
            'self': dict(name=n['name'], sex=n['sex'], age=n['age_months'] // 12, job=n['job'],
                         tradition=w.factions[n['faction']]['tradition'] if n['faction'] is not None else None,
                         office=n.get('office'), temperament=temperament.prompt_view(n)),
            'draft': {k: v for k, v in rule_voice(w, n).items() if k in ('self_ref', 'manner')}}


def voice_schema():
    return dict(type='object', properties=dict(
        self_ref=dict(type='string', minLength=1, maxLength=4),
        manner=dict(type='string', minLength=4, maxLength=30),
        phrases=dict(type='array', minItems=1, maxItems=2, items=dict(type='string', minLength=2, maxLength=12))),
        required=['self_ref', 'manner', 'phrases'], additionalProperties=False)


def valid_voice(card):
    if not isinstance(card, dict) or set(card) != {'self_ref', 'manner', 'phrases'}:
        return False
    texts = [card['self_ref'], card['manner']] + (card['phrases'] if isinstance(card['phrases'], list) else [None])
    if not all(isinstance(t, str) for t in texts) or not 1 <= len(card['phrases']) <= 2:
        return False
    ref = unicodedata.normalize('NFKC', card['self_ref']).strip()
    if not 1 <= len(ref) <= 4 or not re.fullmatch(r'[一-鿿]+', ref):
        return False
    for t in texts[1:]:
        t = unicodedata.normalize('NFKC', t).strip()
        if BANNED.search(t) or re.search(r'\d', t) or not re.search(r'[一-鿿]', t):
            return False
    return 4 <= len(card['manner'].strip()) <= 30 and all(2 <= len(p.strip()) <= 12 for p in card['phrases'])


def voice_request(model, ctx):
    wire = {k: v for k, v in ctx.items() if not k.startswith('_')}
    started = time.perf_counter()
    try:
        if model.service:
            model.service.ensure(model.config)
        messages = [dict(role='system', content=VOICE_SYSTEM),
                    dict(role='user', content=json.dumps(wire, ensure_ascii=False, separators=(',', ':')))]
        result, raw, usage, ms = model.complete(messages, voice_schema(), 160, '定下说话口吻')
    except Exception as exc:
        from .ai import ModelRequestError
        raise ModelRequestError(str(exc), 1, {}) from exc
    usage = dict(usage, request_count=1, stages=[dict(stage='定下说话口吻', latency_ms=ms, **usage)])
    return result, (time.perf_counter() - started) * 1000, usage, raw


def apply_voice(w, ctx, result):
    n = w.npcs.get(ctx['npc_id'])
    if not n or not n['alive']:
        return False, '人物已离世'
    if n.get('voice'):
        return False, '此人已有自己的口吻'
    if not valid_voice(result):
        return False, '口吻不合要求，仍用规则口吻'
    n['voice'] = dict(version=VERSION, self_ref=result['self_ref'].strip(), manner=result['manner'].strip(),
                      phrases=[p.strip() for p in result['phrases']], source='LLM', month=w.month)
    w.metrics['voices_set'] = w.metrics.get('voices_set', 0) + 1
    return True, '口吻已定'


def view(w, n):
    card = voice(w, n)
    phrases = '、'.join('「' + p + '」' for p in card['phrases'])
    return dict(card, text=f"自称「{card['self_ref']}」，称你「{card['address_player']}」；{card['manner']}"
                          + (f"；常说{phrases}" if phrases else '') + '。')
