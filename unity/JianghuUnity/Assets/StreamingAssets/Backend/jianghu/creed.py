"""人生信条：the model writes who a person is; the rules act it out every week.

A creed is a short, long-lived account of a person in terms the rules can
use: what they value most, the line they will not cross, how they answer a
wrong, how they meet temptation, the wish they live for, and a few things
they lean toward or away from. The local model writes it the first time it
thinks about someone (and again at a turning point or after some years);
from then on every weekly choice that person makes is weighed by it, with no
further model call. People the model has not reached yet have no creed and
act as before, so the calibrated rules are unchanged for them.
"""
from copy import deepcopy

VERSION = 1
REVIEW_MONTHS = 36
VALUES = ('亲情', '友情', '情爱', '公道', '武道', '财富', '名望', '自在', '安稳', '门派', '家国')
BOTTOM = ('不负亲人', '不欺弱小', '不背弃朋友', '不违王法', '不辱师门', '无所顾忌')
WRONGED = ('隐忍', '讨回公道', '以牙还牙', '一笑置之')
TEMPTATION = ('不为所动', '量力而取', '见利忘义')
DREAMS = {'wealth': '安家立业', 'mastery': '武学有成', 'belonging': '有所归属', 'renown': '名动一方',
          'freedom': '行遍江湖', 'revenge': '讨回公道'}
LEAN_KINDS = ('work', 'trade', 'befriend', 'court', 'aid', 'care', 'neighbor_care', 'donate_relief', 'travel',
              'leisure', 'gamble', 'rest', 'rumor', 'duty', 'sect_service', 'seek_teacher', 'family', 'learn_trade',
              'meditate', 'steal', 'extort', 'smear', 'betray')
LEAN_LABELS = {'work': '谋生', 'trade': '合营', 'befriend': '结交', 'court': '倾诉心意', 'aid': '接济', 'care': '照料',
               'neighbor_care': '探病照护', 'donate_relief': '捐助义仓', 'travel': '远行', 'leisure': '消遣',
               'gamble': '赌局', 'rest': '休养', 'rumor': '交换见闻', 'duty': '尽责', 'sect_service': '门中履职',
               'seek_teacher': '求师', 'family': '养育', 'learn_trade': '学手艺', 'meditate': '静思',
               'steal': '行窃', 'extort': '勒索', 'smear': '造谣中伤', 'betray': '背信'}
WEIGHTS = (-12, -6, 6, 12)
PREDATORY = {'steal', 'extort', 'smear', 'betray', 'embezzle'}
# What each value pulls a person toward: kind -> bonus at full weight.
VALUE_PULL = {'亲情': {'family': 8, 'care': 6}, '友情': {'befriend': 6, 'aid': 6}, '情爱': {'court': 8, 'propose': 8},
              '公道': {'report': 10, 'public_service': 4}, '武道': {'seek_teacher': 8, 'duel': 4},
              '财富': {'work': 5, 'trade': 6, 'gamble': 3}, '名望': {'public_service': 6, 'duty': 5, 'donate_relief': 6},
              '自在': {'travel': 8, 'leisure': 3, 'duty': -4}, '安稳': {'rest': 3, 'travel': -6, 'duel': -8},
              '门派': {'sect_service': 8, 'duty': 6}, '家国': {'public_service': 8, 'donate_relief': 4}}
VALUE_ORDER = (1., .6, .3)


def schema():
    return {'type': 'object', 'properties': {
        'motto': {'type': 'string', 'minLength': 2, 'maxLength': 16},
        'values': {'type': 'array', 'minItems': 2, 'maxItems': 3, 'items': {'type': 'string', 'enum': list(VALUES)}},
        'bottom_line': {'type': 'string', 'enum': list(BOTTOM)},
        'when_wronged': {'type': 'string', 'enum': list(WRONGED)},
        'temptation': {'type': 'string', 'enum': list(TEMPTATION)},
        'dream': {'type': 'string', 'enum': list(DREAMS)},
        'leanings': {'type': 'array', 'maxItems': 3, 'items': {'type': 'object', 'properties': {
            'kind': {'type': 'string', 'enum': list(LEAN_KINDS)},
            'weight': {'type': 'integer', 'enum': list(WEIGHTS)}},
            'required': ['kind', 'weight'], 'additionalProperties': False}}},
        'required': ['motto', 'values', 'bottom_line', 'when_wronged', 'temptation', 'dream', 'leanings'],
        'additionalProperties': False}


PROMPT = '''creed_due为真时，写下你的人生信条creed，它会长期决定你日常的取舍：motto一句十六字内的信条；values按轻重选两三样你最看重的；bottom_line你绝不越过的底线（无所顾忌也可以）；when_wronged受了欺辱怎么办；temptation面对不义之财怎么办；dream此生最想要的；leanings至多三类你格外愿意（正数）或格外不愿（负数）做的事。信条要符合你的性格，不必是好人。'''


