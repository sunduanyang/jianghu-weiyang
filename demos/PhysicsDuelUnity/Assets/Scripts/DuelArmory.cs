using UnityEngine;

public sealed partial class PhysicsDuel {
    bool armory;
    int playerWeaponIndex=DuelWeapon.SwordIndex,enemyWeaponIndex=DuelWeapon.SwordIndex;
    int pendingPlayerWeapon,pendingEnemyWeapon;
    void OpenArmory(){pendingPlayerWeapon=playerWeaponIndex;pendingEnemyWeapon=enemyWeaponIndex;armory=true;controls.ReleaseAll();player.ClearMouseCapture();}
    void CloseArmory(){armory=false;}
    void ApplyArmory(){playerWeaponIndex=pendingPlayerWeapon;enemyWeaponIndex=pendingEnemyWeapon;armory=false;ResetRound();}
    void DrawArmory() {
        Panel(new Rect(235,205,1130,560));
        Label(new Rect(275,220,1050,58),"演 武 兵 器",35,ink,TextAnchor.MiddleCenter);
        Label(new Rect(275,283,1050,29),"双方独立选择 · 双手兵器占用副手，不配盾 · 装备后重开",17,jade,TextAnchor.MiddleCenter);
        DrawWeaponChoices(275,"布衣客 / 你",ref pendingPlayerWeapon,jade);
        DrawWeaponChoices(825,training?"黄裳客 / 陪练":"黄裳客 / 对手",ref pendingEnemyWeapon,vermilion);
        if(Button(new Rect(470,667,320,54),started?"装备并重新开局":"确认兵器",true))ApplyArmory();
        if(Button(new Rect(810,667,320,54),"取消 · 保留当前选择"))CloseArmory();
        Label(new Rect(275,729,1050,25),"W 打开 / 关闭兵器选择    V 查看刀刃、斧头、锤头与枪头的实际碰撞形状",14,ink,TextAnchor.MiddleCenter);
    }
    void DrawWeaponChoices(float x,string title,ref int selected,Color color) {
        Label(new Rect(x,317,500,27),title,21,color,TextAnchor.MiddleCenter);
        for(int i=0;i<DuelWeapon.All.Length;i++) {
            DuelWeapon w=DuelWeapon.All[i];Rect r=new Rect(x+i%3*166,350+i/3*78,158,68);
            Fill(r,new Color(color.r,color.g,color.b,i==selected?.19f:.065f));
            float scale=44/w.Length;
            Vector2 origin=new Vector2(r.x+16,r.y+54-w.tail*scale);
            WeaponArt(w,p=>origin+new Vector2(p.x,-p.y)*scale,scale);
            Label(new Rect(r.x+34,r.y+4,122,29),w.name+(i==selected?"·选":""),17,color);
            Label(new Rect(r.x+34,r.y+32,122,27),(w.TwoHanded?"双手":"单手")+" · 起手 "+w.cost,13,ink);
            if(GUI.Button(r,"",button))selected=i;
        }
        var chosen=DuelWeapon.All[selected];
        Label(new Rect(x,589,500,28),chosen.description,17,color,TextAnchor.MiddleCenter);
        Label(new Rect(x,620,500,25),chosen.GripLabel+"    重量 "+chosen.mass.ToString("0.00")+"    挥速 ×"+chosen.handling.ToString("0.00")+"    起手气力 "+chosen.cost,14,ink,TextAnchor.MiddleCenter);
    }
}
