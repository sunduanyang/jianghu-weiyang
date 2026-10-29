using System;
using Jianghu;
using UnityEngine;

public sealed partial class PhysicsDuel {
    public sealed partial class Fighter {
        const float KickDuration=.5f,KickWindup=.2f,KickForce=1350;
        bool kickPaid;
        float kickedUntil=-10,kickImmunityUntil=-10;
        int sourceKickReceiverStage;
        float sourceKickReceiverAt;
        CapsuleCollider2D kickShape;
        public bool ReferenceBeingKicked=>Reference&&(SourceRigActive?sourceKickReceiverStage!=0:game.Clock<kickedUntil);
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
            TickSourceKickReceiver();
            if((health<=0||PassedOut||ReferenceDowned)&&ReferenceKicking)kickUntil=game.Clock;
            if(ReferenceKicking&&!kickPaid&&game.Clock>=kickUntil-KickDuration+KickWindup){
                kickPaid=true;ConsumeReferenceStamina(10);
                if(stamina<=0){exhaustedUntil=game.Clock+3;kickUntil=game.Clock;}
            }
            if(kickShape!=null){kickShape.enabled=ReferenceKickActive;if(SourceRigActive&&kickShape.gameObject.activeSelf!=ReferenceKickActive)kickShape.gameObject.SetActive(ReferenceKickActive);}
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
            if(SourceRigActive)return ReceiveSourceRigKick(attacker);
            kickImmunityUntil=kickedUntil=game.Clock+.5f;
            float vertical=mover.linearVelocity.y;
            mover.linearVelocity=pelvis.rb.linearVelocity=Vector2.zero;
            // One reference 60 Hz AddForce call; express its impulse explicitly
            // so four internal contact steps cannot divide the push by four.
            Vector2 impulse=new Vector2(attacker.facing*KickForce,vertical)/60;
            mover.CombatAddForce(impulse,ForceMode2D.Impulse);
            if(PassedOut)pelvis.rb.CombatAddForce(impulse*20,ForceMode2D.Impulse);
            float dealt=ApplyReferenceGlobalDamage(1);
            game.hits++;attacker.landedHits++;attacker.lastHit=game.Clock;
            game.Impact(pelvis.rb.position,false,dealt);return true;
        }
        bool ReceiveSourceRigKick(Fighter attacker){
            var config=sourceRig.Source("KickReceiver");var baseBody=sourceRig.Resolve<Rigidbody2D>(config["baseRigidbody2D"]);var pelvisBody=sourceRig.Resolve<Rigidbody2D>(config["pelvisRigidbody2D"]);
            float vertical=baseBody.linearVelocity.y;
            Vector2 force=new Vector2((config["flipped"].B?1:-1)*(float)config["kickForce"].N,vertical);
            baseBody.linearVelocity=pelvisBody.linearVelocity=Vector2.zero;
            // Native OnTriggerEnter2D uses AddForce(Vector2), Force mode. Its
            // one 60 Hz force application is represented as an impulse so the
            // host's extra contact substeps cannot divide the source push.
            Vector2 impulse=force/60;baseBody.CombatAddForce(impulse,ForceMode2D.Impulse);
            if(PassedOut)pelvisBody.CombatAddForce(impulse*20,ForceMode2D.Impulse);
            float dealt=ApplyReferenceGlobalDamage(1);kickImmunityUntil=game.Clock+.5f;
            sourceKickReceiverStage=1;sourceKickReceiverAt=game.Clock+(float)config["timeUntilDampingStart"].N;
            kickedUntil=sourceKickReceiverAt+(float)config["timeUntilDampingReset"].N;
            game.hits++;attacker.landedHits++;attacker.lastHit=game.Clock;game.Impact(pelvisBody.position,false,dealt);return true;
        }
        void TickSourceKickReceiver(){
            if(!SourceRigActive||sourceKickReceiverStage==0||game.referenceSubstep!=0||game.Clock<sourceKickReceiverAt)return;
            if(sourceKickReceiverStage==1){sourceKickReceiverStage=2;sourceKickReceiverAt=game.Clock+(float)sourceRig.Source("KickReceiver")["timeUntilDampingReset"].N;kickedUntil=sourceKickReceiverAt;}
            else{sourceKickReceiverStage=0;sourceKickReceiverAt=0;kickedUntil=game.Clock;}
            // KickReceiver/ct.MoveNext has two WaitForSeconds but no damping
            // writes. dampingWhenKicked and cached OnEnable damping are unused
            // in the supplied executable and must not invent an extra force.
        }
        J CaptureReferenceKick(){
            var state=J.O("version",1,"paid",kickPaid,"kicked_left",Mathf.Max(0,kickedUntil-game.Clock),"immunity_left",Mathf.Max(0,kickImmunityUntil-game.Clock));
            if(SourceRigActive)state["source_receiver"]=J.O("version",1,"stage",sourceKickReceiverStage,"wait",sourceKickReceiverStage==0?0:Mathf.Max(0,sourceKickReceiverAt-game.Clock));
            return state;
        }
        void RestoreReferenceKick(J s){
            var state=s["kick_state"];
            // Legacy active kicks predate a cost/receiver. Do not retroactively
            // charge a kick whose saved pose is already past its windup.
            kickPaid=state.Has?state["paid"].B:(float)s["kick_left"].N<=KickDuration-KickWindup;
            kickedUntil=game.Clock+(float)state["kicked_left"].N;kickImmunityUntil=game.Clock+(float)state["immunity_left"].N;
            if(SourceRigActive){
                var receiver=state["source_receiver"];float left=(float)state["kicked_left"].N,second=(float)sourceRig.Source("KickReceiver")["timeUntilDampingReset"].N;
                sourceKickReceiverStage=receiver.Has?receiver["stage"].I:left>second?1:left>0?2:0;
                float wait=receiver.Has?(float)receiver["wait"].N:sourceKickReceiverStage==1?left-second:left;
                sourceKickReceiverAt=sourceKickReceiverStage==0?0:game.Clock+wait;
            }
            if(kickShape!=null){kickShape.enabled=ReferenceKickActive;if(SourceRigActive&&kickShape.gameObject.activeSelf!=ReferenceKickActive)kickShape.gameObject.SetActive(ReferenceKickActive);}
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
