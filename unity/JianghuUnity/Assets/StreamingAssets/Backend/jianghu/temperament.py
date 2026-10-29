"""品性：five stable temperament axes, an experience-shaped outlook and one stance.

Layers (all stored on the person, all 0..100, 50 is the midpoint):
- traits   — five bipolar temperament axes (稳定性格)，change slowly through experience.
- beliefs  — three world-outlook values (世界观)，shaped by what happens to the person.
- stance   — derived, never stored: 刚正 / 仁善 / 中庸 / 叛逆 / 唯我 (处世立场).
Mood, pressure and loneliness stay transient and are summarized as one line.

New people are drawn from archetypes weighted by their livelihood and sect
tradition, so a village has recognizable characters instead of uniform noise.
Generation uses a stream keyed by seed and person; the world RNG is never
consumed here. Legacy eight-trait saves convert with a fixed formula.
"""
from copy import deepcopy
import hashlib
import random

VERSION = 2
# key, high pole, low pole, high description, low description, high word, low word
AXES = (
    ('kindness', '仁厚', '冷酷', '怜悯他人，不忍见人受苦', '只顾自身，难为旁人所动', '仁厚', '冷酷'),
    ('integrity', '信义', '诡诈', '守信重诺，不负所托', '欺瞒取巧，见利易变', '重信义', '多诡诈'),
    ('boldness', '刚烈', '谨慎', '冲动敢为，遇事好斗', '审慎多疑，先求周全', '刚烈', '谨慎'),
    ('ambition', '进取', '淡泊', '志在高处，争先求成', '知足安分，不慕虚名', '进取', '淡泊'),
    ('sociability', '合群', '孤僻', '喜好交游，乐于往来', '内敛寡言，独来独往', '合群', '孤僻'),
)
KEYS = tuple(row[0] for row in AXES)
NAMES = {row[0]: row[1] for row in AXES}
POLES = {row[0]: (row[2], row[1]) for row in AXES}
OUTLOOK = (
    ('trust', '人可相托', '信人以诚', '多疑世人'),
    ('justice', '善有善报', '笃信公道', '不信报应'),
    ('tradition', '传承有益', '尊奉传承', '轻视成规'),
)
OUTLOOK_KEYS = tuple(row[0] for row in OUTLOOK)
OUTLOOK_NAMES = {row[0]: row[1] for row in OUTLOOK}
STANCES = {
    'upright': ('刚正', '重公道与原则，敢于担当，直言不讳。'),
    'kind': ('仁善', '先体谅他人，重情分与善意。'),
    'balanced': ('中庸', '不偏不倚，凡事权衡，求两全之道。'),
    'defiant': ('叛逆', '不服成规，质疑惯例，自有主张。'),
    'self': ('唯我', '以自身所求为先，信自己胜过信旁人。'),
}
STANCE_THRESHOLD = 14.
# Legacy eight-trait names, kept only to label old personality-change records.
LEGACY_NAMES = {'compassion': '仁厚', 'ambition': '进取', 'caution': '谨慎', 'sociability': '外向',
                'loyalty': '忠诚', 'honesty': '诚实', 'greed': '贪欲', 'aggression': '暴烈'}

