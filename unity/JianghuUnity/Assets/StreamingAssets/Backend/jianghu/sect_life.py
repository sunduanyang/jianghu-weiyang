"""Player life inside a sect: commissions, precepts, lineage, trials and relations.

Rules follow the rest of the engine: every query is pure, every action is
validated in full before it changes anything, money comes out of a real
treasury, and each outcome is an event with its causes. Boards are derived
from the current world on demand (a local random stream keyed by seed, sect
and month), so viewing never consumes the world RNG or writes the save.
"""
from copy import deepcopy
import hashlib
import random

from .atlas import PLACES
from . import combat_attributes, territory

VERSION = 1
ACTIVE_LIMIT = 2
SEVERITY = {'insult': 1, 'steal': 2, 'rob': 3, 'assault': 3}
ACT_NAMES = {'insult': '羞辱', 'steal': '偷窃', 'rob': '抢夺', 'assault': '袭击'}
EXPEL_SEVERITY = 6
COMMON_PRECEPT = dict(id='kin', label='同门不相残', text='不得羞辱、偷窃、抢夺或袭击本门同门。',
                      acts=('insult', 'steal', 'rob', 'assault'), scope='same_sect')
PRECEPTS = {
    '佛门': dict(id='ahimsa', label='戒杀戒盗', text='不得无故袭击、抢夺或偷窃他人。',
                 acts=('steal', 'rob', 'assault'), scope='any'),
    '道门': dict(id='purity', label='清静守正', text='不得偷窃、抢夺他人财物。',
                 acts=('steal', 'rob'), scope='any'),
    '帮会': dict(id='weak', label='不欺贫弱', text='不得偷窃、抢夺或袭击家资不足二十银的贫弱之人。',
                 acts=('steal', 'rob', 'assault'), scope='poor'),
    '剑派': dict(id='chivalry', label='行侠仗义', text='不得偷窃、抢夺他人财物。',
                 acts=('steal', 'rob'), scope='any'),
    '巫蛊': dict(id='village', label='护佑寨民', text='不得偷窃、抢夺或袭击本地治下之人。',
                 acts=('steal', 'rob', 'assault'), scope='compatriot'),
    '明教': dict(id='mutual', label='患难相恤', text='不得偷窃、抢夺或袭击家资不足二十银的贫弱之人。',
                 acts=('steal', 'rob', 'assault'), scope='poor'),
}
ESCORT_SUBJECT = {'佛门': '香客', '道门': '香客', '帮会': '流民', '剑派': '行旅', '巫蛊': '药材',
                  '明教': '教众', '密宗': '经卷'}
# How a sect names a commission against a sworn enemy, by its own camp.
FEUD_VERBS = {'正': '除魔', '邪': '立威', '中': '了断'}
FEUD_THRESHOLD = -20.
# Standing between camps: 正道 sects hold together, 邪道 sects only tolerate each other.
CAMP_RELATIONS = {('正', '正'): 15., ('邪', '邪'): 5., ('正', '邪'): -25.,
                  ('中', '正'): 5., ('中', '邪'): -10., ('中', '中'): 0.}
KINDS = {
    # months: how many calendar months, counting the current one, the commission stays open.
    'patrol': dict(name='巡守', points=4, contribution=2., silver=0., reputation=0., months=1, grade=9),
    'escort': dict(name='护送', points=1, contribution=4., silver=3., reputation=0., months=2, grade=9),
    'relief': dict(name='赈济', points=1, contribution=3., silver=0., reputation=1., months=2, grade=9),
    'visit': dict(name='致书', points=1, contribution=3., silver=0., reputation=0., months=2, grade=8),
    'hunt': dict(name='缉拿', points=3, contribution=6., silver=5., reputation=3., months=2, grade=7),
    'feud': dict(name='寻仇', points=3, contribution=6., silver=4., reputation=2., months=2, grade=7),
}
INTERACTIONS = {
    # key: (label, category, points, description)
    'sect_errand': ('门派差事', 5, 1, '办理本门交付的差事；成败与报酬按差事条件结算。'),
    'sect_trial': ('晋升比试', 1, 3, '晋阶条件达标满一月后，可请本门目标品级或更高之人考校；在本门驻地地区以物理切磋定胜负，胜出即刻晋阶，不必等满考核月数。'),
    'sect_apprentice': ('拜师入室', 2, 2, '拜本门四品及以上、品级高于你的前辈为师；须情分15、信任5，对方依性情决定。'),
    'sect_counsel': ('请师父指点', 2, 2, '每月一次，师父考较功课：记贡献1，师徒情分加深。'),
    'sect_guide': ('指点后辈', 2, 2, '四品及以上可每月指点一位低你两品以上的同门，计为本月履职。'),
    'sect_recruit': ('引荐入门', 5, 3, '三品及以上可引荐门外之人入本门；须情分20，对方依信念与交情决定，计为履职。'),
}
RELATION_LABELS = ((30, '交好'), (10, '和睦'), (-9.999, '平常'), (-29.999, '嫌隙'))


# ---------------------------------------------------------------- state

def hero(w):
    return w.player['protagonist']


def state(w):
    """Read-only view of the player's sect life (never inserts defaults)."""
    h = hero(w)
    s = h.get('sect_life')
    if not s or s.get('faction') != h.get('faction'):
        return blank(h.get('faction'))
    return s


def blank(faction):
    return dict(version=VERSION, faction=faction, commissions=[], taken=[], completed=0, failed=0,
                discipline=[], master=None, master_since=None, counsel_month=None, guide_month=None,
                trial_month=None, asked={}, records=[])


def own(w):
    """Mutable state; only called by validated actions."""
    h = hero(w)
    if not h.get('sect_life') or h['sect_life'].get('faction') != h.get('faction'):
        h['sect_life'] = blank(h.get('faction'))
    return h['sect_life']


def member(w):
    h = hero(w)
    return h['created'] and h.get('faction') is not None


def player_grade(w):
    from . import organizations
    m = organizations.player_membership(w)
    return m['grade'] if m else None


def npc_grade(w, n):
    from . import organizations
    return organizations.rank(w, n)['grade'] if n['faction'] is not None else None


def tie(w, npc_id):
    return w.player['ties'].get(str(npc_id), {})


def own_tie(w, npc_id):
    return w.player['ties'].setdefault(str(npc_id), dict(affinity=0., trust=0.))


