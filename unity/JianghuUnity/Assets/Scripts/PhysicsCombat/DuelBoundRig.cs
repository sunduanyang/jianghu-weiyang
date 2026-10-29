using System.Collections.Generic;
using Jianghu;
using UnityEngine;

// 切磋时用「系统 · 关节绑定」做好的角色。这里只负责把物理骨架报告成「每个部位的骨骼两端」，
// 摆放与绘制由 RigPoseRenderer 完成：每块起点放在关节上，沿骨骼方向旋转，全身一个比例，不拉伸。
// 碰撞、质量和伤害仍由原物理骨架决定。
//
// 换物理引擎（例如 Physics Core 2D）时，只要 Limb 仍提供 A、B、length，并改写下面的 AnkleWorld，
// 这个文件和整套绑定就不必再动。
public sealed partial class PhysicsDuel {
    public sealed partial class Fighter {
        RigBinding boundRig;bool boundRigChecked;float boundRigScale;
        public bool UsesBoundRig=>boundRig!=null;
        public float BoundRigMetersPerPixel=>boundRigScale;
        public RigBinding BoundRig=>boundRig;

        bool TryDrawBoundRig(bool debug){
            if(!boundRigChecked){
                boundRigChecked=true;boundRig=RigLibrary.ForDuel(human);
                if(boundRig!=null){boundRigScale=RigPoseRenderer.FitScale(boundRig,BoundSample);if(!(boundRigScale>0))boundRig=null;}
            }
            if(boundRig==null)return false;
            bool sameAsPlayer=!human&&RigLibrary.Opponent==RigLibrary.Active;
            Color tint=game.Clock<hitTint?new Color(1,.62f,.52f):sameAsPlayer?new Color(.84f,.9f,1f):Color.white;
            if(shield!=null&&HasShield)DrawWeapon(shield,true);
            RigPoseRenderer.Draw(boundRig,BoundSample,ScreenPoint,boundRigScale,Unit,facing<0,tint,Vector2.zero,
                weapon.id!="unarmed"?(System.Action)(()=>DrawWeapon(sword,false)):null);
            if(debug){
                foreach(var l in limbs.Values){game.Line(ScreenPoint(l.A),ScreenPoint(l.B),1,new Color(.1f,.65f,.61f,.8f));game.Circle(ScreenPoint(l.A),Vector2.one*5,new Color(.7f,.15f,.1f));}
                foreach(var collider in colliders)DrawCollider(collider);
            }
            return true;
        }
        // 每个部位的骨骼两端（世界坐标，米，y 向上）。
        RigBoneSample? BoundSample(string id){
            switch(id){
                case "torso":return new RigBoneSample(pelvis.B,torso.B);
                case "neck":return new RigBoneSample(torso.B,head.A);
                case "head":
                    // 头仍连着躯干时从颈根起；被砍下后跟随头本身。
                    if(Vector2.Distance(torso.B,head.A)>=.2f)return new RigBoneSample(head.A,head.B);
                    return new RigBoneSample(boundRig!=null&&boundRig.Usable("neck")?head.A:torso.B,head.B);
                case "footNear":case "footFar":{
                    if(!limbs.TryGetValue(id,out var foot))return null;
                    Vector2 ankle=AnkleWorld(foot);return new RigBoneSample(ankle,ankle+(foot.B-foot.A),true);
                }
                default:
                    return limbs.TryGetValue(id,out var limb)?new RigBoneSample(limb.A,limb.B):(RigBoneSample?)null;
            }
        }
        // 脚踝的世界位置：脚与小腿之间关节的锚点。这是绑定代码里唯一直接读物理关节的地方。
        Vector2 AnkleWorld(Limb foot)=>foot.joint!=null?Limb.BodyPoint(foot.rb,foot.joint.anchor):foot.A;

        // 比例检查：各骨长原画换算后与物理骨长的相对差。
        public Dictionary<string,float> BoundRigMismatch(){
            var r=new Dictionary<string,float>();if(boundRig==null)return r;
            foreach(var id in RigPoseRenderer.FitBones){
                if(!boundRig.Usable(id))continue;var s=BoundSample(id);if(!s.HasValue)continue;
                float phys=Vector2.Distance(s.Value.A,s.Value.B);if(phys>0)r[id]=boundRig.BoneLength(id)*boundRigScale/phys-1;
            }
            return r;
        }
        public Dictionary<string,float> BoundRigSeams()=>boundRig==null?new Dictionary<string,float>():RigPoseRenderer.MeasureSeams(boundRig,BoundSample,boundRigScale,facing<0);
    }
}
