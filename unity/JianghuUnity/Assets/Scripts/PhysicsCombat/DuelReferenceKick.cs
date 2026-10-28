using System;
using Jianghu;
using UnityEngine;

public sealed partial class PhysicsDuel {
    public sealed partial class Fighter {
        const float KickDuration=.5f,KickWindup=.2f,KickForce=1350;
        bool kickPaid;
        float kickedUntil=-10,kickImmunityUntil=-10;
        CapsuleCollider2D kickShape;
        public bool ReferenceBeingKicked=>Reference&&game.Clock<kickedUntil;
        public bool ReferenceKickActive=>ReferenceKicking&&kickPaid&&!PassedOut&&!ReferenceDowned&&health>0;
        void BuildReferenceKick(){
            // Separate trigger and receiver, as in the reference prefab.
            var foot=limbs["footNear"];
            var go=new GameObject("kick trigger");go.transform.SetParent(foot.rb.transform,false);go.layer=10;
            kickShape=go.AddComponent<CapsuleCollider2D>();kickShape.size=new Vector2(.25f,.5f);kickShape.isTrigger=true;kickShape.enabled=false;
            go.AddComponent<DuelKickTrigger>().owner=this;colliders.Add(kickShape);
            var receiver=new GameObject("kick receiver");receiver.transform.SetParent(pelvis.rb.transform,false);receiver.layer=10;
            var box=receiver.AddComponent<BoxCollider2D>();box.size=new Vector2(.8f,1.6f);box.isTrigger=true;
            referenceKickReceiver=box;
            receiver.AddComponent<DuelKickReceiver>().owner=this;colliders.Add(box);
        }
        bool KickReference(){
            if(!game.Live||health<=0||PassedOut||ReferenceDowned||ReferenceKicking)return false;
            foreach(string side in new[]{"Near","Far"})if(severed.Contains("thigh"+side)||severed.Contains("shin"+side)||severed.Contains("foot"+side))return false;
            kickUntil=game.Clock+KickDuration;kickPaid=false;return true;
        }
        void UpdateReferenceKick(){
            if((health<=0||PassedOut||ReferenceDowned)&&ReferenceKicking)kickUntil=game.Clock;
            if(ReferenceKicking&&!kickPaid&&game.Clock>=kickUntil-KickDuration+KickWindup){
                kickPaid=true;ConsumeReferenceStamina(10);
                if(stamina<=0){exhaustedUntil=game.Clock+3;kickUntil=game.Clock;}
            }
            if(kickShape!=null)kickShape.enabled=ReferenceKickActive;
        }
        Vector2 ReferenceKickFoot(string side){
            float t=KickDuration-(kickUntil-game.Clock);
            string path="FeetContainer/Feet/"+(side=="Near"?"FootLeftPoint":"FootRightPoint");
            var motion=DuelSourceMotion.Kick;
            Vector2 delta=motion.Position(path,t)-motion.Position(path,0);
            return mover.position+new Vector2(facing*((side=="Near"?.20f:-.20f)+delta.x),.075f+delta.y);
        }
        Vector2 ReferenceKickPelvis(){
            if(!ReferenceKicking)return Vector2.zero;
            float t=KickDuration-(kickUntil-game.Clock);
            Vector2 p=DuelSourceMotion.Kick.Position("Top",t);
            return new Vector2(facing*p.x,p.y);
        }
        public bool ReceiveReferenceKick(Fighter attacker){
            if(!Reference||attacker==this||!game.Live||!attacker.ReferenceKickActive||health<=0||game.Clock<kickImmunityUntil)return false;
            kickImmunityUntil=kickedUntil=game.Clock+.5f;
            float vertical=mover.linearVelocity.y;
            mover.linearVelocity=pelvis.rb.linearVelocity=Vector2.zero;
            // One reference 60 Hz AddForce call; express its impulse explicitly
            // so four internal contact steps cannot divide the push by four.
            Vector2 impulse=new Vector2(attacker.facing*KickForce,vertical)/60;
            mover.AddForce(impulse,ForceMode2D.Impulse);
            if(PassedOut)pelvis.rb.AddForce(impulse*20,ForceMode2D.Impulse);
            float dealt=ApplyReferenceGlobalDamage(1);
            game.hits++;attacker.landedHits++;attacker.lastHit=game.Clock;
            game.Impact(pelvis.rb.position,false,dealt);return true;
        }
        J CaptureReferenceKick()=>J.O("version",1,"paid",kickPaid,"kicked_left",Mathf.Max(0,kickedUntil-game.Clock),"immunity_left",Mathf.Max(0,kickImmunityUntil-game.Clock));
        void RestoreReferenceKick(J s){
            var state=s["kick_state"];
            // Legacy active kicks predate a cost/receiver. Do not retroactively
            // charge a kick whose saved pose is already past its windup.
            kickPaid=state.Has?state["paid"].B:(float)s["kick_left"].N<=KickDuration-KickWindup;
            kickedUntil=game.Clock+(float)state["kicked_left"].N;kickImmunityUntil=game.Clock+(float)state["immunity_left"].N;
            if(kickShape!=null)kickShape.enabled=ReferenceKickActive;
        }
    }
}

public sealed class DuelKickTrigger:MonoBehaviour {
    [NonSerialized] public PhysicsDuel.Fighter owner;
    void OnTriggerEnter2D(Collider2D other){var receiver=other.GetComponent<DuelKickReceiver>();if(owner!=null&&receiver!=null&&receiver.owner!=null)receiver.owner.ReceiveReferenceKick(owner);}
}
public sealed class DuelKickReceiver:MonoBehaviour {
    [NonSerialized] public PhysicsDuel.Fighter owner;
}
