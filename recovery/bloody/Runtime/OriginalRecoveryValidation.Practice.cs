using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Jianghu;
using UnityEngine;

public sealed partial class OriginalRecoveryValidation {
    readonly List<J> practiceCases=new List<J>();
    readonly List<OriginalPracticeImpact> practiceImpacts=new List<OriginalPracticeImpact>();
    OriginalPracticeDummy practiceFixture;
    OriginalRig practiceAttacker;
    OriginalRig practiceTypeEnemy;
    OriginalItem practiceProjectile;
    OriginalPracticeValidationWatchdog practiceWatchdog;
    J practiceExpected,practiceCurrent;
    bool practiceTimedOut;
    float practicePreviousTimeScale;
    double practiceStarted;

    // Host calls before its normal TryStart/Spawn and yields out when true.
    // No activation, save mutation or physics changes without the opt-in flag.
    public static bool TryStartPractice(OriginalPreview host) {
        var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-validateOriginalPractice");if(at<0)return false;
        var probe=host.gameObject.AddComponent<OriginalRecoveryValidation>();probe.preview=host;
        probe.output=at+1<args.Length&&!args[at+1].StartsWith("-",StringComparison.Ordinal)?args[at+1]:Path.Combine(Application.persistentDataPath,"OriginalPracticeValidation");
        double seconds=60;int watchAt=Array.IndexOf(args,"-originalPracticeWatchdogSeconds");
        if(watchAt>=0&&watchAt+1<args.Length&&double.TryParse(args[watchAt+1],out var requested)&&!double.IsNaN(requested)&&!double.IsInfinity(requested))seconds=Math.Max(10,Math.Min(120,requested));
        probe.previousSimulation=Physics2D.simulationMode;probe.practicePreviousTimeScale=Time.timeScale;probe.practiceStarted=Time.realtimeSinceStartupAsDouble;
        Directory.CreateDirectory(probe.output);host.enabled=false;if(host.hud!=null)host.hud.gameObject.SetActive(false);
        Physics2D.simulationMode=SimulationMode2D.Script;Time.timeScale=1;QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;
        Application.logMessageReceived+=probe.ObserveLog;
        probe.practiceWatchdog=host.gameObject.AddComponent<OriginalPracticeValidationWatchdog>();
        probe.practiceWatchdog.Initialize(seconds,()=>{if(probe==null||probe.finished)return;probe.practiceTimedOut=true;probe.Issue("practice","realtime_watchdog","Exceeded "+seconds+" seconds");probe.FinishPractice();});
        probe.StartCoroutine(probe.RunPractice());return true;
    }
    IEnumerator RunPractice() {
        Exception failure=null;
        try {
            catalog=J.Parse(Resources.Load<TextAsset>("OriginalItemCatalog").text);
            practiceExpected=J.Parse(Resources.Load<TextAsset>("OriginalPracticeValidationSource").text);
            if(preview.dummyPrefab==null||preview.playerPrefab==null)throw new InvalidOperationException("Practice probe requires original Dummy and player prefabs");
        }catch(Exception ex){failure=ex;}
        if(failure!=null){Issue("practice","setup",failure.ToString());FinishPractice();yield break;}
        WritePractice(false);
        foreach(string name in new[]{"original_dummy_assembly","real_dagger_contact_and_source_audio","real_cannon_circlecast"}) {
            var errors=new List<string>();practiceCurrent=J.O("name",name,"complete",false);
            yield return GuardLifecycle(PracticeCase(name,practiceCurrent,errors),errors);
            PracticeCleanup();yield return null;
            var unique=errors.Distinct().ToList();practiceCurrent["complete"]=new J(true);practiceCurrent["passed"]=new J(unique.Count==0);practiceCurrent["errors"]=Strings(unique);
            foreach(string error in unique)Issue(name,"practice",error);
            practiceCases.Add(practiceCurrent);practiceCurrent=null;WritePractice(false);
        }
        FinishPractice();
    }
    IEnumerator PracticeCase(string name,J result,List<string> errors) {
        practiceImpacts.Clear();practiceFixture=OriginalPracticeDummy.Create(preview.dummyPrefab,new Vector3(1.5f,20,0));
        practiceFixture.name="Original practice validation Dummy";
        practiceFixture.ImpactOccurred+=impact=>practiceImpacts.Add(impact);
        result["before"]=PracticeInspectDummy(practiceFixture,errors);
        result["source_audio_bindings"]=PracticeInspectAudio(practiceFixture,errors);
        if(name=="original_dummy_assembly") {
            Physics2D.SyncTransforms();Physics2D.Simulate(1f/240);Physics2D.Simulate(1f/240);
            result["after"]=PracticeInspectDummy(practiceFixture,errors);InspectFinite(practiceFixture.transform,"practice_assembly",errors);
            yield return PracticeEquipmentTypes(result,errors);
            yield return PracticeEquipmentCOMCaches(result,errors);yield break;
        }
        practiceAttacker=CreateFixture(true,new Vector3(-10,20,0));
        J itemRow=name=="real_cannon_circlecast"?catalog["items"].A.First(r=>r["cannon"].Has):catalog["items"].A.First(r=>r["name"].S=="HandItem_Dagger_1");
        practiceAttacker.EquipOriginal(itemRow["resource"].S,null);practiceProjectile=practiceAttacker.leftItem;
        if(practiceProjectile==null)throw new InvalidOperationException("Practice attack fixture did not equip the source weapon");
        foreach(var body in practiceAttacker.GetComponentsInChildren<Rigidbody2D>(true))body.simulated=false;
        foreach(var collider in practiceAttacker.GetComponentsInChildren<Collider2D>(true))if(!collider.transform.IsChildOf(practiceProjectile.transform))collider.enabled=false;
        foreach(var joint in practiceAttacker.GetComponentsInChildren<Joint2D>(true))joint.enabled=false;
        practiceProjectile.transform.SetParent(null,true);
        var target=practiceFixture.BodyParts.First(part=>part.SourcePath=="Dummy_Head");
        var targetCollider=target.Body.GetComponents<Collider2D>().First(c=>c.enabled&&!c.isTrigger&&c.gameObject.activeInHierarchy&&c.gameObject.layer==11);
        if(Physics2D.GetIgnoreLayerCollision(15,targetCollider.gameObject.layer))throw new InvalidOperationException("Original player-item/Dummy collision matrix does not allow contact");
        Physics2D.SyncTransforms();
        // The top of the original head is clear of the attached shield/arms.
        // This selects a launch point; it changes no target shape or transform.
        Vector2 targetPoint=(Vector2)targetCollider.bounds.center+Vector2.up*targetCollider.bounds.extents.y*.55f;
        result["target"]=J.O("path",target.SourcePath,"collider",targetCollider.GetType().Name,"layer",targetCollider.gameObject.layer,"point_x",Number(targetPoint.x),"point_y",Number(targetPoint.y));
        result["weapon"]=J.O("resource",itemRow["resource"].S,"item_id",practiceProjectile.itemId,"source",itemRow["source"]);
        if(name=="real_dagger_contact_and_source_audio") {
            // Both source Dagger1(item197) and Dummy's Dagger5(item202)
            // use MetalSpike on their striking polygon, not MetalBlade.
            var striker=practiceProjectile.GetComponentsInChildren<Collider2D>(true).FirstOrDefault(c=>c.enabled&&c.gameObject.activeInHierarchy&&!c.isTrigger&&c.CompareTag("HandItem")&&c.attachedRigidbody!=null&&OriginalCombatRules.Surface(c.sharedMaterial)==OriginalSurface.MetalSpike);
            if(striker==null)throw new InvalidOperationException("Source dagger has no active MetalSpike collider");
            result["striker"]=J.O("kind",striker.GetType().Name,"material",striker.sharedMaterial.name,"surface",OriginalCombatRules.Surface(striker.sharedMaterial).ToString(),"source_material_reference",itemRow["fields"]["HandItem_MetalSpike"],"source_material_unchanged",true,"layer",striker.gameObject.layer);
            foreach(var collider in practiceProjectile.GetComponentsInChildren<Collider2D>(true))collider.enabled=collider==striker;
            foreach(var body in practiceProjectile.GetComponentsInChildren<Rigidbody2D>(true)) {
                body.simulated=body==striker.attachedRigidbody;body.gravityScale=0;body.linearDamping=body.angularDamping=0;body.linearVelocity=Vector2.zero;body.angularVelocity=0;
            }
            var launchBody=striker.attachedRigidbody;launchBody.bodyType=RigidbodyType2D.Dynamic;launchBody.collisionDetectionMode=CollisionDetectionMode2D.Continuous;
            Physics2D.SyncTransforms();
            Vector2 start=targetPoint+Vector2.left*(targetCollider.bounds.extents.x+striker.bounds.extents.x+.08f);
            launchBody.position+=start-(Vector2)striker.bounds.center;Physics2D.SyncTransforms();
            var witness=launchBody.gameObject.AddComponent<OriginalValidationCollisionObserver>();witness.target=targetCollider;
            const float launchSpeed=40;launchBody.linearVelocity=Vector2.right*launchSpeed;
            int steps=0;
            for(;steps<180&&!practiceImpacts.Any(i=>i.Item==practiceProjectile);steps++) {
                if(practiceWatchdog.Expired)throw new TimeoutException("Practice realtime watchdog expired during dagger simulation");
                Physics2D.SyncTransforms();Physics2D.Simulate(1f/240);
            }
            var accepted=practiceImpacts.Where(i=>i.Item==practiceProjectile).ToArray();
            if(witness.targetContacts==0)errors.Add("Dagger produced no actual OnCollisionEnter2D against the original head collider");
            if(accepted.Length==0)errors.Add("Real dagger contact produced no original Dummy ProcessImpact event");
            if(accepted.Length>0) {
                var impact=accepted[0];if(impact.Cannon)errors.Add("Dagger impact was incorrectly marked as cannon");
                if(impact.SoundIndex!=8)errors.Add("MetalSpike did not reach original dummy sound index8");
                if(impact.Velocity.magnitude<10)errors.Add("Accepted blade contact did not reach source impactSpeedThreshold10");
                result["accepted_impact"]=PracticeImpact(impact);
            }
            result["actual_target_contacts"]=new J(witness.targetContacts);result["contact_point_speed"]=Number(witness.relativeSpeed);result["launch_speed"]=new J(launchSpeed);result["simulation_steps"]=new J(steps);
            result["fixture"]=new J("Original dynamic Dummy translated rigidly to y20 with all five target body types, colliders, layers, masses, damping and joints retained. Detached original dagger MetalSpike collider launched horizontally at40m/s with its unchanged source material. Only attacker motion/joints/unrelated colliders isolated. This is controlled fixture placement, not normal hand input.");
        } else {
            foreach(var body in practiceProjectile.GetComponentsInChildren<Rigidbody2D>(true))body.simulated=false;
            foreach(var collider in practiceProjectile.GetComponentsInChildren<Collider2D>(true))collider.enabled=false;
            var cannon=practiceProjectile.GetComponent<OriginalHandCannon>();if(cannon==null)throw new InvalidOperationException("Original cannon component missing");
            var muzzle=practiceProjectile.RefTransform(itemRow["cannon"]["muzzle"]);var aim=practiceProjectile.RefTransform(itemRow["cannon"]["projectileTargetPoint"]);
            if(muzzle==null||aim==null)throw new InvalidOperationException("Original cannon muzzle/target missing");
            Vector2 originalDirection=aim.position-muzzle.position;if(originalDirection.sqrMagnitude<=1e-8f)throw new InvalidOperationException("Original cannon source aim has no direction");
            practiceProjectile.transform.Rotate(0,0,-Mathf.Atan2(originalDirection.y,originalDirection.x)*Mathf.Rad2Deg,Space.World);
            practiceProjectile.transform.position+=(Vector3)(targetPoint+Vector2.left*3-(Vector2)muzzle.position);
            Physics2D.SyncTransforms();
            var velocities=practiceFixture.BodyParts.ToDictionary(part=>part,part=>part.Body.linearVelocity);
            var spins=practiceFixture.BodyParts.ToDictionary(part=>part,part=>part.Body.angularVelocity);
            cannon.Fire();var shot=cannon.LastShot;var accepted=practiceImpacts.Where(i=>i.Item==practiceProjectile&&i.Cannon).ToArray();
            if(shot.hitCount<=0||shot.dummyBodyPart==null)errors.Add("Original cannon CircleCast did not select a real Dummy body part");
            if(shot.bodyPart!=null)errors.Add("Practice cannon selected a fighter BodyPart");
            if(accepted.Length!=1)errors.Add("Original cannon did not produce exactly one Dummy ProcessImpact event");
            if(shot.dummyBodyPart!=null) {
                var selected=shot.dummyBodyPart;
                if((shot.layerMask&(1<<selected.gameObject.layer))==0)errors.Add("CircleCast selected a dummy outside the original mask");
                Vector2 expectedVelocity=velocities[selected]+shot.direction*12;
                if(Vector2.Distance(selected.Body.linearVelocity,expectedVelocity)>.001f)errors.Add("Original cannon did not apply its source additive velocity12");
                if(Mathf.Abs(selected.Body.angularVelocity-spins[selected])>20.001f)errors.Add("Original cannon spin exceeds source random[-1,1]*20");
                result["selected_body_velocity_before"]=PracticeVector(velocities[selected]);result["selected_body_velocity_after"]=PracticeVector(selected.Body.linearVelocity);
            }
            if(accepted.Length>0) {
                var impact=accepted[0];if(impact.Part!=shot.dummyBodyPart)errors.Add("CircleCast selection and Dummy impact part disagree");
                float speed=(float)itemRow["cannon"]["muzzleSpeed"].N;if(Mathf.Abs(impact.Velocity.magnitude-speed)>.001f)errors.Add("Cannon impact velocity differs from original muzzleSpeed");
                result["accepted_impact"]=PracticeImpact(impact);
            }
            result["cast"]=J.O("hit_count",shot.hitCount,"mask",shot.layerMask,"blocker_layers",shot.blockerLayers,"radius",Number(shot.radius),"range",Number(shot.range),"origin",PracticeVector(shot.origin),"direction",PracticeVector(shot.direction),"selected_dummy",shot.dummyBodyPart==null?"":shot.dummyBodyPart.SourcePath);
            result["fixture"]=new J("Original dynamic Dummy translated rigidly to y20, with all target physical properties retained. Source cannon root alone rotated/translated to aim from3m left of the original head. Production Fire performs the unchanged original CircleCast and calls the selected Dummy component. No direct ProcessImpact call by this probe.");
        }
        result["impact_count"]=new J(practiceImpacts.Count(i=>i.Item==practiceProjectile));
        result["after"]=PracticeInspectDummy(practiceFixture,errors);InspectFinite(practiceFixture.transform,"practice_after_impact",errors);
        var soundSource=PracticeAudioSource(practiceFixture);result["production_audio_source"]=J.O("enabled",soundSource!=null&&soundSource.enabled,"active",soundSource!=null&&soundSource.gameObject.activeInHierarchy,"pitch",Number(soundSource==null?0:soundSource.pitch),"is_playing_observation",soundSource!=null&&soundSource.isPlaying);
        result["production_sound_returned_before_impact_event"]=new J(practiceImpacts.Any(i=>i.Item==practiceProjectile&&i.SoundIndex>=0));
        result["direct_process_impact_called_by_probe"]=new J(false);result["target_physics_modified_by_probe"]=new J(false);
        yield return null;
    }
    static J PracticeVector(Vector2 value)=>J.O("x",Number(value.x),"y",Number(value.y));
    IEnumerator PracticeEquipmentTypes(J result,List<string> errors) {
        practiceAttacker=CreateFixture(true,new Vector3(-20,40,0));practiceTypeEnemy=CreateFixture(false,new Vector3(20,40,0));
        practiceAttacker.opponent=practiceTypeEnemy;practiceTypeEnemy.opponent=practiceAttacker;
        var sourceRows=new[]{
            catalog["items"].A.First(r=>r["name"].S=="None"),
            catalog["items"].A.First(r=>r["fields"]["Type"].I==7&&r["category"].I==8&&!r["two_handed"].B),
            catalog["items"].A.First(r=>r["name"].S=="HandItem_Dagger_1"),
            catalog["items"].A.First(r=>r["fields"]["Type"].I==6&&r["category"].I==8&&r["two_handed"].B)};
        var cases=new List<J>();
        foreach(var rig in new[]{practiceAttacker,practiceTypeEnemy})foreach(var row in sourceRows)foreach(int hand in row["two_handed"].B?new[]{2}:new[]{0,1}) {
            if(practiceWatchdog.Expired)throw new TimeoutException("Practice equipment-type watchdog expired");
            var localErrors=new List<string>();rig.EquipOriginal(hand==1?null:row["resource"].S,hand==1?row["resource"].S:null);
            var item=hand==1?rig.rightItem:rig.leftItem;bool shieldMount=row["fields"]["Type"].I==7;
            if(item==null)throw new InvalidOperationException("Equipment type fixture has no source item");
            string mount=hand==2?"handItemL_TransformDoubleHandedWeapon":hand==0?(shieldMount?"handItemL_TransformShield":"handItemL_TransformWeapon"):(shieldMount?"handItemR_TransformShield":"handItemR_TransformWeapon");
            var settings=EquipmentSettings(rig);if(item.transform.parent!=rig.ResolveTransform(settings[mount]))localErrors.Add("Item mount differs from source Type6/7/2H branch");
            if(item.OriginalUsesShieldMount!=shieldMount||item.isShield!=(row["category"].I==8))localErrors.Add("Equipment mount kind and physical isShield category were conflated");
            var grips=new List<J>();
            foreach(int side in hand==2?new[]{0,1}:new[]{hand}) {
                var com=side==0?rig.leftHandCOM:rig.rightHandCOM;float mass=(float)settings[shieldMount?"handCOMShieldMass":"handCOMWeaponMass"].N;
                if(Mathf.Abs(com.mass-mass)>.0001f)localErrors.Add("Hand COM mass differs from original Type branch");
                var arms=new List<J>();
                foreach(var reference in settings[side==0?"armLeftRidibodies":"armRightRidibodies"].A) {
                    var body=rig.Resolve<Rigidbody2D>(reference);float damping=shieldMount?3:5;
                    if(body==null||Mathf.Abs(body.linearDamping-damping)>.0001f||Mathf.Abs(body.angularDamping-damping)>.0001f)localErrors.Add("Source Type branch arm damping differs");
                    arms.Add(J.O("path",reference["path"].S,"linear_damping",Number(body==null?0:body.linearDamping),"angular_damping",Number(body==null?0:body.angularDamping)));
                }
                var joint=rig.Resolve<FixedJoint2D>(settings[side==0?"handFixedJoint2DL":"handFixedJoint2DR"]);
                if(joint==null||joint.connectedBody!=item.itemBody||joint.connectedBody==joint.GetComponent<Rigidbody2D>())localErrors.Add("Final grip is not connected to instantiated item body");
                grips.Add(J.O("side",side,"com_mass",Number(com.mass),"expected_com_mass",Number(mass),"arm_damping",new J(arms),"joint_connected_to_item",joint!=null&&joint.connectedBody==item.itemBody));
            }
            bool Flag(string name)=>(bool)typeof(OriginalRig).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(rig);
            J aiFlags=new J();
            if(!rig.human) {
                bool weapon=!item.twoHanded&&row["fields"]["Type"].I==6,shield=!item.twoHanded&&shieldMount&&row["fields"]["Name"].S!="Nothing";
                bool wl=Flag("aiWeaponLeft"),wr=Flag("aiWeaponRight"),sl=Flag("aiShieldLeft"),sr=Flag("aiShieldRight"),two=Flag("aiTwoHanded");
                if(wl!=(hand==0&&weapon)||wr!=(hand==1&&weapon)||sl!=(hand==0&&shield)||sr!=(hand==1&&shield)||two!=(hand==2))localErrors.Add("AI flags differ from populated source mount and Nothing name predicate");
                aiFlags=J.O("weapon_left",wl,"shield_left",sl,"weapon_right",wr,"shield_right",sr,"two_handed",two);
            }
            Physics2D.SyncTransforms();Physics2D.Simulate(1f/240);Physics2D.Simulate(1f/240);InspectFinite(rig.transform,"equipment_type_case",localErrors);
            cases.Add(J.O("human",rig.human,"resource",row["resource"].S,"item_id",item.itemId,"hand",hand,"equipment_type",item.OriginalEquipmentType,"category",item.category,"uses_shield_mount",item.OriginalUsesShieldMount,"physical_is_shield",item.isShield,"expected_mount",mount,"grips",new J(grips),"ai_flags",aiFlags,"passed",localErrors.Count==0,"errors",Strings(localErrors)));
            foreach(string error in localErrors)errors.Add("equipment_type "+row["resource"].S+" / "+rig.human+" / "+hand+": "+error);
            ClearEquipment(rig,settings);yield return null;
        }
        result["equipment_type_cases"]=new J(cases);result["equipment_type_cases_expected"]=new J(14);
        if(cases.Count!=14)errors.Add("Equipment type fixtures did not run all14 source cases");
    }
    IEnumerator PracticeEquipmentCOMCaches(J result,List<string> errors) {
        if(practiceAttacker!=null){practiceAttacker.gameObject.SetActive(false);Destroy(practiceAttacker.gameObject);practiceAttacker=null;}
        yield return null;
        var steps=new List<J>();
        var shield= catalog["items"].A.First(r=>r["item_id"].I==235&&!r["resource_alias"].B);
        var dagger= catalog["items"].A.First(r=>r["name"].S=="HandItem_Dagger_1");
        var two= catalog["items"].A.First(r=>r["fields"]["Type"].I==6&&r["category"].I==8&&r["two_handed"].B);
        for(int flow=0;flow<3;flow++) {
            if(practiceWatchdog.Expired)throw new TimeoutException("Practice COM cache watchdog expired");
            practiceAttacker=CreateFixture(true,new Vector3(-20,40,0));practiceAttacker.opponent=practiceTypeEnemy;practiceTypeEnemy.opponent=practiceAttacker;
            if(practiceAttacker.OriginalEquipmentWeaponCOM!=null||practiceAttacker.OriginalEquipmentShieldCOM!=null)errors.Add("Fresh source player COM caches were not serialized null");
            if(flow==0) {
                practiceAttacker.EquipOriginal(shield["resource"].S,dagger["resource"].S);
                PracticeCOMCheck(steps,"first_left235_shield_right197_dagger",practiceAttacker,practiceTypeEnemy,practiceAttacker.rightItem.counterweightBody.transform,practiceAttacker.OriginalEquipmentChestCOM,errors);
                // Source Destroy is deferred. Neither Equip nor the probe
                // clears applier caches before ApplyLeft then ApplyRight.
                practiceAttacker.EquipOriginal(shield["resource"].S,dagger["resource"].S);
                PracticeCOMCheck(steps,"same_frame_reapply_retains_valid_previous_cache",practiceAttacker,practiceTypeEnemy,practiceAttacker.rightItem.counterweightBody.transform,practiceAttacker.leftItem.counterweightBody.transform,errors);
                var oldShield=practiceAttacker.OriginalEquipmentShieldCOM;
                practiceAttacker.EquipOriginal(two["resource"].S,null);
                PracticeCOMCheck(steps,"swap_to_2H_overwrites_only_weapon",practiceAttacker,practiceTypeEnemy,practiceAttacker.leftItem.counterweightBody.transform,oldShield,errors);
                yield return null;
                if(oldShield!=null)errors.Add("Old source shield COM did not become Unity-null after deferred Destroy");
                if(!ReferenceEquals(practiceAttacker.OriginalEquipmentShieldCOM,oldShield))errors.Add("2H swap artificially cleared the source shield cache CLR reference");
                PracticeCOMCheck(steps,"after_frame_2H_retains_destroyed_shield_reference",practiceAttacker,practiceTypeEnemy,practiceAttacker.leftItem.counterweightBody.transform,oldShield,errors);
                practiceAttacker.EquipOriginal(dagger["resource"].S,null);
                PracticeCOMCheck(steps,"swap_back_single_uses_Unity_null_fallback",practiceAttacker,practiceTypeEnemy,practiceAttacker.OriginalEquipmentChestCOM,practiceAttacker.rightItem.counterweightBody.transform,errors);
            } else if(flow==1) {
                practiceAttacker.EquipOriginal(dagger["resource"].S,null);
                PracticeCOMCheck(steps,"first_left197_dagger_right0_None",practiceAttacker,practiceTypeEnemy,practiceAttacker.OriginalEquipmentChestCOM,practiceAttacker.rightItem.counterweightBody.transform,errors);
            } else {
                practiceAttacker.EquipOriginal(two["resource"].S,null);
                PracticeCOMCheck(steps,"first_2H_weapon_cache_only_shield_stays_null",practiceAttacker,practiceTypeEnemy,practiceAttacker.leftItem.counterweightBody.transform,null,errors);
            }
            practiceAttacker.gameObject.SetActive(false);Destroy(practiceAttacker.gameObject);practiceAttacker=null;yield return null;
        }
        result["equipment_com_cache_steps"]=new J(steps);result["equipment_com_cache_steps_expected"]=new J(7);
        if(steps.Count!=7)errors.Add("COM cache fixture did not complete all7 reference transitions");
    }
    static J PracticeCOMReference(Transform target) {
        if(ReferenceEquals(target,null))return J.O("state","CLR_null");
        if(target==null)return J.O("state","Unity_destroyed_reference_retained");
        return J.O("state","live","name",target.name,"x",Number(target.position.x),"y",Number(target.position.y));
    }
    static void PracticeCOMCheck(List<J> steps,string name,OriginalRig actor,OriginalRig observer,Transform weapon,Transform shield,List<string> errors) {
        bool weaponSame=ReferenceEquals(actor.OriginalEquipmentWeaponCOM,weapon),shieldSame=ReferenceEquals(actor.OriginalEquipmentShieldCOM,shield);
        observer.SetOriginalAIWeaponProperties();
        var aiShield=(Transform)typeof(OriginalRig).GetField("aiPlayerShieldCOM",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(observer);
        bool aiWeaponSame=ReferenceEquals(observer.OriginalAIPlayerWeaponCOM,weapon),aiShieldSame=ReferenceEquals(aiShield,shield);
        if(!weaponSame||!shieldSame||!aiWeaponSame||!aiShieldSame)errors.Add("COM cache/reference copy mismatch: "+name);
        steps.Add(J.O("step",name,"weapon",PracticeCOMReference(actor.OriginalEquipmentWeaponCOM),"shield",PracticeCOMReference(actor.OriginalEquipmentShieldCOM),"weapon_reference_matches",weaponSame,"shield_reference_matches",shieldSame,"AI_weapon_reference_matches",aiWeaponSame,"AI_shield_reference_matches",aiShieldSame));
    }
    static J PracticeShape(Collider2D collider) {
        var shape=J.O("kind",collider.GetType().Name,"offset",PracticeVector(collider.offset));
        if(collider is BoxCollider2D box){shape["size"]=PracticeVector(box.size);shape["edge_radius"]=Number(box.edgeRadius);}
        if(collider is CapsuleCollider2D capsule){shape["size"]=PracticeVector(capsule.size);shape["direction"]=new J((int)capsule.direction);}
        if(collider is CircleCollider2D circle)shape["radius"]=Number(circle.radius);
        if(collider is PolygonCollider2D polygon)shape["paths"]=new J(Enumerable.Range(0,polygon.pathCount).Select(i=>new J(polygon.GetPath(i).Select(PracticeVector).ToList())).ToList());
        return shape;
    }
    static bool PracticeSame(J expected,J actual) {
        if(expected.D.Count>0)return expected.D.All(pair=>actual.D.ContainsKey(pair.Key)&&PracticeSame(pair.Value,actual[pair.Key]));
        if(expected.value is List<J>)return actual.value is List<J>&&expected.A.Count==actual.A.Count&&expected.A.Select((value,index)=>PracticeSame(value,actual[index])).All(same=>same);
        if(expected.value is string)return expected.S==actual.S;
        return Math.Abs(expected.N-actual.N)<.00001;
    }
    J PracticeInspectDummy(OriginalPracticeDummy dummy,List<string> errors) {
        if(dummy.GetComponentInChildren<OriginalRig>(true)!=null||dummy.GetComponentInChildren<OriginalBodyPart>(true)!=null)errors.Add("Dummy contains an invented fighter/vitality body component");
        if(dummy.BodyParts.Count!=5||dummy.FixedItems.Count!=2)errors.Add("Dummy does not contain source five parts/two fixed weapons");
        if(!dummy.FixedItems.Select(item=>item.itemId).OrderBy(id=>id).SequenceEqual(new[]{202,229}))errors.Add("Dummy fixed weapon IDs are not the original202/229 pair");
        if(dummy.GetComponentsInChildren<Rigidbody2D>(true).Length!=10)errors.Add("Dummy rigidbody count differs from source10");
        var rows=new List<J>();
        foreach(var expected in practiceExpected["parts"].A) {
            var part=dummy.BodyParts.FirstOrDefault(p=>p.SourcePath==expected["path"].S);if(part==null){errors.Add("Source dummy body missing: "+expected["path"].S);continue;}
            var body=part.Body;var colliders=body.GetComponents<Collider2D>();var shapes=new List<J>();
            var observed=J.O("path",part.SourcePath,"body_type",(int)body.bodyType,"mass",Number(body.mass),"gravity",Number(body.gravityScale),"linear_damping",Number(body.linearDamping),"angular_damping",Number(body.angularDamping),"simulated",body.simulated?1:0);
            foreach(var collider in colliders)shapes.Add(J.O("enabled",collider.enabled?1:0,"trigger",collider.isTrigger?1:0,"layer",collider.gameObject.layer,"shape",PracticeShape(collider)));
            observed["colliders"]=new J(shapes);var comparable=J.Parse(expected.ToString());foreach(var c in comparable["colliders"].A)c.D.Remove("id");
            if(!PracticeSame(comparable,observed))errors.Add("Original dummy body/collider settings changed or differ from source: "+part.SourcePath);
            rows.Add(observed);
        }
        var weapons=new List<J>();foreach(var item in dummy.FixedItems) {
            if(item.owner!=null||item.gameObject.layer!=16||item.OriginalPlayerSide)errors.Add("Dummy fixed weapon owner/side/layer differs from source false PlayerOrEnemy");
            if(item.itemId!=202&&item.itemId!=229)errors.Add("Unexpected dummy fixed item ID");
            if(item.itemBody==null||item.counterweightBody==null)errors.Add("Dummy fixed weapon has unresolved source rigidbodies");
            if(item.itemBody!=null&&item.counterweightBody!=null) {
                var mass=OriginalCombatRules.RuntimeMass(item.category,item.twoHanded,item.flailHead);
                if(Mathf.Abs(item.itemBody.mass-mass.x)>.0001f||Mathf.Abs(item.counterweightBody.mass-mass.y)>.0001f)errors.Add("Dummy fixed weapon mass differs from original cqe");
            }
            weapons.Add(J.O("item_id",item.itemId,"owner_null",item.owner==null,"layer",item.gameObject.layer,"player_side",item.OriginalPlayerSide,"category",item.category,"mass",Number(item.itemBody==null?0:item.itemBody.mass),"counterweight_mass",Number(item.counterweightBody==null?0:item.counterweightBody.mass)));
        }
        return J.O("body_parts",new J(rows),"fixed_weapons",new J(weapons),"rigidbody_count",dummy.GetComponentsInChildren<Rigidbody2D>(true).Length);
    }
    static AudioSource PracticeAudioSource(OriginalPracticeDummy dummy) {
        var source=J.Parse(Resources.Load<TextAsset>("OriginalPracticeSource").text);var row=source["controllers"].A.First(r=>r["script"].S=="ImpactSFX");return dummy.Resolve<AudioSource>(row["data"]["audioSource"]);
    }
    static J PracticeInspectAudio(OriginalPracticeDummy dummy,List<string> errors) {
        var source=J.Parse(Resources.Load<TextAsset>("OriginalPracticeSource").text);var row=source["controllers"].A.First(r=>r["script"].S=="ImpactSFX");var catalog=Resources.Load<OriginalVisualCatalog>("OriginalVisualCatalog");var fields=new List<J>();
        if(PracticeAudioSource(dummy)==null)errors.Add("Source dummy ImpactSFX AudioSource does not bind");
        foreach(string field in new[]{"metalSharpHitFlesh","metalBashFlesh_Small","woodBashFlesh_Big","woodBashFlesh_Small"}) {
            var audio=row["sounds"][field];var clips=new List<J>();if(!audio.Has){errors.Add("Source dummy AudioObject missing: "+field);continue;}
            int count=audio["data"]["audioClip"].A.Count;if(count==0)errors.Add("Source dummy AudioObject has no clips: "+field);
            for(int i=0;i<count;i++) {
                string identity=audio["assets"]["audioClip["+i+"]"].S;var clip=catalog.Get<AudioClip>(identity);
                if(clip==null||clip.samples<=0||clip.frequency<=0)errors.Add("Source dummy audio clip does not bind/decode: "+identity);
                clips.Add(J.O("identity",identity,"name",clip==null?"":clip.name,"samples",clip==null?0:clip.samples,"frequency",clip==null?0:clip.frequency,"channels",clip==null?0:clip.channels));
            }
            fields.Add(J.O("field",field,"clips",new J(clips)));
        }
        return new J(fields);
    }
    static J PracticeImpact(OriginalPracticeImpact impact)=>J.O("part",impact.Part==null?"":impact.Part.SourcePath,"item_id",impact.Item==null?0:impact.Item.itemId,"cannon",impact.Cannon,"point",PracticeVector(impact.Point),"velocity",PracticeVector(impact.Velocity),"speed",Number(impact.Velocity.magnitude),"sound_index",impact.SoundIndex,"sound_volume",Number(impact.SoundVolume),"feedback_force",Number(impact.FeedbackForce),"camera_impulse",Number(impact.CameraImpulse));
    void PracticeCleanup() {
        if(practiceProjectile!=null)Destroy(practiceProjectile.gameObject);
        if(practiceFixture!=null)Destroy(practiceFixture.gameObject);
        if(practiceAttacker!=null){practiceAttacker.gameObject.SetActive(false);Destroy(practiceAttacker.gameObject);}
        if(practiceTypeEnemy!=null){practiceTypeEnemy.gameObject.SetActive(false);Destroy(practiceTypeEnemy.gameObject);}
        practiceProjectile=null;practiceFixture=null;practiceAttacker=null;practiceTypeEnemy=null;
    }
    bool PracticePassed=>!practiceTimedOut&&issues.Count==0&&practiceCases.Count==3&&practiceCases.All(c=>c["passed"].B);
    void WritePractice(bool complete) {
        var report=J.O("scope","Original Practice Dummy assembly, unchanged body physics and real weapon contact/circlecast fixtures","unity",Application.unityVersion,"source_apk_sha256",practiceExpected==null?"":practiceExpected["source_apk_sha256"].S,
            "complete",complete,"passed",complete&&PracticePassed,"realtime_watchdog_expired",practiceTimedOut,"elapsed_realtime",Time.realtimeSinceStartupAsDouble-practiceStarted,
            "cases_expected",3,"cases_run",practiceCases.Count,"cases_passed",practiceCases.Count(c=>c["passed"].B),"cases",new J(practiceCases),"current_case",practiceCurrent,"issues",new J(issues),
            "runtime_parity_verified",false,"target_physics_modified_by_probe",false,"direct_process_impact_called_by_probe",false,"normal_gameplay_input",false,
            "limits",Strings(new[]{"Controlled attacker placement/velocity and original cannon Fire validate production contact dispatch; this does not establish ordinary hand-input success or APK trajectory parity.","Dummy is translated rigidly to y20 to isolate the source layout from arena props; no dummy collider, layer, body type, mass, damping, gravity or joint is changed by the probe.","Source AudioClip references are checked and production PlaySound returns before its impact event; audible device output is not measured.","This fixture does not test onboarding, tutorial UI, environment tint coroutine, Spooky mode or haptic/camera platform consumption."}));
        File.WriteAllText(Path.Combine(output,"original-practice-validation.json"),report.ToString());
    }
    void FinishPractice() {
        if(finished)return;finished=true;if(practiceWatchdog!=null)practiceWatchdog.Cancel();StopAllCoroutines();PracticeCleanup();WritePractice(true);
        Application.logMessageReceived-=ObserveLog;Physics2D.simulationMode=previousSimulation;Time.timeScale=practicePreviousTimeScale;
        Debug.Log("Original Practice validation: "+practiceCases.Count+" cases, "+issues.Count+" issues. "+output);Application.Quit(PracticePassed?0:1);
    }
}

public sealed class OriginalPracticeValidationWatchdog : MonoBehaviour {
    double deadline;Action expired;bool active;
    public bool Expired=>active&&Time.realtimeSinceStartupAsDouble>=deadline;
    public void Initialize(double seconds,Action callback){if(double.IsNaN(seconds)||double.IsInfinity(seconds)||seconds<=0)throw new ArgumentOutOfRangeException(nameof(seconds));deadline=Time.realtimeSinceStartupAsDouble+seconds;expired=callback;active=true;}
    public void Cancel(){active=false;expired=null;}
    void Update(){if(!Expired)return;active=false;var callback=expired;expired=null;callback?.Invoke();}
}
