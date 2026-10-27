using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jianghu;
using UnityEngine;

public sealed partial class PhysicsDuel {
    J sparringResults;
    IEnumerator SparringSmoke() {
        Directory.CreateDirectory(smokePath);started=true;
        yield return CheckSparring();
        bool ok=sparringResults["passed"].B;
        File.WriteAllText(Path.Combine(smokePath,"spar-smoke.json"),J.O("passed",ok,"version","0.9.0","sparring",sparringResults).ToString());
        Application.Quit(ok?0:2);
    }
    IEnumerator CheckSparring() {
        int[,] pairs={{0,4},{1,3},{6,7},{5,8}};
        var cases=new List<J>();
        for(int i=0;i<pairs.GetLength(0);i++) {
            training=false;playerWeaponIndex=pairs[i,0];enemyWeaponIndex=pairs[i,1];ResetRound();
            int initialHits=hits,initialBlocks=blocks;float until=Time.time+45,peakGap=0,peakLimit=0,leastStamina=100;
            while(Time.time<until&&!finished) {
                peakGap=Mathf.Max(peakGap,player.MaxJointGap(),enemy.MaxJointGap());
                peakLimit=Mathf.Max(peakLimit,player.MaxLimitError(),enemy.MaxLimitError());
                leastStamina=Mathf.Min(leastStamina,player.stamina,enemy.stamina);
                yield return new WaitForSeconds(.05f);
            }
            bool attacked=player.attackId>0&&enemy.attackId>0;
            bool hit=hits>initialHits&&(player.health<120||enemy.health<120);
            bool budget=leastStamina<95&&leastStamina>=0&&player.motionStaminaSpent+enemy.motionStaminaSpent>0;
            bool stable=peakGap<.10f&&peakLimit<6;
            cases.Add(J.O("player",player.weapon.id,"opponent",enemy.weapon.id,"passed",attacked&&hit&&budget&&stable,"both_attack",attacked,"natural_hits",hits-initialHits,"clash_events",blocks-initialBlocks,"health_player",player.health,"health_opponent",enemy.health,"seconds",elapsed,"minimum_stamina",leastStamina,"motion_effort_spent",player.motionStaminaSpent+enemy.motionStaminaSpent,"peak_joint_gap",peakGap,"peak_limit_error",peakLimit,"worst_joint",player.worstJoint+" | "+enemy.worstJoint));
            File.WriteAllText(Path.Combine(smokePath,"sparring-progress.json"),new J(cases).ToString());
            ScreenCapture.CaptureScreenshot(Path.Combine(smokePath,"spar-"+player.weapon.id+"-vs-"+enemy.weapon.id+".png"));
            yield return new WaitForSeconds(.1f);
        }
        sparringResults=J.O("passed",cases.All(c=>c["passed"].B),"uses_normal_stamina_and_contacts",true,"cases",new J(cases));
        File.WriteAllText(Path.Combine(smokePath,"sparring.json"),sparringResults.ToString());
        playerWeaponIndex=enemyWeaponIndex=DuelWeapon.SwordIndex;training=true;ResetRound();yield return new WaitForSeconds(1.5f);
    }
}
