using System.Collections.Generic;
using Jianghu;
using UnityEngine;

// 切磋时用「系统 · 关节绑定」做好的角色。每块拆件的起点放在对应物理骨骼的关节上，
// 按骨骼方向旋转，全身只用一个比例，不拉伸。碰撞、质量和伤害仍由原物理骨架决定。
public sealed partial class PhysicsDuel {
    public sealed partial class Fighter {
        RigBinding boundRig;bool boundRigChecked;float boundRigScale;
        public bool UsesBoundRig=>boundRig!=null;
        public float BoundRigMetersPerPixel=>boundRigScale;

        bool TryDrawBoundRig(bool debug){
            if(!boundRigChecked){boundRigChecked=true;boundRig=RigLibrary.ForDuel(human);if(boundRig!=null&&!FitBoundRig())boundRig=null;}
            if(boundRig==null)return false;
            bool mirror=facing<0;float pixel=boundRigScale*Unit;   // 屏幕像素 / 原画像素
            Color tint=game.Clock<hitTint?new Color(1,.62f,.52f):!human&&RigLibrary.EnemyToo?new Color(.84f,.9f,1f):Color.white;
            if(shield!=null&&HasShield)DrawWeapon(shield,true);
            // 衣摆与躯干一起转动，所以先求出躯干的转角。
            Vector2 waist=ScreenPoint(pelvis.B);float torsoTurn=BoundTurn("torso",waist,ScreenPoint(torso.B),false,mirror);
            foreach(string id in boundRig.Order){
                if(!boundRig.Usable(id))continue;var piece=boundRig.SlotPiece(id);if(piece?.Tex==null)continue;
                if(id=="skirtFront"||id=="skirtBack"){
                    RigPuppet.DrawPiece(piece.Tex,waist,Vector2.zero,torsoTurn,boundRig.Point(id,"pivot"),pixel,mirror,tint);continue;
                }
                if(!BoundBone(id,out Vector2 a,out Vector2 b,out bool sole))continue;
                Vector2 sa=ScreenPoint(a);float turn=BoundTurn(id,sa,ScreenPoint(b),sole,mirror);
                RigPuppet.DrawPiece(piece.Tex,sa,Vector2.zero,turn,boundRig.Point(id,"pivot"),pixel,mirror,tint);
                if(id=="handNear"&&weapon.id!="unarmed"){DrawWeapon(sword,false);RigPuppet.DrawPiece(piece.Tex,sa,Vector2.zero,turn,boundRig.Point(id,"pivot"),pixel,mirror,tint);}
            }
            if(!boundRig.Usable("handNear")&&weapon.id!="unarmed")DrawWeapon(sword,false);
            if(debug){
                foreach(var l in limbs.Values){game.Line(ScreenPoint(l.A),ScreenPoint(l.B),1,new Color(.1f,.65f,.61f,.8f));game.Circle(ScreenPoint(l.A),Vector2.one*5,new Color(.7f,.15f,.1f));}
                foreach(var collider in colliders)DrawCollider(collider);
            }
            return true;
        }
        // 屏幕上骨骼方向减去原画方向，就是这块要转的角度。镜像时原画方向左右翻转。
        float BoundTurn(string id,Vector2 screenA,Vector2 screenB,bool sole,bool mirror){
            float screen=Mathf.Atan2(screenB.y-screenA.y,screenB.x-screenA.x)*Mathf.Rad2Deg;
            float drawn=sole?0:boundRig.DrawnAngle(id);if(mirror)drawn=180-drawn;
            return screen-drawn;
        }
        // 每个部位对应的物理骨骼两端（世界坐标，米，y 向上）。sole=true 表示脚：沿脚底方向摆放。
        bool BoundBone(string id,out Vector2 a,out Vector2 b,out bool sole){
            a=b=Vector2.zero;sole=false;
            switch(id){
                case "torso":a=pelvis.B;b=torso.B;return true;
                case "neck":a=torso.B;b=head.A;return true;
                case "head":
                    // 头仍连着躯干时从颈根起；被砍下后跟随头本身。
                    a=Vector2.Distance(torso.B,head.A)<.2f?(boundRig.Usable("neck")?head.A:torso.B):head.A;b=head.B;return true;
                case "footNear":case "footFar":{
                    if(!limbs.TryGetValue(id,out var foot))return false;
                    a=foot.joint!=null?Limb.BodyPoint(foot.rb,foot.joint.anchor):foot.A;
                    b=a+(foot.B-foot.A);sole=true;return true;
                }
                default:{
                    if(!limbs.TryGetValue(id,out var limb))return false;
                    a=limb.A;b=limb.B;return true;
                }
            }
        }
        // 用最小二乘求「米 / 原画像素」，让原画骨长整体最接近物理骨长。
        bool FitBoundRig(){
            double num=0,den=0;
            void Add(string id,float physical){if(!boundRig.Usable(id)||physical<=0)return;double art=boundRig.BoneLength(id);num+=art*physical;den+=art*art;}
            Add("torso",Vector2.Distance(pelvis.B,torso.B));
            foreach(string side in new[]{"Near","Far"})foreach(string part in new[]{"upper","forearm","thigh","shin"})
                if(limbs.TryGetValue(part+side,out var l))Add(part+side,l.length);
            if(den<=0)return false;
            boundRigScale=(float)(num/den);return boundRigScale>0&&!float.IsNaN(boundRigScale);
        }
        // 比例检查：各骨长原画换算后与物理骨长的相对差。
        public Dictionary<string,float> BoundRigMismatch(){
            var r=new Dictionary<string,float>();if(boundRig==null)return r;
            foreach(string side in new[]{"Near","Far"})foreach(string part in new[]{"upper","forearm","thigh","shin"})
                if(boundRig.Usable(part+side)&&limbs.TryGetValue(part+side,out var l))r[part+side]=boundRig.BoneLength(part+side)*boundRigScale/l.length-1;
            return r;
        }
    }
}