# Archetype centres: kindness, integrity, boldness, ambition, sociability, tradition, trust, justice
ARCHETYPES = {
    'xia': ('侠义', (72, 75, 66, 52, 55, 55, 60, 72)),
    'shijing': ('市井', (45, 45, 45, 58, 72, 45, 50, 45)),
    'laoshi': ('本分', (64, 72, 28, 30, 42, 72, 62, 62)),
    'yinyi': ('隐逸', (56, 62, 32, 18, 20, 40, 45, 55)),
    'xiaoxiong': ('枭雄', (24, 36, 72, 88, 58, 35, 35, 30)),
    'jianhua': ('奸猾', (22, 18, 38, 68, 62, 40, 25, 25)),
    'mangfu': ('莽直', (46, 52, 86, 46, 52, 50, 55, 50)),
    'yuzhi': ('迂直', (58, 82, 30, 62, 32, 80, 55, 78)),
    'langzi': ('浪子', (50, 42, 70, 45, 64, 15, 50, 45)),
}
ORDER = KEYS + OUTLOOK_KEYS
BASE_WEIGHT = .6
ROLE_WEIGHTS = {
    '农人': dict(laoshi=5, shijing=2, yinyi=1, mangfu=1),
    '行商': dict(shijing=5, jianhua=3, xiaoxiong=1),
    '药师': dict(laoshi=3, xia=2, yinyi=2, yuzhi=1),
    '匠人': dict(laoshi=4, shijing=2, yuzhi=1),
    '镖师': dict(mangfu=4, xia=3, langzi=2),
    '渔人': dict(laoshi=3, yinyi=2, shijing=2),
    '猎户': dict(mangfu=3, yinyi=3, langzi=1),
    '书生': dict(yuzhi=5, yinyi=1, xiaoxiong=1),
    '艺人': dict(langzi=4, shijing=3),
    '短工': dict(shijing=3, mangfu=2, langzi=2, jianhua=1),
}
TRADITION_WEIGHTS = {
    '佛门': dict(laoshi=3, xia=2, yinyi=2), '道门': dict(yinyi=4, xia=2), '帮会': dict(xia=3, shijing=2, langzi=2),
    '剑派': dict(xia=4, langzi=1), '武人集团': dict(mangfu=3, xiaoxiong=2), '部族武帐': dict(mangfu=3, xia=2),
    '祖灵信仰': dict(laoshi=3, mangfu=1), '萨满信仰': dict(laoshi=2, yinyi=2, mangfu=1),
}
NOISE = 11.
# Desires lean on temperament: (base, {axis: weight}) — the noise term stays per person.
DESIRE_LEANS = {'wealth': {'boldness': -.45}, 'mastery': {'ambition': .5}, 'belonging': {'sociability': .65},
                'renown': {'ambition': .85}, 'freedom': {'boldness': .65}}


def clamp(value, lo=0., hi=100.):
    return round(max(lo, min(hi, value)), 4)


def stream(seed, person, *parts):
    digest = hashlib.sha256(':'.join(map(str, (seed, person, 'temperament-v2') + parts)).encode()).digest()
    return random.Random(int.from_bytes(digest[:8], 'big'))


# ----------------------------------------------------------- generation

def archetype_weights(job=None, tradition=None):
    weights = dict.fromkeys(ARCHETYPES, BASE_WEIGHT)
    for source in (ROLE_WEIGHTS.get(job, {}), TRADITION_WEIGHTS.get(tradition, {})):
        for key, weight in source.items():
            weights[key] += weight
    return weights


def draw(seed, person, job=None, tradition=None):
    """Deterministic temperament and outlook for one person and role."""
    rng = stream(seed, person, job or '', tradition or '')
    weights = archetype_weights(job, tradition)
    key = rng.choices(list(weights), list(weights.values()))[0]
    centre = dict(zip(ORDER, ARCHETYPES[key][1]))
    values = {k: clamp(rng.gauss(centre[k], NOISE), 2, 98) for k in ORDER}
    return key, {k: values[k] for k in KEYS}, {k: values[k] for k in OUTLOOK_KEYS}


def assign(w, n, job=None, tradition=None):
    """(Re)draw temperament when a person's life context becomes known; keeps desire noise."""
    before = dict(n['traits'])
    key, traits, outlook = draw(w.seed, n['id'], job or n.get('job'), tradition)
    n['traits'] = traits
    n['initial_traits'] = dict(traits)
    n['beliefs'] = outlook
    n['temperament_version'] = VERSION
    n['archetype'] = key
    retune_desires(n, before)
    if 'family_size' in n:
        # Same rule as demography.initialize, re-evaluated for the settled temperament.
        n['family_size'] = 3 + int(traits['kindness'] > 60) + int((n['id']+w.seed) % 5 == 0)
    return key


