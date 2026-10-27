using System;
using UnityEngine;

// Distances are metres from the grip along local +Y. These definitions feed
// both the visible silhouette and the real damaging collider.
public sealed class DuelWeapon {
    public readonly string id,name,description,rangeLabel;
    public readonly float reach,tail,mass,cost,handling,damage,pressure,distance,hitStart;
    public readonly float supportGrip,readyAngle;
    public bool TwoHanded=>supportGrip>0;
    public string GripLabel=>TwoHanded?"双手":"单手配盾";
    public readonly Vector2[] head;
    public float Length=>reach+tail;
    public float HitFraction=>id=="sword"?.24f:(hitStart+tail)/Length;
    public Vector2 Local(Vector2 p,float facing)=>new Vector2(p.x*facing,p.y+tail-Length/2);
    DuelWeapon(string id,string name,string description,string rangeLabel,float reach,float tail,float mass,float cost,float handling,float damage,float pressure,float distance,float hitStart,Vector2[] head,float supportGrip=0,float readyAngle=75) {
        this.id=id;this.name=name;this.description=description;this.rangeLabel=rangeLabel;this.reach=reach;this.tail=tail;this.mass=mass;this.cost=cost;this.handling=handling;this.damage=damage;this.pressure=pressure;this.distance=distance;this.hitStart=hitStart;this.head=head;
        this.supportGrip=supportGrip;this.readyAngle=readyAngle;
    }
    static Vector2[] Points(params float[] coordinates){var p=new Vector2[coordinates.Length/2];for(int i=0;i<p.Length;i++)p[i]=new Vector2(coordinates[i*2],coordinates[i*2+1]);return p;}
    static Vector2[] MaceHead(){var p=new Vector2[16];for(int i=0;i<p.Length;i++){float a=i*Mathf.PI/8,r=i%2==0?.18f:.125f;p[i]=new Vector2(Mathf.Cos(a)*r,.72f+Mathf.Sin(a)*r);}return p;}
    public const int SwordIndex=2;
    public static readonly DuelWeapon[] All={
        new DuelWeapon("dagger","匕首","近身快攻，消耗低，单次伤害较轻。","极短",.58f,.12f,.28f,5,1.18f,.72f,.75f,1.18f,.12f,Points(-.035f,.12f,.035f,.12f,.045f,.40f,0,.58f,-.045f,.40f)),
        new DuelWeapon("sabre","短刀","轻快劈斩，距离与消耗较均衡。","中短",.97f,.14f,.52f,7,1.08f,.93f,.95f,1.65f,.15f,Points(-.03f,.15f,.06f,.18f,.09f,.74f,.025f,.97f,-.025f,.91f,-.035f,.40f)),
        new DuelWeapon("sword","单手剑","单手配盾，距离、挥速和冲击均衡。","中长",1.18f,.16f,.70f,9,1,1,1,1.95f,.14f,null),
        new DuelWeapon("axe","单手斧","斧头劈击强，压盾更重，收招较慢。","中短",.96f,.20f,1.05f,12,.82f,1.20f,1.30f,1.65f,.56f,Points(-.055f,.60f,.17f,.56f,.30f,.66f,.28f,.96f,.03f,.92f,-.055f,.78f)),
        new DuelWeapon("mace","钉头锤","锤头冲击重，压盾强，挥动费力。","中短",.90f,.18f,1.30f,14,.72f,1.15f,1.45f,1.58f,.54f,MaceHead()),
        new DuelWeapon("spear","长枪","双手分握长杆，以枪头远刺。","长",2,.30f,1,11,.86f,1.02f,.95f,2.70f,1.65f,Points(-.022f,1.65f,-.065f,1.74f,0,2,.065f,1.74f,.022f,1.65f),.44f,16),
        new DuelWeapon("greatsword","双手大剑","双手握长柄，挥斩有力，不能配盾。","长",1.55f,.20f,1.45f,13,.78f,1.18f,1.20f,2.25f,.44f,Points(-.055f,.44f,.055f,.44f,.05f,1.38f,0,1.55f,-.05f,1.38f),.22f,50),
        new DuelWeapon("greataxe","双手战斧","双手分担重斧，重劈与压杆。","中长",1.50f,.24f,2.15f,17,.62f,1.40f,1.55f,2.10f,1.02f,Points(-.08f,1.08f,.25f,1.02f,.42f,1.15f,.39f,1.50f,.04f,1.43f,-.10f,1.30f),.38f,45),
        new DuelWeapon("warhammer","双手战锤","双手抡击重锤，冲击强，回收较慢。","中长",1.35f,.24f,2.35f,18,.58f,1.42f,1.65f,2,1.03f,Points(-.25f,1.03f,.25f,1.03f,.25f,1.35f,-.25f,1.35f),.36f,45)
    };
}

