"""技艺：锻造、医术、诗书，以营生熟练度为唯一造诣。

One number per craft. For NPCs the number *is* the matching trade's
`career.experience` entry (匠人 → 锻造, 药师 → 医术, 书生 → 诗书), so a
master smith is never a novice craftsman and old saves need no migration.
The protagonist has no trade record and keeps the same scale in
`player['crafts']['skill']`.

Craft outcomes are derived from a stream keyed by seed and the new item's
identity; the world RNG is never consumed, so a forging is reproducible.
Items without a `craft` record behave exactly as before.
"""
from copy import deepcopy
import hashlib
import math
import random

from . import careers
from .physical_reference import PARTS

VERSION = 1
CRAFTS = (
    ('forging', '锻造', '匠人', '打造与修补兵器；取向与火候改变兵器的质量、操控与压盾。'),
    ('medicine', '医术', '药师', '诊治伤病、接骨疗伤、依方制药；造诣越高疗效越好。'),
    ('letters', '诗书', '书生', '题字作文，墨宝赠人；懂行的人更珍视亲笔之作。'),
)
KEYS = tuple(row[0] for row in CRAFTS)
NAMES = {row[0]: row[1] for row in CRAFTS}
PROFESSION = {row[0]: row[2] for row in CRAFTS}
CRAFT_OF = {row[2]: row[0] for row in CRAFTS}
LEVELS = careers.LEVELS

# ----------------------------------------------------------------- proficiency


def stream(w, *parts):
    digest = hashlib.sha256(':'.join(map(str, (w.seed, 'crafts-v1') + parts)).encode()).digest()
    return random.Random(int.from_bytes(digest[:8], 'big'))


def prepare(actor):
    """The protagonist's own record; NPCs keep theirs in the trade record."""
    saved = actor.setdefault('crafts', {'version': VERSION, 'skill': {}})
    for key in KEYS:
        saved['skill'].setdefault(key, 0.)
    return saved


def value(actor, craft):
    if 'career' in actor:
        return float(actor['career']['experience'].get(PROFESSION[craft], 0.))
    return float(actor.get('crafts', {}).get('skill', {}).get(craft, 0.))


def level(actor, craft):
    xp = value(actor, craft)
    return next(row for row in reversed(LEVELS) if xp >= row[0])


def gain(actor, craft, amount):
    if 'career' in actor:
        result = careers.gain(actor, PROFESSION[craft], amount)
    else:
        skill = prepare(actor)['skill']
        before = skill[craft]
        after = round(min(100., before+amount), 4)
        skill[craft] = after
        result = {'before': before, 'after': after, 'gain': round(after-before, 4)}
    return {'craft': craft, 'name': NAMES[craft], **{k: result[k] for k in ('before', 'after', 'gain')}}


def practice(actor, craft, base):
    """Practice slows as one approaches mastery; it never stops entirely."""
    return gain(actor, craft, round(max(.05, base*(1-value(actor, craft)/120)), 4))


def best(actor):
    return max(KEYS, key=lambda k: (value(actor, k), -KEYS.index(k)))


def teaching_gain(teacher, craft):
    # Same law as an NPC's paid trade lesson (`learn_trade`).
    return 2+value(teacher, craft)*.03


def entry(actor, craft):
    xp = value(actor, craft)
    title = level(actor, craft)[1]
    following = next((row for row in LEVELS if row[0] > xp), None)
    name, profession, description = NAMES[craft], PROFESSION[craft], dict((r[0], r[3]) for r in CRAFTS)[craft]
    return dict(id=craft, name=name, profession=profession, description=description,
                value=round(xp, 2), level=title,
                next_level=dict(experience=following[0], title=following[1]) if following else None)


# --------------------------------------------------------------------- forging

FORGE_DIFFICULTY = {9: 0, 8: 8, 7: 18, 6: 28, 5: 40, 4: 52, 3: 64, 2: 76, 1: 85}
FORGE_MATERIAL_UNITS = {9: 1, 8: 1, 7: 2, 6: 2, 5: 3, 4: 3, 3: 4, 2: 4, 1: 5}
WOODEN = frozenset(('wood_sword',))
TEMPERS = (
    ('heavy', '重锻', '加厚加重，压盾更狠，收势稍慢。'),
    ('balanced', '均衡', '不偏不倚，火候好时更耐磨损。'),
    ('light', '轻锻', '削薄减重，出手更快、更省气力。'),
)
TEMPER_NAMES = {row[0]: row[1] for row in TEMPERS}
MOD_LIMITS = (.85, 1.15)
QUALITY_LABELS = ((.3, '粗糙'), (.5, '寻常'), (.7, '精良'), (.88, '上乘'), (1.01, '绝品'))
FORGE_POINTS, COMPOSE_POINTS, TREAT_POINTS = 3, 2, 2


