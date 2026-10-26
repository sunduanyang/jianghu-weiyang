"""Portable source/skin contract checks; Unity exercises the actual FK renderer."""
import hashlib
import json
import math
from pathlib import Path
import struct
import unittest

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'art/puppet-cast-24'
RUNTIME = ROOT / 'unity/JianghuUnity/Assets/Resources/PaperCast'


class PaperCastTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.spec = json.loads((SOURCE/'cast-spec.json').read_text(encoding='utf-8'))
        cls.index = json.loads((RUNTIME/'index.json').read_text(encoding='utf-8'))

    def test_all_24_complete_skins_and_405_original_cutouts_are_published(self):
        self.assertEqual(len(self.spec), 24)
        self.assertEqual(set(self.index['skins']), {p['id'] for p in self.spec} | {'reference-jade', 'reference-body', 'reference-bamboo', 'reference-plain'})
        self.assertEqual(sum(len(self.index['skins'][p['id']]['parts']) for p in self.spec), 405)
        self.assertEqual(sum(len(s['parts']) for s in self.index['skins'].values()), self.index['expected_parts'])
        core = {'head-neck', 'torso', 'front-skirt', 'back-skirt'} | {f'{side}-{part}' for side in ('near', 'far') for part in ('upper-arm', 'forearm', 'hand', 'thigh', 'shin', 'foot')}
        for role in self.spec:
            with self.subTest(role=role['id']):
                skin = self.index['skins'][role['id']]
                self.assertEqual(set(skin['parts']), core | {p['id'] for p in role['accessories']})
                manifest = json.loads((SOURCE/role['id']/'parts-manifest.json').read_text(encoding='utf-8'))
                self.assertEqual(hashlib.sha256((SOURCE/role['id']/'parts-sheet.png').read_bytes()).hexdigest(), manifest['source_sha256'])
                for part in skin['parts'].values():
                    path = SOURCE/part['source']
                    self.assertTrue(path.resolve().is_relative_to(SOURCE.resolve()))
                    self.assertEqual(hashlib.sha256(path.read_bytes()).hexdigest(), part['sha256'])

    def test_runtime_atlases_keep_rgba_and_import_without_npot_resizing(self):
        for cid, skin in self.index['skins'].items():
            with self.subTest(skin=cid):
                path = RUNTIME/(cid+'.png')
                blob = path.read_bytes()
                self.assertEqual(blob[:8], b'\x89PNG\r\n\x1a\n')
                self.assertEqual(list(struct.unpack('>II', blob[16:24])), skin['size'])
                self.assertEqual(blob[25], 6, 'Atlas must retain RGBA')
                self.assertEqual(hashlib.sha256(blob).hexdigest(), skin['atlas_sha256'])
                meta = path.with_suffix('.png.meta').read_text(encoding='utf-8')
                self.assertIn('nPOTScale: 0', meta)
                self.assertIn('maxTextureSize: 4096', meta)
                self.assertIn('enableMipMap: 1', meta)
                self.assertIn('filterMode: 2', meta)

    def test_bind_anchors_and_atlas_rectangles_are_finite_and_do_not_overlap(self):
        rig = self.index['rig']
        joints = {p for bone in rig['bones'].values() for p in bone} | set(rig['attachments'])
        for skin in self.index['skins'].values():
            if not skin.get('reference'):
                self.assertGreater(skin['shoulders']['Near'][0], 0)
                self.assertLess(skin['shoulders']['Far'][0], 0)
            boxes = []
            for name, part in skin['parts'].items():
                with self.subTest(skin=skin['id'], slot=name):
                    for key in ('pivot', 'tip'):
                        if key not in part:
                            continue
                        for value, limit in zip(part[key], part['size']):
                            self.assertTrue(math.isfinite(value))
                            self.assertGreaterEqual(value, 0)
                            self.assertLess(value, limit)
                    if 'bone' in part:
                        self.assertIn(part['bone'], rig['bones'])
                        self.assertGreater(math.dist(part['pivot'], part['tip']), 20)
                    else:
                        self.assertIn(part['joint'], joints)
                    x, y, w, h = part['uv']
                    self.assertGreaterEqual(min(x, y), 0)
                    self.assertLessEqual(max(x+w, y+h), 1)
                    for a, b, c, d in boxes:
                        self.assertFalse(x < c and x+w > a and y < d and y+h > b)
                    boxes.append((x, y, x+w, y+h))

    def test_all_skins_share_the_same_data_defined_skeleton_and_seven_clips(self):
        rig = json.loads((ROOT/'unity/paper-rig.json').read_text(encoding='utf-8'))
        self.assertEqual(self.index['rig'], rig)
        self.assertEqual(set(rig['clips']), {'idle', 'walk', 'talk', 'attack', 'guard', 'hurt', 'defeated'})
        for clip in rig['clips'].values():
            keys = clip['keys']
            self.assertEqual(keys[0]['t'], 0)
            self.assertEqual(keys[-1]['t'], 1)
            self.assertEqual([k['t'] for k in keys], sorted({k['t'] for k in keys}))
            for key in keys:
                self.assertTrue(set(key) <= set(rig['rest']) | {'t'})
                self.assertTrue(all(math.isfinite(v) for v in key.values()))
        self.assertTrue(all('clips' not in s and 'rig' not in s for s in self.index['skins'].values()))

    def test_reference_rivets_and_crops_keep_supplied_pixels(self):
        from PIL import Image
        for cid, expected in [('reference-jade', 16), ('reference-body', 15)]:
            skin = self.index['skins'][cid]
            self.assertEqual(len(skin['parts']), expected)
            atlas = Image.open(RUNTIME/(cid+'.png')).convert('RGBA')
            for part in skin['parts'].values():
                source = ROOT/'art'/part['source']
                self.assertEqual(hashlib.sha256(source.read_bytes()).hexdigest(), part['source_sha256'])
                crop = Image.open(source).convert('RGBA').crop(part['source_rect'])
                x, y, w, h = part['uv']
                px, py = round(x*atlas.width), round((1-y-h)*atlas.height)
                packed = atlas.crop((px, py, px+part['size'][0], py+part['size'][1]))
                self.assertEqual(crop.tobytes(), packed.tobytes())
                for anchor in [part['pivot']] + ([part['tip']] if 'tip' in part else []):
                    self.assertGreater(crop.getpixel(tuple(round(v) for v in anchor))[3], 100)

    def test_reference_segment_lengths_come_from_assembled_landmarks(self):
        calibration = json.loads((ROOT/'art/rig-reference/calibration.json').read_text(encoding='utf-8'))
        points = calibration['landmarks']
        for cid in ('reference-jade', 'reference-body'):
            skin = self.index['skins'][cid]
            self.assertEqual(skin['canvas'], calibration['canvas'])
            self.assertEqual(skin['canvas_origin'], calibration['origin'])
            for side in ('Near', 'Far'):
                for kind, a, b in [('upper_arm', 'shoulder', 'elbow'), ('forearm', 'elbow', 'wrist'), ('thigh', 'hip', 'knee'), ('shin', 'knee', 'ankle')]:
                    self.assertAlmostEqual(skin['lengths'][kind+side], math.dist(points[a+side], points[b+side]))

    def test_bamboo_trial_preserves_body_aspect_and_retained_source_pixels(self):
        import numpy as np
        from PIL import Image
        directory=ROOT/'art/rig-bamboo'
        manifest=json.loads((directory/'binding.json').read_text(encoding='utf-8'))
        self.assertEqual(hashlib.sha256((directory/'source.png').read_bytes()).hexdigest(),manifest['source_sha256'])
        source=Image.open(directory/'source.png').convert('RGBA')
        skin=self.index['skins']['reference-bamboo']
        self.assertEqual(len(skin['parts']),17)
        atlas=Image.open(RUNTIME/'reference-bamboo.png').convert('RGBA')
        for row in manifest['parts']:
            part=skin['parts'][row['id']]
            image=Image.open(ROOT/'art'/part['source']).convert('RGBA')
            expected=np.array(source.crop(row['source_rect']));actual=np.array(image);retained=actual[:,:,3]>0
            self.assertTrue(np.array_equal(actual[retained],expected[retained]))
            x,y,w,h=part['uv'];px,py=round(x*atlas.width),round((1-y-h)*atlas.height)
            self.assertEqual(image.tobytes(),atlas.crop((px,py,px+image.width,py+image.height)).tobytes())
            if row['id']=='sheathed-sword':continue
            self.assertNotIn('breadth',part);self.assertNotIn('shear',part)
            if 'bone' in part:
                bone=part['bone'];kind='upper_arm' if bone.startswith('upper') else 'forearm' if bone.startswith('forearm') else 'thigh' if bone.startswith('thigh') else 'shin' if bone.startswith('shin') else 'spine'
                side='Near' if bone.endswith('Near') else 'Far'
                self.assertAlmostEqual(skin['lengths'][kind if kind=='spine' else kind+side],math.dist(part['pivot'],part['tip']))
            else:self.assertEqual(part['height'],part['pivot'][1] if row['id']=='head-neck' else image.height)

    def test_plain_trial_has_fifteen_pieces_and_one_pelvis_without_stretch(self):
        import numpy as np
        from PIL import Image
        directory=ROOT/'art/rig-plain';manifest=json.loads((directory/'binding.json').read_text(encoding='utf-8'))
        self.assertEqual(hashlib.sha256((directory/'source.png').read_bytes()).hexdigest(),manifest['source_sha256'])
        skin=self.index['skins']['reference-plain'];self.assertEqual(len(skin['parts']),15)
        self.assertIn('pelvis',skin['parts']);self.assertNotIn('front-skirt',skin['parts']);self.assertNotIn('back-skirt',skin['parts'])
        self.assertLess(skin['parts']['head-neck']['layer'],skin['parts']['torso']['layer'],'Neck must sit behind the collar')
        source=Image.open(directory/'source.png').convert('RGB');atlas=Image.open(RUNTIME/'reference-plain.png').convert('RGBA')
        for row in manifest['parts']:
            p=skin['parts'][row['id']];image=Image.open(ROOT/'art'/p['source']).convert('RGBA');a=np.array(image);original=np.array(source.crop(row['source_rect']))
            self.assertTrue(np.array_equal(a[:,:,:3][a[:,:,3]>0],original[a[:,:,3]>0]))
            self.assertGreater((a[:,:,3]==0).sum(),100)
            self.assertNotIn('breadth',p);self.assertNotIn('shear',p)
            x,y,w,h=p['uv'];px,py=round(x*atlas.width),round((1-y-h)*atlas.height)
            self.assertEqual(image.tobytes(),atlas.crop((px,py,px+image.width,py+image.height)).tobytes())


if __name__ == '__main__':
    unittest.main()
