"""Encode frames captured from the native Unity rig studio as a viewable GIF."""
import argparse
from pathlib import Path
from PIL import Image

parser = argparse.ArgumentParser()
parser.add_argument('frames', type=Path)
parser.add_argument('output', type=Path)
args = parser.parse_args()
files = sorted(args.frames.glob('*.png'))
if len(files) != 72:
    raise SystemExit(f'Expected 72 recorded native frames, found {len(files)}')
frames = []
for file in files:
    with Image.open(file) as image:
        image.thumbnail((1008, 576), Image.Resampling.LANCZOS)
        frames.append(image.convert('RGB').quantize(colors=192))
durations = [50]*24+[40]*24+[100]*24
for first in (0, 24, 48):
    durations[first] = 300
durations[-1] = 450
args.output.parent.mkdir(parents=True, exist_ok=True)
frames[0].save(args.output, save_all=True, append_images=frames[1:], duration=durations, loop=0, optimize=True)
print(f'{args.output} ({args.output.stat().st_size} bytes)')