public sealed partial class PhysicsDuel {
    public sealed partial class Fighter {
        void EquipWeapon() {
            Vector2 grip=limbs["handNear"].B,axis=weapon.TwoHanded?ReadyDirection:new Vector2(facing*.53f,.85f).normalized;
            sword=Make("sword",grip-axis*weapon.tail,grip+axis*weapon.reach,.075f,weapon.mass,"sword");
            sword.rb.gameObject.layer=10;
            sword.rb.linearDamping=.12f;sword.rb.angularDamping=.18f;
            primaryGrip=sword.rb.gameObject.AddComponent<FixedJoint2D>();
            primaryGrip.connectedBody=limbs["handNear"].rb;primaryGrip.autoConfigureConnectedAnchor=false;
            primaryGrip.anchor=sword.LocalPoint(grip);
            primaryGrip.connectedAnchor=Limb.BodyLocalPoint(limbs["handNear"].rb,grip);
            primaryGrip.frequency=0;primaryGrip.enableCollision=false;
            if(weapon.head==null) {
                var box=ReplaceBox(sword,new Vector2(.065f,1.03f),new Vector2(0,.145f));box.sharedMaterial=game.bladeMaterial;
            } else {
                RemoveShape(sword);var shape=sword.Frame.gameObject.AddComponent<PolygonCollider2D>();
                var points=new Vector2[weapon.head.Length];for(int i=0;i<points.Length;i++)points[i]=weapon.Local(weapon.head[i],facing);
                shape.points=points;shape.sharedMaterial=game.bladeMaterial;sword.shape=shape;colliders.Add(shape);
            }
            var shaft=sword.Frame.gameObject.AddComponent<BoxCollider2D>();
            float safeEnd=weapon.hitStart-.025f;
            shaft.size=new Vector2(weapon.id=="spear"?.045f:.075f,safeEnd+weapon.tail);
            shaft.offset=weapon.Local(new Vector2(0,(safeEnd-weapon.tail)/2),1);shaft.sharedMaterial=game.bladeMaterial;colliders.Add(shaft);
            if(weapon.id=="sword"||weapon.id=="dagger"||weapon.id=="sabre"||weapon.id=="greatsword") {
                var hilt=sword.Frame.gameObject.AddComponent<BoxCollider2D>();hilt.size=new Vector2(weapon.id=="dagger"?.17f:.24f,.045f);
                hilt.offset=weapon.Local(new Vector2(0,weapon.id=="greatsword"?.40f:.10f),1);hilt.sharedMaterial=game.bladeMaterial;colliders.Add(hilt);
            }
        }
    }
    // Scanline fill stays in the existing IMGUI canvas, including its scaling
    // and camera shake. No separate rendering/material path is needed.
    void WeaponPolygon(Vector2[] points,Color fill,Color outline) {
        float low=points[0].y,high=low;foreach(var p in points){low=Mathf.Min(low,p.y);high=Mathf.Max(high,p.y);}
        var crossings=new float[points.Length];
        for(float y=low;y<high;y+=1.25f) {
            int count=0;
            for(int i=0;i<points.Length;i++){Vector2 a=points[i],b=points[(i+1)%points.Length];if((a.y<=y&&b.y>y)||(b.y<=y&&a.y>y))crossings[count++]=a.x+(y-a.y)*(b.x-a.x)/(b.y-a.y);}
            Array.Sort(crossings,0,count);for(int i=0;i+1<count;i+=2)Fill(new Rect(crossings[i],y,crossings[i+1]-crossings[i],1.5f),fill);
        }
        for(int i=0;i<points.Length;i++)Line(points[i],points[(i+1)%points.Length],1.7f,outline);
    }
    void WeaponArt(DuelWeapon weapon,Func<Vector2,Vector2> project,float scale) {
        Color dark=new Color(.16f,.22f,.21f),steel=new Color(.77f,.81f,.76f),wood=new Color(.37f,.26f,.16f);
        Vector2 a=project(new Vector2(0,-weapon.tail)),b=project(new Vector2(0,weapon.reach));
        if(weapon.id=="sword") {
            Line(a,b,scale*.082f,dark);Line(project(new Vector2(0,.07f)),project(new Vector2(0,1.155f)),scale*.049f,steel);
            Line(project(new Vector2(.016f,.09f)),project(new Vector2(.016f,1.155f)),Mathf.Max(.8f,scale*.008f),Color.white);
        } else {
            Line(a,project(new Vector2(0,weapon.hitStart+.05f)),Mathf.Max(1,scale*(weapon.id=="spear"?.045f:.075f)),dark);
            Line(a,project(new Vector2(0,weapon.hitStart+.05f)),Mathf.Max(.8f,scale*(weapon.id=="spear"?.026f:.046f)),wood);
            var points=new Vector2[weapon.head.Length];for(int i=0;i<points.Length;i++)points[i]=project(weapon.head[i]);
            WeaponPolygon(points,weapon.id=="mace"?new Color(.52f,.59f,.56f):steel,dark);
            Line(project(new Vector2(0,weapon.hitStart+.035f)),project(new Vector2(0,weapon.reach-.045f)),Mathf.Max(.8f,scale*.012f),new Color(.92f,.92f,.82f));
        }
        if(weapon.id=="sword"||weapon.id=="dagger"||weapon.id=="sabre"||weapon.id=="greatsword") {
            float guardAt=weapon.id=="greatsword"?.40f:.10f;
            Line(project(new Vector2(-.12f,guardAt)),project(new Vector2(.12f,guardAt)),Mathf.Max(1,scale*.04f),new Color(.47f,.34f,.19f));
            Line(a,project(new Vector2(0,guardAt-.02f)),Mathf.Max(1,scale*.065f),wood);
        }
        if(weapon.TwoHanded) {
            foreach(float grip in new[]{0f,weapon.supportGrip})Line(project(new Vector2(0,grip-.055f)),project(new Vector2(0,grip+.055f)),Mathf.Max(1,scale*.083f),new Color(.24f,.21f,.15f));
        }
    }
}
