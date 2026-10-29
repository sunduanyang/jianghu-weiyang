"""Catalog/lifecycle audit for the recovered outdoor environment domain."""
from collections import defaultdict,Counter
import hashlib,json
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
def identity(r):return r['source']['file']+':'+str(r['source']['id'])
def main():
    path=ROOT/'output/bloody-full-port-20261028/original-visual-catalog.json'
    d=json.loads(path.read_text(encoding='utf-8'));h=d['hierarchy']
    paths=defaultdict(list)
    for r in d['scene']:
        if r.get('scene_path') and identity(r) in h:paths[r['scene_path']].append(r)
    collisions={p:rs for p,rs in paths.items() if len({(h[identity(r)]['root'],tuple(h[identity(r)]['indices'])) for r in rs})>1}
    lights=[dict(identity=identity(r),path=r.get('scene_path'),root=h[identity(r)]['root']) for r in d['scene'] if r['script']=='Light2D']
    region=next(r for r in d['scene'] if r['script']=='RegionConfiguration' and r['data']['regionId']==1)
    index=next(i for i,s in enumerate(region['data']['stages']) if s['levelType']==1)
    prefab=region['assets'][f'stages[{index}].graphicsContainer'];name=d['assets'][prefab]['scene_path']
    props=[r for r in d['scene'] if r['script']=='CustomizeEnviroProp' and h[identity(r)]['root']==name]
    ground=next(r for r in d['scene'] if r['script']=='CustomizeGroundSpriteShapes' and h[identity(r)]['root']==name)
    out=ROOT/'output/bloody-full-port-20261028/environment-check';out.mkdir(exist_ok=True)
    report=dict(
        source_apk_sha256=d['source_apk_sha256'],catalog_sha256=hashlib.sha256(path.read_bytes()).hexdigest(),hierarchy_entries=len(h),
        duplicate_named_paths=len(collisions),affected_records_by_script=dict(Counter(r['script'] for rs in collisions.values() for r in rs)),
        collisions={p:[dict(identity=identity(r),script=r['script'],indices=h[identity(r)]['indices']) for r in rs] for p,rs in collisions.items()},
        light_count=len(lights),lights=lights,
        first_bout=dict(GameMode=0,RegionId=1,ArenaSeed=0,BBLevelType=1,level_name='Monastery',evidence='apk-reference36-live/first-bout-environment.json',
            prefab=prefab,root=name,prop_count=len(props),ground_identity=identity(ground),ground_hierarchy=h[identity(ground)],
            note='Hour and Day were not read from the live game; caller must provide them. CampaignStage=1 is HUD evidence, not an extra live memory read.'),
        runtime_domain=dict(implemented='Explicit context; ordinary outdoor levels 0..4 in four catalog regions; source prefab and source prop arrays; native draw order inside methods',
            unsupported=['Christmas environment extras','Spooky coroutines','ThroneRoom lifecycle'],
            lifecycle=['Remove Editor RestoreEnvironment randomization before attaching runtime driver.',
                'Retain every source Transform, including the disabled original Camera; child indices affect native seeds.',
                'Initialize(catalog, Environment Transform, TimeOfDay, Weather) once before any Customize.',
                'Bind OnAmbientMixerValue to recovered AmbienceLowpassVolume if original ambient audio is present.',
                'Customize(explicit Context) at the source menu/arena transition; do not run again as a routine per-frame update.',
                'Driver initializes environment RNG, mountain/hill, moon/stars, clears stage children with deferred Destroy, instantiates source stage.',
                'New floor receives CurrentSeed + its actual source sibling (float), synchronously before six global big-building props.',
                'Stage AutoCustomize props execute their individual Starts later. Explicit global props use the chosen environment seed.',
                'Weather context updates without an extra weather roll; source Awake delayed roll remains. In-arena completion activates existing weather type.',
                'Ground contexts apply only to new stage instances; reapplying the same floor would add offsets twice and is rejected.'],
            validation_limits=['Original ARM instructions execute under Unicorn with bounded Unity API/RNG hooks; RNG values are synthetic.',
                'The actual C# recovery source is compared, not a Python translation of the C#.',
                'Assembly compilation uses current Unity API assemblies read-only; no Unity scene runtime parity claim.',
                'Start ordering across separate scripts is delegated to Unity; each prop has its own source InitState. No forced global reseed after Starts.',
                'OriginalGroundSource full spline loop predates this work; this task restores explicit source OnEnable seed/lifecycle and float precision.',
                'Missing holiday/throne paths throw explicitly. This audit does not claim the whole original game or combat is 1:1.']))
    (out/'environment-recovery-audit.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(dict(hierarchy_entries=len(h),duplicate_named_paths=len(collisions),affected_records_by_script=report['affected_records_by_script'],lights=len(lights),first_bout_props=len(props),first_bout_floor_sibling=h[identity(ground)]['indices'][-1])))
if __name__=='__main__':main()