def change_tie(r, affinity, trust):
    for key, delta in (('affinity', affinity), ('trust', trust)):
        r[key] = round(max(-100., min(100., r.get(key, 0.)+delta)), 4)


def stream(w, *parts):
    digest = hashlib.sha256(':'.join(map(str, (w.seed, 'sect-life-v1') + parts)).encode()).digest()
    return random.Random(int.from_bytes(digest[:8], 'big'))


def nearby(w, origin, hops=2):
    seen, frontier = {origin: 0}, [origin]
    for depth in range(1, hops+1):
        frontier = [b for a in frontier for b in sorted(w.neighbors.get(a, [])) if b not in seen]
        for b in frontier:
            seen.setdefault(b, depth)
    return seen


def at_seat(w, f):
    from . import city_maps
    h = hero(w)
    if h['place'] != f['place']:
        return False
    city = city_maps._world_layout(w, h['place'])
    area = city_maps.city_regions.area_at(city, h['row'], h['col'])
    return area is not None and area.get('faction') == f['id']


# ------------------------------------------------------------ relations

def camp_relation(a, b):
    pair = (a.get('alignment', '中'), b.get('alignment', '中'))
    return CAMP_RELATIONS.get(pair, CAMP_RELATIONS.get(pair[::-1], 0.))


def base_relation(a, b):
    from .sect_catalog import feud
    standing = feud(a['key'], b['key']) if a.get('key') and b.get('key') else None
    return ((10 if a['tradition'] == b['tradition'] else 0) + (5 if a['country'] == b['country'] else 0)
            + camp_relation(a, b) + (standing['value'] if standing else 0.))


def relation(w, a, b):
    fa, fb = w.factions[a], w.factions[b]
    return round(max(-100., min(100., base_relation(fa, fb) + fa.get('relations', {}).get(str(b), 0.))), 4)


def adjust_relation(w, a, b, delta):
    if a == b:
        return
    for x, y in ((a, b), (b, a)):
        rel = w.factions[x].setdefault('relations', {})
        rel[str(y)] = round(max(-100., min(100., rel.get(str(y), 0.)+delta)), 4)


def relation_label(value):
    return next((label for floor, label in RELATION_LABELS if value >= floor), '交恶')


def relations_view(w, f):
    from .sect_catalog import feud
    rows = [dict(faction=g['id'], name=g['name'], value=relation(w, f['id'], g['id']),
                 alignment=g.get('alignment', '中'), alignment_label=g.get('alignment_label', '中立'))
            for g in w.factions if g['id'] != f['id']]
    for row in rows:
        row['label'] = relation_label(row['value'])
        standing = feud(f['key'], w.factions[row['faction']]['key'])
        if standing:
            row['feud'] = '宿敌' if standing['kind'] == 'bitter' else '争雄'
            row['feud_note'] = standing['note']
    return sorted(rows, key=lambda r: (-r['value'], r['faction']))


# ------------------------------------------------------------ commissions

def members_at(w, f, place):
    return [w.npcs[i] for i in w.residents.get(place, []) if w.npcs[i]['alive'] and w.npcs[i]['faction'] == f['id']]


def issuer(w, f):
    officers = sorted((n for n in members_at(w, f, f['place']) if 3 <= npc_grade(w, n) <= 6 and n['age_months'] >= 192),
                      key=lambda n: (npc_grade(w, n), n['id']))
    if officers:
        return stream(w, f['id'], w.month, 'issuer').choice(officers[:4])
    leader = w.npcs.get(f['leader'])
    return leader if leader and leader['alive'] and leader['faction'] == f['id'] else None


def commission(w, f, kind, who=None, **extra):
    spec = KINDS[kind]
    target = extra.pop('target', None)
    place = extra.pop('place', f['place'])
    giver = who
    row = dict(id=f"{f['id']}:{w.month}:{kind}", kind=kind, kind_name=spec['name'], faction=f['id'],
               month=w.month, deadline=w.month+spec['months']-1, min_grade=spec['grade'],
               points=spec['points'], issuer=giver['id'] if giver else None,
               issuer_name=giver['name'] if giver else '门中执事',
               target=target['id'] if target else None, target_name=target['name'] if target else None,
               place=place, place_name=PLACES[place]['name'],
               reward=dict(contribution=spec['contribution'], silver=spec['silver'], reputation=spec['reputation']),
               **extra)
    return row


