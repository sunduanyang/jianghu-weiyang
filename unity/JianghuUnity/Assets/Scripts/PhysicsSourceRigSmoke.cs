using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Jianghu {
public sealed partial class PhysicsBattleSmoke {
    // Unity normalizes quaternions during Transform assignment. Compare
    // physical float32 state (two ULP plus JSON's G7 precision), accept q == -q.
    static bool SameSourceValue(J a,J b){
        if(a.D.Count>0||b.D.Count>0){
            if(a.D.Count!=b.D.Count)return false;
            bool quat=a.D.ContainsKey("qx")&&b.D.ContainsKey("qx");
            if(quat){double dot=0,aa=0,bb=0;foreach(string q in new[]{"qx","qy","qz","qw"}){dot+=a[q].N*b[q].N;aa+=a[q].N*a[q].N;bb+=b[q].N*b[q].N;}if(System.Math.Abs(dot)/System.Math.Sqrt(aa*bb)<.99999999)return false;}
            return a.D.All(p=>b.D.ContainsKey(p.Key)&&(quat&&new[]{"qx","qy","qz","qw"}.Contains(p.Key)||SameSourceValue(p.Value,b[p.Key])));
        }
        if(a.A.Count>0||b.A.Count>0)return a.A.Count==b.A.Count&&a.A.Select((p,i)=>SameSourceValue(p,b[i])).All(v=>v);
        if(a.value is string||b.value is string||a.value is bool||b.value is bool)return a.S==b.S;
        return a.Has==b.Has&&System.Math.Abs(a.N-b.N)<=System.Math.Max(.00001,System.Math.Max(System.Math.Abs(a.N),System.Math.Abs(b.N))*2e-7);
    }
    IEnumerator AuditSourceRig(bool resume){
        if(resume)yield return Call("/api/martial",null,j=>app.Martial=j);
        else yield return Call("/api/intervene",J.O("action","challenge","npc_id",1),j=>app.Martial=j);
        app.ShowBattleForSmoke();yield return WaitArena();if(app.PhysicsArena==null)yield break;
        var arena=app.PhysicsArena;
        Check("formal campaign uses source body graph",arena.Campaign&&arena.player.SourceRigActive&&arena.enemy.SourceRigActive);
        string path=Path.Combine(client.SaveDir,"source-rig-expected.json");
        if(resume){
            var expected=J.Parse(File.ReadAllText(path));var actual=arena.CaptureCampaign();
            File.WriteAllText(Path.Combine(output,"cold-expected.json"),expected.ToString());File.WriteAllText(Path.Combine(output,"cold-actual.json"),actual.ToString());
            for(int i=0;i<2;i++)foreach(string key in new[]{"source_rig","source_shape","source_equipment","source_ai","source_impalement"})Check("cold restart "+i+" "+key,key=="source_rig"?SameSourceValue(expected["fighters"][i]["reference"][key],actual["fighters"][i]["reference"][key]):SameSaveValue(expected["fighters"][i]["reference"][key],actual["fighters"][i]["reference"][key]));
            yield return Picture("source-rig-restart");yield return app.SavePhysicsForSmoke("surrender");Check("source rig battle settlement",app.Martial["battle"]["settled"].B);yield break;
        }
        var result=arena.ProbeSourceRig();File.WriteAllText(Path.Combine(output,"source-rig.json"),result.ToString());
        foreach(string side in new[]{"player","enemy"}){
            var row=result[side];var snap=row["snapshot"];
            Check("raw source fields applied in Unity "+side,row["validation"]["failures"].I==0,row["validation"]);
            Check("source head base uses shared character neck anchor "+side,row["head_base_to_hinge"].N<.00001&&row["head_base_to_neck_tip"].N<.00001&&System.Math.Abs(row["head_bind_length"].N-.30)<.00001&&row["head_bind_up"].N>.299,row);
            Check("source held weapons share discrete collision stepping with hands "+side,row["main_collision_mode"].S=="Discrete"&&(row["off_collision_mode"].S==""||row["off_collision_mode"].S=="Discrete"),row);
            var shape=row["shape"];Check("source constructor body configuration "+side,shape["config"]["Gender"].I==1&&shape["config"]["ShouldersWidth"].N==.125&&shape["physical_changes"].A.Any(p=>p["field"].S=="PelvisPointTransform"&&System.Math.Abs(p["after"]["y"].N-.42)<.000001),shape);
            Check("complete source physics graph "+side,row["parts"].I==17&&row["organs"].I==3&&snap["bodies"].A.Count==(side=="player"?34:31)&&snap["joints"].A.Count==32&&snap["points"].A.Count==(side=="player"?14:13),row);
            Check("source source-rig restart "+side,SameSourceValue(result["saved"]["fighters"][side=="player"?0:1]["reference"]["source_rig"],result["restored"]["fighters"][side=="player"?0:1]["reference"]["source_rig"]));
        }
        Check("source rig remains within scene",result["maximum_height"].N<6&&result["maximum_speed"].N<=100.1,result);
        Check("source skeleton constraints remain joined",result["maximum_gap"].N<.15,J.O("gap",result["maximum_gap"]));
        Check("source skeleton resumes stable physics",result["resumed_gap"].N<.15,J.O("gap",result["resumed_gap"]));
        var nothingRows=new System.Collections.Generic.List<J>();
        foreach(var spec in new[]{DuelWeapon.Fist,DuelWeapon.All.First(w=>w.id=="dagger"),DuelWeapon.All.First(w=>w.id=="greatsword")}){
            var nothing=arena.ProbeSourceNothing(new J(new System.Collections.Generic.List<J>{ReferenceFighter(spec),ReferenceFighter(spec)}));nothingRows.Add(nothing);
            foreach(string side in new[]{"player","enemy"}){
                int count=spec.TwoHanded?0:spec.id=="unarmed"?2:1;
                Check("source Nothing slot count "+spec.id+" "+side,nothing[side].A.Count==count,nothing[side]);
                foreach(var item in nothing[side].A)Check("source Nothing properties "+spec.id+" "+side+" "+item["side"].S,item["circle"].B&&System.Math.Abs(item["radius"].N-.059922490268945694)<.000001&&item["surface"].S=="WoodBlunt"&&item["mass"].N==1&&item["drag"].N==.5&&item["angular_drag"].N==2.5&&item["counter_mass"].N==1&&item["com_mass"].N==.25&&item["source_grip"].B&&item["connected"].B&&item["grip_gap"].N<.0001,item);
                Check("source Nothing grip settles "+spec.id+" "+side,nothing["settled_"+side].A.All(item=>item["grip_gap"].N<.05),nothing["settled_"+side]);
            }
        }
        File.WriteAllText(Path.Combine(output,"source-nothing-equipment.json"),new J(nothingRows).ToString());
        var topology=arena.ProbeSourceConstraintComponents();File.WriteAllText(Path.Combine(output,"source-constraint-components.json"),topology.ToString());
        Check("live constraint audit detects real displacement",topology["live_before"].N>.5&&topology["other_live_gap"].N>.5,topology);
        Check("detached chain is separate from live actor constraints",topology["forearm_cut"].B&&topology["child_hinge_retained"].B&&topology["child_health"].N==50&&topology["detached_actual_gap"].N>.5&&topology["live_after_cut"].N<.5,topology);
        var exhaustion=arena.ProbeSourceExhaustion();File.WriteAllText(Path.Combine(output,"source-exhaustion.json"),exhaustion.ToString());
        foreach(string stage in new[]{"asleep","awake","kick_exhaustion"}){var state=exhaustion[stage];Check("source body properties on exhaustion "+stage,state["passed_out"].B==(stage!="awake")&&state["bodies"].A.All(p=>System.Math.Abs(p["mass"].N-p["expected_mass"].N)<.00001&&System.Math.Abs(p["drag"].N-p["expected_drag"].N)<.00001&&System.Math.Abs(p["angular_drag"].N-p["expected_angular_drag"].N)<.00001),state);}
        Check("source exhaustion applies once and retains a later injury override",exhaustion["injured_while_asleep_mass"].N==9,exhaustion);
        var ai=arena.ProbeSourceAI();File.WriteAllText(Path.Combine(output,"source-ai.json"),ai.ToString());
        Check("seven native AI states scheduled",ai["states"].A.Count==7&&ai["states"].A.All(s=>s["jobs"].I>0),ai["states"]);
        foreach(string key in new[]{"parallel_relays","missing_binding_held","tap_kick","continuation_equal","pause_realtime_stable","fixture_realtime_stable"})Check("source AI "+key,ai[key].B);
        var impale=arena.ProbeSourceImpalement();File.WriteAllText(Path.Combine(output,"source-impalement.json"),impale.ToString());
        foreach(var item in impale["cases"].A){bool positive=item["case"].S=="threshold"||item["case"].S=="spike"||item["case"].S=="overkill_accumulates";Check("organ trigger "+item["case"].S,item["trigger_events"].I>0&&item["joint"].B==positive&&(item["stage"].I>0)==positive,item);}
        var phase=impale["cases"][0]["phases"];Check("impalement wait phases and drop",phase["restored"].B&&phase["drop_retains"].B&&string.Join(",",phase["states"].A.Select(s=>s["stage"].I))=="1,2,3,0",phase);
        Check("impalement cleanup on disable",impale["cases"][9]["disabled_cleanup"].B);
        var injury=arena.ProbeSourceInjuryDeath();File.WriteAllText(Path.Combine(output,"source-injury-death.json"),injury.ToString());
        foreach(var item in injury["injuries"].A){var before=item["before"];string name=item["case"].S;
            Check("source injury restart "+name,SameSourceValue(before,item["restored"]));
            if(name=="bashed_arm")Check("source blunt injury retains hinge",before["mode"].I==1&&!before["severed"].B&&before["cut_joint_enabled"].B,before);
            else if(name=="pierced_arm")Check("source piercing retains hinge",before["mode"].I==2&&before["severed"].B&&before["mass"].N==9&&before["cut_joint_enabled"].B,before);
            else Check("source cut uses configured joint "+name,before["mode"].I==3&&before["severed"].B&&!before["cut_joint_enabled"].B&&before["retained_by_bout"].B,before);
            if(name=="cut_arm")Check("source related hand keeps its health",before["related"].A.Any(p=>p["key"].S=="handNear"&&p["health"].N==50&&!p["severed"].B));
        }
        var stages=injury["death"]["stages"];Check("source death contains seven stages",stages.A.Count==7);
        Check("source death freezes base and disables extra shapes",stages[0]["body_type"].I==2&&stages[0]["root_layer"].I==22&&!stages[0]["kick"].B&&!stages[0]["overlap"].B&&stages[0]["extra"].A.All(p=>!p["active"].B),stages[0]);
        Check("source death applies each serialized dead property",stages[1]["bodies"].A.All(p=>System.Math.Abs(p["mass"].N-p["dead_mass"].N)<.00001&&System.Math.Abs(p["drag"].N-p["dead_drag"].N)<.00001&&System.Math.Abs(p["angular_drag"].N-p["dead_angular_drag"].N)<.00001),stages[1]);
        Check("source death softens exact joint list",stages[2]["body_joints"].A.Count>0&&stages[2]["body_joints"].A.All(p=>p["frequency"].N==10&&p["damping"].N==0),stages[2]);
        Check("source death drops then releases configured joints",stages[3]["corpse"].B&&stages[4]["killed_joints"].A.All(p=>!p["enabled"].B),stages[4]);
        var kick=arena.ProbeSourceKickReceiver();File.WriteAllText(Path.Combine(output,"source-kick-receiver.json"),kick.ToString());
        foreach(var item in kick["cases"].A){var hit=item["after_trigger"];string label=(item["victim_player"].B?"player":"enemy")+(item["passed_out"].B?" passed out":" awake");
            Check("source kick actual trigger "+label,item["trigger_active"].B&&hit["health"].N==99&&hit["being_kicked"].B&&hit["state"]["source_receiver"]["stage"].I==1,hit);
            Check("source kick two waits and restart "+label,item["phases"].A.Count==2&&item["phases"].A.All(p=>SameSourceValue(p["before"],p["restored"]))&&item["phases"][1]["before"]["state"]["source_receiver"]["stage"].I==2&&!item["complete"]["being_kicked"].B,item["phases"]);
            Check("source kick has no invented damping "+label,hit["root_drag"].N==2.25&&hit["pelvis_drag"].N==2.25&&item["complete"]["root_drag"].N==2.25&&item["complete"]["pelvis_drag"].N==2.25);
            Check("source kick correct physical push "+label,hit["root_vx"].N*hit["expected_force_x"].N>0&&(!item["passed_out"].B||System.Math.Abs(hit["pelvis_vx"].N)>.1),hit);
        }
        // Return from destructive isolated fixtures to a real live battle,
        // then persist the complete graph through its normal backend path.
        result=arena.ProbeSourceRig();yield return Picture("source-rig-formal");
        yield return app.SavePhysicsForSmoke("checkpoint");var saved=app.Martial["battle"]["frame"];
        Check("formal backend saves source graph and AI",saved["fighters"][0]["reference"]["source_rig"]["version"].I==1&&saved["fighters"][1]["reference"]["source_ai"]["version"].I==1,J.O("error",client.Error));
        File.WriteAllText(path,saved.ToString());
    }
}
}