def quality_label(q):
    return next(label for limit, label in QUALITY_LABELS if q < limit)


def forgeable():
    from . import items, weapons
    return [key for key, d in items.CATALOG.items() if d['category'] == 'weapon' and key in weapons.CATALOG]


def forge_fee(grade):
    return 2 + (9-grade)*3


def forge_material(template):
    return 'timber' if template in WOODEN else 'iron'


def modifiers(temper, q):
    """Bounded multipliers on the template's physical stats; damage stays within a grade step."""
    mods = {'mass': 1., 'handling': 1., 'cost': 1., 'damage': 1., 'pressure': 1.}
    wear = 1.
    if temper == 'heavy':
        mods.update(mass=1.06+.06*q, handling=.97, pressure=1.04+.06*q)
    elif temper == 'light':
        mods.update(mass=.95-.07*q, handling=1.04+.06*q, cost=.95)
    else:
        mods['handling'] = 1.+.06*q
        wear = 1-.4*q
    # Raw damage is linear in the coefficient (16c/15 in the reference law): at most +3%.
    mods['damage'] = 1+.03*max(0., (q-.5)*2)
    if q < .3:
        mods['handling'] *= .95+q/.3*.05
    lo, hi = MOD_LIMITS
    return {k: round(max(lo, min(hi, v)), 4) for k, v in mods.items()}, round(wear, 4)


def arm_strain(actor):
    wound = actor.get('wound') or {}
    return max((p['severity'] for k, p in (wound.get('parts') or {}).items()
                if k.startswith(('upper', 'forearm', 'hand')) and p['kind'] in ('fracture', 'maimed')), default=0.)


def forge_quality(w, actor, template, key):
    from . import items
    grade = items.CATALOG[template]['grade']
    skill = value(actor, 'forging')
    tool = .05 if any(i['template'] == 'hammer' for i in items.objects(w, owner_key(actor))) else 0.
    noise = stream(w, 'forge', key).uniform(-.08, .08)
    q = .35 + (skill-FORGE_DIFFICULTY[grade])*.012 + tool + noise - .15*arm_strain(actor)
    return round(max(.05, min(1., q)), 4)


def forge_reason(w, actor, template, temper='balanced'):
    from . import items
    if template not in forgeable():
        return '此物不能锻造'
    if temper not in TEMPER_NAMES:
        return '请选择锻造取向'
    d = items.CATALOG[template]
    need = FORGE_DIFFICULTY[d['grade']]-15
    if value(actor, 'forging') < need:
        return f"锻造造诣须达{need}"
    if actor['wealth'] < forge_fee(d['grade']):
        return f"需{forge_fee(d['grade'])}银炉火工费"
    if material_shortfall(w, actor, template):
        return f"{items.CATALOG[forge_material(template)]['name']}不足"
    return ''


def owner_key(actor):
    return 'player' if 'protagonist' in actor else actor['id']


def material_shortfall(w, actor, template):
    """Units neither in the bag nor buyable as bulk goods from the local market."""
    from . import economics, items
    units = FORGE_MATERIAL_UNITS[items.CATALOG[template]['grade']]
    material = forge_material(template)
    held = sum(1 for i in items.objects(w, owner_key(actor)) if i['template'] == material
               and i['id'] not in actor['equipment'].values())
    missing = max(0, units-held)
    if missing and economics.active(w):
        m = economics.market(w, economics.place_of(w, owner_key(actor)))
        bulk = missing*.5
        stock = economics.prepare(actor)['stock'][material]
        need = max(0., bulk-stock)
        fee = forge_fee(items.CATALOG[template]['grade'])
        if m['stock'][material] >= need and actor['wealth'] >= fee + need*economics.ask(m, material):
            return 0
    return missing