def inherit(w, n, parents):
    """Children take after both parents, with a family-specific variation."""
    rng = stream(w.seed, n['id'], 'inherit')
    for k in KEYS:
        mean = sum(p['traits'][k] for p in parents) / len(parents)
        n['traits'][k] = clamp(mean + rng.gauss(0, NOISE*.9), 2, 98)
    for k in OUTLOOK_KEYS:
        mean = sum(p['beliefs'][k] for p in parents) / len(parents)
        n['beliefs'][k] = clamp(mean + rng.gauss(0, NOISE), 2, 98)
    n['initial_traits'] = dict(n['traits'])
    n['temperament_version'] = VERSION
    n['archetype'] = None


def base_desires(traits, noise):
    """noise: five world-RNG draws taken by the caller (keeps the shared sequence)."""
    nw, nm, nb, nr, nf = noise
    return {'wealth': clamp(35 + (100-traits['boldness'])*.45 + nw),
            'mastery': clamp(30 + traits['ambition']*.5 + nm),
            'belonging': clamp(20 + traits['sociability']*.65 + nb),
            'renown': clamp(traits['ambition']*.85 + nr),
            'freedom': clamp(20 + traits['boldness']*.65 + nf), 'revenge': 0.}


def retune_desires(n, before):
    desires = n.get('desires') or {}
    for key, leans in DESIRE_LEANS.items():
        if key in desires:
            delta = sum(weight*(n['traits'][axis]-before.get(axis, 50.)) for axis, weight in leans.items())
            desires[key] = clamp(desires[key] + delta)


# ------------------------------------------------------------ migration

def legacy_extras(seed, n):
    """The former reproducible draw for honesty / greed / aggression on very old saves."""
    rng = random.Random(seed*1000003+n['id']*7919)
    return {k: rng.uniform(12, 88) for k in ('honesty', 'greed', 'aggression')}


def convert(old, extras=None):
    """Fixed formula from the eight legacy traits; averaged axes are re-spread to the old range."""
    get = lambda k: old[k] if k in old else (extras or {}).get(k, 50.)
    spread = lambda v: clamp(50 + (v-50)*1.4, 2, 98)
    return {'kindness': spread(.65*get('compassion') + .35*(100-get('greed'))),
            'integrity': spread(.5*get('honesty') + .5*get('loyalty')),
            'boldness': spread(.5*get('aggression') + .5*(100-get('caution'))),
            'ambition': clamp(get('ambition'), 2, 98),
            'sociability': clamp(get('sociability'), 2, 98)}


def migrate(w, n):
    """Upgrade one person in place. Idempotent; never touches the world RNG."""
    if n.get('temperament_version', 1) >= VERSION and set(n.get('traits', {})) == set(KEYS):
        return False
    traits = n.get('traits', {})
    legacy_scale = n.get('temperament_version', 1) < VERSION
    if 'kindness' not in traits:
        extras = legacy_extras(w.seed, n)
        initial = n.get('initial_traits') or traits
        n['legacy_traits'] = {k: round(v, 4) for k, v in traits.items()}
        n['traits'] = convert(traits, extras)
        n['initial_traits'] = convert({**traits, **initial}, extras)
    beliefs = n.setdefault('beliefs', {})
    for k in OUTLOOK_KEYS:
        value = beliefs.get(k, .6 if k == 'tradition' else .5)
        # Version-1 outlooks were stored on 0..1.
        beliefs[k] = clamp(value*100 if legacy_scale and value <= 1.0001 else value)
    n['drift_month'] = {}
    n.setdefault('archetype', None)
    n['temperament_version'] = VERSION
    return True


# -------------------------------------------------------------- reading

def lean(n, spec):
    """An aptitude lean such as 'kindness' or '-boldness' (the low pole), 0..100."""
    return 100-n['traits'][spec[1:]] if spec.startswith('-') else n['traits'][spec]


