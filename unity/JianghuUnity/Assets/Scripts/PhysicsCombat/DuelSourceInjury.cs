using System;
using System.Linq;
using Jianghu;
using UnityEngine;

public sealed partial class PhysicsDuel {
    public sealed partial class Fighter {
        bool sourceWasLimp;
        void SetSourceObjects(J refs,bool active){foreach(var reference in refs.A){var t=sourceRig.ResolveTransform(reference);if(t!=null)t.gameObject.SetActive(active);}}
        void SetSourcePartDead(string key){
            J data=sourceParts[key];var body=referenceBones[key].rb;
            var dead=sourceRig.Resolve<Collider2D>(data["deadCollider"]);
            if(dead!=null){foreach(var reference in data["aliveColliders"].A){var collider=sourceRig.Resolve<Collider2D>(reference);if(collider!=null)collider.enabled=false;}dead.enabled=true;}
            body.mass=(float)data["deadMass"].N;body.linearDamping=(float)data["deadDrag"].N;body.angularDamping=(float)data["deadAngularDrag"].N;body.gravityScale=1;
        }
        void SetSourceRigLimp(bool limp){
            sourceWasLimp=limp;
            foreach(var pair in sourceParts){
                var body=referenceBones[pair.Key].rb;J data=pair.Value;
                if(health<=0){SetSourcePartDead(pair.Key);continue;}
                Vector3 normal=referenceNormalPhysics[pair.Key];
                body.mass=limp?(float)data["passedOutMass"].N:normal.x;
                body.linearDamping=limp?(float)data["passedOutDrag"].N:normal.y;
                body.angularDamping=limp?(float)data["passedOutAngularDrag"].N:normal.z;
            }
            // ExtraColliders are source GameObjects, not arbitrary broad-phase
            // shapes inferred from the local rig. Dead stages only disable.
            SetSourceObjects(sourceRig.Source("CharacterVitality")["ExtraColliders"],!limp&&health>0);
        }
        void ApplySourceRigDeathStage(int stage){
            var vitality=sourceRig.Source("CharacterVitality");
            switch(stage){
                case 1:
                    // CharacterVitality.cmu, VA 0x350d948, before cp starts.
                    mover.bodyType=RigidbodyType2D.Static;sourceRig.Root.layer=22;
                    foreach(var shape in sourceRig.Root.GetComponents<Collider2D>())DuelSourceRig.ApplyLayerCollision(shape);
                    kickUntil=game.Clock;if(kickShape!=null)kickShape.enabled=false;
                    var extraKick=sourceRig.Resolve<Collider2D>(vitality["extraColliderKick"]);if(extraKick!=null)extraKick.enabled=false;
                    var overlap=sourceRig.Resolve<Collider2D>(vitality["overlapCollider"]);if(overlap!=null)overlap.enabled=false;
                    var vapor=sourceRig.ResolveTransform(vitality["vaporContainer"]);if(vapor!=null)vapor.gameObject.SetActive(false);
                    SetSourceObjects(vitality["ExtraColliders"],false);foreach(var hand in referenceHands.Values)hand.active=false;
                    break;
                case 2:
                    foreach(var reference in vitality["bodyParts"].A)if(sourcePartIds.TryGetValue(DuelSourceRig.Id(reference),out var part))SetSourcePartDead(part);
                    break;
                case 3:
                    foreach(var reference in vitality["bodyFixedJoints2D"].A){var joint=sourceRig.Resolve<FixedJoint2D>(reference);if(joint!=null){joint.frequency=10;joint.dampingRatio=0;}}
                    break;
                case 4:
                    var corpse=sourceRig.ResolveTransform(vitality["weaponSelfColliderWhenDead"]);if(corpse!=null)corpse.gameObject.SetActive(true);
                    DropSourceHand("handNear");DropSourceHand("handFar");
                    break;
                case 5:
                    foreach(var reference in vitality["fixedJoints2DToKill"].A){var joint=sourceRig.Resolve<Joint2D>(reference);if(joint!=null)joint.enabled=false;}
                    break;
                case 6:SetSourceObjects(vitality["gameObjectsToDeactivateUponDeath"],false);break;
                case 7:SetSourceObjects(vitality["gameObjectsToActivateUponDeath"],true);break;
            }
        }
        void DropSourceHand(string key){
            var data=sourceParts[key];var joint=sourceRig.Resolve<FixedJoint2D>(data["handFixedJoint2D"]);
            string side=data["bodyPartType"].I==2?"Near":"Far";
            referenceHands[side].active=false;
            if(joint!=null)joint.enabled=false;
            // Local item objects already belong to Fighter.root (without a
            // physical parent). Never disable the item GameObject: a dropped
            // impaling item retains its timed FixedJoint2D constraint.
            bool both=ReferenceArmDisabled("Near")&&ReferenceArmDisabled("Far");
            if(health<=0||!weapon.TwoHanded||both){
                string slot=weapon.TwoHanded?"Near":side;DropReferenceWeight(slot);
                var item=slot=="Near"?sword:shield;
                if(item!=null)foreach(var shape in item.Frame.GetComponents<Collider2D>())DuelSourceRig.ApplyItemLayer(shape,human,true);
            }
            if(weapon.TwoHanded&&referenceBodies.TryGetValue("handCOM"+side,out var com))com.mass=.55f;
        }
        void DisableSourcePartControls(string key,bool damagedSurface){
            var data=sourceParts[key];int type=data["bodyPartType"].I;
            if(type==2||type==3)DropSourceHand(key);
            if(damagedSurface){
                // cdy tests actual ancestry, rather than assuming every hand
                // auxiliary body is a descendant of the injured bone.
                foreach(string side in new[]{"Near","Far"}){
                    var rb=referenceBodies["handCOM"+side];if(!rb.transform.IsChildOf(referenceBones[key].rb.transform))continue;
                    rb.gameObject.SetActive(false);referenceDriverStarts.Remove(rb);referenceDriverGoals.Remove(rb);
                }
            }
            if(data["triggersAnyLegSevered"].B){referenceBodies["pelvisCOM"].mass=5;kickUntil=game.Clock;if(kickShape!=null)kickShape.enabled=false;}
        }
        void RebuildSourceJointAtRest(string key){
            var data=sourceParts[key];var joint=sourceRig.Resolve<AnchoredJoint2D>(data["joint2D"]);if(joint==null)return;
            var part=referenceBones[key].rb.transform;
            var raw=sourceRig.Data["transforms"].D.Values.First(t=>DuelSourceRig.Id(t["m_GameObject"])==DuelSourceRig.Id(data["m_GameObject"]));
            J rest=raw["m_LocalPosition"];joint.autoConfigureConnectedAnchor=false;
            part.localPosition=new Vector3((float)rest["x"].N,(float)rest["y"].N,(float)rest["z"].N);
            joint.autoConfigureConnectedAnchor=true;joint.autoConfigureConnectedAnchor=false;
        }
        void DamageSourcePartSurface(string key,bool cut){
            var data=sourceParts[key];var body=referenceBones[key].rb;var transform=body.transform;
            Vector2 velocity=body.linearVelocity;float spin=body.angularVelocity;
            foreach(var descendant in transform.GetComponentsInChildren<Rigidbody2D>()){
                if(descendant.gameObject.layer==15||descendant.gameObject.layer==16)continue;
                descendant.gameObject.layer=17;
                foreach(var shape in descendant.GetComponents<Collider2D>())DuelSourceRig.ApplyLayerCollision(shape);
            }
            // The source's second pass explicitly includes inactive children.
            // It changes physics type but does not indiscriminately relayer
            // those inactive objects from the first pass.
            if(cut)foreach(var descendant in transform.GetComponentsInChildren<Rigidbody2D>(true)){
                if(descendant.gameObject.layer==15||descendant.gameObject.layer==16)continue;
                descendant.bodyType=RigidbodyType2D.Dynamic;descendant.interpolation=RigidbodyInterpolation2D.Interpolate;descendant.gravityScale=1;
                if(descendant!=body)descendant.linearVelocity=mover.linearVelocity;
            }
            DisableSourcePartControls(key,true);
            foreach(var reference in data["relatedBodyParts"].A){
                if(!sourcePartIds.TryGetValue(DuelSourceRig.Id(reference),out var related)||!referenceBones[related].rb.transform.IsChildOf(transform))continue;
                SetSourcePartDead(related);if(cut)RebuildSourceJointAtRest(related);
            }
            if(cut){
                RebuildSourceJointAtRest(key);SetSourcePartDead(key);
                // Neck's configured joint is on HEAD, not its own hinge.
                var joint=sourceRig.Resolve<Joint2D>(data["joint2D"]);if(joint!=null)joint.enabled=false;
                var local=transform.localPosition;local.z=(float)data["severedNewZPosition"].N;transform.localPosition=local;
                // Deparent from the source physical graph but retain the bout's
                // nonphysical lifetime owner so restart cannot leak limbs.
                transform.SetParent(root.transform,true);body.linearVelocity=velocity;body.angularVelocity=spin;
            }
            // Destroyed source objects include Head COM and Foot COM bodies.
            // Keep inactive tombstones to preserve exact snapshot ID sets.
            foreach(var reference in data["objectsToDestroy"].A){var t=sourceRig.ResolveTransform(reference);if(t==null)continue;if(t.parent!=null){t.parent.gameObject.layer=17;foreach(var shape in t.parent.GetComponents<Collider2D>())DuelSourceRig.ApplyLayerCollision(shape);}t.gameObject.SetActive(false);}
            SetSourceObjects(data["objectsToActivate"],true);
            body.linearDamping=0;body.angularDamping=5;body.mass=9;body.sharedMaterial=sourceRig.Material(data["deadPhysicsMaterial"]);
            severed.Add(key);partHealth[key]=0;
        }
        void SeverSourceRig(string key){if(!severed.Contains(key))DamageSourcePartSurface(key,true);}
        void ApplySourcePartMode(string key,DuelReferenceRules.PartMode mode){
            if(mode==DuelReferenceRules.PartMode.Bashed)DisableSourcePartControls(key,false);
            else if(mode==DuelReferenceRules.PartMode.Severed)SeverSourceRig(key);
            else if(mode==DuelReferenceRules.PartMode.Pierced)DamageSourcePartSurface(key,false);
            if(sourceParts[key]["bodyPartType"].I==0)ApplyReferenceGlobalDamage(health);
            if(mode==DuelReferenceRules.PartMode.Pierced||mode==DuelReferenceRules.PartMode.Severed)StartReferenceBleeding();
            if(health<=0)BeginReferenceDeath();
        }
    }
}