def consume_materials(w, actor, template):
    """Use bag materials first, then bulk market goods (0.5 bulk unit per piece)."""
    from . import economics, items
    units = FORGE_MATERIAL_UNITS[items.CATALOG[template]['grade']]
    material = forge_material(template)
    held = [i for i in items.objects(w, owner_key(actor)) if i['template'] == material
            and i['id'] not in actor['equipment'].values()][:units]
    used = [deepcopy(i) for i in held]
    for item in held:
        items.remove(w, item)
    bulk = {}
    cost = 0.
    missing = units-len(held)
    if missing:
        stock = economics.prepare(actor)['stock']
        amount = missing*.5
        short = max(0., amount-stock[material])
        if short:
            _, cost = economics.buy(w, owner_key(actor), material, short)
        stock[material] = economics.q(stock[material]-amount)
        economics.state(w)['consumed'][material] += amount
        economics.market(w, economics.place_of(w, owner_key(actor)))['used'][material] += amount
        bulk = {material: amount}
    return used, bulk, round(cost, 4)


def pay(w, actor, amount, place=None):
    from . import economics
    actor['wealth'] = round(actor['wealth']-amount, 4)
    if economics.active(w):
        economics.fee(w, economics.place_of(w, owner_key(actor)) if place is None else place, amount)
    else:
        w.items['market']['wealth'] += amount


def forge(w, actor, template, temper, source=None):
    """Create one forged weapon in the actor's bag. Caller has validated forge_reason."""
    from . import items
    d = items.CATALOG[template]
    key = str(w.items['next_id'])
    q = forge_quality(w, actor, template, key)
    used, bulk, material_cost = consume_materials(w, actor, template)
    fee = forge_fee(d['grade'])
    pay(w, actor, fee)
    mods, wear = modifiers(temper, q)
    item = items.create(w, owner_key(actor), template, source or f"{actor['name']}亲手锻造")
    item['craft'] = dict(version=VERSION, kind='forge', maker=owner_key(actor), maker_name=actor['name'],
                         skill=round(value(actor, 'forging'), 2), quality=q, quality_label=quality_label(q),
                         temper=temper, mods=mods, wear=wear, month=w.month)
    growth = practice(actor, 'forging', 1.5+(9-d['grade'])*.4)
    return item, dict(template=template, temper=temper, quality=q, mods=mods, wear=wear, fee=fee,
                      materials=used, bulk_inputs=bulk, material_cost=material_cost, practice=growth)


def mods_of(item):
    craft = item.get('craft') or {}
    return craft.get('mods') or {}


def wear_factor(item):
    return (item.get('craft') or {}).get('wear', 1.)


def price_factor(item):
    craft = item.get('craft') or {}
    return round(.7+.6*craft['quality'], 4) if craft.get('kind') == 'forge' else 1.


def label(item):
    craft = item.get('craft') or {}
    if craft.get('kind') == 'forge':
        return f"{craft['maker_name']}所铸 · {TEMPER_NAMES[craft['temper']]} · {craft['quality_label']}火候"
    if craft.get('kind') == 'brew':
        return f"{craft['maker_name']}所制 · 药力×{craft['potency']:g}"
    if craft.get('kind') == 'writing':
        return f"{craft['maker_name']}亲笔 · 《{craft['title']}》"
    return ''


# ------------------------------------------------------------------- repairing

SELF_REPAIR_CAP, SKILLED_REPAIR = 70, 15


def repair_cap(actor):
    return 100 if value(actor, 'forging') >= SKILLED_REPAIR else SELF_REPAIR_CAP


def mend_price(item):
    from . import items
    return max(2, math.ceil(items.spec(item)['price']*(100-item['durability'])/100*.25*1.5))


def mend_target(w, owner='player'):
    from . import items
    actor = items.actor(w, owner)
    equipped = set(actor['equipment'].values())
    rows = [i for i in items.objects(w, owner) if items.spec(i)['category'] in items.SLOTS and i['durability'] < 100]
    return min(rows, key=lambda i: (i['id'] not in equipped, i['durability'], int(i['id'])), default=None)


# ------------------------------------------------------------------- medicine

KIND_NAMES = {'cut': '外伤', 'fracture': '骨折', 'maimed': '重创'}
KIND_RANK = {'cut': 0, 'fracture': 1, 'maimed': 2}
SIDE_NAMES = {'Near': '近侧', 'Far': '远侧'}
PART_BASE = {'head': '头部', 'neck': '颈部', 'belly': '腹部', 'torso': '胸背', 'pelvis': '腰胯',
             'upper': '上臂', 'forearm': '前臂', 'hand': '手掌', 'thigh': '大腿', 'shin': '小腿', 'foot': '脚掌'}