def stance_scores(n):
    t, b = n['traits'], n['beliefs']
    return {'upright': (t['integrity']-50) + .4*(t['boldness']-50) + .3*(b['justice']-50),
            'kind': (t['kindness']-50) + .3*(t['sociability']-50) + .15*(t['integrity']-50),
            'defiant': (50-b['tradition']) + .4*(t['boldness']-50) + .15*(50-t['integrity']),
            'self': .8*(t['ambition']-50) + .8*(50-t['kindness'])}


def stance(n):
    scores = stance_scores(n)
    key = max(scores, key=lambda k: (scores[k], k))
    return key if scores[key] >= STANCE_THRESHOLD else 'balanced'


def degree(value):
    return '极' if value >= 85 or value <= 15 else '颇' if value >= 70 or value <= 30 else ''


def word(key, value):
    row = next(r for r in AXES if r[0] == key)
    return degree(value) + (row[5] if value >= 50 else row[6])


def descriptors(n, limit=3):
    """Only distinctive axes are named; a middling axis says nothing."""
    rows = sorted(((abs(n['traits'][k]-50), k) for k in KEYS), reverse=True)
    return [word(k, n['traits'][k]) for distance, k in rows if distance >= 20][:limit]


def outlook_words(n):
    result = []
    for key, _, high, low in OUTLOOK:
        v = n['beliefs'][key]
        if v >= 72:
            result.append(high)
        elif v <= 28:
            result.append(low)
    return result


def mood_line(n):
    m = n.get('mood', {})
    signals = [(m.get('anger', 0), 35, '愤懑难平'), (m.get('fear', 0), 35, '忧惧不安'),
               (n.get('pressure', 0), 60, '为生计所迫'), (n.get('loneliness', 0), 70, '孤寂寡欢'),
               (m.get('joy', 0), 35, '心情舒畅')]
    active = [text for value, floor, text in sorted(signals, key=lambda r: -(r[0]-r[1])) if value >= floor]
    return '，'.join(active[:2]) or '心境平和'


def summary(n):
    """One line for prompts and lists: 立场 + distinctive words."""
    words = descriptors(n)
    return STANCES[stance(n)][0] + ('：' + '、'.join(words) if words else '')


def prompt_view(n):
    """What a language model is told about this person's temperament."""
    return dict(stance=STANCES[stance(n)][0], stance_meaning=STANCES[stance(n)][1],
                temperament=[dict(axis=f"{POLES[k][1]}↔{POLES[k][0]}", value=round(n['traits'][k]),
                                  word=word(k, n['traits'][k]) if abs(n['traits'][k]-50) >= 20 else '居中')
                             for k in KEYS],
                outlook=outlook_words(n) or ['看法平常'], mood=mood_line(n))


def view(n):
    """Player-facing temperament. Pure."""
    key = stance(n)
    changes = []
    for row in n.get('personality_changes', [])[-12:][::-1]:
        axis = row.get('axis')
        changes.append(dict(row, name=NAMES.get(axis) or OUTLOOK_NAMES.get(axis) or LEGACY_NAMES.get(axis, axis),
                            legacy=axis in LEGACY_NAMES and axis not in KEYS))
    return dict(version=VERSION, stance=key, stance_label=STANCES[key][0], stance_text=STANCES[key][1],
                words=descriptors(n), summary=summary(n),
                axes=[dict(key=k, high=POLES[k][1], low=POLES[k][0], value=round(n['traits'][k], 1),
                           initial=round(n.get('initial_traits', {}).get(k, n['traits'][k]), 1),
                           word=word(k, n['traits'][k]) if abs(n['traits'][k]-50) >= 20 else '居中',
                           text=next(r[3] if n['traits'][k] >= 50 else r[4] for r in AXES if r[0] == k))
                      for k in KEYS],
                outlook=[dict(key=k, name=OUTLOOK_NAMES[k], value=round(n['beliefs'][k], 1)) for k in OUTLOOK_KEYS],
                outlook_words=outlook_words(n), mood=mood_line(n), changes=changes)
