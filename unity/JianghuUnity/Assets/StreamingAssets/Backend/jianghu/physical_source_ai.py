"""Bounded checkpoints for the closed set of recovered combat AI routines."""
from copy import deepcopy
import json
import math
import re

ROUTINES = frozenset((
    'state', 'action', 'reactive', 'counter', 'attack', 'walk', 'ciw', 'cix',
    'kick', 'pull', 'victory', 'left-delay', 'procedural',
    'guard-left', 'guard-right', 'guard-double', 'legacy-resume',
))
CLIPS = frozenset(f'{hands}_{action}' for hands in ('1Hand', '2Hands')
                  for action in ('SwingDown', 'SwingUp', 'Stab')) | {''}
VECTOR_KEYS = frozenset(('x', 'y', 'z'))
PROFILE_KEYS = frozenset((
    'duration', 'windupSnap', 'windupEnd', 'strikeEnd', 'windupAngle',
    'windupRadius', 'strikeFromAngle', 'strikeToAngle', 'strikeRadius',
    'followAngle', 'followRadius', 'holdExtension',
))
FIELD = re.compile(r'(?:ai|source)[A-Za-z0-9_]{0,80}\Z')
ITERATOR_FIELD = re.compile(r'(?:<>1__state|<[A-Za-z][A-Za-z0-9_]*>[0-9A-Za-z_]*|direction|hand|delay)\Z')
SOURCE_FIELDS = frozenset((
    'sourceAIInitialized', 'sourceLeftActive', 'sourceRightActive',
    'sourceNubLeft', 'sourceNubRight', 'sourceAIFrame',
    'sourceTapLeft', 'sourceTapRight', 'sourceDoubleLeft', 'sourceDoubleRight',
    'sourceLegacyResume', 'sourceLegacyWait', 'sourceRealtimeValue',
))


def _fail():
    raise ValueError('原作战斗AI状态格式、版本或范围不合法')


def _number(value, low=-10_000_000, high=10_000_000, integer=False):
    if type(value) not in ((int,) if integer else (int, float)):
        _fail()
    if not math.isfinite(value) or not low <= value <= high:
        _fail()
    return value


def _object(value, keys=None, maximum=256):
    if not isinstance(value, dict) or len(value) > maximum:
        _fail()
    if keys is not None and set(value) != set(keys):
        _fail()
    return value


def _boolean(value):
    if type(value) is not bool:
        _fail()


def _value(value, depth=0):
    if depth > 2:
        _fail()
    if type(value) is bool:
        return
    if type(value) in (int, float):
        _number(value)
        return
    _object(value)
    if set(value) not in (set(('x', 'y')), set(('x', 'y', 'z')), set(PROFILE_KEYS)):
        _fail()
    for key, item in value.items():
        if key == 'holdExtension':
            _boolean(item)
        else:
            _number(item)


def _fields(value, iterator=False):
    _object(value)
    pattern = ITERATOR_FIELD if iterator else FIELD
    for key, item in value.items():
        if not isinstance(key, str) or not pattern.fullmatch(key):
            _fail()
        if not iterator and key.startswith('source') and key not in SOURCE_FIELDS:
            _fail()
        if not iterator and key == 'sourceRealtimeValue':
            _number(item, 0, 10_000_000)
        _value(item)
    if iterator:
        _number(value.get('<>1__state'), -1, 64, True)


def _relay(value):
    _object(value, ('parent', 'rest', 'defending', 'enabled', 'playing', 'clip', 'time', 'offset', 'started_at', 'tweens'))
    for key in ('parent', 'rest'):
        _object(value[key], ('x', 'y'))
        _value(value[key])
    for key in ('defending', 'enabled', 'playing'):
        _boolean(value[key])
    if not isinstance(value['clip'], str) or value['clip'] not in CLIPS:
        _fail()
    # A stopped legacy Animation retains its state time while later waits run.
    _number(value['time'], 0, 200)
    _number(value['offset'], 0, 200)
    _number(value['started_at'])
    if not value['clip'] and (value['time'] or value['playing']):
        _fail()
    tweens = value['tweens']
    if not isinstance(tweens, list) or len(tweens) > 32:
        _fail()
    for tween in tweens:
        _object(tween, ('start', 'target', 'age', 'duration', 'axes', 'circ', 'world_y'))
        for key in ('start', 'target'):
            _object(tween[key], ('x', 'y'))
            _value(tween[key])
        _number(tween['age'], 0, 200)
        _number(tween['duration'], 0, 10)
        _number(tween['axes'], 1, 3, True)
        _boolean(tween['circ'])
        _boolean(tween['world_y'])


def validate(data, previous=None):
    """Validate and copy; existing initialized queues cannot be downgraded.

    The runtime restores only locally constructed, explicitly named routines.
    No persisted field is interpreted as an assembly, type, or method name.
    """
    _object(data)
    if type(data.get('version')) is not int or data['version'] != 1:
        _fail()
    _boolean(data.get('initialized'))
    if not data['initialized']:
        _object(data, ('version', 'initialized'))
        if previous and previous.get('initialized'):
            _fail()
        return deepcopy(data)
    _object(data, ('version', 'initialized', 'clock', 'fields', 'state', 'persona',
                   'reaction', 'procedural', 'random_seed', 'random_draws',
                   'walk_taps', 'kick_schedules', 'left', 'right', 'double', 'jobs'))
    _number(data['clock'], 0, 10_000_000)
    _number(data['state'], 0, 6, True)
    _number(data['persona'], 0, 1, True)
    _number(data['reaction'], .01, 10)
    _boolean(data['procedural'])
    _number(data['random_seed'], -2**31, 2**31-1, True)
    _number(data['random_draws'], 0, 1_000_000, True)
    _number(data['walk_taps'], 0, 1_000_000, True)
    _number(data['kick_schedules'], 0, 1_000_000, True)
    _fields(data['fields'])
    for key in ('left', 'right', 'double'):
        _relay(data[key])
    jobs = data['jobs']
    if not isinstance(jobs, list) or len(jobs) > 128:
        _fail()
    edges = {}
    for index, job in enumerate(jobs):
        _object(job, ('done', 'due', 'realtime', 'child', 'stack'))
        _boolean(job['done'])
        _boolean(job['realtime'])
        _number(job['due'], -200, 10)
        child = _number(job['child'], -1, len(jobs)-1, True)
        if child >= 0:
            edges[index] = child
        if not isinstance(job['stack'], list) or len(job['stack']) > 8:
            _fail()
        for iterator in job['stack']:
            _object(iterator, ('routine', 'fields'))
            if not isinstance(iterator['routine'], str) or iterator['routine'] not in ROUTINES:
                _fail()
            _fields(iterator['fields'], iterator=True)
    for start in edges:
        seen = set()
        current = start
        while current in edges:
            if current in seen:
                _fail()
            seen.add(current)
            current = edges[current]
    try:
        encoded = json.dumps(data, allow_nan=False, separators=(',', ':'))
    except (ValueError, TypeError):
        _fail()
    if len(encoded.encode('utf-8')) > 262_144:
        _fail()
    return deepcopy(data)