LIMBS = frozenset(k for k in PARTS if k.startswith(('upper', 'forearm', 'hand', 'thigh', 'shin', 'foot')))
SET_BONE = 25
BREW_SKILL = {'salve': 0, 'pill': 15}


def part_name(key):
    for side, word in SIDE_NAMES.items():
        if key.endswith(side):
            return word+PART_BASE[key[:-len(side)]]
    return PART_BASE[key]


def battle_parts(packet):
    """Per-part injuries from a settled physical frame; an absent frame records none."""
    parts = ((packet or {}).get('reference') or {}).get('parts') or {}
    result = {}
    for key, p in sorted(parts.items()):
        if key not in PARTS or not isinstance(p, dict):
            continue
        loss = max(0., min(1., 1-p.get('health', PARTS[key])/PARTS[key]))
        if p.get('severed'):
            result[key] = {'severity': 1., 'kind': 'maimed'}
        elif key in LIMBS and loss >= .6:
            result[key] = {'severity': round(loss, 4), 'kind': 'fracture'}
        elif loss >= .25:
            result[key] = {'severity': round(loss, 4), 'kind': 'cut'}
    return result


def months(part):
    kind, severity = part['kind'], part['severity']
    return {'cut': 1+round(severity*3), 'fracture': 3+round(severity*4), 'maimed': 8}[kind]


def injure(actor, parts, cause, base_months=0):
    """Merge new part injuries into the actor's wound; never shortens an existing one."""
    old = actor.get('wound') or {}
    merged = deepcopy(old.get('parts') or {})
    for key, p in parts.items():
        prior = merged.get(key)
        if not prior:
            merged[key] = dict(p)
        else:
            merged[key] = {'severity': round(max(prior['severity'], p['severity']), 4),
                           'kind': max(prior['kind'], p['kind'], key=KIND_RANK.get)}
    remaining = max([old.get('remaining', 0), base_months] + [months(p) for p in parts.values()])
    wound = dict(remaining=remaining, event_id=cause)
    if merged:
        wound['parts'] = merged
    actor['wound'] = wound
    return wound


def settle_battle(battle, actors):
    """Shared by robbery, assault and sect hunts. Spars never call this."""
    packets = ((battle.get('frame') or {}).get('fighters')) or []
    result = []
    for side, (actor, fighter) in enumerate(zip(actors, battle['fighters'])):
        lost = 1-fighter['vigor']/fighter['vigor_max']
        parts = battle_parts(packets[side] if side < len(packets) else None)
        if lost > .1 or parts:
            injure(actor, parts, battle['cause'], 2+round(lost*3) if lost > .1 else 0)
        result.append(parts)
    return result


def work_capacity(n):
    """Arm fractures halve manual output while they last."""
    if n.get('career', {}).get('profession') not in ('匠人', '渔人', '猎户', '农人'):
        return 1.
    return round(1-.5*min(1., arm_strain(n)), 4)


def travel_strain(n):
    wound = n.get('wound') or {}
    leg = max((p['severity'] for k, p in (wound.get('parts') or {}).items()
               if k.startswith(('thigh', 'shin', 'foot')) and p['kind'] in ('fracture', 'maimed')), default=0.)
    return round(1+min(1., leg), 4)


def worst_part(wound):
    parts = (wound or {}).get('parts') or {}
    if not parts:
        return None
    return max(parts, key=lambda k: (KIND_RANK[parts[k]['kind']], parts[k]['severity'], k))


def treat_parts(wound, skill, potency=1.):
    """Treat the worst injured part. Bones need a practised hand; without it only pain eases."""
    key = worst_part(wound)
    if key is None:
        return None
    part = wound['parts'][key]
    before = deepcopy(part)
    power = (.25+skill*.006)*potency
    if part['kind'] in ('fracture', 'maimed') and skill < SET_BONE:
        wound['remaining'] = max(1, wound['remaining']-1)
        outcome = 'relieved'
    else:
        part['severity'] = round(max(0., part['severity']-power), 4)
        if part['kind'] == 'fracture':
            part['kind'] = 'cut'
            outcome = 'set'
        elif part['kind'] == 'maimed' and part['severity'] < .5:
            part['kind'] = 'fracture'
            outcome = 'treated'
        else:
            outcome = 'treated'
        if part['severity'] < .15:
            del wound['parts'][key]
        needed = max((months(p) for p in wound['parts'].values()), default=1)
        wound['remaining'] = max(1, min(wound['remaining'], needed))
        if not wound['parts']:
            del wound['parts']
    return dict(part=key, part_name=part_name(key), before=before,
                after=deepcopy(wound.get('parts', {}).get(key)), outcome=outcome, power=round(power, 4))


