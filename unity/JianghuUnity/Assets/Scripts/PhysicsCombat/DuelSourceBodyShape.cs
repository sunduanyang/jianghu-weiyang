using System;
using System.Collections.Generic;
using Jianghu;
using UnityEngine;

// Numeric part of CharacterConfigApplier.ApplyGender -> Weight/Height/
// ShouldersWidth/WaistWidth/HipsWidth. Constants verified against ARM64 pools.
public static class DuelSourceBodyShape {
    public struct Values {
        public int gender;
        public float height,weight,shoulders,waist,hips;
        public float AppliedHeight=>height+(gender==0?.25f:0);
        public float AppliedShoulders=>shoulders+(gender==0?-.0075f:0);
        public float AppliedHips=>hips+(gender==0?.015f:0);
        public float WeightBlend=>weight*100+(gender==0?25:0);
        public float WaistBlend=>waist*100+(gender==0?-50:0);
        public Vector2 ChestAnchor=>new Vector2(1.1053270299044016e-8f,AppliedHeight/25-.12643469870090485f);
        public Vector2 NeckAnchor=>new Vector2(-.0115117896348238f,AppliedHeight/25-.18918809294700623f);
        public Vector3 PelvisPosition=>new Vector3(0,AppliedHeight/-7.5f+.41999998688697815f,0);
        public Vector3 ThighScale=>new Vector3(AppliedHeight*-.25f+1,AppliedHeight*-.25f+1,1);
        public Vector3 SkirtScale=>new Vector3(1,AppliedHeight/-5+1,1);
        public Vector3 ShoulderLeftPosition=>new Vector3(-AppliedShoulders,.1665651947259903f,-.0010000000474974513f);
        public Vector3 ShoulderRightPosition=>new Vector3(AppliedShoulders,.1665651947259903f,0);
        public Vector2 ShoulderLeftAnchor=>new Vector2(-.0024999999441206455f-AppliedShoulders,-.1665651947259903f);
        public Vector2 ShoulderRightAnchor=>new Vector2(AppliedShoulders+.0024999999441206455f,-.16050639748573303f);
        public Vector3 HipLeftPosition=>new Vector3(-.08842559903860092f-AppliedHips,-.006424466148018837f,-6.055831835993786e-9f);
        public Vector3 HipRightPosition=>new Vector3(AppliedHips+.08886130899190903f,-.006424466148018837f,-6.055831835993786e-9f);
        public Vector2 HipLeftAnchor=>new Vector2(-.07500000298023224f-AppliedHips,.027499999850988388f);
        public Vector2 HipRightAnchor=>new Vector2(AppliedHips+.10499999672174454f,.027499999850988388f);
        // Preserve the Single payload as a Double in J instead of emitting a
        // short Single spelling (.45 versus the actual .44999998807907104).
        public J Packet()=>J.O("Gender",gender,"Height",(double)height,"Weight",(double)weight,"ShouldersWidth",(double)shoulders,"WaistWidth",(double)waist,"HipsWidth",(double)hips);
    }
    static float Read(J data,string key,float fallback){float value=data!=null&&data[key].Has?(float)data[key].N:fallback;if(float.IsNaN(value)||float.IsInfinity(value))throw new ArgumentException("Invalid source body shape "+key);return value;}
    // Original CharacterConfigData::.ctor (VA0x3427288) defaults: Male1,
    // ShouldersWidth=.125, other five body floats zero. No name/skin guessing.
    public static Values Evaluate(J currentConfig=null){
        int gender=currentConfig!=null&&currentConfig["Gender"].Has?currentConfig["Gender"].I:1;
        if(gender!=0&&gender!=1)throw new ArgumentException("Source BBGender must be Female0 or Male1");
        return new Values{gender=gender,height=Mathf.Clamp(Read(currentConfig,"Height",0),-.44999998807907104f,.44999998807907104f),weight=Mathf.Clamp(Read(currentConfig,"Weight",0),-.550000011920929f,.550000011920929f),shoulders=Mathf.Clamp(Read(currentConfig,"ShouldersWidth",.125f),.0949999988079071f,.1550000011920929f),waist=Mathf.Clamp(Read(currentConfig,"WaistWidth",0),-.75f,.75f),hips=Mathf.Clamp(Read(currentConfig,"HipsWidth",0),-.03500000014901161f,.03500000014901161f)};
    }
}

