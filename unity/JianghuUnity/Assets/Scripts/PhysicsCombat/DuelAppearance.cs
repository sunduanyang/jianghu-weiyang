using Jianghu;
using UnityEngine;

public sealed partial class PhysicsDuel {
    public sealed partial class Fighter {
        // Follow the actual physical segments. This also supports the additional
        // spine segments and detached limbs of the reference combat rig, without
        // changing its masses, colliders, drives or damage calculations.
        void DrawSegmentAppearance(bool debug){
            game.Circle(ScreenPoint(new Vector2(Hip.x,.01f)),new Vector2(145,16),new Color(.1f,.17f,.14f,.12f));
            if(shield!=null&&HasShield)DrawWeapon(shield,true);
            float drawingScale=head.length/(float)skin["parts"]["head-neck"]["height"].N;
            if(head.joint!=null&&head.joint.enabled)game.Line(ScreenPoint(torso.B),ScreenPoint(head.A),.11f*Unit,elbowTone);
            foreach(J part in parts){
                if(part["source"].S.Contains("prop")||part==skin["parts"]["prop"])continue;
                Vector2 anchor;float angle,fit;
                if(part["bone"].Has){
                    string name=part["bone"].S;Limb limb=limbs[name];
                    Vector2 a=ScreenPoint(name=="torso"?pelvis.B:limb.A),b=ScreenPoint(limb.B);
                    Vector2 source=Point(part["tip"])-Point(part["pivot"]);if(part["mirror"].B)source.x=-source.x;if(facing<0)source.x=-source.x;
                    anchor=a;angle=Mathf.Atan2((b-a).y,(b-a).x)*Mathf.Rad2Deg-Mathf.Atan2(source.y,source.x)*Mathf.Rad2Deg;fit=(b-a).magnitude/source.magnitude;
                    if(name.StartsWith("upper"))game.Circle(ScreenPoint(limb.B),Vector2.one*.12f*Unit,elbowTone);
                }else{
                    string follow=part["follow"].S;
                    Limb limb=part==skin["parts"]["head-neck"]?head:follow.StartsWith("hand")||follow.StartsWith("foot")?limbs[follow]:pelvis;
                    anchor=ScreenPoint(limb==head?head.A:follow.StartsWith("hand")?limb.A:follow.StartsWith("foot")?limb.rb.position:pelvis.B);
                    fit=(float)part["height"].N/(float)part["size"][1].N*drawingScale*Unit;
                    angle=limb==head?-head.rb.rotation:follow.StartsWith("hand")?-limb.rb.rotation-180:follow.StartsWith("foot")?-limb.rb.rotation-facing*90:-pelvis.rb.rotation;
                    if(limb==head)fit=head.length/Mathf.Max(1,Point(part["pivot"]).y)*Unit;
                }
                DrawPart(part,anchor,angle,fit);
            }
            if(weapon.id!="unarmed")DrawWeapon(sword,false);
            if(weapon.TwoHanded)foreach(string key in new[]{"far-hand","near-hand"}){
                J hand=skin["parts"][key];Limb limb=limbs[key=="near-hand"?"handNear":"handFar"];
                DrawPart(hand,ScreenPoint(limb.A),-limb.rb.rotation-180,(float)hand["height"].N/(float)hand["size"][1].N*drawingScale*Unit);
            }
            if(debug)foreach(var collider in colliders)DrawCollider(collider);
        }
    }
}