def treat(healer, patient, potency=1.):
    """One treatment of the patient's health, illness and worst injury."""
    skill = value(healer, 'medicine')
    before = dict(health=patient['health'], wound=deepcopy(patient.get('wound')),
                  illness=deepcopy(patient.get('illness')))
    healing = round((4+skill*.1)*potency, 4)
    patient['health'] = round(min(100., patient['health']+healing), 4)
    power = (.25+skill*.006)*potency
    if patient.get('illness'):
        patient['illness']['severity'] = round(patient['illness']['severity']*(1-min(.6, power)), 4)
    part = None
    wound = patient.get('wound')
    if wound and wound.get('parts'):
        part = treat_parts(wound, skill, potency)
    elif wound:
        wound['remaining'] = max(0, wound['remaining']-(1+round(power*4)))
        if wound['remaining'] <= 0:
            patient['wound'] = None
    return dict(skill=round(skill, 2), healing=healing, part=part, before=before,
                after=dict(health=patient['health'], wound=deepcopy(patient.get('wound')),
                           illness=deepcopy(patient.get('illness'))))


def needs_care(n):
    return bool(n.get('wound') or n.get('illness') or n.get('health', 100) < 60)


def brew_reason(actor, recipe):
    need = BREW_SKILL.get(recipe, 0)
    if value(actor, 'medicine') < need:
        return f'医术造诣须达{need}'
    return ''


def potency(actor):
    return round(min(1.4, 1+value(actor, 'medicine')*.004), 3)


def wound_view(actor):
    wound = actor.get('wound') or {}
    parts = [dict(key=k, name=part_name(k), kind=p['kind'], kind_name=KIND_NAMES[p['kind']],
                  severity=p['severity'], months=months(p))
             for k, p in sorted((wound.get('parts') or {}).items(), key=lambda kv: (-KIND_RANK[kv[1]['kind']], -kv[1]['severity']))]
    return dict(wounded=bool(wound), remaining=wound.get('remaining', 0), parts=parts,
                illness=bool(actor.get('illness')), health=actor.get('health'))


def player_month(w):
    """The protagonist's wounds now run their course like everyone else's."""
    wound = w.player.get('wound')
    if not wound:
        return
    wound['remaining'] -= 1
    if wound['remaining'] <= 0:
        w.player['wound'] = None


# ------------------------------------------------------------------- letters

WRITING_GRADES = ((10, 9), (22, 8), (35, 7), (48, 6), (62, 5), (76, 4), (101, 3))
TITLES = ('秋水', '松风', '听雨', '归雁', '山居', '江月', '竹影', '梅花', '寒江', '春晓', '望岳', '夜泊',
          '怀古', '题壁', '静夜', '晚晴', '渔歌', '别离', '长亭', '闻笛', '落霞', '孤鸿', '野渡', '秋声')
COMPOSE_FEE = 1.


def writing_grade(score):
    return next(grade for limit, grade in WRITING_GRADES if score < limit)


def clean_title(title):
    if title is None:
        return None
    if not isinstance(title, str):
        raise ValueError('题目须为文字')
    title = ''.join(ch for ch in title.strip() if ch.isprintable() and ch not in '《》<>{}[]"\'\\')
    if not 1 <= len(title) <= 12:
        raise ValueError('题目须为1至12字')
    return title


def compose(w, actor, title=None):
    from . import items
    key = str(w.items['next_id'])
    rng = stream(w, 'compose', key)
    score = value(actor, 'letters') + rng.uniform(-10, 10)
    grade = writing_grade(score)
    title = title or rng.choice(TITLES)
    pay(w, actor, COMPOSE_FEE)
    item = items.create(w, owner_key(actor), f'writing_{grade}', f"{actor['name']}亲笔书写")
    item['craft'] = dict(version=VERSION, kind='writing', maker=owner_key(actor), maker_name=actor['name'],
                         skill=round(value(actor, 'letters'), 2), title=title, month=w.month)
    growth = practice(actor, 'letters', 1.2)
    return item, dict(grade=grade, title=title, score=round(score, 2), fee=COMPOSE_FEE, practice=growth)


