using System;
using System.Collections.Generic;
using System.Linq;
using Jianghu;
using UnityEngine;

// Opt-in artwork trial. It only draws original detached components over the
// current physical bodies; no masses, colliders, controls or saves are changed.
public sealed partial class PhysicsDuel {
    public static bool LaceTrial=>GameClient.Arg("-puppetArtTrial").Length>0;
    public void BeginLaceAcceptance(){
        if(!LaceTrial||GameClient.Arg("-puppetTrialSmoke").Length==0)throw new InvalidOperationException("The lace acceptance flag is required");
        smoke=true;started=true;training=true;muted=true;player.testOverride=enemy.testOverride=true;
    }
    public void LaceAcceptanceHands(Vector2 primary,Vector2 secondary){
        if(!LaceTrial||GameClient.Arg("-puppetTrialSmoke").Length==0)throw new InvalidOperationException("The lace acceptance flag is required");
        if(!controls.right.captured)controls.Begin(9111,new Vector2(1235,655));
        if(!controls.left.captured)controls.Begin(9112,new Vector2(365,655));
        controls.Move(9111,controls.right.center+new Vector2(primary.x,-primary.y)*DuelControls.Pad.Radius);
        controls.Move(9112,controls.left.center+new Vector2(secondary.x,-secondary.y)*DuelControls.Pad.Radius);
    }
    public sealed partial class Fighter {
        static J laceCatalog;
        static Texture2D laceAtlas;
        static List<J> laceParts;
        public static double LaceAnchorError {get;private set;}
        public static int LaceAnchorSamples {get;private set;}
        public static void ResetLaceProbe(){LaceAnchorError=0;LaceAnchorSamples=0;}
        public static J LaceProbe()=>J.O("samples",LaceAnchorSamples,"max_anchor_error_pixels",LaceAnchorError);
        void LaceResources(){
            if(laceCatalog!=null)return;
            var text=Resources.Load<TextAsset>("PuppetTrial/index");laceAtlas=Resources.Load<Texture2D>("PuppetTrial/atlas");
            if(text==null||laceAtlas==null)throw new InvalidOperationException("The carved puppet trial resources are missing");
            laceCatalog=J.Parse(text.text);laceParts=laceCatalog["parts"].A.OrderBy(p=>p["layer"].N).ToList();
            if(laceParts.Count!=15)throw new InvalidOperationException("The trial requires 15 detached anatomical components");
        }
        bool TryDrawLacePuppet(bool debug){if(!LaceTrial||!Reference)return false;DrawLacePuppet(debug,true);return true;}
        public void DrawLacePuppet(bool debug,bool equipment){
            LaceResources();
            if(head.joint!=null&&head.joint.enabled)game.Line(ScreenPoint(torso.B),ScreenPoint(head.A),.085f*Unit,new Color(.98f,.87f,.67f));
            if(equipment&&shield!=null&&HasShield)DrawWeapon(shield,true);
            foreach(J part in laceParts)DrawLacePart(part);
            if(equipment&&weapon.id!="unarmed"){
                DrawWeapon(sword,false);
                // Both hands remain visibly around their actual weapon grips.
                DrawLacePart(laceParts.First(p=>p["id"].S=="near-hand"));
                if(weapon.TwoHanded)DrawLacePart(laceParts.First(p=>p["id"].S=="far-hand"));
            }
            if(debug){
                foreach(var limb in referenceBones.Values){game.Line(ScreenPoint(limb.A),ScreenPoint(limb.B),1,new Color(.08f,.63f,.6f,.9f));game.Circle(ScreenPoint(limb.A),Vector2.one*4,new Color(.88f,.36f,.12f));}
            }
        }
        void DrawLacePart(J part){
            string name=part["bone"].S;
            Vector2 a,b,heel=Vector2.zero;
            if(name=="spine"){a=pelvis.B;b=torso.B;}
            else if(name=="pelvis"){a=pelvis.B;b=pelvis.A;}
            else {
                Limb limb=limbs[name];a=limb.A;b=limb.B;
                if(name.StartsWith("foot")){a=Limb.BodyPoint(limb.rb,limb.joint.anchor);heel=limb.A;}
            }
            Vector2 target=ScreenPoint(a),delta=ScreenPoint(b)-target;
            Vector2 pivot=Point(part["pivot"]),tip=Point(part["tip"]),size=Point(part["size"]),sourceHeel=Point(part["heel_pivot"]);
            Rect uv=new Rect((float)part["uv"][0].N,(float)part["uv"][1].N,(float)part["uv"][2].N,(float)part["uv"][3].N);
            if(facing<0){pivot.x=size.x-pivot.x;tip.x=size.x-tip.x;sourceHeel.x=size.x-sourceHeel.x;uv.x+=uv.width;uv.width=-uv.width;}
            Vector2 source=tip-pivot;
            Matrix4x4 affine=Matrix4x4.identity;
            if(part["heel_pivot"].Has){
                Vector2 h=sourceHeel-pivot,t=ScreenPoint(heel)-target;
                float det=source.x*h.y-h.x*source.y;
                if(Mathf.Abs(det)<.001f)throw new InvalidOperationException("Foot calibration must have three non-collinear anchors");
                affine.m00=(delta.x*h.y-t.x*source.y)/det;affine.m01=(t.x*source.x-delta.x*h.x)/det;
                affine.m10=(delta.y*h.y-t.y*source.y)/det;affine.m11=(t.y*source.x-delta.y*h.x)/det;
            }else{
                Vector2 q=new Vector2(-source.y,source.x),tq=new Vector2(-delta.y,delta.x)*(float)part["breadth"].N;
                float square=source.sqrMagnitude;
                affine.m00=(delta.x*source.x+tq.x*q.x)/square;affine.m01=(delta.x*source.y+tq.x*q.y)/square;
                affine.m10=(delta.y*source.x+tq.y*q.x)/square;affine.m11=(delta.y*source.y+tq.y*q.y)/square;
            }
            affine.m03=target.x-affine.m00*pivot.x-affine.m01*pivot.y;
            affine.m13=target.y-affine.m10*pivot.x-affine.m11*pivot.y;
            Matrix4x4 prior=GUI.matrix;Color color=GUI.color;
            try{
                GUI.matrix=prior*affine;GUI.color=game.Clock<hitTint?new Color(1,.72f,.65f):Color.white;
                GUI.DrawTextureWithTexCoords(new Rect(0,0,size.x,size.y),laceAtlas,uv,true);
                if(Event.current.type==EventType.Repaint){
                    LaceAnchorError=Math.Max(LaceAnchorError,Vector2.Distance(GUI.matrix.MultiplyPoint3x4(pivot),prior.MultiplyPoint3x4(target)));
                    LaceAnchorError=Math.Max(LaceAnchorError,Vector2.Distance(GUI.matrix.MultiplyPoint3x4(tip),prior.MultiplyPoint3x4(ScreenPoint(b))));
                    LaceAnchorSamples+=2;
                    if(part["heel_pivot"].Has){LaceAnchorError=Math.Max(LaceAnchorError,Vector2.Distance(GUI.matrix.MultiplyPoint3x4(sourceHeel),prior.MultiplyPoint3x4(ScreenPoint(heel))));LaceAnchorSamples++;}
                }
            }finally{GUI.matrix=prior;GUI.color=color;}
        }
    }
}
