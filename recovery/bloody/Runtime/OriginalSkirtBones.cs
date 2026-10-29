using System;
using Jianghu;
using UnityEngine;

// SkirtBonesAnimator 0x3516368..0x35168a8: source world rotations are
// constrained in the skirt parent's frame, then Slerped by .75 each Render.
public sealed class OriginalSkirtBones : MonoBehaviour {
    OriginalRig rig;
    Transform skirtThighL,skirtCalfL,thighL,calfL,skirtThighR,skirtCalfR,thighR,calfR;
    Vector2 rangeL,rangeR;
    bool leftSevered,rightSevered;
    public void Initialize(OriginalRig owner,OriginalVisualCatalog catalog,J record) {
        rig=owner;
        Transform Node(string key) {
            string path=catalog.Data["assets"][record["assets"][key].S]["scene_path"].S;int slash=path.IndexOf('/');
            var node=slash<0?rig.transform:rig.transform.Find(path.Substring(slash+1));
            if(node==null)throw new InvalidOperationException("Original skirt transform missing: "+key+" / "+path);return node;
        }
        skirtThighL=Node("skirtThighL");skirtCalfL=Node("skirtCalfL");thighL=Node("thighL");calfL=Node("calfL");
        skirtThighR=Node("skirtThighR");skirtCalfR=Node("skirtCalfR");thighR=Node("thighR");calfR=Node("calfR");
        rangeL=new Vector2((float)record["data"]["thighAngleRangeL"]["min"].N,(float)record["data"]["thighAngleRangeL"]["max"].N);
        rangeR=new Vector2((float)record["data"]["thighAngleRangeR"]["min"].N,(float)record["data"]["thighAngleRangeR"]["max"].N);
        rig.PartModeChanged+=OnPartMode;
    }
    void OnPartMode(OriginalBodyPart part,OriginalPartMode mode) {
        if(mode!=OriginalPartMode.Pierced&&mode!=OriginalPartMode.Severed)return;
        if(part.type==4)leftSevered=true;if(part.type==5)rightSevered=true;
    }
    Quaternion Constrained(Transform skirt,Transform leg,Vector2 range) {
        var parent=skirt.parent==null?Quaternion.identity:skirt.parent.rotation;
        var euler=(Quaternion.Inverse(parent)*leg.rotation).eulerAngles;
        float angle=euler.z%360;if(angle>180)angle-=360;
        if(rig.OriginalFlipped)angle=-angle;angle=Mathf.Clamp(angle,range.x,range.y);if(rig.OriginalFlipped)angle=-angle;
        return Quaternion.Slerp(skirt.rotation,parent*Quaternion.Euler(euler.x,euler.y,angle),.75f);
    }
    void LateUpdate() {
        if(rig==null)return;
        if(!leftSevered&&skirtThighL!=null&&thighL!=null){skirtThighL.rotation=Constrained(skirtThighL,thighL,rangeL);if(skirtCalfL!=null&&calfL!=null)skirtCalfL.rotation=Quaternion.Slerp(skirtCalfL.rotation,calfL.rotation,.75f);}
        if(!rightSevered&&skirtThighR!=null&&thighR!=null){skirtThighR.rotation=Constrained(skirtThighR,thighR,rangeR);if(skirtCalfR!=null&&calfR!=null)skirtCalfR.rotation=Quaternion.Slerp(skirtCalfR.rotation,calfR.rotation,.75f);}
    }
    void OnDestroy(){if(rig!=null)rig.PartModeChanged-=OnPartMode;}
}