def appreciation(n):
    """How much a reader values a written piece: their own learning and regard for tradition."""
    belief = (n.get('beliefs') or {}).get('tradition', 50)
    value_ = .6 + value(n, 'letters')/100 + (belief-50)/200 + (.3 if n.get('job') in ('书生', '艺人') else 0.)
    return round(max(.3, min(1.8, value_)), 4)


def appreciation_words(a):
    return '对方细细品读，爱不释手' if a >= 1.3 else '对方欣然收下' if a >= .9 else '对方似乎不甚在意'


# ---------------------------------------------------------------------- views

def options(w, actor):
    from . import items
    tempers = [dict(id=k, name=n, description=d) for k, n, d in TEMPERS]
    rows = []
    for key in forgeable():
        d = items.CATALOG[key]
        rows.append(dict(template=key, name=d['name'], grade=d['grade'], **items.grade_info(d['grade']),
                         difficulty=FORGE_DIFFICULTY[d['grade']], fee=forge_fee(d['grade']),
                         material=items.CATALOG[forge_material(key)]['name'],
                         material_template=forge_material(key), units=FORGE_MATERIAL_UNITS[d['grade']],
                         reason=forge_reason(w, actor, key)))
    rows.sort(key=lambda r: (-r['grade'], r['template']))
    return tempers, rows


def view(w):
    from . import items
    actor = w.player
    h = actor['protagonist']
    tempers, rows = options(w, actor)
    locked = bool(w.battle and w.battle['status'] == 'active')
    held = {}
    for i in items.objects(w, 'player'):
        if items.spec(i)['category'] == 'material':
            held[i['template']] = held.get(i['template'], 0)+1
    recipes = [dict(template=k, name=items.CATALOG[k]['name'], skill=BREW_SKILL.get(k, 0),
                    reason=brew_reason(actor, k), ingredients={items.CATALOG[t]['name']: q for t, q in v.items()})
               for k, v in items.RECIPES.items()]
    return dict(version=VERSION, crafts=[entry(actor, k) for k in KEYS], points=h.get('points', 0),
                wealth=actor['wealth'], locked=locked, created=h.get('created', False),
                forge=dict(points=FORGE_POINTS, tempers=tempers, templates=rows),
                compose=dict(points=COMPOSE_POINTS, fee=COMPOSE_FEE,
                             reason=reason(w, 'compose'), titles=list(TITLES[:6])),
                treat=dict(points=TREAT_POINTS, reason=reason(w, 'treat_self'), potency=potency(actor),
                           set_bone=SET_BONE),
                brew=dict(recipes=recipes, potency=potency(actor)),
                repair=dict(cap=repair_cap(actor), skilled=SKILLED_REPAIR),
                materials=held, wound=wound_view(actor))


def reason(w, operation, template=None, temper='balanced'):
    from . import items
    h = w.player['protagonist']
    if not h.get('created'):
        return '请先创建主角'
    if w.battle and w.battle['status'] == 'active':
        return '请先结束当前交手'
    cost = {'forge': FORGE_POINTS, 'compose': COMPOSE_POINTS, 'treat_self': TREAT_POINTS}[operation]
    if h.get('points', 0) < cost:
        return f'需要{cost}行动力'
    if operation == 'forge':
        return forge_reason(w, w.player, template, temper)
    if operation == 'compose':
        if w.player['wealth'] < COMPOSE_FEE:
            return f'需{COMPOSE_FEE:g}银纸墨'
        return ''
    if not needs_care(w.player):
        return '眼下没有需要调治的伤病'
    return ''


