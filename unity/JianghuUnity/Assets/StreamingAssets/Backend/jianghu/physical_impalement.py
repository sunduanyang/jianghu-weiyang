"""Checkpoint contract for the recovered organ-trigger / fixed-joint sequence.

This validates state; Unity owns overlap detection and the physics clock.
"""

ORGANS = {'Brain': 'head', 'Guts': 'belly', 'Ribcage': 'torso'}
MAX_ACCUMULATED_DAMAGE = 600  # 3 accepted impacts, local <=100, vital multiplier <=2
MAX_DURATION = 1 + MAX_ACCUMULATED_DAMAGE * .1
JOINT_FIELDS = [('anchor_x', -20000, 20000), ('anchor_y', -20000, 20000),
                ('connected_x', -20000, 20000), ('connected_y', -20000, 20000)]


def validate(data, previous, number, fields):
    """Return sanitized source_impalement or None for a pre-upgrade snapshot.

    Call with the value under reference.source_impalement and its previous
    value. Missing a previously stored version is a rejected downgrade.
    """
    if data is None and previous is None:
        return None
    if (not isinstance(data, dict) or type(data.get('version')) is not int
            or data['version'] != 1 or type(data.get('stage')) is not int
            or data['stage'] not in (0, 1, 2, 3)
            or type(data.get('attacker')) is not int):
        raise ValueError('内脏穿刺状态版本或阶段不合法')
    stage = data['stage']
    accumulated = number(data.get('accumulated_damage'), 0,
                         MAX_ACCUMULATED_DAMAGE + .001, 'impale accumulated damage')
    duration = number(data.get('duration'), 0, MAX_DURATION + .001, 'impale duration')
    wait = number(data.get('wait'), 0,
                  (0, .02501, duration + .00001, .25001)[stage], 'impale wait')
    organ, slot, attacker = data.get('organ'), data.get('slot'), data['attacker']
    result = dict(version=1, accumulated_damage=accumulated, stage=stage,
                  wait=wait, duration=duration, organ=organ, attacker=attacker, slot=slot)
    if stage == 0:
        if duration or wait or organ != '' or slot != '' or attacker != -1 or 'joint' in data:
            raise ValueError('未穿刺状态携带了连接或等待')
    else:
        if organ not in ORGANS or slot not in ('Near', 'Far') or attacker not in (0, 1) or duration < 1.2 - .00001:
            raise ValueError('穿刺器官、武器或时长不匹配')
        if not isinstance(data.get('joint'), dict):
            raise ValueError('穿刺关节锚点缺失')
        result['joint'] = fields(data['joint'], JOINT_FIELDS, 'source_impalement.joint')
    return result


def validate_links(fighters):
    """Check cross-fighter references after validating the complete frame.

    Both arguments must be sanitized Unity fighter snapshots, whose health and
    reference.parts already passed the owning combat validator.
    """
    occupied = set()
    for victim_index, fighter in enumerate(fighters):
        reference = fighter.get('reference', {})
        state = reference.get('source_impalement')
        if not state or not state['stage']:
            continue
        attacker_index = state['attacker']
        if attacker_index >= len(fighters) or attacker_index == victim_index:
            raise ValueError('穿刺关节不能连接自身或不存在的人物')
        if fighter['health'] > .001:
            raise ValueError('致命穿刺后仍有剩余生命')
        linked = ORGANS[state['organ']]
        if reference['parts'][linked]['severed']:
            raise ValueError('内脏连接部位已切断')
        # The same weapon has one temporary source FixedJoint2D. A new overlap
        # supersedes its previous binding rather than creating a second joint.
        key = attacker_index, state['slot']
        if key in occupied:
            raise ValueError('同一武器存在重复穿刺连接')
        occupied.add(key)
