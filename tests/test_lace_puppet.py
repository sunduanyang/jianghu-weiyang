"""Original component provenance, alpha preservation and nondegenerate anchors."""
import hashlib
import json
from pathlib import Path
import unittest

from PIL import Image

ROOT=Path(__file__).resolve().parents[1]
ART=ROOT/'art/puppet-lace-blue'
RESOURCE=ROOT/'unity/JianghuUnity/Assets/Resources/PuppetTrial'


class LacePuppetTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.index=json.loads((RESOURCE/'index.json').read_text(encoding='utf-8'))
        cls.sheet=Image.open(ART/'design-sheet.png').convert('RGBA')
        cls.atlas=Image.open(RESOURCE/'atlas.png').convert('RGBA')

    def test_all_fifteen_slots_come_from_the_detached_source_panels(self):
        self.assertEqual(self.index['source_sha256'],hashlib.sha256((ART/'design-sheet.png').read_bytes()).hexdigest())
        expected={'head-neck','torso','pelvis'}|{side+'-'+part for side in ('near','far') for part in ('upper-arm','forearm','hand','thigh','shin','foot')}
        self.assertEqual({p['id'] for p in self.index['parts']},expected)
        for p in self.index['parts']:
            self.assertGreaterEqual(p['source_rect'][0],530)
            self.assertEqual(p['source_region'],'right-hand detached component artwork')
            raw=Image.open(ART/'parts'/f'{p["id"]}.png').convert('RGBA')
            self.assertEqual(raw.tobytes(),self.sheet.crop(p['source_rect']).tobytes())

    def test_atlas_preserves_original_colors_and_never_fills_transparent_holes(self):
        self.assertEqual(self.index['atlas_sha256'],hashlib.sha256((RESOURCE/'atlas.png').read_bytes()).hexdigest())
        transparent=0
        for p in self.index['parts']:
            u,v,w,h=p['uv'];x=round(u*self.atlas.width);y=round((1-v-h)*self.atlas.height)
            original=self.sheet.crop(p['runtime_rect'])
            crop=self.atlas.crop((x,y,x+original.width,y+original.height))
            self.assertEqual(crop.convert('RGB').tobytes(),original.convert('RGB').tobytes())
            self.assertTrue(all(a<=b for a,b in zip(crop.getchannel('A').tobytes(),original.getchannel('A').tobytes())))
            self.assertEqual(hashlib.sha256(crop.tobytes()).hexdigest(),p['rgba_sha256'])
            transparent+=crop.getchannel('A').histogram()[0]
        self.assertGreater(transparent,10000)

    def test_feet_have_three_distinct_anchors_and_all_joints_are_inside_their_parts(self):
        for p in self.index['parts']:
            for key in ('pivot','tip'):
                self.assertTrue(all(0<=v<bound for v,bound in zip(p[key],p['size'])))
            a,b=p['pivot'],p['tip']
            self.assertGreater((a[0]-b[0])**2+(a[1]-b[1])**2,100)
            if 'heel_pivot' in p:
                h=p['heel_pivot']
                self.assertGreater(abs((b[0]-a[0])*(h[1]-a[1])-(h[0]-a[0])*(b[1]-a[1])),100)

    def test_complete_lower_coat_is_preserved_outside_the_runtime_spine_crop(self):
        p=next(p for p in self.index['parts'] if p['id']=='torso')
        self.assertLess(p['runtime_rect'][3],p['source_rect'][3])
        self.assertTrue((ART/'parts/torso.png').is_file())
