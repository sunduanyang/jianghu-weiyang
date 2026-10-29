using System;
using System.Collections.Generic;
using System.Linq;
using Jianghu;
using UnityEngine;

public sealed partial class PhysicsDuel {
    public sealed partial class Fighter {
        public void ReferenceProbeInput(Vector2 main,Vector2 off){
            if(!game.IsSmoke)throw new InvalidOperationException("Probe input requires isolated acceptance mode");
            // Exercise the pointer-style route. A source AI already started
            // by a previous frame otherwise overrides these synthetic sticks
            // in SourceAIHandCommand, even when testOverride stops ReadAI.
            aiStarted=false;aiDriving=true;aiMain=main;aiOff=off;
        }
        void TranslateReferenceProbe(float offset){
            foreach(var rb in root.GetComponentsInChildren<Rigidbody2D>())rb.position+=Vector2.right*offset;
            foreach(var h in referenceHands.Values)h.target+=Vector2.right*offset;
        }
        public void ReferenceProbePlace(float x){if(!game.IsSmoke)throw new InvalidOperationException("Probe placement requires isolated acceptance mode");TranslateReferenceProbe(x-mover.position.x);}
    }
    // Runs the actual rig, input drivers, weapon colliders and contact solver.
    // Damage is excluded so severing / exhaustion cannot masquerade as release.
    public J ProbeReferenceBind(bool bodyTarget,float arc,bool retreat=true){
        if(!IsSmoke||!ReferenceCombat)throw new InvalidOperationException("Reference bind probes require acceptance mode");
        ResetRound();RunCampaign(true);fixtureMode=referenceBindProbe=true;muted=true;
        referenceProbeBody=bodyTarget;referenceProbeContactEvents=0;
        player.testOverride=enemy.testOverride=true;
        player.maxStamina=enemy.maxStamina=100000;player.stamina=enemy.stamina=100000;
        // Keep the shipping opening distance. The old fixed +/-1.15 setup
        // starts long poles interpenetrating, invalidating a release probe.
        if(bodyTarget)foreach(var c in enemy.colliders)if(c.attachedRigidbody==enemy.sword.rb||enemy.shield!=null&&c.attachedRigidbody==enemy.shield.rb)c.enabled=false;
        // A blade-versus-blade fixture must not be intercepted first by the
        // independently driven Nothing fists. Keep the same two live rigs,
        // hands and primary grips; isolate only the untested offhand shapes.
        if(!bodyTarget)foreach(var actor in new[]{player,enemy})if(!actor.weapon.TwoHanded&&actor.shield!=null&&!actor.HasShield)
            foreach(var c in actor.colliders)if(c.attachedRigidbody==actor.shield.rb)c.enabled=false;
        Physics2D.SyncTransforms();
        if(UseCorePhysics)CoreWorld.Synchronize();
        var weaponShapes=player.colliders.Where(c=>c!=null&&c.enabled&&c.gameObject.activeInHierarchy&&!c.isTrigger&&c.attachedRigidbody==player.sword.rb).Distinct().ToArray();
        // Imported source body parts use their own layer matrix. Layer 9 was
        // only the old rig, so it silently measured an empty body target set.
        bool BodyTarget(Collider2D c){var contact=c.GetComponent<DuelContact>();return contact!=null&&contact.owner==enemy&&contact.limb!=null&&contact.kind!="sword"&&contact.kind!="shield";}
        var targetShapes=enemy.colliders.Where(c=>c!=null&&c.enabled&&c.gameObject.activeInHierarchy&&!c.isTrigger&&(bodyTarget?BodyTarget(c):c.attachedRigidbody==enemy.sword.rb)).Distinct().ToArray();
        if(weaponShapes.Length==0||targetShapes.Length==0)throw new InvalidOperationException("Contact withdrawal probe has no actual collider targets");
        float initialDistance=float.PositiveInfinity;
        foreach(var a in weaponShapes)foreach(var b in targetShapes)initialDistance=Mathf.Min(initialDistance,a.CombatDistance(b));
        // A short dagger at the former overhead guard never crosses the
        // bounded attack sweep. Use a reachable guard for that contact case;
        // retain every other body/weapon scenario, including the spear bind.
        float guardY=player.weapon.id=="greatsword"||player.weapon.id=="dagger"?.05f:.5f;
        float guardX=player.weapon.id=="dagger"?.5f:-.15f;
        int contacts=0,pressed=0,deepFrames=0,clearFrames=0,approachFrames=0;float maxDepth=0,maxGap=0,release=-1,straightRelease=-1,stuck=0,maxStuck=0;
        var path=new List<J>();Vector2 startTip=Vector2.zero;
        for(int frame=0;frame<420;frame++){
            bool withdrawing=frame>=240;
            referenceProbeCollecting=frame>=60&&frame<240;
            float t=frame/60f;
            Vector2 command=frame<60?Vector2.zero:frame<180?new Vector2(Mathf.Cos(t*arc)*.8f,Mathf.Sin(t*arc)*.65f):withdrawing?new Vector2(-.8f,.55f):new Vector2(.85f,.05f);
            if(bodyTarget&&player.weapon.id=="greatsword"&&frame>=60&&!withdrawing)command.y-=.35f;
            player.ReferenceProbeInput(command,frame>=360&&player.weapon.TwoHanded?new Vector2(-.2f,-.6f):command);enemy.ReferenceProbeInput(new Vector2(guardX,guardY),Vector2.zero);
            // The former raised guard puts the newly free wrist's greatsword
            // above the other blade. Approach through the real walking drive
            // until contact is observed, retaining the original press period.
            bool approach=frame>=60&&(frame<120||frame<225&&contacts==0&&referenceProbeContactEvents==0);
            player.move=approach?1:withdrawing&&retreat?-1:0;if(approach)approachFrames++;enemy.move=0;
            if(frame==240)startTip=player.sword.B;
            SimulateFighters();
            bool contact=false,deep=false;float depth=0;
            foreach(var a in weaponShapes)foreach(var b in targetShapes){float d=a.CombatDistance(b);if(d<.006f)contact=true;if(d<-.025f)deep=true;depth=Mathf.Max(depth,-d);}
            if(frame>=60)maxDepth=Mathf.Max(maxDepth,depth);
            if(frame>=60&&frame<240&&contact)contacts++;
            if(frame>=228&&frame<240&&contact)pressed++;
            if(deep&&frame>=60)deepFrames++;
            if(withdrawing){clearFrames=contact?0:clearFrames+1;if(clearFrames>=6&&release<0)release=(frame-245)/60f;if(contact&&(player.sword.B-startTip).magnitude<.12f)stuck+=1f/60;else stuck=0;maxStuck=Mathf.Max(maxStuck,stuck);}
            if(frame==359)straightRelease=release;
            maxGap=Mathf.Max(maxGap,player.MaxJointGap());
            if(frame%6==0)path.Add(J.O("time",t,"tip_x",player.sword.B.x,"tip_y",player.sword.B.y,"target_tip_x",enemy.sword.B.x,"target_tip_y",enemy.sword.B.y,"hip_x",player.Hip.x,"contact",contact,"depth",depth));
        }
        var result=J.O("weapon",player.weapon.id,"target",bodyTarget?"body":"weapon","arc",arc,"retreat",retreat,"physics_substeps",referenceSteps,"actual_contact_events",referenceProbeContactEvents,"contact_frames",contacts,"pressed_last_12",pressed,"straight_release_seconds",straightRelease,"twist_release_seconds",straightRelease<0&&release>=2?release-2:-1,"release_seconds",release,"stationary_bind_seconds",maxStuck,"deep_frames",deepFrames,"maximum_depth",maxDepth,"maximum_joint_gap",maxGap,"withdrawal_tip_travel",Vector2.Distance(startTip,player.sword.B),"path",new J(path));
        result["approach_frames"]=new J(approachFrames);result["enemy_guard_y"]=new J(guardY);result["initial_target_distance"]=new J(initialDistance);
        result["enemy_guard_x"]=new J(guardX);
        result["target_weapon"]=new J(enemy.weapon.id);
        result["weapon_colliders"]=new J(weaponShapes.Length);result["target_colliders"]=new J(targetShapes.Length);result["source_body_target"]=new J(bodyTarget&&enemy.SourceRigActive);
        result["blade_friction"]=new J(player.sword.shape.sharedMaterial.friction);result["friction_combine"]=new J((int)player.sword.shape.sharedMaterial.frictionCombine);result["baumgarte"]=new J(Physics2D.baumgarteScale);
        fixtureMode=referenceBindProbe=false;PauseCampaign();return result;
    }
}
