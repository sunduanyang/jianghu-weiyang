using System;
using System.IO;
using Jianghu;
using UnityEngine;

// Production numerics versus actual original ARM64 setter outputs.
// This probe does not simulate Unity physics.
class Program {
    static int count;
    static void Check(double actual, double expected, string key) {
        count++;
        if(Math.Abs(actual-expected)>2e-6)
            throw new Exception(key+" expected="+expected+" actual="+actual);
    }
    static double[] Output(DuelSourceBodyShape.Values s,J call) {
        string field=call["field"].S,property=call["property"].S;
        if(property=="blend") {
            int index=call["index"].I;
            float weight=field=="topL4SkinnedMeshRenderer"?index==8?s.WeightBlend:0:index==0?s.WeightBlend:0;
            if(index!=0&&field!="topL4SkinnedMeshRenderer")
                weight=index==(s.WaistBlend<=0?1:2)?Math.Abs(s.WaistBlend):0;
            return new double[]{weight};
        }
        if(property=="connectedAnchor") {
            Vector2 v=field switch {
                "ChestJoint2D"=>s.ChestAnchor,
                "NeckJoint2D"=>s.NeckAnchor,
                "ShoulderLeftHingeJoint2D"=>s.ShoulderLeftAnchor,
                "ShoulderRightHingeJoint2D"=>s.ShoulderRightAnchor,
                "HipLeftHingeJoint2D"=>s.HipLeftAnchor,
                "HipRightHingeJoint2D"=>s.HipRightAnchor,
                _=>throw new Exception(field)
            };
            return new double[]{v.x,v.y};
        }
        Vector3 vector=field switch {
            "PelvisPointTransform"=>s.PelvisPosition,
            "ThighLTransform"=>s.ThighScale,
            "ThighRTransform"=>s.ThighScale,
            "ThighLSkirtTransform"=>s.SkirtScale,
            "ThighRSkirtTransform"=>s.SkirtScale,
            "ShoulderLeftTransform"=>s.ShoulderLeftPosition,
            "ShoulderRightTransform"=>s.ShoulderRightPosition,
            "HipLeftTransform"=>s.HipLeftPosition,
            "HipRightTransform"=>s.HipRightPosition,
            _=>throw new Exception(field)
        };
        return new double[]{vector.x,vector.y,vector.z};
    }
    static void Main() {
        foreach(var test in J.Parse(File.ReadAllText("native-oracle.json")).A) {
            var shape=DuelSourceBodyShape.Evaluate(test["input"]);
            var config=shape.Packet();
            foreach(var pair in test["config"].D)
                Check(config[pair.Key].N,pair.Value.N,pair.Key);
            foreach(var call in test["calls"].A) {
                var actual=Output(shape,call);
                for(int i=0;i<actual.Length;i++)
                    Check(actual[i],call["value"][i].N,call["field"].S+"."+call["property"].S+"."+i);
            }
        }
        Console.WriteLine(count+" native output comparisons passed");
    }
}