def board(w, f):
    """Commissions on offer this month for one sect. Pure."""
    rng = stream(w, f['id'], w.month, 'board')
    who = issuer(w, f)
    rows = [commission(w, f, 'patrol', who, label=f"{f['duty']} · {f['seat_name']}",
                       detail=f"在{f['seat_name']}{f['duty']}，当月办理，耗4行动力。")]
    reach = nearby(w, f['place'])
    active = {p['id'] for p in w.active_places}
    exits = sorted(p for p in w.neighbors.get(f['place'], []) if p in active)
    if exits:
        dest = rng.choice(exits)
        subject = ESCORT_SUBJECT.get(f['tradition'], '行旅')
        rows.append(commission(w, f, 'escort', who, place=dest, subject=subject,
                               label=f"护送{subject}至{PLACES[dest]['name']}",
                               detail=f"本月或下月内抵达{PLACES[dest]['name']}，在门派页交割。"))
    needy = sorted((n for p in reach if reach[p] <= 1 for n in (w.npcs[i] for i in w.residents.get(p, []))
                    if n['alive'] and n['age_months'] >= 192 and n['wealth'] < 15
                    and n['faction'] != f['id']), key=lambda n: (n['wealth'], n['id']))[:3]
    if needy:
        n = rng.choice(needy)
        rows.append(commission(w, f, 'relief', who, target=n, place=n['place'], silver_out=4.,
                               label=f"赈济{PLACES[n['place']]['name']}的{n['name']}",
                               detail=f"本月或下月内当面将门中库银4两交付{n['name']}。"))
    envoys = {}
    for p, depth in reach.items():
        for n in (w.npcs[i] for i in w.residents.get(p, [])):
            if (n['alive'] and n['faction'] is not None and n['faction'] != f['id']
                    and w.factions[n['faction']]['kind'] == 'sect' and npc_grade(w, n) <= 5 and n['age_months'] >= 192):
                envoys.setdefault(n['faction'], []).append(n)
    if envoys:
        ranked = sorted(envoys, key=lambda g: (relation(w, f['id'], g), g))[:2]
        g = rng.choice(ranked)
        n = min(envoys[g], key=lambda q: (npc_grade(w, q), q['id']))
        rows.append(commission(w, f, 'visit', who, target=n, place=n['place'], other=g,
                               label=f"致书{w.factions[g]['name']} · {n['name']}",
                               detail=f"本月或下月内当面向{n['name']}递交本门书信，修好两派往来。"))
    outlaws = sorted((n for p in reach for n in (w.npcs[i] for i in w.residents.get(p, []))
                      if n['alive'] and n['age_months'] >= 216 and n['faction'] != f['id'] and n['health'] > 25
                      and not tie(w, n['id']).get('married') and not tie(w, n['id']).get('sworn')
                      and (n.get('rank_progress', {}).get('embargo_until', -1) > w.month or n['reputation'] <= -15)),
                     key=lambda n: (n['reputation'], n['id']))[:3]
    if outlaws:
        n = rng.choice(outlaws)
        rows.append(commission(w, f, 'hunt', who, target=n, place=n['place'],
                               label=f"缉拿{PLACES[n['place']]['name']}的{n['name']}",
                               detail=f"{n['name']}恶名在外或新近获罪。本月或下月内寻到此人，在交互「门派差事」中以真实交手将其制服。"))
    foes = sorted((n for p in reach for n in (w.npcs[i] for i in w.residents.get(p, []))
                   if n['alive'] and n['age_months'] >= 216 and n['health'] > 25
                   and n['faction'] is not None and n['faction'] != f['id']
                   and w.factions[n['faction']]['kind'] == 'sect'
                   and relation(w, f['id'], n['faction']) <= FEUD_THRESHOLD
                   and not tie(w, n['id']).get('married') and not tie(w, n['id']).get('sworn')),
                  key=lambda n: (relation(w, f['id'], n['faction']), -npc_grade(w, n), n['id']))[:3]
    if foes:
        n = rng.choice(foes)
        g = w.factions[n['faction']]
        verb = FEUD_VERBS.get(f.get('alignment', '中'), '了断')
        rows.append(commission(w, f, 'feud', who, target=n, place=n['place'], other=g['id'], verb=verb,
                               label=f"{verb} · {g['name']}的{n['name']}",
                               detail=f"{g['name']}与本门{relation_label(relation(w, f['id'], g['id']))}。本月或下月内在{PLACES[n['place']]['name']}寻到{n['name']}，"
                                      f"在交互「门派差事」中以真实交手胜之。"))
    return rows


def describe(w, c):
    r = c['reward']
    parts = [f"贡献{r['contribution']:g}"]
    if r['silver']:
        parts.append(f"{r['silver']:g}银")
    if r['reputation']:
        parts.append(f"声望{r['reputation']:g}")
    span = '当月' if c['deadline'] == c['month'] else f"限{c['deadline']-c['month']+1}月内"
    return '、'.join(parts) + ' · ' + span


def view_commission(w, c):
    row = deepcopy(c)
    row['reward_text'] = describe(w, c)
    row['months_left'] = c['deadline'] - w.month
    row['target_alive'] = c['target'] is None or (c['target'] in w.npcs and w.npcs[c['target']]['alive'])
    row['target_place'] = w.npcs[c['target']]['place'] if c['target'] in w.npcs else c['place']
    row['target_place_name'] = PLACES[row['target_place']]['name']
    if c['kind'] == 'patrol':
        row['step'] = f"走入{w.factions[c['faction']]['seat_name']}，在门派页「交割差事」。"
    elif c['kind'] == 'escort':
        row['step'] = f"前往{c['place_name']}，在门派页「交割差事」。"
    else:
        row['step'] = f"前往{row['target_place_name']}找到{c['target_name']}，在人物交互中选择「门派差事」。"
    return row


def find_active(w, cid):
    return next((c for c in state(w)['commissions'] if c['id'] == cid), None)


def errand_for(w, n):
    return next((c for c in state(w)['commissions'] if c.get('target') == n['id'] and c['kind'] in ('relief', 'visit', 'hunt', 'feud')), None)


def accept_reason(w, f, c):
    s = state(w)
    if c['id'] in s['taken']:
        return '这件差事已经领过'
    if len(s['commissions']) >= ACTIVE_LIMIT:
        return f'同时至多办理{ACTIVE_LIMIT}件差事'
    if player_grade(w) > c['min_grade']:
        return f"须{c['min_grade']}品及以上方可领此差事"
    if c['kind'] == 'relief' and f['fund'] < c['silver_out']:
        return '门中库银不足，暂不能拨付赈银'
    return ''


def complete_reason(w, f, c):
    h = hero(w)
    if w.month > c['deadline']:
        return '差事已过期限'
    if c['kind'] == 'patrol' and not at_seat(w, f):
        return '请先走入这座门派驻地'
    if c['kind'] == 'escort' and h['place'] != c['place']:
        return f"请先抵达{c['place_name']}"
    if c['kind'] not in ('patrol', 'escort'):
        return '此差事须当面办理，请在对方的人物交互中选择「门派差事」'
    if h['points'] < c['points']:
        return f"需要{c['points']}行动力"
    return ''


# ----------------------------------------------------------- precepts

def precepts(f):
    rows = [dict(COMMON_PRECEPT)]
    extra = PRECEPTS.get(f['tradition'])
    if extra:
        rows.append(dict(extra))
    for row in rows:
        row['acts_text'] = '、'.join(ACT_NAMES[a] for a in row['acts'])
    return rows


def breached(w, f, act, n):
    hits = []
    for p in precepts(f):
        if act not in p['acts']:
            continue
        scope = p['scope']
        if (scope == 'any' or scope == 'same_sect' and n['faction'] == f['id']
                or scope == 'poor' and n['wealth'] < 20
                or scope == 'compatriot' and territory.owner(w, n['place']) == f['country']):
            hits.append(p)
    return hits


def unatoned(w, since=24):
    return [r for r in state(w)['discipline'] if not r['atoned'] and r['month'] > w.month - since]


# ---------------------------------------------------------- lineage

def master(w):
    """The player's living master in the current sect, or None."""
    s = state(w)
    n = w.npcs.get(s['master']) if s['master'] is not None else None
    if n and n['alive'] and n['faction'] == hero(w).get('faction'):
        return n
    return None


