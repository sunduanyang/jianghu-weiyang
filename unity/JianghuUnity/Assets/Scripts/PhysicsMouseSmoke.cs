using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Jianghu {
public sealed partial class PhysicsBattleSmoke {
    IEnumerator AuditMouse(){
        var mouse=new DuelMouseHands();Vector2 cursor=new Vector2(700,460);
        mouse.Press(true,cursor,Vector2.right*.7f,Vector2.up*.65f,0);
        mouse.Move(cursor+new Vector2(30,0),100,100,.05f);
        Vector2 main=mouse.main.value,off=mouse.off.value;
        Check("mouse directly extends only the selected hand",main.x>.99f&&mouse.off.value==off);
        mouse.Press(false,cursor,Vector2.zero,Vector2.zero,.06f);
        Check("adding the other button picks up its target without a jump",mouse.main.value==main&&mouse.off.value==off&&mouse.ActiveHand==2);
        mouse.Move(cursor+new Vector2(-25,0),100,100,.11f);
        Check("both held buttons move both independent targets",mouse.main.value!=main&&mouse.off.value!=off);
        mouse.Release(true,.12f);Check("releasing the old button cannot steal or cancel the new hand",mouse.ActiveHand==1);
        Vector2 mainReleased=mouse.main.value;off=mouse.off.value;
        mouse.Move(cursor+new Vector2(-15,-10),100,100,.15f);
        Check("releasing one button leaves the other hand under mouse control",mouse.main.value==mainReleased&&mouse.off.value!=off);
        mouse.Release(false,.12f);
        mouse=new DuelMouseHands();mouse.Press(true,cursor,Vector2.right*.7f,Vector2.up*.65f,0);mouse.Move(cursor+new Vector2(20,0),100,100,.05f);mouse.Release(true,.051f);
        for(int i=0;i<240;i++)mouse.Advance(1f/120);
        Check("a released straight thrust reaches full extension and holds",Mathf.Abs(mouse.main.value.magnitude-1)<.0001f&&Mathf.Abs(mouse.main.value.y)<.0001f);
        DuelMouseHands Circle(){
            var m=new DuelMouseHands();m.Press(true,cursor,Vector2.right*.8f,Vector2.up*.7f,0);
            Vector2 prior=Vector2.right*.8f,p=cursor;
            for(int i=1;i<=24;i++){float a=i*Mathf.PI/12;Vector2 next=new Vector2(Mathf.Cos(a),Mathf.Sin(a))*.8f;Vector2 d=(next-prior)*100;p+=new Vector2(d.x,-d.y);m.Move(p,100,100,i/60f);prior=next;}
            m.Release(true,.401f);return m;
        }
        var circle=Circle();float speed=Mathf.Abs(circle.main.angularSpeed);Vector2 heldOff=circle.off.value;
        for(int i=0;i<30;i++)circle.Advance(1f/120);
        Check("released circular cuts settle within 250 ms and do not spin",circle.main.travel>0&&circle.main.travel<20&&!circle.main.Coasting&&circle.off.value==heldOff,J.O("released_speed",speed,"after_quarter_second",circle.main.angularSpeed,"coast_degrees",circle.main.travel));
        for(int i=0;i<1200;i++)circle.Advance(1f/120);
        Check("coasting eventually settles without retracting the target",!circle.main.Coasting&&Mathf.Abs(circle.main.value.magnitude-.8f)<.001f);
        var at30=Circle();var at90=Circle();for(int i=0;i<60;i++)at30.Advance(1f/30);for(int i=0;i<180;i++)at90.Advance(1f/90);
        Check("passive motion agrees across render rates",Vector2.Distance(at30.main.value,at90.main.value)<.015f,J.O("target_error",Vector2.Distance(at30.main.value,at90.main.value)));
        circle=Circle();circle.Freeze();Vector2 frozen=circle.main.value;circle.Advance(1);
        Check("braking or pausing preserves pose and clears pointer inertia",circle.ActiveHand<0&&!circle.main.Coasting&&Vector2.Distance(frozen,circle.main.value)<.0001f);
        var input=new DuelControls();Check("mouse no longer captures a floating joystick",!input.Begin(-1,cursor)&&!input.left.captured&&!input.right.captured);

        var scene=SceneManager.CreateScene("Mouse hand acceptance",new CreateSceneParameters(LocalPhysicsMode.Physics2D));
        var root=new GameObject("Mouse hand acceptance");root.SetActive(false);SceneManager.MoveGameObjectToScene(root,scene);
        var arena=root.AddComponent<PhysicsDuel>();arena.campaign=new PhysicsDuel.CampaignOptions{playerSkin="m-youth-farmer",enemySkin="f-youth-farmer",battle=FeelBattle(DuelWeapon.All[2])};
        app.enabled=false;root.SetActive(true);
        var cases=new List<J>();
        int variant=0;
        foreach(var weapon in DuelWeapon.All.Concat(new[]{DuelWeapon.Fist,DuelWeapon.All[2]})){
            bool hasShield=variant++==DuelWeapon.All.Length+1;
            arena.PauseCampaign();arena.campaign.battle=J.O("fighters",new J(new List<J>{AuditFighter(weapon,hasShield),AuditFighter(weapon,hasShield)}));arena.ResetRound();arena.player.testOverride=arena.enemy.testOverride=true;
            foreach(var a in arena.player.colliders)foreach(var b in arena.enemy.colliders)Physics2D.IgnoreCollision(a,b);
            arena.RunCampaign(true);yield return new WaitForSecondsRealtime(.5f);
            var m=arena.MouseForSmoke;var fighter=arena.player;
            float seedAxis=fighter.MouseAxis(false);
            m.Press(true,cursor,fighter.MouseSeed(true),fighter.MouseSeed(false),0,fighter.MouseAxis(true),seedAxis);
            Check("picking up the mouse preserves offhand orientation "+weapon.id,Mathf.Abs(Mathf.DeltaAngle(m.off.Axis,seedAxis))<.001f);
            Vector2 previous=m.main.value,pointer=cursor;float gap=0,limit=0,start=arena.Clock,changedAt=-1,after=0;int selected=0;bool crossed=false,dual=false;
            while(arena.Clock-start<5){
                float t=arena.Clock-start;
                if(t>1.5f&&selected==0){m.Release(true,t);m.Press(false,pointer,Vector2.zero,Vector2.zero,t);selected=1;previous=m.off.value;changedAt=arena.Clock;}
                if(t<3){
                    float a=t*5.5f;Vector2 target=new Vector2(Mathf.Cos(a),Mathf.Sin(a))*.88f;
                    Vector2 delta=(target-previous)*(selected==0?fighter.MousePixels(true):fighter.MousePixels(false));pointer+=new Vector2(delta.x,-delta.y);
                    m.Move(pointer,fighter.MousePixels(true),fighter.MousePixels(false),t+.01f);previous=target;
                }else if(selected==1){m.Release(false,t);selected=2;}
                if(t>3.3f&&selected==2){m.Press(true,pointer,Vector2.zero,Vector2.zero,t);m.Press(false,pointer,Vector2.zero,Vector2.zero,t);selected=3;}
                if(selected==3){
                    pointer+=new Vector2(.3f,-.13f);m.Move(pointer,fighter.MousePixels(true),fighter.MousePixels(false),t+.01f);
                    dual|=m.IsControlled(true)&&m.IsControlled(false);
                    if(t>4){m.Release(true,t);m.Release(false,t);selected=4;}
                }
                if(changedAt>0&&arena.Clock-changedAt>.5f&&!crossed){after=m.main.travel;crossed=true;}
                gap=Mathf.Max(gap,fighter.MaxJointGap());limit=Mathf.Max(limit,fighter.MaxLimitError());yield return new WaitForFixedUpdate();
            }
            bool pass=dual&&gap<.15f&&limit<8&&fighter.PrimaryGripError<.04f&&(!weapon.TwoHanded||fighter.SupportGripError<.04f)&&fighter.mainMotionSpent>0&&m.main.travel>0&&m.main.travel<50&&!m.main.Coasting&&!m.off.Coasting;
            cases.Add(J.O("weapon",weapon.id,"shield",hasShield,"passed",pass,"both_hands_controlled",dual,"gap",gap,"limit",limit,"main_effort",fighter.mainMotionSpent,"off_effort",fighter.offMotionSpent,"coast_degrees",m.main.travel,"coast_after_switch",after,"primary_grip",fighter.PrimaryGripError,"support_grip",fighter.SupportGripError));
            File.WriteAllText(Path.Combine(output,"mouse-weapons.json"),new J(cases).ToString());
            Check("mouse handoff and real constraints "+weapon.id,pass,cases.Last());
        }
        arena.PauseCampaign();var memory=arena.MouseForSmoke.main.value;
        Check("campaign pause retains the remembered target with no live input",arena.MouseForSmoke.main.hasTarget&&arena.MouseForSmoke.ActiveHand<0&&!arena.MouseForSmoke.main.Coasting);
        arena.ResetRound();Check("new round clears previous hand targets",!arena.MouseForSmoke.main.hasTarget&&!arena.MouseForSmoke.off.hasTarget);
        yield return SceneManager.UnloadSceneAsync(scene);app.enabled=true;Time.timeScale=1;
    }
}
}

public sealed partial class PhysicsDuel {
    public DuelMouseHands MouseForSmoke {get {if(!IsSmoke)throw new System.InvalidOperationException("Isolated acceptance only");return controls.mouse;}}
}
