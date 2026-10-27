using UnityEngine;

public sealed partial class PhysicsDuel {
    readonly DuelControls controls=new DuelControls();
    void DrawControls() {
        if(!started)return;
        bool two=player.weapon.TwoHanded;
        if(Live&&controls.left.captured)DrawPad(controls.left,two||!player.HasShield?"副手":"盾",jade);
        if(Live&&controls.right.captured)DrawPad(controls.right,two?"主手":"剑",vermilion);
        Label(new Rect(395,777,810,32),two?"左键主手 · 右键牵引副手，两手合力移械 / 转动 · K 架杆":(player.HasShield?"左键运械 · 右键控盾":"左键主手 · 右键副手护身出拳")+" · 按下出现轮盘，松开隐藏",17,ink,TextAnchor.MiddleCenter);
        Fill(new Rect(395,825,810,79),new Color(.2f,.27f,.23f,.055f));
        DrawMoveButton(controls.back,"←",controls.MoveAxis<0);
        DrawMoveButton(controls.dash,"进身",false);
        DrawMoveButton(controls.forward,"→",controls.MoveAxis>0);
        Label(new Rect(406,837,198,56),"A / D 移步\nSpace 进身",15,ink,TextAnchor.MiddleCenter);
        Label(new Rect(996,837,198,56),two?"J 变招 · K 架杆\nV 碰撞框 · H 帮助":"J 变招 · K 防守\nV 碰撞框 · H 帮助",15,ink,TextAnchor.MiddleCenter);
        Label(new Rect(440,908,720,24),"触屏：左半屏副手 / 右半屏主手    Esc 暂停、保存或收招",14,new Color(.30f,.36f,.30f),TextAnchor.MiddleCenter);
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