def recommended(w, who, target):
    """A living, senior master with real regard shortens the wait by a month."""
    from . import sect_ranks
    if who == 'player':
        n = master(w)
        regard = tie(w, n['id']).get('affinity', 0) if n else 0
        faction = hero(w).get('faction')
    else:
        pupil = w.npcs[who]
        n = w.npcs.get(pupil.get('mentor'))
        faction = pupil['faction']
        regard = pupil['relationships'].get(str(n['id']), {}).get('affinity', 0) if n else 0
    if not n or not n['alive'] or faction is None or n['faction'] != faction:
        return None
    grade = sect_ranks.standing(w.factions[faction], n)['grade']
    return n if grade <= target and regard >= 30 else None


# -------------------------------------------------------- interactions

def interaction_rows(w, n):
    """Sect interactions that apply to this person (listing only; never mutates)."""
    if not hero(w)['created'] or not n['alive']:
        return []
    rows = []
    h = hero(w)
    grade = player_grade(w) if member(w) else None
    same = member(w) and n['faction'] == h['faction']
    errand = errand_for(w, n) if member(w) else None
    if errand:
        label, category, points, _ = INTERACTIONS['sect_errand']
        rows.append(('sect_errand', f"{label} · {errand.get('verb') or errand['kind_name']}", 1 if errand['kind'] in ('hunt', 'feud') else category,
                     errand['points'], f"{errand['label']}：{errand['detail']}"))
    if same:
        their = npc_grade(w, n)
        from . import rank_progression
        if rank_progression.active(w) and their < grade:
            rows.append(('sect_trial',) + INTERACTIONS['sect_trial'])
        mine = master(w)
        if mine is None and their <= 4 and their < grade:
            rows.append(('sect_apprentice',) + INTERACTIONS['sect_apprentice'])
        if mine is not None and mine['id'] == n['id']:
            rows.append(('sect_counsel',) + INTERACTIONS['sect_counsel'])
        if grade <= 4 and their >= grade + 2:
            rows.append(('sect_guide',) + INTERACTIONS['sect_guide'])
    if member(w) and grade <= 3 and n['faction'] is None and n['age_months'] >= 192:
        rows.append(('sect_recruit',) + INTERACTIONS['sect_recruit'])
    return rows


def interaction_points(w, n, key):
    if key == 'sect_errand':
        c = errand_for(w, n)
        return c['points'] if c else INTERACTIONS[key][2]
    return INTERACTIONS[key][2]


def interaction_reason(w, n, key):
    """Called after the shared checks (alive, same place, points, no battle)."""
    from . import rank_progression
    h = hero(w)
    if not member(w):
        return '尚未加入门派'
    f = w.factions[h['faction']]; s = state(w); grade = player_grade(w); r = tie(w, n['id'])
    if key == 'sect_errand':
        c = errand_for(w, n)
        if not c:
            return '对方不是你所办差事的对象'
        if w.month > c['deadline']:
            return '差事已过期限'
        if c['kind'] == 'relief' and f['fund'] < c['silver_out']:
            return '门中库银不足，暂不能拨付赈银'
        if c['kind'] in ('hunt', 'feud'):
            if min(h['age_months'], n['age_months']) < 216:
                return '双方须年满18岁'
            if min(w.player['health'], n['health']) <= 25:
                return '双方须体力高于25才能交手'
        return ''
    if key == 'sect_recruit':
        if grade > 3:
            return '三品及以上方可引荐入门'
        if n['faction'] is not None:
            return '对方已有门派'
        if n['age_months'] < 192:
            return '对方须年满16岁'
        if combat_attributes.rating(n) < 12:
            return '对方战斗能力不足12，尚不能入门'
        if w.month < n.get('faction_rejoin_after', -1):
            return '对方辞别旧门未满一年'
        if s['asked'].get(f"recruit:{n['id']}") == w.month:
            return '本月已向对方提过此事'
        if r.get('affinity', 0) < 20:
            return '引荐入门需要情分20'
        return ''
    if n['faction'] != f['id']:
        return '对方不是本门中人'
    their = npc_grade(w, n)
    if key == 'sect_trial':
        if not rank_progression.active(w):
            return '品阶考核尚未启用'
        conditions = rank_progression.sect_conditions(w, 'player')
        if not conditions or conditions['leader_only']:
            return '主持之位须由门中推举，不经比试'
        if not conditions['qualified']:
            unmet = [q['label'] for q in conditions['requirements'] if not q['met']]
            return conditions['blocked'] or ('晋阶条件尚未达标：' + '、'.join(unmet))
        if conditions['completed_months'] < 1:
            return '考核达标满一月后方可请求比试'
        if their > conditions['target']:
            return f"须由{conditions['target']}品或更高品级的同门考校"
        if h['place'] != f['place']:
            return f"晋升比试须在本门驻地{PLACES[f['place']]['name']}举行"
        if s['trial_month'] == w.month:
            return '本月已比试过，下月再请考校'
        if min(h['age_months'], n['age_months']) < 216:
            return '双方须年满18岁'
        if min(w.player['health'], n['health']) <= 25:
            return '双方须体力高于25才能交手'
        return ''
    if key == 'sect_apprentice':
        if master(w) is not None:
            return '已有师父'
        if their > 4 or their >= grade:
            return '须拜四品及以上、品级高于你的前辈'
        if n['age_months'] < 252:
            return '对方年资尚浅，未能收徒'
        if s['asked'].get(f"master:{n['id']}") == w.month:
            return '本月已向对方提过此事'
        if r.get('affinity', 0) < 15 or r.get('trust', 0) < 5:
            return '拜师需要情分15、信任5'
        return ''
    if key == 'sect_counsel':
        mine = master(w)
        if mine is None or mine['id'] != n['id']:
            return '对方不是你的师父'
        if s['counsel_month'] == w.month:
            return '本月已请师父指点过'
        if n['health'] <= 30:
            return '师父身体欠安'
        return ''
    if key == 'sect_guide':
        if grade > 4:
            return '四品及以上方可指点后辈'
        if their < grade + 2:
            return '只能指点低你两品以上的同门'
        if n['age_months'] < 120:
            return '对方年纪尚小'
        if s['guide_month'] == w.month:
            return '本月已指点过后辈'
        return ''
    return '未知门派交互'