def perform(w, body):
    from . import items, protagonist
    allowed = {'operation', 'template', 'temper', 'title'}
    if not isinstance(body, dict) or set(body)-allowed:
        raise ValueError('技艺操作只接受既定事项')
    operation = body.get('operation')
    if operation not in ('forge', 'compose', 'treat_self'):
        raise ValueError('未知技艺操作')
    temper = body.get('temper', 'balanced')
    template = body.get('template')
    title = clean_title(body.get('title')) if operation == 'compose' else None
    why = reason(w, operation, template, temper)
    if why:
        raise ValueError(why)
    h = w.player['protagonist']
    actor = w.player
    name = actor['name']
    before = dict(wealth=actor['wealth'], points=h['points'])
    if operation == 'forge':
        item, facts = forge(w, actor, template, temper)
        d = items.spec(item)
        q = facts['quality']
        text = (f"{name}起炉锻打，{TEMPER_NAMES[temper]}成一柄{d['name']}，火候{quality_label(q)}，"
                f"支付{facts['fee']}银炉火工费；锻造造诣增长{facts['practice']['gain']:.2f}。")
        if q >= .88:
            actor['reputation'] += 1
            facts['renown'] = 1
            text += '此兵锻纹精绝，名声渐起。'
        kind = 'craft_forge'
    elif operation == 'compose':
        item, facts = compose(w, actor, title)
        d = items.spec(item)
        text = (f"{name}研墨挥毫，写成《{facts['title']}》一幅，为{d['name']}（{items.GRADE_NAMES[d['grade']]}），"
                f"耗用{COMPOSE_FEE:g}银纸墨；诗书造诣增长{facts['practice']['gain']:.2f}。")
        kind = 'craft_compose'
    else:
        salve = next((i for i in items.objects(w, 'player') if i['template'] == 'salve'
                      and i['id'] not in actor['equipment'].values()), None)
        boost = 1.3 if salve else 1.
        facts = treat(actor, actor, boost)
        if salve:
            facts['salve'] = deepcopy(salve)
            items.remove(w, salve)
        facts['practice'] = practice(actor, 'medicine', .8)
        part = facts['part']
        text = f"{name}为自己调治伤病，恢复{facts['healing']:g}体力"
        if part:
            text += '，' + {'relieved': f"{part['part_name']}骨伤未能接正，只得止痛", 'set': f"接正{part['part_name']}骨伤",
                            'treated': f"处理{part['part_name']}伤处"}[part['outcome']]
        text += ('，敷用一份金疮药' if salve else '') + f"；医术造诣增长{facts['practice']['gain']:.2f}。"
        item = None
        kind = 'craft_treat'
    cost = {'forge': FORGE_POINTS, 'compose': COMPOSE_POINTS, 'treat_self': TREAT_POINTS}[operation]
    h['points'] -= cost
    h['revision'] += 1
    facts.update(operation=operation, player_points=cost, before=before,
                 item_id=item['id'] if item else None, item=deepcopy(item) if item else None)
    e = protagonist.record(w, kind, text, facts)
    if item:
        item['origin_event'] = item['last_event'] = e['id']
        item['craft']['event'] = e['id']
    w.checkpoint()
    return dict(event=e, crafts=view(w))


# ------------------------------------------------------------- NPC craftspeople

def market_forge(w, n, event):
    """A practised smith now and then forges a blade and sells it to the trading house."""
    from . import items
    if n['job'] != '匠人' or value(n, 'forging') < 35 or len(n['inventory']) >= 9 or n['wealth'] < 24:
        return None
    rng = stream(w, 'market-forge', n['id'], w.month)
    choices = [k for k in forgeable() if FORGE_DIFFICULTY[items.CATALOG[k]['grade']]-15 <= value(n, 'forging')
               and not forge_reason(w, n, k)]
    if not choices:
        return None
    # A smith works near the edge of their skill rather than whittling practice swords.
    choices = sorted(choices, key=lambda k: (-items.CATALOG[k]['grade'], k))[-4:]
    template = rng.choice(choices)
    temper = rng.choice([row[0] for row in TEMPERS])
    item, facts = forge(w, n, template, temper, f"{n['name']}铸造售卖")
    price = items.sell_price(item)
    sold = w.items['market']['wealth'] >= price
    if sold:
        w.items['market']['wealth'] -= price
        n['wealth'] = round(n['wealth']+price, 4)
        items.transfer(w, item, 'market')
    d = items.spec(item)
    text = (f"{n['name']}起炉{TEMPER_NAMES[temper]}{d['name']}一柄，火候{quality_label(facts['quality'])}"
            + (f"，以{price:g}银售予货栈。" if sold else '，暂留身边。'))
    e = w.emit('craft_forge', [n['id']], text, .25, [event['id']],
               {**facts, 'item_id': item['id'], 'sold': sold, 'silver': price if sold else 0, 'producer': n['id']})
    item['origin_event'] = item['last_event'] = e['id']
    item['craft']['event'] = e['id']
    return e