public sealed partial class PhysicsDuel {
    public sealed partial class Fighter {
        J sourceBodyShapeConfig=new J(),sourceBodyShapeApplied=new J();
        public J CaptureSourceBodyShape()=>J.Parse(sourceBodyShapeConfig.ToString());
        public J SourceBodyShapeEvidence=>sourceBodyShapeApplied;
        public J ConfigureSourceBodyShape(J currentConfig=null){
            if(sourceRig==null)return new J();
            var shape=DuelSourceBodyShape.Evaluate(currentConfig);J config=sourceRig.Source("CharacterConfigApplier");var changes=new List<J>();int visualWrites=0;
            J Vec2(Vector2 v)=>J.O("x",v.x,"y",v.y);
            J Vec3(Vector3 v)=>J.O("x",v.x,"y",v.y,"z",v.z);
            void Position(string name,Vector3 value){var t=sourceRig.ResolveTransform(config[name]);if(t==null)throw new InvalidOperationException("Missing source shape Transform "+name);changes.Add(J.O("field",name,"property","localPosition","id",DuelSourceRig.Id(config[name]),"before",Vec3(t.localPosition),"after",Vec3(value)));t.localPosition=value;}
            void Scale(string name,Vector3 value){var t=sourceRig.ResolveTransform(config[name]);if(t==null)throw new InvalidOperationException("Missing source shape Transform "+name);changes.Add(J.O("field",name,"property","localScale","id",DuelSourceRig.Id(config[name]),"before",Vec3(t.localScale),"after",Vec3(value)));t.localScale=value;}
            void Anchor(string name,Vector2 value){var joint=sourceRig.Resolve<AnchoredJoint2D>(config[name]);if(joint==null)throw new InvalidOperationException("Missing source shape joint "+name);changes.Add(J.O("field",name,"property","connectedAnchor","id",DuelSourceRig.Id(config[name]),"before",Vec2(joint.connectedAnchor),"after",Vec2(value)));joint.connectedAnchor=value;}
            void Blend(string name,int index,float value){var renderer=sourceRig.Resolve<SkinnedMeshRenderer>(config[name]);if(renderer==null)return;renderer.SetBlendShapeWeight(index,value);visualWrites++;}
            // Original ApplyWeight: renderer-only; never pretend body mass or
            // collider density changes when the physical-only graph has no mesh.
            foreach(string key in new[]{"topSkinnedMeshRenderer","topL1SkinnedMeshRenderer","topL2SkinnedMeshRenderer","topL3SkinnedMeshRenderer","glovesSkinnedMeshRenderer","bottomSkinnedMeshRenderer","bottomL1SkinnedMeshRenderer","bottomL2SkinnedMeshRenderer","bottomL3SkinnedMeshRenderer","shoesSkinnedMeshRenderer"})Blend(key,0,shape.WeightBlend);
            Blend("topL4SkinnedMeshRenderer",8,shape.WeightBlend);
            Anchor("ChestJoint2D",shape.ChestAnchor);Anchor("NeckJoint2D",shape.NeckAnchor);Position("PelvisPointTransform",shape.PelvisPosition);
            Scale("ThighLTransform",shape.ThighScale);Scale("ThighRTransform",shape.ThighScale);Scale("ThighLSkirtTransform",shape.SkirtScale);Scale("ThighRSkirtTransform",shape.SkirtScale);
            Position("ShoulderLeftTransform",shape.ShoulderLeftPosition);Position("ShoulderRightTransform",shape.ShoulderRightPosition);Anchor("ShoulderLeftHingeJoint2D",shape.ShoulderLeftAnchor);Anchor("ShoulderRightHingeJoint2D",shape.ShoulderRightAnchor);
            foreach(string key in new[]{"topSkinnedMeshRenderer","topL1SkinnedMeshRenderer","topL2SkinnedMeshRenderer","topL3SkinnedMeshRenderer"}){Blend(key,shape.WaistBlend<=0?1:2,Mathf.Abs(shape.WaistBlend));Blend(key,shape.WaistBlend<=0?2:1,0);}
            Position("HipLeftTransform",shape.HipLeftPosition);Position("HipRightTransform",shape.HipRightPosition);Anchor("HipLeftHingeJoint2D",shape.HipLeftAnchor);Anchor("HipRightHingeJoint2D",shape.HipRightAnchor);
            sourceBodyShapeConfig=shape.Packet();sourceBodyShapeApplied=J.O("config",sourceBodyShapeConfig,"physical_changes",changes,"visual_blendshape_writes",visualWrites,"physical_only_visual_omission",visualWrites==0);
            return sourceBodyShapeApplied;
        }
    }
}
