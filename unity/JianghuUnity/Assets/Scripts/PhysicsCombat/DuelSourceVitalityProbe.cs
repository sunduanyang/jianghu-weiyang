using System;
using System.Linq;
using Jianghu;
using UnityEngine;

public sealed partial class PhysicsDuel {
    public J ProbeSourceExhaustion(){
        if(!IsSmoke)throw new InvalidOperationException("Source exhaustion probe requires isolated save");
        Clock=elapsed=0;ResetRound();RunCampaign(true);fixtureMode=true;
        var result=player.ProbeSourceExhaustionFlow();fixtureMode=false;PauseCampaign();return result;
    }
    public sealed partial class Fighter {
        internal J ProbeSourceExhaustionFlow(){
            J Physical(bool asleep)=>new J(referenceBones.Select(p=>{var rb=p.Value.rb;var data=sourceParts[p.Key];var normal=referenceNormalPhysics[p.Key];return J.O("key",p.Key,"mass",rb.mass,"drag",rb.linearDamping,"angular_drag",rb.angularDamping,
                "expected_mass",asleep?(float)data["passedOutMass"].N:normal.x,"expected_drag",asleep?(float)data["passedOutDrag"].N:normal.y,"expected_angular_drag",asleep?(float)data["passedOutAngularDrag"].N:normal.z);}).ToList());
            stamina=0;TickReferenceVitality(1f/60);var asleep=J.O("passed_out",PassedOut,"time",ExhaustedLeft,"bodies",Physical(true));
            ApplyReferenceDamage("forearmNear",31,referenceBones["forearmNear"].rb.position,DuelReferenceRules.Surface.MetalBlade,0);
            TickReferenceVitality(1f/60);float injuredWhileAsleep=referenceBones["forearmNear"].rb.mass;
            game.Clock=exhaustedUntil+.0001f;TickReferenceVitality(1f/60);var awake=J.O("passed_out",PassedOut,"stamina",stamina,"bodies",Physical(false));
            game.Clock=recoveryUntil+.0001f;stamina=5;kickUntil=game.Clock+.2999f;kickPaid=false;UpdateReferenceKick();TickReferenceVitality(1f/60);
            return J.O("asleep",asleep,"awake",awake,"injured_while_asleep_mass",injuredWhileAsleep,"kick_exhaustion",J.O("passed_out",PassedOut,"bodies",Physical(true)));
        }
    }
}
