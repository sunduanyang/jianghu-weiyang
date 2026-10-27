"""Independent split-panel provenance, socket calibration and game rig contracts."""
import hashlib,json,math,os,unittest
from pathlib import Path
import numpy as np
from PIL import Image
ROOT=Path(__file__).resolve().parents[1]
SOURCE=ROOT/'art/puppet-modern-24'
RUNTIME=Path(os.environ.get('JIANGHU_ART_OUTPUT',ROOT/'unity/JianghuUnity/Assets/Resources'))/'PaperCast'
class PaperCastTests(unittest.TestCase):
 @classmethod
 def setUpClass(cls):
  cls.manifest=json.loads((SOURCE/'manifest.json').read_text(encoding='utf-8'))
  cls.index=json.loads((RUNTIME/'index.json').read_text(encoding='utf-8'))
 def test_all_24_characters_use_independent_split_components(self):
  self.assertEqual(self.index['binding_basis'],'independent exploded component panels')
  self.assertEqual(self.index['version'],7)
  archive=ROOT/'output/imagegen/puppet-cast24-modern-flat-20261026.zip'
  if archive.exists():self.assertEqual(self.index['source_archive_sha256'],hashlib.sha256(archive.read_bytes()).hexdigest())
  ids={c['id'] for c in self.manifest['characters']}
  self.assertEqual(len(ids),24);self.assertEqual(set(self.index['skins']),ids)
  self.assertEqual({p.stem for p in RUNTIME.glob('*.png')},ids)
  core={'head-neck','torso','pelvis'}|{f'{s}-{p}' for s in ('near','far') for p in ('upper-arm','forearm','hand','thigh','shin','foot')}
  self.assertEqual(sum(len(s['parts']) for s in self.index['skins'].values()),self.index['expected_parts'])
  for skin in self.index['skins'].values():
   self.assertLessEqual(core,set(skin['parts']));self.assertNotIn('mesh_resource',skin);self.assertNotIn('mesh_layers',skin)
   self.assertEqual(skin['binding_basis'],self.index['binding_basis'])
 def test_every_drawable_pixel_has_provenance_in_the_right_hand_exploded_panels(self):
  for c in self.manifest['characters']:
   self.assertEqual(hashlib.sha256((SOURCE/c['file']).read_bytes()).hexdigest(),c['sha256'])
   sheet=Image.open(SOURCE/c['file']).convert('RGB')
   for name,p in self.index['skins'][c['id']]['parts'].items():
    with self.subTest(skin=c['id'],part=name):
     self.assertEqual(p['source_region'],'right-hand exploded component panel');self.assertGreaterEqual(p['source_rect'][0],420)
     original=Image.open(SOURCE/p['source_component']).convert('RGBA')
     self.assertEqual(hashlib.sha256((SOURCE/p['source_component']).read_bytes()).hexdigest(),p['source_component_sha256'])
     crop=sheet.crop(p['source_rect'])
     if p['source_extraction_mirror']:crop=crop.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
     a=np.array(original);b=np.array(crop);solid=a[:,:,3]>250
     self.assertTrue(np.array_equal(a[:,:,:3][solid],b[solid]))
     processed=Image.open(SOURCE/p['source']).convert('RGBA')
     if p['source_mirror']:original=original.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
     a=np.array(original);b=np.array(processed);visible=b[:,:,3]>0
     self.assertTrue(np.array_equal(a[:,:,:3][visible],b[:,:,:3][visible]))
     self.assertTrue(np.all(b[:,:,3]<=a[:,:,3]))
 def test_atlas_preserves_the_processed_split_parts_and_never_contains_final_cutouts(self):
  for cid,skin in self.index['skins'].items():
   path=RUNTIME/(cid+'.png');atlas=Image.open(path)
   self.assertEqual(hashlib.sha256(path.read_bytes()).hexdigest(),skin['atlas_sha256']);self.assertEqual(list(atlas.size),skin['size'])
   for p in skin['parts'].values():
    self.assertIn('/split-bound-parts/',p['source']);source=SOURCE/p['source'];im=Image.open(source)
    self.assertEqual(hashlib.sha256(source.read_bytes()).hexdigest(),p['sha256'])
    x,y,w,h=p['uv'];px=round(x*atlas.width);py=round((1-y-h)*atlas.height)
    self.assertEqual(im.tobytes(),atlas.crop((px,py,px+im.width,py+im.height)).tobytes())
 def test_every_part_uses_a_calibrated_joint_or_segment_with_finite_uv(self):
  rig=self.index['rig'];joints={j for b in rig['bones'].values() for j in b}|set(rig['attachments'])
  for skin in self.index['skins'].values():
   boxes=[]
   for name,p in skin['parts'].items():
    self.assertFalse(p.get('hidden',False),'Every anatomical part must be bound and available in walk')
    with self.subTest(skin=skin['id'],part=name):
     for key in ('pivot','tip'):
      if key in p:
       for v,limit in zip(p[key],p['size']):self.assertTrue(math.isfinite(v) and 0<=v<limit)
     if 'bone' in p:self.assertIn(p['bone'],rig['bones']);self.assertGreater(math.dist(p['pivot'],p['tip']),10)
     else:self.assertIn(p['joint'],joints);self.assertGreater(p['height'],0)
     x,y,w,h=p['uv'];self.assertGreaterEqual(min(x,y),0);self.assertLessEqual(max(x+w,y+h),1.000001)
     for a,b,c,d in boxes:self.assertFalse(x<c and x+w>a and y<d and y+h>b)
     boxes.append((x,y,x+w,y+h))
 def test_shared_clips_age_proportions_and_forward_knee_constraints(self):
  self.assertEqual(self.index['rig'],json.loads((ROOT/'unity/paper-rig.json').read_text(encoding='utf-8')))
  self.assertEqual(set(self.index['rig']['clips']),{'walk'})
  for sex in ('m','f'):
   child=self.index['skins'][sex+'-child'];adult=self.index['skins'][sex+'-youth-farmer']
   for segment in ('spine','thigh','shin'):self.assertLess(child['lengths'][segment],adult['lengths'][segment]*.7)
  for skin in self.index['skins'].values():
   for side in ('Near','Far'):
    points=skin['bind_joints'];hip,knee,ankle=[points[x+side] for x in ('hip','knee','ankle')]
    self.assertGreaterEqual((knee[0]-hip[0])*(ankle[1]-hip[1])-(knee[1]-hip[1])*(ankle[0]-hip[0]),-.000001)
    for kind,a,b in [('upper_arm','shoulder','elbow'),('forearm','elbow','wrist'),('thigh','hip','knee'),('shin','knee','ankle')]:self.assertAlmostEqual(skin['lengths'][kind+side],math.dist(points[a+side],points[b+side]),places=3)
 def test_comparison_reference_is_not_used_to_draw_actors_or_portraits(self):
  code=(ROOT/'unity/JianghuUnity/Assets/Scripts/PaperCast.cs').read_text(encoding='utf-8-sig')
  figure=code[code.index('void RenderPaperFigure'):code.index('void DrawPaperPerson')]
  self.assertNotIn('PaperReference/',figure);self.assertNotIn('paperSkinning',figure)
  bust=code[code.index('void DrawPaperBust'):code.index('void PaperPart')]
  self.assertIn('PaperBust/',bust);self.assertNotIn('PaperReference/',bust)
  for skin in self.index['skins'].values():
   self.assertEqual(skin['portrait']['uv'],skin['parts']['head-neck']['uv'])
 def test_hands_are_empty_without_removing_worn_or_back_equipment(self):
  self.assertEqual(self.index['equipment_policy'],'no hand-held objects')
  removed=['m-youth-farmer','m-elder','m-scholar','m-fisher','m-artisan','m-performer','m-daoist','m-monk','m-beggar']
  for cid in removed:self.assertNotIn('prop',self.index['skins'][cid]['parts'])
  for skin in self.index['skins'].values():
   for name,p in skin['parts'].items():
    if p.get('joint','').startswith('wrist'):self.assertIn(name,('near-hand','far-hand'))
  for cid in ['m-elder','m-scholar']:
   self.assertTrue(self.index['skins'][cid]['parts']['near-hand']['source_component'].endswith('/far-hand.png'))
  for cid in ['m-hunter','f-healer','m-escort','f-escort','f-swordswoman']:
   self.assertIn('prop',self.index['skins'][cid]['parts']);self.assertNotIn(self.index['skins'][cid]['parts']['prop']['joint'],['wristNear','wristFar'])
 def test_composite_bust_and_visual_metrics_describe_the_split_assembly(self):
  for cid,skin in self.index['skins'].items():
   actual=Image.open(SOURCE/cid/'split-rest-composite.png').convert('RGBA')
   self.assertEqual(actual.tobytes(),Image.open(RUNTIME.parent/'PaperBust'/(cid+'.png')).convert('RGBA').tobytes())
   reference=Image.open(RUNTIME.parent/'PaperReference'/(cid+'.png')).convert('RGBA')
   a=np.array(actual)[:,:,3]>127;b=np.array(reference)[:,:,3]>127
   iou=float((a&b).sum()/max((a|b).sum(),1));self.assertAlmostEqual(iou,skin['reference_comparison']['silhouette_iou'])
   self.assertNotEqual(actual.tobytes(),reference.tobytes())
 def test_visible_materials_meet_at_sockets_through_the_walk(self):
  from scripts.review_paper_walk import audit
  checks=audit(self.index)
  self.assertEqual(len(checks),24*13*14)
  failures=[c for c in checks if c['gap_world_units'] is None or c['gap_world_units']>2]
  self.assertEqual(failures,[])
 def test_complete_trouser_pixels_are_retained_and_upper_seat_follows_the_body(self):
  from scripts.review_paper_walk import matrix
  from bake_split_cast import pose,rotation
  seats=0
  for cid,skin in self.index['skins'].items():
   for name in skin['pelvis_structure']['seat_parts']:
    seats+=1;part=skin['parts'][name];leg=skin['parts'][name.replace('-seat','-thigh')]
    original=Image.open(SOURCE/part['source_component']).convert('RGBA')
    if part['source_mirror']:original=original.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
    a=np.asarray(Image.open(SOURCE/part['source']))[:,:,3];b=np.asarray(Image.open(SOURCE/leg['source']))[:,:,3]
    hidden=np.asarray(Image.open(SOURCE/part['occluded_source']))[:,:,3]
    self.assertTrue(np.array_equal(np.maximum(np.maximum(a,b),hidden),np.asarray(original)[:,:,3]),cid+' lost upper trouser pixels')
    self.assertEqual(hashlib.sha256((SOURCE/part['occluded_source']).read_bytes()).hexdigest(),part['occluded_source_sha256'])
    self.assertTrue(part['freeze_to_body']);reference=None
    for phase in [None]+[i/12 for i in range(12)]:
     joints,angles=pose(skin,phase or 0,phase is not None);linear,offset=matrix(skin,name,part,self.index['rig'],joints,angles)
     waist=linear@np.array(part['waist_source_anchor'])+offset
     body_local=rotation(-angles['body'])@(waist-joints['hip'])
     if reference is None:reference=body_local
     self.assertLess(np.linalg.norm(body_local-reference),.001,cid+' seat swings with the thigh')
  self.assertEqual(seats,18)
 def test_required_rear_hems_and_straw_cape_are_preserved(self):
  for cid in ['m-youth-farmer','m-child','m-escort','f-escort','m-hunter','m-artisan','m-beggar','f-swordswoman']:
   part=self.index['skins'][cid]['parts']['torso'];self.assertIn('restore_lower_body',part['source_mask'])
   alpha=np.asarray(Image.open(SOURCE/part['source']))[:,:,3];waist=int(part['pivot'][1])
   self.assertGreater(int((alpha[waist+8:]>127).sum()),100,cid+' rear cloth was cut off at the waist')
  part=self.index['skins']['m-fisher']['parts']['torso']
  self.assertEqual(Image.open(SOURCE/part['source']).tobytes(),Image.open(SOURCE/part['source_component']).tobytes())
if __name__=='__main__':unittest.main()
