using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Jianghu;
using UnityEngine;

public sealed partial class OriginalRecoveryValidation {
    readonly List<J> lifecycleCases=new List<J>();
    readonly List<OriginalImpact> lifeImpacts=new List<OriginalImpact>();
    readonly Dictionary<Rigidbody2D,RigidbodyType2D> lifeBodyTypes=new Dictionary<Rigidbody2D,RigidbodyType2D>();
    readonly Dictionary<Collider2D,bool> lifeColliderEnabled=new Dictionary<Collider2D,bool>();
    OriginalRig lifeOwner,lifeVictim;
    OriginalItem lifeBlade;
    Collider2D lifeBladeCollider;
    J lifeSource;
    J lifeTargetCandidates;

    IEnumerator RunLifecycle() {
        lifeSource=J.Parse(Resources.Load<TextAsset>("OriginalCombatSource").text);
        foreach(string name in new[]{"temporary_impale_joint","severed_arm_two_hand_grip_and_bleeding","staged_death_and_cleanup"}) {
            var errors=new List<string>();var result=J.O("name",name);
            IEnumerator test=name=="temporary_impale_joint"?ImpaleLifecycle(result,errors):name=="severed_arm_two_hand_grip_and_bleeding"?ArmLifecycle(result,errors):DeathLifecycle(result,errors);
            yield return GuardLifecycle(test,errors);
            yield return GuardLifecycle(CleanupLifecycle(result,errors),errors);
            var unique=errors.Distinct().ToList();result["errors"]=Strings(unique);result["passed"]=new J(unique.Count==0);
            result["damage_mode_or_health_injected"]=new J(false);
            result["fixture"]=new J("Original source rigs/items/colliders; targets temporarily anchored for controlled physical contacts, then original body types and normal FixedUpdate restored for lifecycle observation. No health, local damage, death or part mode setters are called.");
            lifecycleCases.Add(result);foreach(string error in unique)Issue(name,"lifecycle",error);Write(false);
        }
    }
    IEnumerator GuardLifecycle(IEnumerator routine,List<string> errors) {
        while(true) {
            object current=null;bool more=false;Exception failure=null;
            try{more=routine.MoveNext();if(more)current=routine.Current;}catch(Exception ex){failure=ex;}
            if(failure!=null){errors.Add("exception: "+failure);yield break;}
            if(!more)yield break;
            if(current is IEnumerator nested)yield return GuardLifecycle(nested,errors);else yield return current;
        }
    }
    IEnumerator BeginLifecycle(bool armedVictim) {
        Physics2D.simulationMode=SimulationMode2D.Script;lifeImpacts.Clear();lifeBodyTypes.Clear();lifeColliderEnabled.Clear();lifeBlade=null;lifeBladeCollider=null;
        lifeOwner=CreateFixture(true,new Vector3(-10,12,0));lifeVictim=CreateFixture(false,new Vector3(0,12,0));
        lifeOwner.opponent=lifeVictim;lifeVictim.opponent=lifeOwner;
        if(armedVictim)lifeVictim.EquipOriginal(LifeItem(r=>r["category"].I==1&&r["two_handed"].B)["resource"].S,null);
        foreach(var body in lifeVictim.GetComponentsInChildren<Rigidbody2D>(true)) {lifeBodyTypes[body]=body.bodyType;body.bodyType=RigidbodyType2D.Kinematic;body.linearVelocity=Vector2.zero;body.angularVelocity=0;}
        foreach(var body in lifeOwner.GetComponentsInChildren<Rigidbody2D>(true)){body.bodyType=RigidbodyType2D.Kinematic;body.linearVelocity=Vector2.zero;body.angularVelocity=0;}
        foreach(var collider in lifeVictim.GetComponentsInChildren<Collider2D>(true))lifeColliderEnabled[collider]=collider.enabled;
        lifeVictim.ImpactOccurred+=impact=>lifeImpacts.Add(impact);
        yield return new WaitForSeconds(.55f);
    }
    J LifeItem(Func<J,bool> predicate) {
        var row=catalog["items"].A.FirstOrDefault(predicate);if(row==null)throw new InvalidOperationException("Required original lifecycle item not found");return row;
    }
    J LifeControllers=>lifeSource[OriginalRig.EnemySource]["controllers"];
    OriginalBodyPart LifePart(Func<OriginalBodyPart,bool> predicate) {
        var part=lifeVictim.BodyParts.FirstOrDefault(predicate);if(part==null)throw new InvalidOperationException("Required original body part not found");return part;
    }
    Collider2D LifeCollider(OriginalBodyPart part,int castMask=-1) {
        var candidates=lifeColliderEnabled.Keys.Where(c=>c!=null&&c.attachedRigidbody==part.body).ToArray();
        bool Eligible(Collider2D c)=>lifeColliderEnabled[c]&&c.gameObject.activeInHierarchy&&!c.isTrigger&&
            c.GetComponent<OriginalBodyPart>()==part&&c.gameObject.layer==part.OriginalCollisionLayer&&
            (castMask==-1?!Physics2D.GetIgnoreLayerCollision(lifeOwner.human?15:16,c.gameObject.layer):(castMask&(1<<c.gameObject.layer))!=0);
        lifeTargetCandidates=new J(candidates.Select(c=>J.O("path",LifePath(c.transform),"type",c.GetType().Name,"tag",c.tag,"layer",c.gameObject.layer,"source_part_layer",part.OriginalCollisionLayer,"source_enabled",lifeColliderEnabled[c],"trigger",c.isTrigger,"on_part_receiver",c.GetComponent<OriginalBodyPart>()==part,"eligible",Eligible(c))).ToList());
        // Source ExtraCollider_Chest/Base share the same attached rigidbody,
        // but live on layer 31 and are not the part's damage colliders. A
        // bounds-only selection accidentally chose those larger self shapes.
        var collider=candidates.Where(Eligible).OrderByDescending(c=>Area(c.bounds)).FirstOrDefault();
        if(collider==null)throw new InvalidOperationException("No original active non-trigger collider for part "+part.sourceId);return collider;
    }
    string LifePath(Transform node) {
        var names=new List<string>();for(var current=node;current!=null&&current!=lifeVictim.transform;current=current.parent)names.Add(current.name);names.Reverse();return string.Join("/",names);
    }
    void FocusLifeCollider(Collider2D target) {
        foreach(var collider in lifeVictim.GetComponentsInChildren<Collider2D>(true))collider.enabled=collider==target;
        Physics2D.SyncTransforms();
    }
    OriginalItem EquipLifeAttacker(J row) {
        if(lifeOwner.leftItem!=null)lifeOwner.leftItem.gameObject.SetActive(false);
        if(lifeOwner.rightItem!=null)lifeOwner.rightItem.gameObject.SetActive(false);
        lifeOwner.EquipOriginal(row["resource"].S,null);var item=lifeOwner.leftItem;
        if(item==null)throw new InvalidOperationException("Original EquipOriginal returned no lifecycle item");
        foreach(var c in lifeOwner.GetComponentsInChildren<Collider2D>(true))if(!c.transform.IsChildOf(item.transform))c.enabled=false;
        foreach(var joint in lifeOwner.GetComponentsInChildren<Joint2D>(true))joint.enabled=false;
        item.transform.SetParent(null,true);foreach(var joint in item.GetComponentsInChildren<Joint2D>(true))joint.enabled=false;
        return item;
    }
    J FireLifeCannon(OriginalBodyPart target) {
        var row=LifeItem(r=>r["cannon"].Has);int mask=row["cannon"][lifeOwner.OriginalFlipped?"targetMask_ShooterIsCharacter2":"targetMask_ShooterIsCharacter1"]["m_Bits"].I;
        var collider=LifeCollider(target,mask);var candidates=lifeTargetCandidates;FocusLifeCollider(collider);
        var item=EquipLifeAttacker(row);
        foreach(var body in item.GetComponentsInChildren<Rigidbody2D>(true))body.simulated=false;
        foreach(var c in item.GetComponentsInChildren<Collider2D>(true))c.enabled=false;
        var muzzle=item.RefTransform(row["cannon"]["muzzle"]);var aim=item.RefTransform(row["cannon"]["projectileTargetPoint"]);
        Vector2 direction=aim.position-muzzle.position;item.transform.Rotate(0,0,-Mathf.Atan2(direction.y,direction.x)*Mathf.Rad2Deg,Space.World);
        item.transform.position+=(Vector3)((Vector2)collider.bounds.center+Vector2.left*3-(Vector2)muzzle.position);
        Physics2D.SyncTransforms();int before=lifeImpacts.Count;float hp=lifeVictim.health;var cannon=item.GetComponent<OriginalHandCannon>();cannon.Fire();var shot=cannon.LastShot;
        return J.O("method","original_cannon_cast","target_part",target.sourceId,"target_path",LifePath(collider.transform),"target_tag",collider.tag,"target_layer",collider.gameObject.layer,"target_candidates",candidates,"cast_mask",shot.layerMask,"cast_hits",shot.hitCount,"selected_part",shot.bodyPart==null?0:shot.bodyPart.sourceId,"impact_events",lifeImpacts.Count-before,"health_before",Number(hp),"health_after",Number(lifeVictim.health));
    }
    J HitLifeBlade(OriginalBodyPart target,J row,float speed,List<string> errors) {
        var collider=LifeCollider(target);var candidates=lifeTargetCandidates;FocusLifeCollider(collider);lifeBlade=EquipLifeAttacker(row);
        lifeBladeCollider=lifeBlade.GetComponentsInChildren<Collider2D>(true).Where(c=>c.enabled&&c.gameObject.activeInHierarchy&&!c.isTrigger&&c.CompareTag("HandItem")&&c.attachedRigidbody!=null&&OriginalCombatRules.Surface(c.sharedMaterial)==OriginalSurface.MetalBlade).FirstOrDefault();
        if(lifeBladeCollider==null)throw new InvalidOperationException("Required source MetalBlade collider missing");
        foreach(var c in lifeBlade.GetComponentsInChildren<Collider2D>(true))c.enabled=c==lifeBladeCollider;
        foreach(var body in lifeBlade.GetComponentsInChildren<Rigidbody2D>(true)){body.simulated=body==lifeBladeCollider.attachedRigidbody;body.gravityScale=0;body.linearDamping=0;body.angularDamping=0;body.linearVelocity=Vector2.zero;body.angularVelocity=0;}
        var striker=lifeBladeCollider.attachedRigidbody;striker.bodyType=RigidbodyType2D.Dynamic;striker.collisionDetectionMode=CollisionDetectionMode2D.Continuous;
        Physics2D.SyncTransforms();striker.position+=(Vector2)collider.bounds.center+Vector2.left*(collider.bounds.extents.x+lifeBladeCollider.bounds.extents.x+.04f)-(Vector2)lifeBladeCollider.bounds.center;
        var observer=striker.gameObject.AddComponent<OriginalValidationCollisionObserver>();observer.target=collider;
        int before=lifeImpacts.Count;float hp=lifeVictim.health,local=target.health;striker.linearVelocity=Vector2.right*speed;
        for(int step=0;step<120&&lifeImpacts.Count==before;step++){Physics2D.SyncTransforms();Physics2D.Simulate(1f/120);}
        if(observer.targetContacts==0)errors.Add("Blade fixture produced no actual source collider contact");
        if(lifeImpacts.Count==before)errors.Add("Blade contact did not pass original source damage gating");
        return J.O("method","original_blade_contact","resource",row["resource"].S,"target_part",target.sourceId,"target_path",LifePath(collider.transform),"target_tag",collider.tag,"target_layer",collider.gameObject.layer,"target_candidates",candidates,"launch_speed",speed,"contact_speed",Number(observer.relativeSpeed),"actual_contacts",observer.targetContacts,"impact_events",lifeImpacts.Count-before,"health_before",Number(hp),"health_after",Number(lifeVictim.health),"part_health_before",Number(local),"part_health_after",Number(target.health),"part_mode",target.mode.ToString());
    }
    void RestoreLifeDynamics() {
        foreach(var pair in lifeBodyTypes)if(pair.Key!=null&&!(lifeVictim.dead&&pair.Key==lifeVictim.rootBody))pair.Key.bodyType=pair.Value;
        lifeVictim.enabled=true;Physics2D.simulationMode=SimulationMode2D.FixedUpdate;
    }
    void CheckLifeFinite(List<string> errors) {
        if(lifeVictim!=null)InspectFinite(lifeVictim.transform,"lifecycle_victim",errors);
        foreach(var body in lifeBodyTypes.Keys)if(body!=null&&(!Finite(body.position)||!Finite(body.rotation)||!Finite(body.linearVelocity)||!Finite(body.angularVelocity)||!Finite(body.mass)||body.mass<=0))errors.Add("Lifecycle has invalid detached/source body "+body.name);
        if(lifeOwner!=null&&lifeOwner.leftItem!=null)InspectFinite(lifeOwner.leftItem.transform,"lifecycle_attacker_item",errors);
    }
    IEnumerator ImpaleLifecycle(J result,List<string> errors) {
        yield return BeginLifecycle(false);
        var chest=LifePart(p=>p.type==1&&p.config["m_GameObject"]["path"].S.EndsWith("/Bone_Chest",StringComparison.Ordinal));
        var arm=LifePart(p=>p.type==2&&p.config["m_GameObject"]["path"].S.EndsWith("/Bone_ArmL",StringComparison.Ordinal));
        var prep=new List<J>{FireLifeCannon(chest)};
        // Pelvis is a source layer-12 self-physics part, excluded from the
        // opposing item layer's collision matrix. Use the original live arm
        // collider: one real blade hit reduces 15 HP below the native <=10
        // impale gate without severing it or damaging the linked chest organ.
        prep.Add(HitLifeBlade(arm,LifeItem(r=>r["name"].S=="HandItem_2HandedSword_6"),80,errors));result["preparation_hits"]=new J(prep);
        result["pre_trigger_health"]=Number(lifeVictim.health);
        result["preparation_arm_mode"]=new J(arm.mode.ToString());
        if(arm.mode!=OriginalPartMode.None){errors.Add("Single physical preparation blade hit unexpectedly disabled the source arm");yield break;}
        if(lifeVictim.dead||lifeVictim.health<=0||lifeVictim.health>10){errors.Add("Original impale HP precondition (0 < health <= 10) was not reached by physical preparation hits");yield break;}
        var organ=lifeVictim.GetComponentsInChildren<OriginalInternalOrgan>(true).FirstOrDefault(o=>o.linkedPart==chest);
        if(organ==null||organ.linkedPart.severed){errors.Add("Original impale organ/linked non-severed part precondition not met");yield break;}
        var organData=LifeControllers["InternalOrgan"].A.First(r=>r["data"]["LinkedBodyPart"]["id"].I==chest.sourceId)["data"];
        var trigger=organ.GetComponentsInChildren<Collider2D>(true).FirstOrDefault(c=>c.isTrigger&&lifeColliderEnabled.ContainsKey(c)&&lifeColliderEnabled[c]);
        if(trigger==null){errors.Add("Original organ trigger collider is not present and enabled in source");yield break;}
        FocusLifeCollider(trigger);
        var body=lifeBladeCollider.attachedRigidbody;body.linearVelocity=Vector2.zero;body.angularVelocity=0;
        Physics2D.SyncTransforms();body.position+=(Vector2)trigger.bounds.center+Vector2.left*(trigger.bounds.extents.x+lifeBladeCollider.bounds.extents.x+.015f)-(Vector2)lifeBladeCollider.bounds.center;
        var witness=body.gameObject.AddComponent<OriginalLifecycleTriggerObserver>();witness.target=trigger;
        float started=Time.time,accumulated=lifeImpacts.Sum(i=>i.globalDamage);body.linearVelocity=Vector2.right*30;
        for(int step=0;step<120&&!lifeBlade.isImpaling;step++){Physics2D.SyncTransforms();Physics2D.Simulate(1f/120);}
        result["organ_trigger_contacts"]=new J(witness.contacts);result["accumulated_physical_impact_damage"]=Number(accumulated);
        bool bound=lifeBlade.impaleJoint!=null&&lifeBlade.impaleJoint.enabled&&lifeBlade.impaleJoint.connectedBody==organ.body&&lifeBlade.isImpaling;
        result["joint_bound_to_original_organ"]=new J(bound);result["death_from_impale"]=new J(lifeVictim.dead);
        if(witness.contacts==0||!bound||!lifeVictim.dead){errors.Add("Actual source organ trigger did not start temporary impalement/death");yield break;}
        if(!LifeMasks(organData,true))errors.Add("Original impale masks did not enable");
        RestoreLifeDynamics();
        float duration=lifeVictim.DamageNumber("impaleDurationBase")+accumulated*lifeVictim.DamageNumber("impaleDurationPerDamage");
        float released=-1,masksOff=-1,until=started+duration+1.25f;
        while(Time.time<until) {
            yield return new WaitForFixedUpdate();CheckLifeFinite(errors);
            if(released<0&&!lifeBlade.isImpaling&&!lifeBlade.impaleJoint.enabled)released=Time.time-started;
            if(released>=0&&LifeMasks(organData,false)){masksOff=Time.time-started;break;}
        }
        result["source_joint_duration"]=Number(duration);result["joint_released_at"]=Number(released);result["masks_cleared_at"]=Number(masksOff);
        if(released<duration+.025f-.03f||released>duration+.025f+.25f)errors.Add("Temporary joint release did not follow source .025 + computed duration waits");
        if(masksOff<0||masksOff-released<.22f||masksOff-released>.5f)errors.Add("Impale mask cleanup did not follow source .25 second tail wait");
    }
    bool LifeMasks(J organData,bool active) {
        foreach(var reference in organData["ImpaleMasks"].A){var t=lifeVictim.ResolveTransform(reference);if(t==null||t.gameObject.activeSelf!=active)return false;}return true;
    }
    IEnumerator ArmLifecycle(J result,List<string> errors) {
        yield return BeginLifecycle(true);
        var arm=LifePart(p=>p.type==2&&p.config["m_GameObject"]["path"].S.EndsWith("/Bone_ArmL",StringComparison.Ordinal));
        var held=lifeVictim.leftItem;var settings=EquipmentSettings(lifeVictim);
        var left=lifeVictim.Resolve<FixedJoint2D>(settings["handFixedJoint2DL"]);var right=lifeVictim.Resolve<FixedJoint2D>(settings["handFixedJoint2DR"]);
        if(held==null||held!=lifeVictim.rightItem||!held.twoHanded||left==null||right==null||!left.enabled||!right.enabled){errors.Add("Original two-hand grip precondition not met");yield break;}
        var attacks=new List<J>();var blade=LifeItem(r=>r["category"].I==1&&r["two_handed"].B);
        for(int attempt=0;attempt<3&&arm.mode==OriginalPartMode.None&&!lifeVictim.dead;attempt++) {
            if(attempt>0)yield return new WaitForSeconds(.2f);
            attacks.Add(HitLifeBlade(arm,blade,80,errors));
        }
        result["physical_hits"]=new J(attacks);result["part_mode"]=new J(arm.mode.ToString());
        var partJoint=lifeVictim.Resolve<Joint2D>(arm.config["joint2D"]);
        bool detached=arm.severed&&arm.mode==OriginalPartMode.Severed&&!arm.transform.IsChildOf(lifeVictim.transform)&&arm.CompareTag("SeveredBodyPart")&&partJoint!=null&&!partJoint.enabled;
        bool remainingGrip=!held.dropped&&!left.enabled&&right.enabled&&right.connectedBody==held.itemBody;
        result["source_limb_detached"]=new J(detached);result["remaining_two_hand_grip"]=new J(remainingGrip);result["native_cross_named_rightArmDisabled"]=new J(lifeVictim.rightArmDisabled);
        if(!detached)errors.Add("Physical blade hits did not produce source severing/detach/tag/joint state");
        if(!remainingGrip||!lifeVictim.rightArmDisabled||lifeVictim.dead)errors.Add("Source one-arm loss did not retain the opposite two-hand grip on a living fighter");
        if(lifeBlade!=null)foreach(var body in lifeBlade.GetComponentsInChildren<Rigidbody2D>(true))body.simulated=false;
        RestoreLifeDynamics();float start=Time.time,before=lifeVictim.health,last=before;int impactsBefore=lifeImpacts.Count;var ticks=new List<J>();
        while(Time.time-start<1.45f) {
            yield return new WaitForFixedUpdate();CheckLifeFinite(errors);
            if(lifeVictim.health!=last){ticks.Add(J.O("after",Number(Time.time-start),"health",Number(lifeVictim.health),"delta",Number(last-lifeVictim.health)));last=lifeVictim.health;}
        }
        result["bleeding_health_before"]=Number(before);result["bleeding_health_after"]=Number(lifeVictim.health);result["bleeding_ticks"]=new J(ticks);
        if(ticks.Count<2||!(lifeVictim.health<before)||lifeImpacts.Count!=impactsBefore)errors.Add("Normal FixedUpdate did not show isolated bleeding ticks after the physical sever");
        if(ticks.Any(t=>Mathf.Abs((float)t["delta"].N-.5f)>.001f))errors.Add("Observed bleeding delta differs from original .5 health ticks");
        if(held.dropped||!right.enabled||right.connectedBody!=held.itemBody)errors.Add("Remaining two-hand grip did not survive physical bleeding observation");
    }
    IEnumerator DeathLifecycle(J result,List<string> errors) {
        yield return BeginLifecycle(true);var vitality=LifeControllers["CharacterVitality"][0]["data"];
        var chest=LifePart(p=>p.type==1&&p.config["m_GameObject"]["path"].S.EndsWith("/Bone_Chest",StringComparison.Ordinal));
        var stages=new List<J>();var sequence=new List<string>();float deathAt=-1;int diedEvents=0;
        lifeVictim.OriginalDeathStageCompleted+=stage=> {
            if(deathAt<0)deathAt=Time.time;sequence.Add("stage"+stage);CheckLifeFinite(errors);
            var snapshot=J.O("stage",stage,"after",Number(Time.time-deathAt),"root_static",lifeVictim.rootBody.bodyType==RigidbodyType2D.Static,"health",Number(lifeVictim.health));
            if(stage==0) {
                var overlap=lifeVictim.Resolve<Collider2D>(vitality["overlapCollider"]);var kick=lifeVictim.Resolve<Collider2D>(vitality["extraColliderKick"]);
                if(!lifeVictim.dead||lifeVictim.rootBody.bodyType!=RigidbodyType2D.Static||lifeVictim.gameObject.layer!=22||(overlap!=null&&overlap.enabled)||(kick!=null&&kick.enabled)||!LifeObjects(vitality["ExtraColliders"],false)||!LifeObject(vitality["vaporContainer"],false))errors.Add("Death stage 0 state mismatch");
            }
            if(stage==1)foreach(var part in lifeVictim.BodyParts)if(part.body!=null&&Mathf.Abs(part.body.mass-(float)part.config["deadMass"].N)>.001f)errors.Add("Death stage 1 original dead mass mismatch: "+part.name);
            if(stage==2)foreach(var reference in vitality["bodyFixedJoints2D"].A){var joint=lifeVictim.Resolve<FixedJoint2D>(reference);if(joint!=null&&(joint.frequency!=10||joint.dampingRatio!=0))errors.Add("Death stage 2 fixed joint parameters mismatch");}
            if(stage==3){var held=lifeVictim.leftItem;var weaponSelf=lifeVictim.ResolveTransform(vitality["weaponSelfColliderWhenDead"]);var equipment=EquipmentSettings(lifeVictim);var leftGrip=lifeVictim.Resolve<FixedJoint2D>(equipment["handFixedJoint2DL"]);var rightGrip=lifeVictim.Resolve<FixedJoint2D>(equipment["handFixedJoint2DR"]);if(held==null||!held.dropped||(leftGrip!=null&&leftGrip.enabled)||(rightGrip!=null&&rightGrip.enabled)||(weaponSelf!=null&&!weaponSelf.gameObject.activeSelf))errors.Add("Death stage 3 original weapon drop/both grips/self collider mismatch");}
            if(stage==4)foreach(var reference in vitality["fixedJoints2DToKill"].A){var joint=lifeVictim.Resolve<FixedJoint2D>(reference);if(joint!=null&&joint.enabled)errors.Add("Death stage 4 did not disable original joint");}
            if(stage==5&&!LifeObjects(vitality["gameObjectsToDeactivateUponDeath"],false))errors.Add("Death stage 5 source objects still active");
            if(stage==6&&!LifeObjects(vitality["gameObjectsToActivateUponDeath"],true))errors.Add("Death stage 6 source objects not active");
            stages.Add(snapshot);
        };
        lifeVictim.Died+=rig=>{sequence.Add("Died");diedEvents++;};
        var casts=new List<J>{FireLifeCannon(chest)};yield return new WaitForSeconds(.05f);casts.Add(FireLifeCannon(chest));result["physical_casts"]=new J(casts);
        if(!lifeVictim.dead){errors.Add("Actual original cannon casts did not reach death");yield break;}
        RestoreLifeDynamics();float until=Time.time+.6f;
        while(Time.time<until){yield return new WaitForFixedUpdate();CheckLifeFinite(errors);}
        result["death_stages"]=new J(stages);result["event_order"]=Strings(sequence);result["died_events"]=new J(diedEvents);
        if(!sequence.SequenceEqual(new[]{"stage0","Died","stage1","stage2","stage3","stage4","stage5","stage6"})||diedEvents!=1)errors.Add("Source death stage/event order did not occur exactly once");
        for(int i=1;i<stages.Count;i++)if((float)stages[i]["after"].N-(float)stages[i-1]["after"].N<.02f)errors.Add("Death coroutine stage advanced before the source .025 second wait");
    }
    bool LifeObject(J reference,bool active){var t=lifeVictim.ResolveTransform(reference);return t==null||t.gameObject.activeSelf==active;}
    bool LifeObjects(J references,bool active)=>references.A.All(r=>LifeObject(r,active));
    IEnumerator CleanupLifecycle(J result,List<string> errors) {
        var tracked=new HashSet<GameObject>();
        foreach(var rig in new[]{lifeOwner,lifeVictim}) {
            if(rig==null)continue;tracked.Add(rig.gameObject);foreach(var part in rig.BodyParts)if(part!=null)tracked.Add(part.gameObject);
            foreach(var node in rig.GetComponentsInChildren<Transform>(true))tracked.Add(node.gameObject);
            foreach(var item in FindObjectsByType<OriginalItem>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(i=>i.owner==rig)) {
                tracked.Add(item.gameObject);var row=FindOriginalItemRow(item);if(row!=null&&row["cannon"].Has){var impact=item.RefTransform(row["cannon"]["impactVFX"]);if(impact!=null)tracked.Add(impact.gameObject);}
            }
        }
        foreach(var body in lifeBodyTypes.Keys)if(body!=null)tracked.Add(body.gameObject);
        // Exercise the host's real cleanup method. Reflection is used only
        // because the production method is private; no state/damage is set.
        var dispose=typeof(OriginalPreview).GetMethod("DisposeRound",BindingFlags.Instance|BindingFlags.NonPublic);
        if(dispose==null){errors.Add("OriginalPreview.DisposeRound cleanup method is absent");yield break;}
        preview.player=lifeOwner;preview.enemy=lifeVictim;dispose.Invoke(preview,null);
        yield return null;yield return null;
        var survivors=tracked.Where(go=>go!=null).Select(go=>go.name).ToArray();
        result["cleanup"]=J.O("method","OriginalPreview.DisposeRound","tracked_objects",tracked.Count,"remaining_objects",Strings(survivors),"passed",survivors.Length==0&&preview.player==null&&preview.enemy==null);
        if(survivors.Length!=0||preview.player!=null||preview.enemy!=null)errors.Add("Actual host cleanup left tracked rig/item/part/detached VFX objects alive: "+string.Join(",",survivors));
        lifeOwner=lifeVictim=null;lifeBlade=null;lifeBladeCollider=null;Physics2D.simulationMode=SimulationMode2D.Script;
    }
    J FindOriginalItemRow(OriginalItem item)=>catalog["items"].A.FirstOrDefault(r=>r["resource"].S==item.config["resource"].S||r["item_id"].I==item.itemId);
}

// Trigger witness records genuine Physics2D callbacks and applies no damage.
public sealed class OriginalLifecycleTriggerObserver : MonoBehaviour {
    public Collider2D target;
    public int contacts;
    void OnTriggerEnter2D(Collider2D collider){if(collider==target)contacts++;}
}
