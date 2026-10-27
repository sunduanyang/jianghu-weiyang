"""Character combat abilities; weapon dynamics and AI decisions stay separate.

Baseline tuning is shared with the Unity client. Migration never uses the
world RNG and preserves custom ability distributions.
"""
from copy import deepcopy
import json
from pathlib import Path
from . import martial_catalog

VERSION = 1
TUNING = json.loads(Path(__file__).with_name('combat_tuning.json').read_text(encoding='utf-8'))
DEFAULT_ATTRIBUTE = TUNING['default_attribute']
CATALOG = (
    ('constitution', '体魄', '决定气血上限。'),
    ('strength', '膂力', '决定手臂驱动力与关节扭矩，帮助带动兵器。'),
    ('power', '劲力', '决定相同接触条件下的伤害倍率。'),
    ('dexterity', '灵巧', '决定运械速度与预设招式的出手、收招节奏。'),
    ('footwork', '身法', '决定移步、起停、转向和进身能力。'),
    ('endurance', '耐力', '决定气力上限、行动消耗和疲劳增长。'),
    ('breathing', '调息', '决定回气速度、回气等待和疲劳消退。'),
    ('rooting', '根基', '决定身体支撑、架势容量、受冲击损失与恢复。'),
    ('protection', '护体', '减少命中后受到的伤害，衣甲另加防护。'),
    ('resilience', '韧性', '减轻受击与破防硬直，减轻受伤后的发力衰减。'),
)
KEYS = tuple(row[0] for row in CATALOG)
DEFAULT_PHYSIQUE = dict(height_cm=170., weight_kg=65., arm_span_cm=170.)


def initialize(actor):
    saved = actor.setdefault('combat_attributes', {'version': VERSION, 'base': {}})
    base = saved['base']
    # Upgrade the former generated uniform baseline once, preserving custom builds.
    if saved.get('baseline_version', 1) < TUNING['version']:
        if len(base) == len(KEYS) and all(base.get(k) == 50. for k in KEYS):
            base.update(dict.fromkeys(KEYS, DEFAULT_ATTRIBUTE))
        saved['baseline_version'] = TUNING['version']
    for key in KEYS:
        base.setdefault(key, DEFAULT_ATTRIBUTE)
    actor.setdefault('combat_physique', deepcopy(DEFAULT_PHYSIQUE))


def base_ratings(actor):
    base = actor.get('combat_attributes', {}).get('base', {})
    return {key: round(max(0., min(100., base.get(key, DEFAULT_ATTRIBUTE))), 2) for key in KEYS}


def ratings(actor):
    base = base_ratings(actor)
    bonus = martial_catalog.bonuses(actor)
    return {key: round(min(100., base[key]+bonus[key]), 2) for key in KEYS}


def rating(actor):
    """Derived social/occupation comparison, never a saved eleventh ability."""
    base = actor.get('combat_attributes', {}).get('base', {})
    return round(max(0., min(100., sum(base.get(k, DEFAULT_ATTRIBUTE) for k in KEYS)/len(KEYS))), 2)


def physique(actor):
    raw = actor.get('combat_physique', {})
    limits = {'height_cm': (140, 210), 'weight_kg': (40, 120), 'arm_span_cm': (140, 220)}
    return {k: max(lo, min(hi, float(raw.get(k, DEFAULT_PHYSIQUE[k]))))
            for k, (lo, hi) in limits.items()}


def parameters(scores, gear=None):
    gear = gear or {}
    c, s, p, d, f, e, b, r, g, v = (scores[k] for k in KEYS)
    result = dict(
        max_health=20+c*1.2, force_scale=.7+s*.006,
        damage_scale=min(2.2, .65+p*.007),
        handling_scale=.75+d*.005, movement_scale=.8+f*.004,
        stamina_max=60+e*.8, effort_scale=1.25-e*.005,
        fatigue_growth=1.25-e*.005, fatigue_recovery=.65+b*.007,
        regen_scale=.65+b*.007, recovery_delay_scale=1.25-b*.005,
        stability_scale=.75+r*.005, balance_max=70+r*.6,
        balance_loss_scale=1.25-r*.005, balance_regen=8+r*.24,
        damage_reduction=min(.45, .015+g*.0017+gear.get('defense', 0)*.018),
        stun_scale=1.25-v*.005, injury_strength=.28+v*.004,
        injury_duration=.42-v*.0028, strength_recovery=1+v*.016)
    for key, multiplier in (('movement_scale','movement_multiplier'), ('effort_scale','effort_multiplier'),
                            ('regen_scale','regen_multiplier'), ('recovery_delay_scale','recovery_delay_multiplier'),
                            ('fatigue_growth','fatigue_growth_multiplier'), ('fatigue_recovery','fatigue_recovery_multiplier')):
        result[key] *= TUNING[multiplier]
    return {k: round(value, 4) for k, value in result.items()}


def view(actor, gear=None):
    scores = ratings(actor)
    base, bonus = base_ratings(actor), martial_catalog.bonuses(actor)
    return dict(version=VERSION, attributes=[dict(id=k, name=name, value=scores[k],
        base=base[k], bonus=bonus[k], applied_bonus=round(scores[k]-base[k],2),
        description=description) for k, name, description in CATALOG],
        ratings=scores, parameters=parameters(scores, gear), physique=physique(actor))
