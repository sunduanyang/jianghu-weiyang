using UnityEngine;

public sealed partial class PhysicsDuel {
    readonly DuelControls controls=new DuelControls();
    void DrawControls() {
        if(!started)return;
        bool two=player.weapon.TwoHanded;
        if(Live){DrawMouseTarget(true,"主手",vermilion);DrawMouseTarget(false,player.HasShield?"盾手":"副手",jade);}
        if(Live&&controls.left.captured)DrawPad(controls.left,two||!player.HasShield?"副手":"盾",jade);
        if(Live&&controls.right.captured)DrawPad(controls.right,two?"主手":"剑",vermilion);
        Label(new Rect(320,777,960,32),"左键主手 · 右键副手 · 两键同按同时控制 · 松开保持 / 续势 · 中键收住",17,ink,TextAnchor.MiddleCenter);
        Fill(new Rect(395,825,810,79),new Color(.2f,.27f,.23f,.055f));
        DrawMoveButton(controls.back,"←",controls.MoveAxis<0);
        DrawMoveButton(controls.dash,"进身",false);
        DrawMoveButton(controls.forward,"→",controls.MoveAxis>0);
        Label(new Rect(406,837,198,56),"A / D 移步\nSpace 进身",15,ink,TextAnchor.MiddleCenter);
        Label(new Rect(996,837,198,56),two?"J 变招 · K 架杆\nV 碰撞框 · H 帮助":"J 变招 · K 防守\nV 碰撞框 · H 帮助",15,ink,TextAnchor.MiddleCenter);
        Label(new Rect(395,908,810,24),"触屏：左右半屏各控一手 · 双击该侧并按住移步    Esc 暂歇",14,new Color(.30f,.36f,.30f),TextAnchor.MiddleCenter);
    }
    void DrawMouseTarget(bool primary,string name,Color color){
        var hand=controls.mouse.Hand(primary);if(!hand.hasTarget)return;
        Vector2 shoulder=player.limbs[primary?"upperNear":"upperFar"].A;
        Vector2 point=ScreenPoint(shoulder+hand.value*player.MouseReach(primary));
        bool active=controls.mouse.IsControlled(primary);
        Circle(point,Vector2.one*(active?22:16),new Color(color.r,color.g,color.b,active?.55f:.3f));
        Line(point+Vector2.left*11,point+Vector2.right*11,1.5f,color);Line(point+Vector2.up*11,point+Vector2.down*11,1.5f,color);
        Label(new Rect(point+new Vector2(-65,-37),new Vector2(130,24)),name+" · "+(active?"操控":hand.Coasting?"续势":"保持"),14,color,TextAnchor.MiddleCenter);
    }
    void DrawMoveButton(Rect r,string label,bool held){Fill(r,new Color(jade.r,jade.g,jade.b,held?.28f:.12f));Label(r,label,24,ink,TextAnchor.MiddleCenter);}
    void DrawPad(DuelControls.Pad pad,string name,Color color) {
        Vector2 c=pad.center;
        Circle(c,Vector2.one*184,new Color(color.r,color.g,color.b,.34f));
        Circle(c,Vector2.one*180,new Color(.94f,.90f,.80f,.32f));
        Circle(c,Vector2.one*145,new Color(color.r,color.g,color.b,.08f));
        for(int i=0;i<8;i++){float a=i*Mathf.PI/4;Vector2 d=new Vector2(Mathf.Cos(a),Mathf.Sin(a));Line(c+d*81,c+d*89,1,new Color(color.r,color.g,color.b,.65f));}
        Vector2 thumb=c+new Vector2(pad.value.x,-pad.value.y)*DuelControls.Pad.Radius;
        if(pad.HasDirection)Line(c,thumb,4,new Color(color.r,color.g,color.b,.5f));
        Circle(thumb+new Vector2(0,2),Vector2.one*44,new Color(.15f,.19f,.16f,.15f));
        Circle(thumb,Vector2.one*42,new Color(color.r,color.g,color.b,.8f));
        Label(new Rect(thumb-new Vector2(22,22),Vector2.one*44),name,19,new Color(.98f,.95f,.85f),TextAnchor.MiddleCenter);
    }
}