def npc_event(w, n, key, text, facts, causes=(), importance=.5):
    r = own_tie(w, n['id'])
    facts = dict(facts, action=key, actor='player', target=n['id'],
                 player_points=facts.get('player_points', INTERACTIONS[key][2]), npc_time_cost=0)
    e = w.emit('player_'+key, [n['id']], text, importance, [n['last_event'], r.get('last_event')] + list(causes), facts)
    r.update(last_event=e['id'], last_contact=w.month)
    hero(w)['records'].append(e['id'])
    s = own(w)
    s['records'] = (s['records'] + [e['id']])[-30:]
    from . import organizations
    if hero(w).get('faction') is not None:
        organizations.record(w.factions[hero(w)['faction']], e)
    return e


def perform_interaction(w, n, key):
    """Validated by player_interactions.reason; returns (event, battle-or-None)."""
    from . import martial, organizations, rank_progression
    from . import physical_combat as combat
    h = hero(w); f = w.factions[h['faction']]; s = own(w); name = w.player['name']
    points = interaction_points(w, n, key)
    battle = None
    if key == 'sect_errand':
        c = errand_for(w, n)
        if c['kind'] == 'hunt':
            martial.sync_actor(w, w.player, True); martial.sync_actor(w, n)
            battle_id = f'{w.seed}-{w.month}-{w.store.next_id}'
            battle = combat.start(w.player, n, w.rng.getrandbits(63), battle_id, spar=False,
                                  equipment=(combat.equipment(w, 'player'), combat.equipment(w, n['id'])))
            battle.update(encounter='sect_hunt', commission=c['id'], faction=f['id'])
            change_tie(own_tie(w, n['id']), -30, -25)
            e = npc_event(w, n, key, f"{name}奉{f['name']}之命缉拿{n['name']}，双方拔兵交手。",
                          dict(commission=c['id'], kind='hunt', battle_id=battle_id, service=False, player_points=points),
                          importance=.7)
            battle['cause'] = e['id']
        elif c['kind'] == 'feud':
            martial.sync_actor(w, w.player, True); martial.sync_actor(w, n)
            battle_id = f'{w.seed}-{w.month}-{w.store.next_id}'
            battle = combat.start(w.player, n, w.rng.getrandbits(63), battle_id, spar=False,
                                  equipment=(combat.equipment(w, 'player'), combat.equipment(w, n['id'])))
            battle.update(encounter='sect_feud', commission=c['id'], faction=f['id'], other=c['other'])
            change_tie(own_tie(w, n['id']), -25, -20)
            e = npc_event(w, n, key, f"{name}奉{f['name']}之命{c['verb']}，向{w.factions[c['other']]['name']}的{n['name']}挑战，双方拔兵交手。",
                          dict(commission=c['id'], kind='feud', other=c['other'], battle_id=battle_id, service=False,
                               player_points=points), importance=.7)
            battle['cause'] = e['id']
        elif c['kind'] == 'relief':
            silver = c['silver_out']
            f['fund'] = round(f['fund']-silver, 4); f['expenses'] = round(f['expenses']+silver, 4)
            n['wealth'] = round(n['wealth']+silver, 4)
            change_tie(own_tie(w, n['id']), 6, 4)
            e = npc_event(w, n, key, f"{name}奉{f['name']}之命，将门中库银{silver:g}两当面交付{n['name']}。",
                          dict(commission=c['id'], kind='relief', silver=silver, payer=f['id'], player_points=points))
            settle_commission(w, c, e)
        else:
            other = c['other']
            adjust_relation(w, f['id'], other, 6)
            change_tie(own_tie(w, n['id']), 3, 3)
            e = npc_event(w, n, key, f"{name}代{f['name']}向{w.factions[other]['name']}{n['name']}递交书信，两派往来稍有修好。",
                          dict(commission=c['id'], kind='visit', other=other, relation_delta=6,
                               relation=relation(w, f['id'], other), player_points=points))
            settle_commission(w, c, e)
    elif key == 'sect_trial':
        conditions = rank_progression.sect_conditions(w, 'player')
        martial.sync_actor(w, w.player, True); martial.sync_actor(w, n)
        battle_id = f'{w.seed}-{w.month}-{w.store.next_id}'
        battle = combat.start(w.player, n, w.rng.getrandbits(63), battle_id, spar=True,
                              equipment=(combat.equipment(w, 'player'), combat.equipment(w, n['id'])))
        battle.update(encounter='sect_trial', faction=f['id'], target_grade=conditions['target'],
                      current_grade=conditions['current'], examiner=n['id'])
        s['trial_month'] = w.month
        e = npc_event(w, n, key, f"{name}在{f['seat_name']}请{n['name']}考校，比试晋升{f['ranks'][conditions['target']-1]}。",
                      dict(faction=f['id'], target_grade=conditions['target'], battle_id=battle_id, player_points=points))
        battle['cause'] = e['id']
    elif key == 'sect_apprentice':
        r = own_tie(w, n['id'])
        accepted = n['mood']['anger'] < 65 and n['traits']['integrity'] + n['traits']['kindness'] + r.get('affinity', 0) >= 90
        s['asked'][f"master:{n['id']}"] = w.month
        if accepted:
            s.update(master=n['id'], master_since=w.month)
            r['sect_master'] = True
            change_tie(r, 6, 6)
            if not h.get('mentor'):
                h['mentor'] = n['id']
        text = (f"{name}向{n['name']}行拜师礼，" +
                (f"{n['name']}收其为入室弟子。" if accepted else f"{n['name']}以缘分未到婉拒。"))
        e = npc_event(w, n, key, text, dict(accepted=accepted, faction=f['id'], player_points=points), importance=.65)
    elif key == 'sect_counsel':
        r = own_tie(w, n['id'])
        h['contribution'] = round(h['contribution']+1, 4)
        change_tie(r, 4, 3)
        s['counsel_month'] = w.month
        e = npc_event(w, n, key, f"{n['name']}考较{name}的功课，记门中贡献1。",
                      dict(contribution=1., faction=f['id'], player_points=points))
    elif key == 'sect_guide':
        r = own_tie(w, n['id'])
        n['contribution'] = round(n['contribution']+.5, 4)
        w.player['reputation'] = round(w.player['reputation']+.5, 4)
        change_tie(r, 4, 3)
        s['guide_month'] = w.month
        e = npc_event(w, n, key, f"{name}在门中指点后辈{n['name']}，记为本月履职。",
                      dict(accepted=True, faction=f['id'], student_contribution=.5, player_points=points))
    elif key == 'sect_recruit':
        r = own_tie(w, n['id'])
        score = r.get('affinity', 0) + n['beliefs']['tradition']*.4 + n['traits']['integrity']*.3
        accepted = n['mood']['anger'] < 65 and score >= 55
        s['asked'][f"recruit:{n['id']}"] = w.month
        if accepted:
            organizations.prepare(n)
            n.update(faction=f['id'], faction_since=w.month, contribution=0.)
            change_tie(r, 4, 4)
        text = (f"{name}引荐{n['name']}拜入{f['name']}，" +
                (f"对方应允，登记为九品{f['ranks'][8]}。" if accepted else '对方尚无此意，婉言谢绝。'))
        e = npc_event(w, n, key, text, dict(accepted=accepted, faction=f['id'], score=round(score, 4),
                                            player_points=points), importance=.65 if accepted else .4)
        if accepted:
            if rank_progression.active(w):
                n.get('rank_progress', {}).pop('sect', None)
                rank_progression.prepare(w, n['id'])
            martial.sync_actor(w, n)
            organizations.elect(w, f)
    else:
        raise ValueError('未知门派交互')
    h['points'] -= points
    return e, battle


