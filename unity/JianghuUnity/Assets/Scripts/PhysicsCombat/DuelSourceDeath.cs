using System.Linq;
using Jianghu;
using UnityEngine;

public sealed partial class PhysicsDuel {
    public sealed partial class Fighter {
        const float SourceDeathDelay=.025f;
        int referenceDeathStage;
        float referenceDeathAt;
        Rigidbody2D referenceCorpseBody;
        CircleCollider2D referenceCorpseShape;
        Collider2D referenceKickReceiver;
        public int ReferenceDeathStage=>referenceDeathStage;
        void BuildReferenceCorpse(){
            // WeaponSelfCollider: source Rigidbody2D 550, collider 587, joint
            // 833. The chest pivot is expressed in the current Limb frame.
            var go=new GameObject("Recovered corpse weapon collider");go.transform.SetParent(root.transform,false);go.layer=10;
            referenceCorpseBody=go.AddComponent<Rigidbody2D>();
            referenceCorpseBody.mass=.25f;referenceCorpseBody.linearDamping=0;referenceCorpseBody.angularDamping=.05f;
            referenceCorpseBody.interpolation=RigidbodyInterpolation2D.Interpolate;
            referenceCorpseBody.position=torso.A+new Vector2(-facing*.0225f,.1025f);
            referenceCorpseShape=go.AddComponent<CircleCollider2D>();referenceCorpseShape.radius=.155f;
            referenceCorpseShape.excludeLayers=~(1<<10);referenceCorpseShape.forceSendLayers=1<<10;referenceCorpseShape.forceReceiveLayers=0;
            colliders.Add(referenceCorpseShape);
            var joint=go.AddComponent<FixedJoint2D>();joint.connectedBody=torso.rb;joint.autoConfigureConnectedAnchor=false;
            joint.connectedAnchor=Limb.BodyLocalPoint(torso.rb,referenceCorpseBody.position);joint.frequency=0;joint.dampingRatio=0;
            go.SetActive(false);
        }
        void BeginReferenceDeath(){
            if(referenceDeathStage!=0)return;
            ApplyReferenceDeathStage(1);referenceDeathAt=game.Clock+SourceDeathDelay;
        }
        void TickReferenceDeath(){
            if(health>0)return;
            if(referenceDeathStage==0)BeginReferenceDeath();
            // Original WaitForSeconds resumes on a subsequent component tick.
            // Additional contact substeps do not advance this coroutine.
            if(game.referenceSubstep!=0||referenceDeathStage>=7||game.Clock<referenceDeathAt)return;
            ApplyReferenceDeathStage(referenceDeathStage+1);referenceDeathAt=game.Clock+SourceDeathDelay;
        }
        void ApplyReferenceDeathStage(int stage){
            referenceDeathStage=stage;
            switch(stage){
                case 1:
                    kickUntil=game.Clock;if(kickShape!=null)kickShape.enabled=false;
                    if(referenceKickReceiver!=null)referenceKickReceiver.enabled=false;
                    foreach(var h in referenceHands.Values)h.active=false;
                    break;
                case 2:SetReferenceLimp(true);break;
                case 3:
                    var joints=new[]{primaryGrip,offhandGrip,supportJoint,
                        referenceBodies["chestCOM"].GetComponent<FixedJoint2D>(),referenceBodies["pelvisCOM"].GetComponent<FixedJoint2D>()};
                    foreach(var joint in joints.Where(j=>j!=null)){joint.frequency=10;joint.dampingRatio=0;}
                    break;
                case 4:
                    var fixedJoint=referenceCorpseBody.GetComponent<FixedJoint2D>();
                    referenceCorpseBody.position=Limb.BodyPoint(fixedJoint.connectedBody,fixedJoint.connectedAnchor);
                    referenceCorpseBody.rotation=fixedJoint.connectedBody.rotation;
                    referenceCorpseBody.gameObject.SetActive(true);
                    foreach(var shape in colliders.Where(c=>c!=referenceCorpseShape&&c.gameObject.layer==10))Physics2D.IgnoreCollision(referenceCorpseShape,shape,false);
                    DisableReferenceArm("Near",false);DisableReferenceArm("Far",false);
                    break;
                case 5:
                    referenceBodies["headCOM"].GetComponent<FixedJoint2D>().enabled=false;
                    break;
                // The source's remaining two stages deactivate footsteps SFX
                // and activate an empty prefab list. There is no gait audio
                // component or additional death object in this game yet.
            }
        }
        J CaptureReferenceDeath()=>J.O("version",1,"stage",referenceDeathStage,"wait",referenceDeathStage>0&&referenceDeathStage<7?Mathf.Max(0,referenceDeathAt-game.Clock):0,
            "body",J.O("x",referenceCorpseBody.position.x,"y",referenceCorpseBody.position.y,"angle",referenceCorpseBody.rotation,
                "vx",referenceCorpseBody.linearVelocity.x,"vy",referenceCorpseBody.linearVelocity.y,"spin",referenceCorpseBody.angularVelocity));
        void RestoreReferenceDeath(J state){
            int stage=state.Has?state["stage"].I:health<=0?7:0;
            referenceDeathStage=0;
            for(int i=1;i<=stage;i++)ApplyReferenceDeathStage(i);
            referenceDeathAt=game.Clock+(float)state["wait"].N;
            if(state.Has){var b=state["body"];referenceCorpseBody.position=new Vector2((float)b["x"].N,(float)b["y"].N);referenceCorpseBody.rotation=(float)b["angle"].N;referenceCorpseBody.linearVelocity=new Vector2((float)b["vx"].N,(float)b["vy"].N);referenceCorpseBody.angularVelocity=(float)b["spin"].N;}
        }
    }
}
