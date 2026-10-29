"""The recovered KickReceiver has two waits and no native damping writes."""


def validate(data, previous, number):
    if data is None and previous is None:
        return None
    if (not isinstance(data, dict) or set(data) != {'version', 'stage', 'wait'}
            or type(data.get('version')) is not int or data['version'] != 1
            or type(data.get('stage')) is not int or data['stage'] not in (0, 1, 2)):
        raise ValueError('原始受踢计时状态不合法')
    stage = data['stage']
    wait = number(data.get('wait'), 0, (0, .35001, .15001)[stage], 'source kick receiver wait')
    return dict(version=1, stage=stage, wait=wait)
