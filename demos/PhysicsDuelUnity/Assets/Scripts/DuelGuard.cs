using System.Linq;
using UnityEngine;

public sealed partial class PhysicsDuel {
    readonly RaycastHit2D[] guardRays=new RaycastHit2D[32];
    readonly ContactFilter2D guardFilter=new ContactFilter2D {useLayerMask=true,layerMask=1<<10,useTriggers=false};
    public int exposedGuardHits;

    bool GuardOccludes(HitPacket hit) {
        Vector2 motion=hit.relativeVelocity;
        if(motion.sqrMagnitude<.0001f)return false;
        // Check the incoming path of this particular body contact. A guard
        // touching a different section of a long blade grants no global lock.
        float travel=Mathf.Clamp(motion.magnitude*Time.fixedDeltaTime+.06f,.08f,.35f);
        Vector2 direction=motion.normalized,from=hit.point-direction*travel;
        int count=Physics2D.Raycast(from,direction,guardFilter,guardRays,travel);
        for(int i=0;i<count;i++) {
            var guard=guardRays[i].collider.GetComponent<DuelContact>();
            if(guard!=null&&guard.owner==hit.other.owner&&(guard.kind=="shield"||guard.kind=="sword"))return true;
        }
        return false;
    }
    bool SameStepGuardContact(HitPacket hit)=>contacts.Any(g=>g.blocked&&g.step==hit.step&&g.strokeId==hit.strokeId&&g.weapon.limb==hit.weapon.limb&&g.other.owner==hit.other.owner);
}
