using System;
using System.Collections.Generic;
using System.Linq;
using Jianghu;
using UnityEngine;

public sealed partial class OriginalRig {
    OriginalVisualCatalog visuals;
    J appearanceSource,appearanceConfig,armorMaterials;
    OriginalFootsteps footsteps;
    int bodyfallType;
    readonly Dictionary<int,OriginalPaintTexture> paintTextures=new Dictionary<int,OriginalPaintTexture>();
    readonly Dictionary<int,J> visualRecords=new Dictionary<int,J>();
    public J AppearanceConfig=>appearanceConfig;
    public float OriginalFootstepVelocity=>gaitVelocityX;
    public int OriginalBodyfallType=>bodyfallType;
    partial void InitializeAppearance() {
        visuals=Resources.Load<OriginalVisualCatalog>("OriginalVisualCatalog");
        if(visuals==null)throw new InvalidOperationException("Original appearance catalog was not bound by the recovery build");
        var records=visuals.Data["rigs"][human?PlayerSource:EnemySource].A;
        foreach(var r in records)visualRecords[r["id"].I]=r;
        appearanceSource=records.First(r=>r["script"].S=="CharacterConfigApplier");
        var footstepSource=records.FirstOrDefault(r=>r["script"].S=="FootstepsSFX");
        if(footstepSource!=null){footsteps=paths[footstepSource["path"].S].gameObject.AddComponent<OriginalFootsteps>();footsteps.Initialize(this,visuals,footstepSource);}
        var skirtSource=records.FirstOrDefault(r=>r["script"].S=="SkirtBonesAnimator");
        if(skirtSource!=null)paths[skirtSource["path"].S].gameObject.AddComponent<OriginalSkirtBones>().Initialize(this,visuals,skirtSource);
        var armorSource=Resources.Load<TextAsset>("OriginalArmorMaterials");
        if(armorSource!=null)armorMaterials=J.Parse(armorSource.text);
        foreach(var r in records.Where(r=>r["script"].S=="CwPaintableMeshTexture")) {
            var renderer=paths[r["path"].S].GetComponent<Renderer>();
            var paint=renderer.gameObject.AddComponent<OriginalPaintTexture>();
            paint.Initialize(renderer,r["data"],visuals.Get<Texture>(r["texture"].S));
            paintTextures[r["id"].I]=paint;
        }
        ApplyOriginalAppearance(appearanceSource["data"]["CurrentConfig"]);
        OriginalEffects.Register(this);
    }
    T AppearanceComponent<T>(string key) where T:Component {
        var refId=appearanceSource["data"][key]["m_PathID"].I;
        // OriginalRigReferences preserves normalized component paths after stripping scripts.
        var config=source["controllers"]["CharacterConfigApplier"][0]["data"];
        if(config[key].Has)return Resolve<T>(config[key]);
        return visualRecords.TryGetValue(refId,out var r)?paths[r["path"].S].GetComponent<T>():null;
    }
    OriginalPaintTexture AppearancePaint(string key) {
        int id=appearanceSource["data"][key]["m_PathID"].I;
        return paintTextures.TryGetValue(id,out var paint)?paint:null;
    }
    J Armor(int type,int id) => visuals.Data["armor"].A.FirstOrDefault(r=>r["type"].I==type&&r["item_id"].I==id&&!r["empty"].B)??new J();
    void Paint(string key,Texture texture,Color color){var p=AppearancePaint(key);if(p!=null)p.Replace(texture,color);}
    void Shape(string key,int index,float weight) {
        var renderer=AppearanceComponent<SkinnedMeshRenderer>(key);
        if(renderer!=null&&renderer.sharedMesh!=null&&index<renderer.sharedMesh.blendShapeCount)renderer.SetBlendShapeWeight(index,weight);
    }
    void FaceTexture(string key,J row) {
        var renderer=AppearanceComponent<Renderer>(key);if(renderer==null)return;
        var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);
        block.SetTexture("_MainTex",visuals.Get<Texture>(row["texture"].S));renderer.SetPropertyBlock(block);
    }
    // CharacterConfigApplier.ApplyAll 0x341d23c, ApplyGender 0x3420afc.
    public void ApplyOriginalAppearance(J config) {
        appearanceConfig=config;
        J d=appearanceSource["data"],defaults=d["bodyPartDefaults"];
        var skin=OriginalVisualCatalog.Gradient(d["skinColorGradient"]).Evaluate((float)config["SkinColorT"].N/100);
        var hair=OriginalVisualCatalog.Gradient(d["hairColorGradient"]).Evaluate((float)config["HairColorT"].N/100);
        var beard=OriginalVisualCatalog.Gradient(d["beardColorGradient"]).Evaluate((float)config["BeardColorT"].N/100);
        string[] face={"Head","Hair","Eyes","Nose","Mouth","Beard"};
        for(int i=0;i<face.Length;i++) {
            string name=face[i];int id=config[name+"Id"].I;if(id==0)id=defaults[name].I;
            J row=Armor(i,id);var texture=visuals.Get<Texture>(row["texture"].S);
            if(name=="Eyes"||name=="Mouth")FaceTexture(name.ToLowerInvariant()+"Renderer",row);
            else Paint(name.ToLowerInvariant()+"PaintableTexture",texture,name=="Hair"?hair:name=="Beard"?beard:skin);
        }
        Texture ConfigTexture(string key)=>visuals.Get<Texture>(appearanceSource["textures"][key].S);
        bool male=config["Gender"].I==1;
        Paint("topPaintableTexture",ConfigTexture(male?"topTextureMale":"topTextureFemale"),skin);
        Paint("bottomPaintableTexture",ConfigTexture(male?"bottomTextureMale":"bottomTextureFemale"),skin);
        Paint("handsPaintableTexture",ConfigTexture("normalModeTexture_Hands"),skin);
        Paint("feetPaintableTexture",ConfigTexture("normalModeTexture_Feet"),skin);
        var beardRenderer=AppearanceComponent<Renderer>("beardSkinnedMeshRenderer");if(beardRenderer!=null)beardRenderer.gameObject.SetActive(male);
        float weight=Mathf.Clamp((float)config["Weight"].N,-.55f,.55f)*100+(male?0:25);
        foreach(string name in new[]{"top","topL1","topL2","topL3","bottom","bottomL1","bottomL2","bottomL3","shoes"})Shape(name+"SkinnedMeshRenderer",0,weight);
        Shape("topL4SkinnedMeshRenderer",8,weight);
        ApplyOriginalBodyShape(config,male);
        // Original face placement blend shapes: 0x34207c8..0x3420a80.
        float eyesHeight=Mathf.Clamp01((float)config["EyesHeight"].N)*100;
        Shape("eyesSkinnedMeshRenderer",5,eyesHeight);Shape("headSkinnedMeshRenderer",1,60-eyesHeight);Shape("hatSkinnedMeshRenderer",0,eyesHeight);
        Shape("eyesSkinnedMeshRenderer",6,Mathf.Clamp01((float)config["EyesWidth"].N)*100-50);
        Shape("noseSkinnedMeshRenderer",0,Mathf.Clamp01((float)config["NoseHeight"].N)*100);
        Shape("noseSkinnedMeshRenderer",1,Mathf.Clamp01((float)config["NoseWidth"].N)*(male?100:75));
        Shape("mouthSkinnedMeshRenderer",1,Mathf.Clamp01((float)config["MouthHeight"].N)*(male?100:75));
        Shape("beardSkinnedMeshRenderer",1,Mathf.Clamp01((float)config["MouthHeight"].N)*100);
        Shape("mouthSkinnedMeshRenderer",0,Mathf.Clamp01((float)config["MouthWidth"].N)*(male?100:75));
        // ApplyAll source order matters for hair tucking and L1 coverings.
        foreach(string slot in new[]{"TopL2","TopL3","TopL4","TopL1","Gloves","BottomL1","BottomL2","BottomL3","Shoes","Hat"})ApplyOriginalArmor(slot,config[slot+"Id"].I);
        if(OriginalExpression!=null)OriginalExpression.Voice.SetVoice(config);
    }
    public void ApplyOriginalArmor(string slot,int itemId) {
        string[] slots={"Hat","TopL1","TopL2","TopL3","TopL4","Gloves","BottomL1","BottomL2","BottomL3","Shoes"};
        int slotIndex=Array.IndexOf(slots,slot);if(slotIndex<0)return;
        int type=slotIndex+8;J row=Armor(type,itemId);bool none=itemId==0;
        // Every source armor family has a real None0 with transparent tint,
        // zero stats and zero material assignments. Unknown nonzero ids must
        // not turn an equipped layer into an implicit None.
        if(!row.Has&&!none)return;
        string lower=char.ToLowerInvariant(slot[0])+slot.Substring(1);
        Texture texture=visuals.Get<Texture>(row["texture"].S);
        // Prefer the original 2x2 None texture. If its optional catalog entry
        // is absent, multiply this layer's retained source texture by clear;
        // Replace(null, white) would instead paint an opaque white fallback.
        if(none&&texture==null)texture=AppearancePaint(lower+"PaintableTexture")?.sourceTexture;
        if(slot=="TopL1") {
            bool covered=Armor(10,appearanceConfig["TopL2Id"].I)["flags"]["CoversL1Arms"].B||Armor(11,appearanceConfig["TopL3Id"].I)["flags"]["CoversL1Arms"].B;
            if(covered&&row["tucked_top_texture"].Has)texture=visuals.Get<Texture>(row["tucked_top_texture"].S);
        }
        Paint(lower+"PaintableTexture",texture,none?Color.clear:OriginalVisualCatalog.Color(row["tint"]));
        if(slot=="TopL1") {
            var paint=AppearancePaint("topL1PaintableTexture");
            if(paint!=null)paint.target.sharedMaterial.SetFloat("_Erases_Bottom_of_L1",appearanceConfig["TuckTopL1"].B?1:0);
        }
        if(slot=="TopL3")Shape("topL3SkinnedMeshRenderer",5,row["flags"]["HasL3GiantShoulders"].B?18:0);
        SetOriginalArmorSlot(slot,none?0:(float)row["defense"].N,none?0:(float)row["weight"].N,0);
        string sourceId=row["source"]["file"].S+":"+row["source"]["id"].S;
        if(!none&&armorMaterials!=null)foreach(var part in armorMaterials[sourceId].D)SetOriginalArmorPart(part.Key,(float)row["defense"].N,part.Value.I);
        if(slot=="TopL1") {
            // Explicit local unequipping also removes a previously selected
            // armor fall sound. The original cfb helper's nonzero material
            // branches retain their existing behavior.
            if(none)bodyfallType=0;
            else if(armorMaterials!=null) {
                int material=armorMaterials[sourceId]["ArmorPart_Chest_TopL1"].I;
                if(material==3||material==4)bodyfallType=1;else if(material>=5)bodyfallType=2;
            }
        }
        if(slot=="Shoes"&&footsteps!=null&&(none||armorMaterials!=null))footsteps.SetArmorMaterial(none?0:armorMaterials[sourceId]["ArmorPart_FootL_Shoes"].I);
        if(slot=="Hat") {
            J flags=row["flags"];
            var hairRenderer=AppearanceComponent<Renderer>("hairSkinnedMeshRenderer");if(hairRenderer!=null)hairRenderer.gameObject.SetActive(!flags["ErasesHair"].B);
            var beardRenderer=AppearanceComponent<Renderer>("beardSkinnedMeshRenderer");if(beardRenderer!=null)beardRenderer.gameObject.SetActive(appearanceConfig["Gender"].I==1&&!flags["ErasesBeard"].B);
            Shape("hairSkinnedMeshRenderer",0,flags["TucksHair"].B?60:0);Shape("hairSkinnedMeshRenderer",2,flags["SqueezesHair"].B?30:0);
            Shape("hatSkinnedMeshRenderer",1,flags["CoversChest"].B?18:0);
            var top=AppearanceComponent<SpringJoint2D>("hatBone_Top");var bottom=AppearanceComponent<SpringJoint2D>("hatBone_Bottom");
            if(top!=null)top.frequency=(float)row["parameters"]["HatBoneFrequency_Top"].N*100;
            if(bottom!=null)bottom.frequency=(float)row["parameters"]["HatBoneFrequency_Bottom"].N*100;
            // EraseOrEnableBeardAndHair 0x3425518 always calls ApplyBeard,
            // including when a new hat (or None) stops tucking the beard.
            int id=appearanceConfig["BeardId"].I;if(id==0)id=appearanceSource["data"]["bodyPartDefaults"]["Beard"].I;
            var beard=Armor(5,id);if(beard.Has) {
                string beardTexture=flags["TucksBeard"].B&&beard["tucked_beard_texture"].Has?beard["tucked_beard_texture"].S:beard["texture"].S;
                var color=OriginalVisualCatalog.Gradient(appearanceSource["data"]["beardColorGradient"]).Evaluate((float)appearanceConfig["BeardColorT"].N/100);
                Paint("beardPaintableTexture",visuals.Get<Texture>(beardTexture),color);
            }
        }
    }
    void ApplyOriginalBodyShape(J config,bool male) {
        // Native ApplyHeight/ShouldersWidth/WaistWidth/HipsWidth 0x3420f30..
        // Literal constants were read from the ELF load segment, including the
        // asymmetric left/right connected anchors retained by the original.
        void Position(string key,Vector3 v){var t=AppearanceComponent<Transform>(key);if(t!=null)t.localPosition=v;}
        void Scale(string key,Vector3 v){var t=AppearanceComponent<Transform>(key);if(t!=null)t.localScale=v;}
        void Anchor(string key,Vector2 v){var joint=AppearanceComponent<AnchoredJoint2D>(key);if(joint!=null)joint.connectedAnchor=v;}
        float height=Mathf.Clamp((float)config["Height"].N,-.45f,.45f)+(male?0:.25f);
        Anchor("ChestJoint2D",new Vector2(1.10532703e-8f,height/25-.126434699f));
        Anchor("NeckJoint2D",new Vector2(-.0115117896f,height/25-.189188093f));
        Position("PelvisPointTransform",new Vector3(0,height/-7.5f+.42f,0));
        Scale("ThighLTransform",new Vector3(1-height*.25f,1-height*.25f,1));Scale("ThighRTransform",new Vector3(1-height*.25f,1-height*.25f,1));
        Scale("ThighLSkirtTransform",new Vector3(1,1-height/5,1));Scale("ThighRSkirtTransform",new Vector3(1,1-height/5,1));
        float shoulders=Mathf.Clamp((float)config["ShouldersWidth"].N,.095f,.155f)+(male?0:-.0075f);
        Position("ShoulderLeftTransform",new Vector3(-shoulders,.166565195f,-.001f));Position("ShoulderRightTransform",new Vector3(shoulders,.166565195f,0));
        Anchor("ShoulderLeftHingeJoint2D",new Vector2(-.0025f-shoulders,-.166565195f));Anchor("ShoulderRightHingeJoint2D",new Vector2(shoulders+.0025f,-.160506397f));
        float waist=Mathf.Clamp((float)config["WaistWidth"].N,-.75f,.75f)*100+(male?0:-50);
        foreach(string name in new[]{"top","topL1","topL2","topL3"}){Shape(name+"SkinnedMeshRenderer",1,Mathf.Max(0,-waist));Shape(name+"SkinnedMeshRenderer",2,Mathf.Max(0,waist));}
        float hips=Mathf.Clamp((float)config["HipsWidth"].N,-.035f,.035f)+(male?0:.015f);
        Position("HipLeftTransform",new Vector3(-.088425599f-hips,-.006424466f,-6.05583184e-9f));Position("HipRightTransform",new Vector3(hips+.088861309f,-.006424466f,-6.05583184e-9f));
        Anchor("HipLeftHingeJoint2D",new Vector2(-.075f-hips,.0275f));Anchor("HipRightHingeJoint2D",new Vector2(hips+.105f,.0275f));
    }
}
