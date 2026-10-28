"""Package extracted numeric animation curves for the shared combat runtime."""
import argparse
import hashlib
import json
from pathlib import Path


def build(source, destination):
    data=json.loads(source.read_text(encoding='utf-8'))
    clips={}
    for key,record in data['clips'].items():
        channels={}
        for curve in record['m_PositionCurves']:
            src=curve['curve']
            if (src['m_PreInfinity'],src['m_PostInfinity'])!=(2,2):
                raise ValueError('Unimplemented curve infinity mode')
            keys=src['m_Curve']
            if any(k['weightedMode']!=0 for k in keys):
                raise ValueError('Weighted curves need explicit native verification')
            channels[curve['path']]=[
                [k['time'],k['value']['x'],k['value']['y'],
                 k['inSlope']['x'],k['inSlope']['y'],
                 k['outSlope']['x'],k['outSlope']['y']] for k in keys]
        rotations={}
        for curve in record['m_RotationCurves']:
            keys=curve['curve']['m_Curve']
            if any(k['weightedMode']!=0 for k in keys):
                raise ValueError('Weighted rotation curve')
            rotations[curve['path']]=[
                [k['time']]+[k[field][axis] for field in ('value','inSlope','outSlope') for axis in 'xyzw']
                for k in keys]
        if any(record.get(k) for k in ('m_EulerCurves','m_ScaleCurves','m_FloatCurves','m_Events')):
            raise ValueError('Motion has additional channels; do not silently discard them')
        clips[key]=dict(name=record['m_Name'],duration=record['duration'],channels=channels,rotations=rotations)
    result=dict(schema=1,apk_sha256=data['source_apk_sha256'],
        extraction_sha256=hashlib.sha256(source.read_bytes()).hexdigest(),
        bundles=data['ai_bundles'],clips=clips,
        kick=data['rigs']['00af04d95140a4b44ac8453d91c032ea']['kick_clip'])
    destination.parent.mkdir(parents=True,exist_ok=True)
    destination.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(dict(bundles=len(result['bundles']),clips=len(clips),
        channels=sum(len(c['channels']) for c in clips.values()),
        keys=sum(len(ch) for c in clips.values() for ch in c['channels'].values()))))


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source',type=Path);parser.add_argument('destination',type=Path)
    args=parser.parse_args();build(args.source,args.destination)