# ------------------------------------------------------- settlement

def reward(w, c, causes, extra=None):
    """Pay a finished commission from the real treasury and record it as duty."""
    from . import protagonist
    h = hero(w); f = w.factions[c['faction']]
    paid = round(min(c['reward']['silver'], max(0., f['fund'])), 4)
    f['fund'] = round(f['fund']-paid, 4); f['expenses'] = round(f['expenses']+paid, 4)
    w.player['wealth'] = round(w.player['wealth']+paid, 4)
    h['contribution'] = round(h['contribution']+c['reward']['contribution'], 4)
    w.player['reputation'] = round(w.player['reputation']+c['reward']['reputation'], 4)
    f['tasks_completed'] = f.get('tasks_completed', 0)+1
    facts = dict(commission=c['id'], kind=c['kind'], faction=f['id'], contribution=c['reward']['contribution'],
                 silver=paid, requested_silver=c['reward']['silver'], reputation=c['reward']['reputation'],
                 issuer=c['issuer'], **(extra or {}))
    text = (f"{w.player['name']}办妥{f['name']}差事「{c['label']}」，记贡献{c['reward']['contribution']:g}"
            + (f"，领库银{paid:g}两" if paid else '') + (f"，声望增{c['reward']['reputation']:g}" if c['reward']['reputation'] else '') + '。')
    if c['reward']['silver'] and paid < c['reward']['silver']:
        text += '门中库银不足，赏银照实支付。'
    e = protagonist.record(w, 'hero_sect_task', text, facts, causes=[x for x in causes if x])
    from . import organizations
    organizations.record(f, e)
    return e


def settle_commission(w, c, cause_event):
    s = own(w)
    s['commissions'] = [x for x in s['commissions'] if x['id'] != c['id']]
    s['completed'] += 1
    e = reward(w, c, [cause_event['id'] if cause_event else None])
    s['records'] = (s['records'] + [e['id']])[-30:]
    return e


def settle_battle(w, battle, n):
    """Called once from the shared battle settlement; returns the result event spec."""
    key = battle.get('encounter')
    if key == 'sect_trial':
        f = w.factions[battle['faction']]
        won = battle['winner'] == 0
        text = (f"{w.player['name']}与{n['name']}比试晋升，" +
                (f"技压考官，获准晋升{f['ranks'][battle['target_grade']-1]}。" if won else '未能胜出，尚待再试。'))

        def after(event):
            from . import rank_progression
            if not won or not member(w) or hero(w)['faction'] != f['id'] or not rank_progression.active(w):
                return
            current = rank_progression.sect_conditions(w, 'player')
            if current and current['current'] == battle['current_grade']:
                rank_progression.change_sect(w, 'player', battle['target_grade'],
                                             f"晋升比试胜出，经{n['name']}考校确认", event['id'])
        return dict(kind='player_sect_trial_result', summary=text, loot=0, after=after)
    if key == 'sect_hunt':
        from . import crafts
        crafts.settle_battle(battle, (w.player, n))
        won = battle['winner'] == 0
        c = find_active(w, battle['commission'])
        text = f"{w.player['name']}缉拿{n['name']}，" + ('将其制服。' if won else '未能得手，对方脱身。')

        def after(event):
            if won and c and find_active(w, c['id']):
                settle_commission(w, c, event)
        return dict(kind='player_sect_hunt_result', summary=text, loot=0, after=after)
    if key == 'sect_feud':
        from . import crafts
        crafts.settle_battle(battle, (w.player, n))
        won = battle['winner'] == 0
        c = find_active(w, battle['commission'])
        other = w.factions[battle['other']]
        text = (f"{w.player['name']}与{other['name']}的{n['name']}交手，" +
                ('胜出，为本门扬名。' if won else '落败，对方扬长而去。'))

        def after(event):
            adjust_relation(w, battle['faction'], other['id'], -3)
            if won and c and find_active(w, c['id']):
                settle_commission(w, c, event)
        return dict(kind='player_sect_feud_result', summary=text, loot=0, after=after)
    return None


# ------------------------------------------------- hostile acts & precepts