def valid(value):
    import re
    if not isinstance(value, dict) or set(value) != set(schema()['required']):
        return False
    motto = value['motto']
    if not isinstance(motto, str) or not 2 <= len(motto.strip()) <= 16 or not re.search(r'[一-鿿]', motto) \
            or re.search(r'JSON|模型|系统|指令|\d', motto):
        return False
    values = value['values']
    if (not isinstance(values, list) or not 2 <= len(values) <= 3 or len(set(values)) != len(values)
            or any(v not in VALUES for v in values)):
        return False
    if value['bottom_line'] not in BOTTOM or value['when_wronged'] not in WRONGED or value['temptation'] not in TEMPTATION:
        return False
    if value['dream'] not in DREAMS:
        return False
    rows = value['leanings']
    if not isinstance(rows, list) or len(rows) > 3:
        return False
    if any(not isinstance(r, dict) or set(r) != {'kind', 'weight'} or r['kind'] not in LEAN_KINDS
           or type(r['weight']) is not int or r['weight'] not in WEIGHTS for r in rows):
        return False
    return len({r['kind'] for r in rows}) == len(rows)


def of(n):
    c = n.get('creed')
    return c if c and c.get('source') == 'LLM' else None


def due(w, n):
    c = of(n)
    return c is None or w.month - c['month'] >= REVIEW_MONTHS


def apply(w, n, value, event_id=None):
    old = of(n)
    n['creed'] = dict(version=VERSION, source='LLM', month=w.month, event_id=event_id,
                      motto=value['motto'].strip(), values=list(value['values']), bottom_line=value['bottom_line'],
                      when_wronged=value['when_wronged'], temptation=value['temptation'], dream=value['dream'],
                      leanings=deepcopy(value['leanings']))
    if old:
        n['creed_history'] = (n.get('creed_history', []) + [dict(month=old['month'], motto=old['motto'])])[-5:]
    w.metrics['creeds_written'] = w.metrics.get('creeds_written', 0) + 1


# ------------------------------------------------------------------ the rules acting it out

def kin(n, q):
    return q['id'] in n['parents'] + n['children'] or n['spouse'] == q['id']


def adjustment(w, n, a):
    """Score bonus a creed adds to one candidate action. Zero for people without a creed."""
    c = n.get('creed')
    if not c or c.get('source') != 'LLM':
        return 0.
    k = a['kind']
    score = 0.
    for weight, value in zip(VALUE_ORDER, c['values']):
        score += VALUE_PULL[value].get(k, 0) * weight
    score += next((r['weight'] for r in c['leanings'] if r['kind'] == k), 0)
    if k in PREDATORY:
        q = w.npcs.get(a.get('target'))
        line = c['bottom_line']
        if line == '不欺弱小' and q and q['wealth'] < n['wealth']:
            score -= 40
        elif line == '不负亲人' and q and kin(n, q):
            score -= 60
        elif line == '不背弃朋友' and q and n['relationships'].get(str(q['id']), {}).get('affinity', 0) > 20:
            score -= 50
        elif line == '不违王法':
            score -= 25
        elif line == '不辱师门' and n['faction'] is not None:
            score -= 15
        score += {'不为所动': -20, '量力而取': 0, '见利忘义': 12 if k in ('betray', 'embezzle') else 6}[c['temptation']]
    if k == 'duel':
        score += {'以牙还牙': 12, '讨回公道': 0, '隐忍': -12, '一笑置之': -15}[c['when_wronged']]
    elif k == 'report':
        score += {'以牙还牙': -5, '讨回公道': 12, '隐忍': -4, '一笑置之': 0}[c['when_wronged']]
    return score


def grudge_fade(n):
    """Extra monthly retention of the wish for revenge, from how this person answers a wrong."""
    c = of(n)
    if not c:
        return 1.
    return {'以牙还牙': 1.01, '讨回公道': 1., '隐忍': .99, '一笑置之': .96}[c['when_wronged']]


def dream_bonus(n, k):
    c = of(n)
    return 10. if c and c['dream'] == k else 0.


def brief(n):
    c = of(n)
    if not c:
        return None
    return dict(motto=c['motto'], values=c['values'], bottom_line=c['bottom_line'], when_wronged=c['when_wronged'],
                temptation=c['temptation'], dream=DREAMS[c['dream']],
                leanings=[('更愿' if r['weight'] > 0 else '不愿') + LEAN_LABELS[r['kind']] for r in c['leanings']])


def view(w, n):
    c = of(n)
    if not c:
        return dict(written=False, text='尚未成形：此人还没被细细想过，日常取舍按性情与处境而定。')
    b = brief(n)
    return dict(written=True, month=c['month'], motto=c['motto'], text='；'.join(
        [f"信条「{b['motto']}」", '看重' + '、'.join(b['values']), '底线：' + b['bottom_line'],
         '受辱时' + b['when_wronged'], '见利时' + b['temptation'], '此生想要' + b['dream']]
        + (['、'.join(b['leanings'])] if b['leanings'] else [])) + '。',
        history=deepcopy(n.get('creed_history', [])))
