"""Strict checkpoints for the three extracted source physical graphs.

The whitelist is generated, not derived from untrusted snapshot data. This
module never resolves arbitrary type names, component fields or transforms.
"""
import json
from pathlib import Path

SCHEMA = json.loads((Path(__file__).parent/'data/source_rig_schema.json').read_text(encoding='utf-8'))
TEMPLATES = SCHEMA['templates']
BODY_FIELDS = [('x', -20000, 20000), ('y', -20000, 20000),
               ('angle', -1000000, 1000000), ('vx', -100, 100),
               ('vy', -100, 100), ('spin', -6000, 6000)]
POINT_FIELDS = [('x', -20000, 20000), ('y', -20000, 20000), ('z', -20000, 20000),
                ('angle', -1000000, 1000000), ('sx', -20, 20), ('sy', -20, 20)]
BODY_PHYSICS_FIELDS = [('mass', .0001, 10000), ('drag', 0, 100000),
                       ('angular_drag', 0, 100000), ('gravity', -100, 100)]
QUATERNION_FIELDS = [(key, -1.0001, 1.0001) for key in ('qx', 'qy', 'qz', 'qw')]
JOINT_FIELDS = [('anchor_x', -20000, 20000), ('anchor_y', -20000, 20000),
                ('connected_x', -20000, 20000), ('connected_y', -20000, 20000)]


def _exact_ids(rows, expected, label):
    if (not isinstance(rows, list) or len(rows) != len(expected)
            or any(not isinstance(row, dict) or type(row.get('id')) is not int for row in rows)
            or {row['id'] for row in rows} != set(expected)):
        raise ValueError('原始物理图ID集合不匹配：' + label)


def _keys(row, expected, label):
    if set(row) != expected:
        raise ValueError('原始物理图字段不匹配：' + label)


def validate(data, previous, number, fields, *, human=None):
    """Return clean source_rig, or None for an untouched legacy checkpoint.

    Production callers should pass human=True for fighter 0 and False for its
    opponent. human=None permits the reserved Player2 template for explicit
    offline graph validation; it is not a player-selectable morph parameter.
    """
    if data is None and previous is None:
        return None
    if (not isinstance(data, dict) or type(data.get('version')) is not int
            or data['version'] != 1 or not isinstance(data.get('template'), str)
            or data['template'] not in TEMPLATES):
        raise ValueError('原始物理图版本或模板不匹配')
    _keys(data, {'version', 'template', 'bodies', 'joints', 'points'}, 'root')
    template = data['template']; spec = TEMPLATES[template]
    if previous is not None:
        if not isinstance(previous, dict) or previous.get('template') != template:
            raise ValueError('原始物理图模板不可替换')
    if human is not None:
        if type(human) is not bool or spec['role'] != ('human' if human else 'enemy'):
            raise ValueError('原始物理图模板与人物身份不匹配')
    bodies, joints, points = (data.get(k) for k in ('bodies', 'joints', 'points'))
    _exact_ids(bodies, spec['bodies'], 'bodies')
    _exact_ids(joints, spec['joints'], 'joints')
    _exact_ids(points, spec['points'], 'points')
    result = dict(version=1, template=template, bodies=[], joints=[], points=[])
    for row in bodies:
        _keys(row, {'id', 'x', 'y', 'angle', 'vx', 'vy', 'spin', 'simulated', 'active',
                    'mass', 'drag', 'angular_drag', 'gravity', 'body_type', 'qx', 'qy', 'qz', 'qw'}, 'body')
        if type(row.get('simulated')) is not bool or type(row.get('active')) is not bool:
            raise ValueError('原始刚体启用状态不合法')
        if type(row.get('body_type')) is not int or row['body_type'] not in (0, 1, 2):
            raise ValueError('原始刚体动态类型不合法')
        clean = dict(id=row['id'], simulated=row['simulated'], active=row['active'], body_type=row['body_type'],
                     **fields(row, BODY_FIELDS + BODY_PHYSICS_FIELDS + QUATERNION_FIELDS, 'source_rig.body'))
        if abs(sum(clean[k] ** 2 for k in ('qx', 'qy', 'qz', 'qw')) - 1) > .0001:
            raise ValueError('原始刚体世界旋转四元数未归一化')
        result['bodies'].append(clean)
    for row in joints:
        _keys(row, {'id', 'enabled', 'anchor_x', 'anchor_y', 'connected_x', 'connected_y'}, 'joint')
        if type(row.get('enabled')) is not bool:
            raise ValueError('原始关节启用状态不合法')
        result['joints'].append(dict(id=row['id'], enabled=row['enabled'],
                                    **fields(row, JOINT_FIELDS, 'source_rig.joint')))
    for row in points:
        _keys(row, {'id', 'x', 'y', 'z', 'angle', 'sx', 'sy'}, 'point')
        clean = dict(id=row['id'], **fields(row, POINT_FIELDS, 'source_rig.point'))
        if abs(clean['sx']) < .001 or abs(clean['sy']) < .001:
            raise ValueError('原始目标点缩放不可为零')
        result['points'].append(clean)
    return result