def after_hostile(w, key, n, event, aware):
    """Apply precepts and inter-sect standing after a settled hostile interaction."""
    if key not in SEVERITY or not aware or not member(w):
        return []
    h = hero(w); f = w.factions[h['faction']]
    if n['faction'] is not None and n['faction'] != f['id']:
        adjust_relation(w, f['id'], n['faction'], {'insult': -2, 'steal': -4, 'rob': -8, 'assault': -8}[key])
    hits = breached(w, f, key, n)
    if not hits:
        return []
    from . import protagonist, rank_progression
    s = own(w); severity = SEVERITY[key]; penalty = severity*2.
    labels = '、'.join(p['label'] for p in hits)
    if rank_progression.active(w):
        p = rank_progression.prepare(w, 'player')['sect']
        p['penalty'] = round(p['penalty']+penalty, 4)
        p['censure_until'] = max(p['censure_until'], w.month+6); p['qualified'] = 0
    else:
        h['contribution'] = round(max(0., h['contribution']-penalty), 4)
    teacher = master(w)
    if teacher:
        change_tie(own_tie(w, teacher['id']), -10, -8)
    text = (f"{w.player['name']}{ACT_NAMES[key]}{n['name']}，触犯{f['name']}门规「{labels}」，"
            f"门中记过：扣有效贡献{penalty:g}，半年内暂停晋升。")
    e = protagonist.record(w, 'hero_sect_discipline', text,
                           dict(faction=f['id'], act=key, victim=n['id'], precepts=[p['id'] for p in hits],
                                severity=severity, penalty=penalty), causes=[event['id']])
    from . import organizations
    organizations.record(f, e)
    s['discipline'].append(dict(month=w.month, act=key, act_name=ACT_NAMES[key], precepts=[p['label'] for p in hits],
                                severity=severity, penalty=penalty, event_id=e['id'], atoned=False, victim=n['id'],
                                victim_name=n['name']))
    s['discipline'] = s['discipline'][-12:]
    s['records'] = (s['records'] + [e['id']])[-30:]
    out = [e]
    if teacher and tie(w, teacher['id']).get('affinity', 0) < -10:
        out.append(sever_master(w, teacher, '弟子屡犯门规，师父与之断绝师徒名分', e['id']))
    if sum(r['severity'] for r in unatoned(w)) >= EXPEL_SEVERITY:
        out.append(depart(w, f, expelled=True, cause=e['id']))
    return out


def sever_master(w, n, reason, cause=None):
    from . import protagonist
    s = own(w)
    s.update(master=None, master_since=None)
    own_tie(w, n['id']).pop('sect_master', None)
    return protagonist.record(w, 'hero_sect_master_end', f"{n['name']}与{w.player['name']}的师徒名分终止：{reason}。",
                              dict(master=n['id'], reason=reason), causes=[cause] if cause else [])


def depart(w, f, expelled=False, cause=None):
    """Shared exit path for leaving and expulsion; history is kept, not rewritten."""
    from . import organizations, protagonist
    h = hero(w)
    s = state(w)
    h.setdefault('membership_history', []).append(dict(
        faction=f['id'], name=f['name'], since=h.get('faction_since'), until=w.month,
        contribution=h['contribution'], completed_commissions=s['completed'], failed_commissions=s['failed'],
        master=s['master'], reason='违反门规被逐出' if expelled else '自行辞别'))
    wait = 24 if expelled else 12
    h.update(faction=None, faction_since=None, contribution=0., faction_rejoin_after=w.month+wait)
    if s['master'] is not None:
        own_tie(w, s['master']).pop('sect_master', None)
    h.pop('sect_life', None)
    text = (f"{w.player['name']}屡犯{f['name']}门规，被逐出门墙，两年内不得另投门派。" if expelled else
            f"{w.player['name']}辞别{f['name']}，一年后方可另择门派。")
    e = protagonist.record(w, 'hero_sect_expelled' if expelled else 'hero_sect_leave', text,
                           dict(faction=f['id'], key=f['key'], expelled=expelled, rejoin_after=w.month+wait),
                           causes=[cause] if cause else [])
    h['membership_history'][-1]['event_id'] = e['id']
    organizations.record(f, e)
    if f['leader'] == -1:
        f.update(leader=None, vacancy_previous=-1, vacancy_event=e['id'])
        organizations.elect(w, f)
    return e


def on_join(w, f, event):
    h = hero(w)
    h['sect_life'] = blank(f['id'])
    h['sect_life']['records'] = [event['id']]


# ------------------------------------------------------------ sect page

def options(w, f):
    """Commission options on the sect page (shape matches organizations.player_options)."""
    h = hero(w)
    if not h['created'] or h.get('faction') != f['id']:
        return []
    seat = at_seat(w, f); s = state(w); rows = []
    def add(action, label, points, reason, choice):
        if not reason and h['points'] < points:
            reason = '本月行动力不足'
        rows.append(dict(action=action, faction=f['id'], label=label, points=points, choice=choice,
                         enabled=not reason, reason=reason))
    for c in s['commissions']:
        if c['kind'] in ('patrol', 'escort'):
            add('sect_complete', f"交割差事 · {c['label']}", c['points'], complete_reason(w, f, c), c['id'])
    for c in board(w, f):
        if any(x['id'] == c['id'] for x in s['commissions']):
            continue
        why = '请先走入这座门派驻地领取差事' if not seat else accept_reason(w, f, c)
        add('sect_accept', f"领取差事 · {c['label']}（{describe(w, c)}）", 0, why, c['id'])
    for c in s['commissions']:
        add('sect_abandon', f"放弃差事 · {c['label']}（扣贡献1）", 0, '', c['id'])
    pending = unatoned(w)
    if pending:
        add('sect_penance', f"向门中请罪 · {pending[-1]['act_name']}{pending[-1]['victim_name']}", 3,
            '' if seat else '请先走入这座门派驻地', str(pending[-1]['event_id']))
    return rows


