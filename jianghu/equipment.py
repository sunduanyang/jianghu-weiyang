"""Equipment state, effective effects and read-only replacement previews."""
from . import combat_attributes, weapons


BONUS_KEYS = ('defense', 'income', 'training', 'recovery')
EFFECTS = (
    ('mass', '物理质量', '千克', 0), ('handling', '操控系数', '×', 1),
    ('cost', '耗气系数', '', -1), ('damage', '杀伤系数', '×', 1),
    ('pressure', '压盾系数', '×', 1), ('durability', '耐久', '/100', 1),
    ('defense', '装备防护', '', 1), ('damage_reduction', '受伤减免', '%', 1),
)


def normalize(w, owner):
    """Clear invalid bindings without changing or creating any possessions."""
    from . import items
    actor = items.actor(w, owner)
    valid = {}
    for slot, item_id in actor['equipment'].items():
        item = w.items['objects'].get(str(item_id))
        if (slot in items.SLOTS and item and item['owner'] == str(owner)
                and item['id'] in actor['inventory'] and items.spec(item)['category'] == slot):
            valid[slot] = item['id']
    actor['equipment'] = valid


def resolve(w, owner, loadout=None):
    from . import items
    actor = items.actor(w, owner)
    loadout = actor['equipment'] if loadout is None else loadout
    weapon_id = loadout.get('weapon')
    weapon = w.items['objects'].get(str(weapon_id))
    usable_weapon = (weapon and weapon['owner'] == str(owner)
                     and weapon['id'] in actor['inventory'] and weapon['durability'] > 0
                     and items.spec(weapon)['category'] == 'weapon')
    profile = weapons.CATALOG[weapon['template']]['profile'] if usable_weapon else 'unarmed'
    two_handed = weapons.PROFILES[profile]['support_grip'] > 0
    result = []
    for slot, name in items.SLOTS.items():
        item_id = loadout.get(slot)
        item = w.items['objects'].get(str(item_id))
        if not (item and item['owner'] == str(owner) and item['id'] in actor['inventory']
                and items.spec(item)['category'] == slot):
            item = None
        status, reason = 'empty', '尚未装备'
        if item:
            if item['durability'] <= 0:
                status, reason = 'broken', '已损坏，修补后恢复效果'
            elif slot == 'shield' and two_handed:
                status, reason = 'suppressed', '双手兵器占用副手，换回单手兵器后自动恢复'
            else:
                status, reason = 'active', '装备效果生效中'
        result.append(dict(slot=slot, name=name, item=item, status=status,
                           active=status == 'active', reason=reason))
    return result


def bonuses(w, owner, loadout=None):
    from . import items
    result = dict.fromkeys(BONUS_KEYS, 0.)
    for entry in resolve(w, owner, loadout):
        if entry['active']:
            spec = items.spec(entry['item'])
            for key in result:
                result[key] += spec.get(key, 0)
    return result


def effects(w, owner, loadout=None):
    from . import items
    actor = items.actor(w, owner)
    resolved = resolve(w, owner, loadout)
    weapon = next(entry for entry in resolved if entry['slot'] == 'weapon')
    item = weapon['item'] if weapon['active'] else None
    stats = weapons.attributes(item) if item else {
        **{key: weapons.PROFILES['unarmed'][key] for key in weapons.STAT_KEYS}, 'durability': 0}
    gear = bonuses(w, owner, loadout)
    combat = combat_attributes.parameters(combat_attributes.ratings(actor), gear)
    return dict(weapon_name=items.spec(item)['name'] if item else '徒手', weapon_attributes=stats,
                bonuses=gear, combat_parameters=combat,
                values={**stats, **gear, 'damage_reduction': round(combat['damage_reduction'] * 100, 3)})


def snapshot(w, owner):
    from . import items
    entries = resolve(w, owner)
    slots = []
    for entry in entries:
        item = entry['item']
        data = {key: value for key, value in entry.items() if key != 'item'}
        data['item'] = ({**items.describe(item), **items.grade_info(items.spec(item)['grade'])}
                        if item else None)
        slots.append(data)
    return dict(slots=slots, equipped_count=sum(e['item'] is not None for e in entries),
                active_count=sum(e['active'] for e in entries), effects=effects(w, owner))


def preview(w, owner, item_id):
    from . import items
    actor = items.actor(w, owner)
    item = items.get(w, owner, item_id)
    slot = items.spec(item)['category']
    if slot not in items.SLOTS:
        raise ValueError('此物不能装备')
    loadout = {**actor['equipment'], slot: item['id']}
    current, after = effects(w, owner), effects(w, owner, loadout)
    keys = ({*weapons.ATTRIBUTE_KEYS} if slot == 'weapon' else set()) | {
        'defense', 'damage_reduction'}
    changes = []
    for key, name, unit, direction in EFFECTS:
        if key not in keys:
            continue
        before, value = current['values'][key], after['values'][key]
        delta = round(value - before, 4)
        if delta or key in (weapons.ATTRIBUTE_KEYS if slot == 'weapon' else ()):
            changes.append(dict(id=key, name=name, unit=unit, before=before, after=value,
                                delta=delta, direction=direction))
    warnings = []
    before_states = {e['slot']: e for e in resolve(w, owner)}
    for entry in resolve(w, owner, loadout):
        before = before_states[entry['slot']]
        if entry['item'] and entry['status'] == 'suppressed':
            warnings.append(entry['name'] + '：' + entry['reason'])
        elif before['status'] == 'suppressed' and entry['active']:
            warnings.append(entry['name'] + '恢复生效')
    return dict(slot=slot, replaces=actor['equipment'].get(slot), changes=changes,
                warnings=warnings, can_equip=item['durability'] > 0,
                equipped=actor['equipment'].get(slot) == item['id'])


def view(w, owner='player'):
    from . import items
    actor = items.actor(w, owner)
    data = snapshot(w, owner)
    candidates = []
    for item in items.objects(w, owner):
        spec = items.spec(item)
        if spec['category'] not in items.SLOTS:
            continue
        candidates.append({**items.describe(item), **items.grade_info(spec['grade']),
                           'slot': spec['category'], 'slot_name': items.SLOTS[spec['category']],
                           'repair_price': items.repair_price(item),
                           'operations': items.operations(w, owner, item),
                           'preview': preview(w, owner, item['id'])})
    locked = bool(w.battle and w.battle['status'] == 'active')
    return {**data, 'owner': str(owner), 'name': actor['name'], 'wealth': actor.get('wealth', 0),
            'alive': actor.get('alive', True), 'locked': locked,
            'lock_reason': '切磋进行中，结束后可整理装备' if locked else '',
            'items': candidates, 'weapon_attribute_catalog': weapons.attribute_catalog()}
