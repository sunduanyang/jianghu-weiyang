using System;
using System.Collections.Generic;
using System.Linq;
using Jianghu;
using UnityEngine;

public sealed partial class PhysicsDuel {
    public sealed partial class Fighter {
        float artPreviewX;
        bool artPreviewReady;
        public void PrepareArtPreview(){
            if(!ModularTrial||!Reference||!game.paused)throw new InvalidOperationException("Art preview requires a paused modular workshop");
            if(puppet==null)puppet=new ModularRig(this);
            artPreviewX=Hip.x;artPreviewReady=true;
        }
        static Vector2 ArtRotate(Vector2 value,float angle){float a=angle*Mathf.Deg2Rad;return new Vector2(value.x*Mathf.Cos(a)-value.y*Mathf.Sin(a),value.x*Mathf.Sin(a)+value.y*Mathf.Cos(a));}
        static void ArtBody(Rigidbody2D body,Vector2 point,float rotation){
            body.position=point;body.rotation=rotation;body.linearVelocity=Vector2.zero;body.angularVelocity=0;
            // A paused scene has no interpolation step. Keep the render transform
            // and the actual bone endpoints in the same pose immediately.
            body.transform.SetPositionAndRotation(point,Quaternion.Euler(0,0,rotation));
        }
        static void ArtSegment(Limb bone,Vector2 start,Vector2 end){
            float angle=Angle(end-start);Vector2 position=(start+end)*.5f;
            if(bone.frame!=null&&bone.frame!=bone.rb.transform){angle-=bone.frame.localEulerAngles.z;position-=ArtRotate(bone.frame.localPosition,angle);}
            ArtBody(bone.rb,position,angle);
        }
        static Vector2 ArtAnchor(Limb bone)=>Limb.BodyPoint(bone.joint.connectedBody,bone.joint.connectedAnchor);
        static float ArtEase(float t){t=Mathf.Clamp01(t);return t*t*(3-2*t);}
        public void PoseArtPreview(float seconds,float walking,float raising){
            if(!artPreviewReady||!game.paused)throw new InvalidOperationException("Do not pose a live combat skeleton");
            float phase=seconds*Mathf.PI*2/1.9f;
            float lift=(.5f-.5f*Mathf.Cos(seconds*Mathf.PI*2/4.8f))*raising;
            var belly=referenceBones["belly"];var neck=referenceBones["neck"];
            Vector2 waist=new Vector2(artPreviewX,.985f-.006f*(1-Mathf.Cos(phase*2))*walking);
            ArtSegment(pelvis,waist-Vector2.up*pelvis.length,waist);
            ArtSegment(belly,pelvis.B,pelvis.B+Vector2.up*belly.length);
            ArtSegment(torso,belly.B,belly.B+Vector2.up*torso.length);
            ArtSegment(neck,torso.B,torso.B+Vector2.up*neck.length);
            ArtSegment(head,neck.B,neck.B+Vector2.up*head.length);
            foreach(string side in new[]{"Near","Far"}){
                bool near=side=="Near";var upper=limbs["upper"+side];var fore=limbs["forearm"+side];var hand=limbs["hand"+side];
                float swing=(near?-1:1)*Mathf.Sin(phase)*.26f*walking;
                float reach=lift*(near?1:.75f);
                float ua=.12f+swing+reach*1.25f,fa=ua+.33f+reach*.32f;
                Vector2 shoulder=ArtAnchor(upper),elbow=shoulder+new Vector2(facing*Mathf.Sin(ua),-Mathf.Cos(ua))*upper.length;
                Vector2 wrist=elbow+new Vector2(facing*Mathf.Sin(fa),-Mathf.Cos(fa))*fore.length;
                ArtSegment(upper,shoulder,elbow);ArtSegment(fore,elbow,wrist);ArtSegment(hand,wrist,wrist+new Vector2(facing*Mathf.Sin(fa),-Mathf.Cos(fa))*hand.length);
                var thigh=limbs["thigh"+side];var shin=limbs["shin"+side];var foot=limbs["foot"+side];
                float cycle=Mathf.Repeat(seconds/1.9f+(near?0:.5f),1),stepX,stepY;
                if(cycle<.65f){stepX=Mathf.Lerp(.10f,-.10f,ArtEase(cycle/.65f));stepY=0;}
                else {float t=(cycle-.65f)/.35f;stepX=Mathf.Lerp(-.10f,.10f,ArtEase(t));stepY=Mathf.Sin(t*Mathf.PI)*.045f;}
                Vector2 hip=ArtAnchor(thigh),idle=new Vector2(artPreviewX+facing*(near?.075f:-.075f),.13f);
                Vector2 goal=Vector2.Lerp(idle,new Vector2(artPreviewX+facing*stepX,.13f+stepY),walking);
                Solve(hip,goal,thigh.length,shin.length,facing,out var knee,out var ankle,.001f);
                ArtSegment(thigh,hip,knee);ArtSegment(shin,knee,ankle);
                float footAngle=facing>0?-90:90;ArtBody(foot.rb,ankle-ArtRotate(foot.joint.anchor,footAngle),footAngle);
            }
        }
        public Dictionary<string,float> ArtAngles()=>referenceBones.ToDictionary(p=>p.Key,p=>p.Value.rb.rotation);
        public float ArtLowestAnkle=>Mathf.Min(limbs["shinNear"].B.y,limbs["shinFar"].B.y);
        public float ArtTorsoLean=>Mathf.Abs(Mathf.DeltaAngle(torso.rb.rotation,0));
    }
}
