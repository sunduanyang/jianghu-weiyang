using System;
using System.Collections.Generic;
using System.Linq;
using Jianghu;
using UnityEngine;

public sealed partial class OriginalRig {
    OriginalVisualCatalog visuals;
    J appearanceSource,appearanceConfig,armorMaterials;
    readonly Dictionary<int,OriginalPaintTexture> paintTextures=new Dictionary<int,OriginalPaintTexture>();
    readonly Dictionary<int,J> visualRecords=new Dictionary<int,J>();
    public J AppearanceConfig=>appearanceConfig;
    partial void InitializeAppearance() {
        visuals=Resources.Load<OriginalVisualCatalog>("OriginalVisualCatalog");
        if(visuals==null)throw new InvalidOperationException("Original appearance catalog was not bound by the recovery build");
        var records=visuals.Data["rigs"][human?PlayerSource:EnemySource].A;
        foreach(var r in records)visualRecords[r["id"].I]=r;
        appearanceSource=records.First(r=>r["script"].S=="CharacterConfigApplier");
        var armorSource=Resources.Load<TextAsset>("OriginalArmorMaterials");
        if(armorSource!=null)armorMaterials=J.Parse(armorSource.text);
        foreach(var r in records.Where(r=>r["script"].S=="CwPaintableMeshTexture")) {
            var renderer=paths[r["path"].S].GetComponent<Renderer>();
            var paint=renderer.gameObject.AddComponent<OriginalPaintTexture>();
            paint.Initialize(renderer,r["data"],visuals.Get<Texture>(r["texture"].S));
            paintTextures[r["id"].I]=paint;
        }
        ApplyOriginalAppearance(appearanceSource["data"]["CurrentConfig"]);
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
        float weight=(float)config["Weight"].N*100+(male?0:25);
        foreach(string name in new[]{"top","topL1","topL2","topL3","bottom","bottomL1","bottomL2","bottomL3","shoes"})Shape(name+"SkinnedMeshRenderer",0,weight);
        Shape("topL4SkinnedMeshRenderer",8,weight);
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
    }
    public void ApplyOriginalArmor(string slot,int itemId) {
        string[] slots={"Hat","TopL1","TopL2","TopL3","TopL4","Gloves","BottomL1","BottomL2","BottomL3","Shoes"};
        int type=Array.IndexOf(slots,slot)+8;J row=Armor(type,itemId);if(!row.Has)return;
        string lower=char.ToLowerInvariant(slot[0])+slot.Substring(1);
        Texture texture=visuals.Get<Texture>(row["texture"].S);
        if(slot=="TopL1"&&appearanceConfig["TuckTopL1"].B&&row["tucked_top_texture"].Has)texture=visuals.Get<Texture>(row["tucked_top_texture"].S);
        Paint(lower+"PaintableTexture",texture,OriginalVisualCatalog.Color(row["tint"]));
        SetOriginalArmorSlot(slot,(float)row["defense"].N,(float)row["weight"].N,0);
        string sourceId=row["source"]["file"].S+":"+row["source"]["id"].S;
        if(armorMaterials!=null)foreach(var part in armorMaterials[sourceId].D)SetOriginalArmorPart(part.Key,(float)row["defense"].N,part.Value.I);
        if(slot=="Hat") {
            J flags=row["flags"];
            var hairRenderer=AppearanceComponent<Renderer>("hairSkinnedMeshRenderer");if(hairRenderer!=null)hairRenderer.gameObject.SetActive(!flags["ErasesHair"].B);
            var beardRenderer=AppearanceComponent<Renderer>("beardSkinnedMeshRenderer");if(beardRenderer!=null)beardRenderer.gameObject.SetActive(appearanceConfig["Gender"].I==1&&!flags["ErasesBeard"].B);
            Shape("hairSkinnedMeshRenderer",0,flags["TucksHair"].B?60:0);Shape("hairSkinnedMeshRenderer",2,flags["SqueezesHair"].B?30:0);
            Shape("hatSkinnedMeshRenderer",1,flags["CoversChest"].B?18:0);
        }
    }
}
