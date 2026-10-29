using System;
using System.Linq;
using Jianghu;
using UnityEngine;

public sealed partial class PhysicsDuel {
    public sealed partial class Fighter {
        public void Draw(bool debug) {
            if(TryDrawCharacterModules(debug))return;
            if(TryDrawModularPuppet(debug))return;
            if(TryDrawLacePuppet(debug))return;
            if(skin["appearance_id"].Has){DrawSegmentAppearance(debug);return;}
            if(Reference){DrawReference(debug);return;}
            game.Circle(ScreenPoint(new Vector2(Hip.x,.01f)),new Vector2(145,16),new Color(.1f,.17f,.14f,.12f));
            Vector2[] trace=trail.ToArray();for(int i=1;i<trace.Length;i++)game.Line(ScreenPoint(trace[i-1]),ScreenPoint(trace[i]),3,new Color(.6f,.31f,.16f,.025f*i));
            if(shield!=null&&HasShield)DrawWeapon(shield,true);
            foreach(J part in parts) {
                if(part["source"].S.Contains("prop")||part==skin["parts"]["prop"])continue;
                // A small skin bridge sits beneath the two sleeve pieces. It
                // closes the bend without adding a visible joint fixture.
                if(part["bone"].S.StartsWith("upper")) {
                    string side=part["bone"].S.Contains("Near")?"Near":"Far";
                    game.Circle(ScreenPoint(Joint("elbow"+side)),Vector2.one*12,elbowTone);
                }
                Vector2 anchor;float angle,fit;
                if(part["bone"].Has) {
                    J bone=rig["bones"][part["bone"].S];Vector2 a=ScreenPoint(Joint(bone[0].S)),b=ScreenPoint(Joint(bone[1].S));
                    Vector2 source=Point(part["tip"])-Point(part["pivot"]);if(part["mirror"].B)source.x=-source.x;if(facing<0)source.x=-source.x;
                    anchor=a;angle=Mathf.Atan2((b-a).y,(b-a).x)*Mathf.Rad2Deg-Mathf.Atan2(source.y,source.x)*Mathf.Rad2Deg;fit=(b-a).magnitude/source.magnitude;
                } else {
                    anchor=ScreenPoint(Joint(part["joint"].S));fit=(float)part["height"].N/(float)part["size"][1].N*scale*Unit;
                    if(part==skin["parts"]["head-neck"]){fit=(float)part["height"].N/Point(part["pivot"]).y*scale*Unit;angle=-head.rb.rotation;}
                    else if(part["follow"].S.Contains("hand"))angle=-limbs[part["follow"].S.Contains("Near")?"handNear":"handFar"].rb.rotation-180;
                    else if(part["follow"].S.Contains("foot"))angle=-limbs[part["follow"].S.Contains("Near")?"footNear":"footFar"].rb.rotation-facing*90;
                    else angle=-pelvis.rb.rotation;
                }
                DrawPart(part,anchor,angle,fit);
            }
            if(weapon.id!="unarmed")DrawWeapon(sword,false);
            if(weapon.TwoHanded) {
                foreach(string key in new[]{"far-hand","near-hand"}) {
                    J hand=skin["parts"][key];Limb limb=limbs[key=="near-hand"?"handNear":"handFar"];
                    float fit=(float)hand["height"].N/(float)hand["size"][1].N*scale*Unit;
                    DrawPart(hand,ScreenPoint(limb.A),-limb.rb.rotation-180,fit);
                }
            }
            if(debug) {
                foreach(var l in limbs.Values){game.Line(ScreenPoint(l.A),ScreenPoint(l.B),1,new Color(.1f,.65f,.61f,.8f));game.Circle(ScreenPoint(l.A),Vector2.one*5,new Color(.7f,.15f,.1f));}
                foreach(var c in colliders)DrawCollider(c);
                if(supportJoint!=null&&supportJoint.enabled)game.Line(ScreenPoint(limbs["handFar"].B),ScreenPoint(SupportGripPoint),3,SupportGripError<.03f?Color.green:Color.red);
                foreach(var f in feet.Values){Color c=f.state==FootPlant.Phase.Planted?new Color(.1f,.55f,.3f,.9f):f.state==FootPlant.Phase.Landing?new Color(.8f,.5f,.1f,.9f):new Color(.1f,.5f,.8f,.9f);game.Circle(ScreenPoint(f.goal),Vector2.one*9,c);game.Line(ScreenPoint(f.anchor),ScreenPoint(f.anchor+Vector2.up*.15f),2,c);}
            }
        }
        void DrawCollider(Collider2D c) {
            if(c==null)return;
            Color color=c.gameObject.layer==10||c.gameObject.layer==15||c.gameObject.layer==16||c.gameObject.layer==17?new Color(.85f,.23f,.10f,.85f):new Color(.1f,.48f,.6f,.7f);
            void Outline(Vector2[] path){for(int i=0;i<path.Length;i++)game.Line(ScreenPoint(c.transform.TransformPoint(path[i])),ScreenPoint(c.transform.TransformPoint(path[(i+1)%path.Length])),1.5f,color);}
            Vector2[] points;
            if(c is PolygonCollider2D p){for(int path=0;path<p.pathCount;path++)Outline(p.GetPath(path).Select(point=>point+p.offset).ToArray());return;}
            else if(c is BoxCollider2D b){Vector2 h=b.size/2;points=new[]{b.offset+new Vector2(-h.x,-h.y),b.offset+new Vector2(h.x,-h.y),b.offset+h,b.offset+new Vector2(-h.x,h.y)};}
            else if(c is CapsuleCollider2D cap){bool vertical=cap.direction==CapsuleDirection2D.Vertical;float r=Mathf.Min(cap.size.x,cap.size.y)/2,half=Mathf.Max(0,(vertical?cap.size.y:cap.size.x)/2-r);points=new Vector2[24];for(int i=0;i<points.Length;i++){float a=i*Mathf.PI*2/points.Length,x=Mathf.Cos(a),y=Mathf.Sin(a);points[i]=cap.offset+new Vector2(x*r+(vertical?0:x>=0?half:-half),y*r+(vertical?y>=0?half:-half:0));}}
            else if(c is CircleCollider2D circle){points=new Vector2[24];for(int i=0;i<points.Length;i++){float angle=i*Mathf.PI*2/points.Length;points[i]=circle.offset+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*circle.radius;}}
            else return;
            Outline(points);
        }
        void DrawPart(J part,Vector2 anchor,float angle,float fit) {
            Matrix4x4 old=GUI.matrix;Color oldColor=GUI.color;
            Vector2 pivot=Point(part["pivot"]),size=Point(part["size"]);Rect uv=new Rect((float)part["uv"][0].N,(float)part["uv"][1].N,(float)part["uv"][2].N,(float)part["uv"][3].N);
            if(part["bone"].S.StartsWith("upper")) {
                // The source sleeve has a triangular skin tab beyond its
                // calibrated elbow. That tab should not protrude at bends.
                float end=Mathf.Min(size.y,Point(part["tip"]).y+2);
                float kept=end/size.y;uv.y+=uv.height*(1-kept);uv.height*=kept;size.y=end;
            }
            if(part["mirror"].B){pivot.x=size.x-pivot.x;uv.x+=uv.width;uv.width=-uv.width;}
            if(facing<0){pivot.x=size.x-pivot.x;uv.x+=uv.width;uv.width=-uv.width;}
            float breadth=part["breadth"].Has?(float)part["breadth"].N:1;
            Matrix4x4 bind=Matrix4x4.identity;
            if(part["bone"].Has) {
                Vector2 axis=Point(part["tip"])-Point(part["pivot"]);
                if(part["mirror"].B)axis.x=-axis.x;if(facing<0)axis.x=-axis.x;axis.Normalize();
                Vector2 q=new Vector2(-axis.y,axis.x);
                float shear=(float)part["shear"].N*(facing<0?-1:1);
                bind.m00=axis.x*axis.x+breadth*q.x*q.x+shear*axis.x*q.x;
                bind.m01=axis.x*axis.y+breadth*q.x*q.y+shear*axis.x*q.y;
                bind.m10=axis.y*axis.x+breadth*q.y*q.x+shear*axis.y*q.x;
                bind.m11=axis.y*axis.y+breadth*q.y*q.y+shear*axis.y*q.y;
            } else bind.m00=breadth;
            GUI.matrix=old*Matrix4x4.Translate(anchor)*Matrix4x4.Rotate(Quaternion.Euler(0,0,angle))*bind*Matrix4x4.Translate(-anchor);
            GUI.color=game.Clock<hitTint?new Color(1,.62f,.52f):Color.white;
            GUI.DrawTextureWithTexCoords(new Rect(anchor.x-pivot.x*fit,anchor.y-pivot.y*fit,size.x*fit,size.y*fit),PaperAppearanceArt.PartAtlas(skin,part,atlas),uv,true);
            GUI.color=oldColor;GUI.matrix=old;
        }
        void DrawWeapon(Limb l,bool isShield) {
            Vector2 a=ScreenPoint(l.A),b=ScreenPoint(l.B),axis=(b-a).normalized,normal=new Vector2(-axis.y,axis.x);
            if(isShield) {
                Vector2 center=(a+b)/2;game.Line(a,b,32,new Color(.18f,.24f,.21f));game.Line(a+axis*5,b-axis*5,25,new Color(.45f,.34f,.22f));
                game.Line(a+normal*8+axis*8,b+normal*8-axis*8,2,new Color(.72f,.59f,.37f));game.Line(a-normal*8+axis*8,b-normal*8-axis*8,2,new Color(.26f,.27f,.21f));
                game.Line(center-normal*13,center+normal*13,7,new Color(.23f,.29f,.24f));
            } else {
                game.WeaponArt(weapon,p=>ScreenPoint(l.WorldPoint(weapon.Local(p,facing))),Unit);
            }
        }
    }

}
