using System;
using Jianghu;
using UnityEngine;

public sealed partial class PhysicsDuel {
    public sealed partial class Fighter {
        CharacterRenderer characterRenderer;
        public string CharacterAppearanceId=>skin["appearance_id"].S;
        public J CharacterRenderFacts()=>characterRenderer?.Probe()??J.O();
        bool TryDrawCharacterModules(bool debug){
            string id=skin["appearance_id"].S;if(!CharacterLook.IsId(id))return false;
            if(characterRenderer==null){characterRenderer=new CharacterRenderer(1024,root.transform,CharacterArt.Look(id).Art);root.AddComponent<ProductionCharacterLifetime>().Release=characterRenderer.Dispose;}
            var pose=new CharacterPose{Left=facing<0};Vector2 origin=new Vector2(Hip.x,0);
            foreach(string key in new[]{"pelvis","torso","head","upperNear","forearmNear","handNear","upperFar","forearmFar","handFar","thighNear","shinNear","footNear","thighFar","shinFar","footFar"}){
                var bone=limbs[key];pose.Add(key,bone.A-origin,bone.B-origin);
            }
            if(Reference){
                pose.Detached.UnionWith(severed);
                var belly=referenceBones["belly"];var neck=referenceBones["neck"];
                pose.Add("belly",belly.A-origin,belly.B-origin);pose.Add("neck",neck.A-origin,neck.B-origin);
            }else{
                Vector2 a=pelvis.B-origin,b=torso.B-origin,mid=Vector2.Lerp(a,b,.4285714f);
                pose.Add("belly",a,mid);pose.Add("torso",mid,b);pose.Add("neck",b,head.A-origin);
            }
            foreach(string side in new[]{"Near","Far"}){var foot=limbs["foot"+side];Vector2 ankle=Limb.BodyPoint(foot.rb,foot.joint.anchor)-origin;if(side=="Near")pose.FootNear=ankle;else pose.FootFar=ankle;}
            float scale=Reference?1:.5f;
            if(Event.current.type==EventType.Repaint)characterRenderer.Render(CharacterArt.Look(id),pose,scale,true);
            if(shield!=null&&HasShield)DrawWeapon(shield,true);
            Rect frame=characterRenderer.FrameRect;Vector2 corner=ScreenPoint(origin+new Vector2(frame.xMin,frame.yMax)/scale);
            GUI.DrawTexture(new Rect(corner,new Vector2(frame.width,frame.height)*Unit/scale),characterRenderer.Texture,ScaleMode.StretchToFill,true);
            if(weapon.id!="unarmed")DrawWeapon(sword,false);
            if(debug)foreach(var collider in colliders)DrawCollider(collider);
            return true;
        }
    }
}
public sealed class ProductionCharacterLifetime:MonoBehaviour {public Action Release;void OnDestroy(){Release?.Invoke();Release=null;}}