def perform_option(w, f, action, choice):
    """Execute a validated sect-page option; caller checked enabled/reason."""
    from . import protagonist
    h = hero(w); s = own(w)
    if action == 'sect_accept':
        c = next(c for c in board(w, f) if c['id'] == choice)
        s['commissions'].append(c)
        s['taken'] = (s['taken'] + [c['id']])[-40:]
        e = protagonist.record(w, 'hero_sect_accept', f"{w.player['name']}自{c['issuer_name']}处领下{f['name']}差事「{c['label']}」。",
                               dict(commission=deepcopy(c), faction=f['id']),
                               causes=[w.npcs[c['issuer']]['last_event']] if c['issuer'] in w.npcs else [])
        s['records'] = (s['records'] + [e['id']])[-30:]
        from . import organizations
        organizations.record(f, e)
        return e, 0
    c = next((x for x in s['commissions'] if x['id'] == choice), None)
    if action == 'sect_complete':
        e = settle_commission(w, c, None)
        return e, c['points']
    if action == 'sect_abandon':
        s['commissions'] = [x for x in s['commissions'] if x['id'] != choice]
        s['failed'] += 1
        h['contribution'] = round(max(0., h['contribution']-1), 4)
        teacher = master(w)
        if teacher:
            change_tie(own_tie(w, teacher['id']), -3, -2)
        e = protagonist.record(w, 'hero_sect_task_failed', f"{w.player['name']}放弃{f['name']}差事「{c['label']}」，扣贡献1。",
                               dict(commission=c['id'], faction=f['id'], reason='主动放弃', contribution=-1.))
        s['records'] = (s['records'] + [e['id']])[-30:]
        return e, 0
    if action == 'sect_penance':
        from . import rank_progression
        strike = next(r for r in s['discipline'] if str(r['event_id']) == choice and not r['atoned'])
        strike['atoned'] = True
        restored = strike['penalty']/2
        if rank_progression.active(w):
            p = rank_progression.prepare(w, 'player')['sect']
            p['penalty'] = round(max(0., p['penalty']-restored), 4)
        teacher = master(w)
        if teacher:
            change_tie(own_tie(w, teacher['id']), 2, 2)
        e = protagonist.record(w, 'hero_sect_penance', f"{w.player['name']}在{f['seat_name']}请罪受罚，门中免去此过一半罚扣（{restored:g}），不再计入逐出门墙之数。",
                               dict(faction=f['id'], strike=strike['event_id'], restored=restored),
                               causes=[strike['event_id']])
        strike['atonement_event'] = e['id']
        s['records'] = (s['records'] + [e['id']])[-30:]
        return e, 3
    raise ValueError('未知门派事项')


# ------------------------------------------------------------- monthly

def monthly(w):
    from . import protagonist
    h = hero(w)
    s = h.get('sect_life')
    if not h.get('created') or not s or s.get('faction') != h.get('faction') or h.get('faction') is None:
        return
    f = w.factions[h['faction']]
    for c in list(s['commissions']):
        target = w.npcs.get(c['target']) if c['target'] is not None else None
        if target is not None and not target['alive']:
            s['commissions'].remove(c)
            protagonist.record(w, 'hero_sect_task_void', f"差事「{c['label']}」的对象{target['name']}已故，差事作罢，不予追究。",
                               dict(commission=c['id'], faction=f['id'], reason='对象已故'), causes=[target['last_event']])
        elif w.month > c['deadline']:
            s['commissions'].remove(c)
            s['failed'] += 1
            h['contribution'] = round(max(0., h['contribution']-1), 4)
            teacher = master(w)
            if teacher:
                change_tie(own_tie(w, teacher['id']), -3, -2)
            e = protagonist.record(w, 'hero_sect_task_failed', f"{w.player['name']}未能如期办妥{f['name']}差事「{c['label']}」，扣贡献1。",
                                   dict(commission=c['id'], faction=f['id'], reason='逾期', contribution=-1.))
            s['records'] = (s['records'] + [e['id']])[-30:]
    if s['master'] is not None and master(w) is None:
        old = w.npcs.get(s['master'])
        if old is None:
            s.update(master=None, master_since=None)
        else:
            sever_master(w, old, '师父已故' if not old['alive'] else '师父已不在本门', old['last_event'])


# ---------------------------------------------------------------- view

def view(w, f):
    """Everything the sect page shows about life in this sect. Pure."""
    from . import organizations, rank_progression, sect_ranks
    h = hero(w)
    result = dict(version=VERSION, precepts=precepts(f), relations=relations_view(w, f),
                  member=bool(h['created'] and h.get('faction') == f['id']),
                  join_rule='须在同一地区与本门三品及以上之人对话，于「互动」栏拜入；战斗能力12以上，辞别旧门满一年。')
    roster = [n for n in w.npcs.values() if n['alive'] and n['faction'] == f['id']]
    def person(n):
        g = sect_ranks.standing(f, n)
        return dict(id=n['id'], name=n['name'], grade=g['grade'], grade_label=g['grade_label'], title=g['title'],
                    place=n['place'], place_name=PLACES[n['place']]['name'], here=n['place'] == h['place'])
    if not result['member']:
        recruiters = sorted((person(n) for n in roster if sect_ranks.standing(f, n)['grade'] <= 3),
                            key=lambda r: (not r['here'], r['grade'], r['id']))
        result['recruiters'] = recruiters[:8]
        return result
    s = state(w); grade = player_grade(w)
    teacher = master(w)
    seniors = sorted((person(n) for n in roster if sect_ranks.standing(f, n)['grade'] <= min(4, grade-1)
                      and n['age_months'] >= 252), key=lambda r: (not r['here'], r['grade'], r['id']))
    conditions = rank_progression.sect_conditions(w, 'player') if rank_progression.active(w) else None
    hint = ''
    if conditions and not conditions['leader_only']:
        if conditions['qualified'] and conditions['completed_months'] >= 1:
            hint = (f"考核已达标（{conditions['completed_months']}/{conditions['required_months']}月）。"
                    f"可在{PLACES[f['place']]['name']}与本门{conditions['target']}品或更高之人对话，于「比武」栏请求晋升比试，胜出即刻晋阶。")
        elif conditions['qualified']:
            hint = f"本月条件已全部达标，下次月考记为达标后即可请求晋升比试（或连续达标{conditions['required_months']}月自动晋阶）。"
        else:
            unmet = [q['label'] for q in conditions['requirements'] if not q['met']]
            hint = conditions['blocked'] or ('尚缺：' + '、'.join(unmet) if unmet else '')
    rec = recommended(w, 'player', conditions['target']) if conditions else None
    result['player'] = dict(
        grade=grade, at_seat=at_seat(w, f), seat_place=f['place'], seat_place_name=PLACES[f['place']]['name'],
        active=[view_commission(w, c) for c in s['commissions']], active_limit=ACTIVE_LIMIT,
        board=[dict(view_commission(w, c), taken=c['id'] in s['taken']) for c in board(w, f)],
        completed=s['completed'], failed=s['failed'],
        master=person(teacher) | dict(affinity=tie(w, teacher['id']).get('affinity', 0)) if teacher else None,
        seniors=seniors[:6] if teacher is None else [],
        recommended=bool(rec),
        discipline=deepcopy(s['discipline'][::-1]),
        strikes=sum(r['severity'] for r in unatoned(w)), expel_at=EXPEL_SEVERITY,
        assessment=conditions, assessment_hint=hint,
        records=[e for i in reversed(s['records'][-8:]) if (e := w.store.event(i))])
    return result
