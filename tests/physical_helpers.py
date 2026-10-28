"""Protocol fixtures only; native acceptance verifies the actual Unity poses."""
from jianghu import physical_combat as combat
from jianghu import physical_reference as reference

def frame(battle, elapsed=.25):
    result=dict(engine=combat.ENGINE,version=1,elapsed=elapsed,hits=battle['hits'],clashes=battle['clashes'],fighters=[])
    for i,f in enumerate(battle['fighters']):
        keys=combat.BODY_KEYS-({'shield'} if f['weapon']['support_grip'] else set())
        bodies=[dict(key=k,x=-2 if i==0 else 2,y=2,angle=0,vx=0,vy=0,spin=0) for k in sorted(keys)]
        feet={s:dict(state=0,phase=0,anchor_x=-2 if i==0 else 2,anchor_y=.2,from_x=0,from_y=.2,target_x=0,target_y=.2) for s in ('Near','Far')}
        result['fighters'].append(dict(health=f['vigor'],stamina=f['stamina'],balance=100,strength=1,heat=0,
            motion_spent=f['motion_spent'],attack_count=f['attack_count'],landed_hits=f['hits'],bodies=bodies,feet=feet))
        if f.get('physics_profile') == reference.PROFILE:
            result['fighters'][-1]['reference'] = dict(profile=reference.PROFILE, exhausted_left=0,
                recovering_left=0, wake_pending=False, effort_tick=0, walk_phase=0, kick_left=0,
                weapon_rig=1, weights=[dict(key=k,x=0,y=1,angle=0,vx=0,vy=0,spin=0) for k in sorted(reference.weight_keys(f))],
                parts={k:dict(health=hp,severed=False) for k,hp in reference.PARTS.items()},
                bodies=[dict(key=k,x=0,y=1,angle=0,vx=0,vy=0,spin=0) for k in sorted(reference.EXTRA_BODIES)],
                hands={s:dict(anchor_x=0,anchor_y=0,target_x=0,target_y=0,heat=0,
                    drain=3 if f['weapon']['support_grip'] else 4.5) for s in ('Near','Far')})
    return result
