using System.Collections.Generic;
using System.Linq;
using Jianghu;
using UnityEngine;

public sealed partial class PhysicsDuel {
    public sealed partial class Fighter {
        readonly Dictionary<string,DuelReferenceRules.PartMode> referencePartModes=new Dictionary<string,DuelReferenceRules.PartMode>();
        readonly Dictionary<string,Vector3> referenceNormalPhysics=new Dictionary<string,Vector3>();
        readonly Dictionary<string,float> referencePartCooldowns=new Dictionary<string,float>();
        int referenceImpactCount;
        float referenceImpactDeadline=-10,referenceBleedAt=-10;
        bool referenceBleeding;
        public DuelReferenceRules.PartMode ReferencePartMode(string key)=>referencePartModes[key];
        public bool ReferenceBleeding=>referenceBleeding;
        public float ReferenceRootDamping=>mover.linearDamping;
        bool ReferenceArmDisabled(string side)=>new[]{"upper","forearm","hand"}.Any(p=>severed.Contains(p+side)||referencePartModes[p+side]!=DuelReferenceRules.PartMode.None);
        void InitializeReferencePart(string key,Vector3 physics){referencePartModes[key]=DuelReferenceRules.PartMode.None;referenceNormalPhysics[key]=physics;referencePartCooldowns[key]=-10;}
        void ClampReferenceRoot(){
            // CharacterMover.clr executes even while passed out/being kicked.
            // It runs at the 60 Hz component clock, before physics integration.
            if(game.referenceSubstep==0&&health>0)mover.linearVelocity=Vector2.ClampMagnitude(mover.linearVelocity,ReferenceDowned?DuelReferenceRules.DownedSpeed:3.6f);
        }
        void TickReferenceVitality(float dt){
            if(health<=0)return;
            if(PassedOut){if(SourceRigActive&&!sourceWasLimp)SetReferenceLimp(true);stamina=0;return;}
            if(exhaustedUntil>0){exhaustedUntil=-10;stamina=DuelReferenceRules.WakeStamina;recoveryUntil=game.Clock+1;SetReferenceLimp(false);}
            if(stamina<=0){exhaustedUntil=game.Clock+3;stamina=0;if(SourceRigActive&&!sourceWasLimp)SetReferenceLimp(true);return;}
            stamina=Mathf.Min(maxStamina,stamina+20*dt);
        }
        void TickReferenceBleeding(){
            // CharacterVitality.FixedUpdateNetwork processes bleeding even
            // during exhaustion. StartBleeding initially waits .65 seconds.
            if(health>0&&referenceBleeding&&game.Clock>=referenceBleedAt){ApplyReferenceGlobalDamage(DuelReferenceRules.BleedDamage);referenceBleedAt=game.Clock+DuelReferenceRules.BleedPeriod;}
        }
        void StartReferenceBleeding(){referenceBleeding=true;referenceBleedAt=game.Clock+DuelReferenceRules.BleedDelay;}
        float ApplyReferenceGlobalDamage(float damage){
            if(health<=0||damage<=0)return 0;
            float dealt=Mathf.Min(health,damage);ConsumeReferenceStamina(damage/2.5f);health-=dealt;
            hitTint=game.Clock+.15f;return dealt;
        }
        public bool AcceptReferenceImpact(string part=null){
            if(part!=null&&(!referencePartCooldowns.ContainsKey(part)||game.Clock<referencePartCooldowns[part]))return false;
            if(game.Clock>referenceImpactDeadline)ResetSourceImpactWindow();
            if(!DuelReferenceRules.RegisterImpact(game.Clock,ref referenceImpactCount,ref referenceImpactDeadline))return false;
            if(part!=null)referencePartCooldowns[part]=game.Clock+DuelReferenceRules.PartCooldown;
            return true;
        }
        void SetReferenceLimp(bool limp){
            if(SourceRigActive){SetSourceRigLimp(limp);return;}
            foreach(var pair in referenceBones){
                string key=pair.Key;var body=pair.Value.rb;bool detached=severed.Contains(key);
                Vector3 props=limp||detached?DuelReferenceRules.RelaxedPhysics(key,health<=0||detached):referenceNormalPhysics[key];
                if(referencePartModes[key]==DuelReferenceRules.PartMode.Pierced||referencePartModes[key]==DuelReferenceRules.PartMode.Severed)props=new Vector3(9,0,5);
                body.mass=props.x;body.linearDamping=props.y;body.angularDamping=props.z;
                if(detached)body.gravityScale=1;
            }
            referenceBodies["headCOM"].gravityScale=referenceBodies["chestCOM"].gravityScale=limp||ReferenceDowned?1:-5;
            referenceBodies["pelvisCOM"].mass=ReferenceDowned?5:3;
        }
        void DisableReferenceArm(string side,bool deactivateDriver=true){
            referenceHands[side].active=false;if(deactivateDriver)DisableReferenceDriver("handCOM"+side);
            if(side=="Near"&&primaryGrip!=null)primaryGrip.enabled=false;
            if(side=="Far"){if(offhandGrip!=null)offhandGrip.enabled=false;if(supportJoint!=null)supportJoint.enabled=false;}
            if(!weapon.TwoHanded||ReferenceArmDisabled("Near")&&ReferenceArmDisabled("Far"))DropReferenceWeight(weapon.TwoHanded?"Near":side);
        }
        void DisableReferenceDriver(string key){
            var rb=referenceBodies[key];rb.simulated=false;
            foreach(var j in rb.GetComponents<FixedJoint2D>())j.enabled=false;
            referenceDriverStarts.Remove(rb);referenceDriverGoals.Remove(rb);
        }
        void SetReferencePartMode(string key,DuelReferenceRules.PartMode mode){
            if(mode==DuelReferenceRules.PartMode.None||referencePartModes[key]!=DuelReferenceRules.PartMode.None)return;
            referencePartModes[key]=mode;
            if(SourceRigActive){ApplySourcePartMode(key,mode);return;}
            if(mode==DuelReferenceRules.PartMode.Severed)SeverReference(key);
            if(key.StartsWith("upper")||key.StartsWith("forearm")||key.StartsWith("hand"))DisableReferenceArm(key.EndsWith("Near")?"Near":"Far",mode!=DuelReferenceRules.PartMode.Bashed);
            if(key.StartsWith("thigh")||key.StartsWith("shin")||key.StartsWith("foot")){
                DisableReferenceDriver("footCOM"+(key.EndsWith("Near")?"Near":"Far"));
                referenceBodies["pelvisCOM"].mass=5;kickUntil=game.Clock;if(kickShape!=null)kickShape.enabled=false;
            }
            if(key=="head"||key=="neck")ApplyReferenceGlobalDamage(health);
            if(mode==DuelReferenceRules.PartMode.Pierced||mode==DuelReferenceRules.PartMode.Severed)StartReferenceBleeding();
            if(health<=0)BeginReferenceDeath();else SetReferenceLimp(PassedOut);
        }
        public float ApplyReferenceDamage(string key,float damage,Vector2 point,Vector2 direction,bool sharp)=>ApplyReferenceDamage(key,damage,point,sharp?DuelReferenceRules.Surface.MetalBlade:DuelReferenceRules.Surface.MetalBlunt,damage);
        public float ApplyReferenceDamage(string key,float damage,Vector2 point,DuelReferenceRules.Surface surface,float knockback){
            if(!partHealth.ContainsKey(key)||severed.Contains(key)||health<=0||referencePartModes[key]==DuelReferenceRules.PartMode.Pierced)return 0;
            if(referencePartModes[key]==DuelReferenceRules.PartMode.None)partHealth[key]=Mathf.Max(0,partHealth[key]-damage);
            float requestedGlobal=damage*(SourceRigActive?(float)sourceParts[key]["overallDamageMultiplier"].N:key=="head"||key=="neck"?.75f:.35f);
            float global=ApplyReferenceGlobalDamage(requestedGlobal);
            // OnHandItemImpact_Authoritative accumulates the requested global
            // damage after applying it, including damage beyond remaining HP.
            AccumulateSourceImpact(requestedGlobal);
            mover.AddForce(new Vector2(-facing*knockback*.01f*game.referenceSteps,0));
            bool vital=key=="head"||key=="neck";
            bool canDisable=SourceRigActive?(sourceParts[key]["bodyPartType"].I==0?health<20:sourceParts[key]["severable"].B):(!vital||health<20)&&key!="pelvis"&&key!="belly"&&key!="torso";
            if(partHealth[key]<=0&&canDisable)SetReferencePartMode(key,DuelReferenceRules.ModeFor(surface));
            return global;
        }
        J CaptureReferenceCore(){
            var modes=J.O();var cooldowns=J.O();
            foreach(string key in referenceBones.Keys){modes[key]=new J((int)referencePartModes[key]);cooldowns[key]=new J(Mathf.Max(0,referencePartCooldowns[key]-game.Clock));}
            return J.O("version",1,"part_modes",modes,"part_cooldowns",cooldowns,"bleeding",referenceBleeding,"bleed_left",referenceBleeding?Mathf.Max(0,referenceBleedAt-game.Clock):0,
                "impact_count",referenceImpactCount,"impact_left",Mathf.Max(0,referenceImpactDeadline-game.Clock));
        }
        void RestoreReferenceCore(J state,J parts){
            if(state.Has){
                foreach(string key in referenceBones.Keys){partHealth[key]=(float)parts[key]["health"].N;}
                foreach(string key in referenceBones.Keys)SetReferencePartMode(key,(DuelReferenceRules.PartMode)state["part_modes"][key].I);
                foreach(string key in referenceBones.Keys)referencePartCooldowns[key]=game.Clock+(float)state["part_cooldowns"][key].N;
                referenceBleeding=state["bleeding"].B;referenceBleedAt=game.Clock+(float)state["bleed_left"].N;
                referenceImpactCount=state["impact_count"].I;referenceImpactDeadline=game.Clock+(float)state["impact_left"].N;
            }else{
                // Earlier snapshots only distinguished severed vs attached.
                foreach(var p in parts.D){partHealth[p.Key]=(float)p.Value["health"].N;if(p.Value["severed"].B)SetReferencePartMode(p.Key,DuelReferenceRules.PartMode.Severed);else if(partHealth[p.Key]<=0&&p.Key!="head"&&p.Key!="neck"&&p.Key!="torso"&&p.Key!="belly"&&p.Key!="pelvis")SetReferencePartMode(p.Key,DuelReferenceRules.PartMode.Bashed);}
                referenceBleeding=false;referenceBleedAt=-10;
            }
        }
    }
}
