"""Grade-based new curricula, fixed native lineage and world energy.

Legacy skill/cultivation/loadouts stay retired. New arts apply selective
passive ability bonuses and are synchronized with current social/sect grade.
"""
from copy import deepcopy
import hashlib
from . import combat_attributes, martial_catalog

ARTS = {}
REALMS = []
GATES = []
KIND_NAMES = {}
RETIRED_ACTIONS = frozenset(('train','mentor','teach','faction_study','faction_guide','read_item'))


def bounded(value, lo=0, hi=100):
    return round(max(lo, min(hi, value)), 4)


def initialize(actor, player=False):
    combat_attributes.initialize(actor)
    actor.setdefault('health', 100.)
    actor.pop('skill', None)
    actor['read_books'] = {}
    if isinstance(actor.get('plan'), dict) and actor['plan'].get('intent') == 'sect_training':
        actor['plan'] = None
    energy = actor.get('martial', {}).get('stamina', 100.)
    curriculum = actor.get('martial', {}).get('curriculum')
    actor['martial'] = {'stamina': bounded(energy), 'arts': {}}
    if isinstance(curriculum, dict) and curriculum.get('version') == martial_catalog.VERSION:
        actor['martial']['curriculum'] = curriculum


def retired(*args, **kwargs):
    raise ValueError('旧修习与装配指令已移除；新功法按身份与品阶自动掌握')


learn = equip = practice = auto_practice = transmit = retired


def recover(actor, amount=8):
    actor['martial']['stamina'] = bounded(actor['martial']['stamina'] + amount)


def resolve(w, actor, player=False):
    """Pure, deterministic entitlement calculation; never consumes world RNG."""
    from .atlas import PLACES, FACTIONS
    from . import careers, social_ranks
    previous = actor.get('martial', {}).get('curriculum', {})
    hero = actor.get('protagonist', {}) if player else {}
    if player and not hero.get('created'):
        return None
    origin = previous.get('origin_place')
    if origin is None:
        visits = actor.get('visited_places', [])
        origin = hero.get('birth_place', hero.get('place')) if player else visits[0] if visits else actor['place']
    country = previous.get('origin_country', PLACES[origin]['country'])
    native_schools = sorted(f['key'] for f in FACTIONS if PLACES[f['place']]['country'] == country)
    civil = previous.get('civil_school')
    if civil not in native_schools:
        key = 'player' if player else actor['id']
        ticket = hashlib.sha256(f'{w.seed}:{key}:{country}:martial-lineage-v1'.encode()).digest()
        civil = native_schools[int.from_bytes(ticket[:8],'big') % len(native_schools)] if native_schools else None
    profile = social_ranks.player_identity(w) if player else actor.get('final_identity') if not actor['alive'] else None
    profile = profile or careers.identity(w, actor)
    grade = profile['grade']
    faction = hero.get('faction') if player else actor['faction']
    member = faction is not None and w.factions[faction]['kind'] == 'sect'
    school = w.factions[faction]['key'] if member else civil
    known = [key for key in martial_catalog.SCHOOL_ARTS.get(school, [])
             if martial_catalog.ARTS[key]['grade'] >= grade] if member else ([f'{school}_g{grade}'] if school else [])
    return dict(version=martial_catalog.VERSION, origin_place=origin, origin_country=country,
                civil_school=civil, system='sect' if member else 'civil', school=school,
                grade=grade, known=known)


def sync_actor(w, actor, player=False):
    resolved = resolve(w, actor, player)
    if resolved is None:
        return False
    if actor['martial'].get('curriculum') == resolved:
        return False
    actor['martial']['curriculum'] = resolved
    return True


def synchronize(w):
    changed = False
    for actor in w.npcs.values():
        if actor['alive'] or not actor['martial'].get('curriculum'):
            changed = sync_actor(w, actor) or changed
    if 'protagonist' in w.player:
        changed = sync_actor(w, w.player, True) or changed
    return changed


def view(actor, equipment=None, world=None, player=False):
    effective = actor
    if world is not None:
        effective = {**actor, 'martial': {**actor['martial'], 'curriculum': resolve(world, actor, player) or {}}}
    c = effective['martial'].get('curriculum', {})
    arts = [deepcopy(martial_catalog.ARTS[key]) for key in c.get('known', []) if key in martial_catalog.ARTS]
    info = deepcopy(c)
    if world is not None and c:
        from .atlas import PLACES
        info.update(origin_name=PLACES[c['origin_place']]['name'], country_name=world.countries[c['origin_country']]['name'])
    return {'stamina': actor['martial']['stamina'], 'arts': arts, 'curriculum': info,
            'combat': combat_attributes.view(effective, equipment)}
