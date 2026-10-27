"""Encode frames captured from the native Unity rig studio as a viewable GIF."""
import argparse,json
from pathlib import Path
from PIL import Image

parser = argparse.ArgumentParser()
parser.add_argument('frames', type=Path)
parser.add_argument('output', type=Path)
args = parser.parse_args()
files = sorted(args.frames.glob('*.png'))
if len(files) != 24:
    raise SystemExit(f'Expected 24 recorded native frames, found {len(files)}')
frames = []
metadata=args.frames.parent/'frame-viewports.json'
view=json.loads(metadata.read_text()) if metadata.exists() else None
for file in files:
    with Image.open(file) as image:
        if view:
            a,b=view['reference'],view['rig'];image=image.crop((round(a['x']),round(a['y']),round(b['x']+b['width']),round(b['y']+b['height'])))
        image.thumbnail((1008, 576), Image.Resampling.LANCZOS)
        frames.append(image.convert('RGB').quantize(colors=192))
durations = [50]*24
args.output.parent.mkdir(parents=True, exist_ok=True)
frames[0].save(args.output, save_all=True, append_images=frames[1:], duration=durations, loop=0, optimize=True)
print(f'{args.output} ({args.output.stat().st_size} bytes)')
