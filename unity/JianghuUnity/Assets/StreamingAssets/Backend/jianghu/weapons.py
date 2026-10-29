"""Item weapon attributes; collision geometry remains private to each type."""
from copy import deepcopy
import json
from pathlib import Path


VERSION = 1
PROFILES = json.loads(Path(__file__).with_name('physical_profiles.json').read_text(encoding='utf-8'))
CATALOG = json.loads(Path(__file__).with_name('weapon_catalog.json').read_text(encoding='utf-8'))
STAT_KEYS = ('mass', 'handling', 'cost', 'damage', 'pressure')
ATTRIBUTE_KEYS = (*STAT_KEYS, 'durability')
ATTRIBUTES = (
    ('mass', '物理质量', '千克', '影响碰撞能量、挥动惯性和耗气。'),
    ('handling', '操控系数', '×', '影响运械及预设招式的出手、收招节奏。'),
    ('cost', '耗气系数', '', '影响预设攻击与持续挥动的气力消耗。'),
    ('damage', '杀伤系数', '×', '在接触能量相同时影响命中伤害。'),
    ('pressure', '压盾系数', '×', '影响击中盾牌时的架势冲击。'),
    ('durability', '耐久', '/100', '归零后兵器失效，可通过修补恢复。'),
)


def attribute_catalog():
    return [dict(id=key, name=name, unit=unit, description=description)
            for key, name, unit, description in ATTRIBUTES]


def attributes(item):
    """Derive template stats without adding duplicated fields to saved instances.

    A forged instance stores only bounded multipliers (`craft.mods`); items
    without them return exactly the template values.
    """
    data = CATALOG[item['template']]
    mods = (item.get('craft') or {}).get('mods') or {}
    stats = {key: (round(data[key]*mods[key], 4) if key in mods else data[key]) for key in STAT_KEYS}
    return {**stats, 'durability': item['durability']}


def profile(item):
    data = CATALOG[item['template']]
    return {**deepcopy(PROFILES[data['profile']]), 'id': data['profile'], **attributes(item)}
