"""Immutable source CharacterConfigData body shape for physical checkpoints.

These are the native Single clamp endpoints, not RPG height/weight units.
Old checkpoints may lack this tag; once present it cannot disappear or morph.
"""
import math
import struct

VERSION = 1
BOUNDS = {
    'Height': (-0.44999998807907104, 0.44999998807907104),
    'Weight': (-0.550000011920929, 0.550000011920929),
    'ShouldersWidth': (0.0949999988079071, 0.1550000011920929),
    'WaistWidth': (-0.75, 0.75),
    'HipsWidth': (-0.03500000014901161, 0.03500000014901161),
}
NEUTRAL = dict(Gender=1, Height=0, Weight=0, ShouldersWidth=.125,
               WaistWidth=0, HipsWidth=0)


def _clean(data):
    if (not isinstance(data, dict) or set(data) != {'version', 'config'}
            or type(data.get('version')) is not int or data['version'] != VERSION):
        raise ValueError('原作身体形体版本或字段不合法')
    config = data.get('config')
    if (not isinstance(config, dict) or set(config) != set(NEUTRAL)
            or type(config.get('Gender')) is not int or config['Gender'] not in (0, 1)):
        raise ValueError('原作身体形体配置或性别不合法')
    result = {'Gender': config['Gender']}
    for key, (low, high) in BOUNDS.items():
        value = config[key]
        if type(value) not in (int, float) or not math.isfinite(value):
            raise ValueError('原作身体形体数值越界: ' + key)
        # Unity's JSON can spell Single(.449999988...) as .45. Validate the
        # actual native Single, with no broad epsilon and no clamp on ingress.
        try:
            value = struct.unpack('<f', struct.pack('<f', value))[0]
        except (OverflowError, struct.error):
            raise ValueError('原作身体形体数值越界: ' + key) from None
        if not low <= value <= high:
            raise ValueError('原作身体形体数值越界: ' + key)
        result[key] = value
    return dict(version=VERSION, config=result)


def validate(data, previous=None):
    if data is None and previous is None:
        return None
    result = _clean(data)
    if previous is not None and result != _clean(previous):
        raise ValueError('原作身体形体配置保存后不可改变')
    return result
