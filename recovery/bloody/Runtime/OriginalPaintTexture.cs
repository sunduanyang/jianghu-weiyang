using Jianghu;
using UnityEngine;

// PaintCore.CwPaintableTexture.Replace 0x5e95b40: size from source,
// CwCommandReplace: textureLod(_Texture,uv,0)*_Color, then regenerate mips.
public sealed class OriginalPaintTexture : MonoBehaviour {
    public RenderTexture current;
    public Texture sourceTexture;
    public Color sourceColor;
    public Renderer target;
    public bool includeScale=true;
    J config;
    Material replace;
    Material decal;
    Mesh baked;
    int materialIndex;
    string property;
    public void Initialize(Renderer renderer,J settings,Texture texture) {
        target=renderer;config=settings;property=settings["slot"]["Name"].S;materialIndex=settings["slot"]["Index"].I;
        // AssetRipper also exports Resources/cwreplace.shader with the same
        // shader name. Bind the recovered asset explicitly, never its dummy.
        replace=new Material(Resources.Load<OriginalVisualCatalog>("OriginalVisualCatalog").RecoveredShader("Hidden/PaintCore/CwReplace"));
        // CwMaterialCloner makes the paint slot unique to this model.
        var materials=renderer.sharedMaterials;for(int i=0;i<materials.Length;i++)materials[i]=new Material(materials[i]);renderer.sharedMaterials=materials;
        Replace(texture,OriginalVisualCatalog.Color(settings["color"]));
    }
    public void Replace(Texture texture,Color color) {
        sourceTexture=texture;sourceColor=color;
        int width=texture!=null?texture.width:config["width"].I,height=texture!=null?texture.height:config["height"].I;
        if(current==null||current.width!=width||current.height!=height) {
            if(current!=null){current.Release();Destroy(current);}
            current=new RenderTexture(width,height,0,RenderTextureFormat.ARGB32){name=target.name+" original paint",filterMode=config["filter"].I<0?(texture!=null?texture.filterMode:FilterMode.Bilinear):(FilterMode)config["filter"].I,
                wrapModeU=config["wrapU"].I<0?(texture!=null?texture.wrapModeU:TextureWrapMode.Repeat):(TextureWrapMode)config["wrapU"].I,
                wrapModeV=config["wrapV"].I<0?(texture!=null?texture.wrapModeV:TextureWrapMode.Repeat):(TextureWrapMode)config["wrapV"].I,
                anisoLevel=config["aniso"].I<0?(texture!=null?texture.anisoLevel:1):config["aniso"].I,
                useMipMap=config["mipMaps"].I==1||(config["mipMaps"].I==2&&texture is Texture2D tex&&tex.mipmapCount>1),autoGenerateMips=false};
            current.Create();
        }
        replace.SetTexture("_Texture",texture!=null?texture:Texture2D.whiteTexture);replace.SetColor("_Color",QualitySettings.activeColorSpace==ColorSpace.Linear?color.linear:color);
        var prior=RenderTexture.active;
        try{Graphics.Blit(texture,current,replace);if(current.useMipMap)current.GenerateMips();}
        finally{RenderTexture.active=prior;}
        target.sharedMaterials[materialIndex].SetTexture(property,current);
    }
    public void PaintPoint(OriginalVisualCatalog catalog,J record,Vector3 point)=>PaintPoint(catalog,record,point,false);
    // The opt-in GPU regression uses the former rasterization as a negative
    // control. Ordinary combat always uses the corrected platform projection.
    internal void PaintPoint(OriginalVisualCatalog catalog,J record,Vector3 point,bool legacyUvForValidation) {
        if(!isActiveAndEnabled||current==null||target==null)return;
        J d=record["data"];int layers=unchecked((int)(long)d["layers"]["m_Bits"].N);
        if((layers&(1<<gameObject.layer))==0||d["group"]["index"].I!=config["group"]["index"].I)return;
        // The game's combat decals all use blend 10 and point projection.
        // Other PaintIn3D modes are intentionally not silently substituted.
        if(d["blendMode"]["Index"].I!=10)return;
        var texture=catalog.Get<Texture>(record["assets"]["texture"].S);if(texture==null)return;
        float angle=(float)d["angle"].N;var randomState=Random.state;
        Random.InitState((int)(point.x*10000));
        foreach(var modifier in d["references"]["RefIds"].A)if(modifier["type"]["class"].S=="CwModifyAngleRandom") {
            var m=modifier["data"];float amount=Random.Range((float)m["min"].N,(float)m["max"].N);
            switch(m["blend"].I){case 0:angle=amount;break;case 1:angle*=amount;break;case 2:angle+=amount;break;}
        }
        Random.state=randomState;
        var shape=catalog.Get<Texture>(record["assets"]["shape"].S);
        var aspectTexture=shape!=null?shape:texture;float aspect=(float)aspectTexture.width/aspectTexture.height;
        Vector3 scale=new Vector3((float)d["scale"]["x"].N,(float)d["scale"]["y"].N,(float)d["scale"]["z"].N)*(float)d["radius"].N;
        if(aspect>1)scale.y/=aspect;else scale.x*=aspect;
        Matrix4x4 matrix=Matrix4x4.TRS(Vector3.zero,Quaternion.Euler(0,0,angle),scale).inverse;
        if(decal==null)decal=new Material(catalog.RecoveredShader("Hidden/PaintCore/CwDecalPointMultiply"));
        if(legacyUvForValidation)decal.EnableKeyword("ORIGINAL_PAINT_LEGACY_UV");else decal.DisableKeyword("ORIGINAL_PAINT_LEGACY_UV");
        decal.SetMatrix("_Matrix",matrix);decal.SetVector("_Position",matrix.MultiplyPoint3x4(point));decal.SetVector("_Direction",Vector3.forward);decal.SetFloat("_In3D",1);
        var coord=Vector4.zero;coord[config["coord"].I]=1;decal.SetVector("_Coord",coord);
        decal.SetTexture("_Texture",texture);decal.SetTexture("_Shape",shape!=null?shape:Texture2D.whiteTexture);
        Vector4 channel=Vector4.zero;channel[d["shapeChannel"].I]=1;decal.SetVector("_ShapeChannel",channel);
        var color=OriginalVisualCatalog.Color(d["color"]);decal.SetColor("_Color",QualitySettings.activeColorSpace==ColorSpace.Linear?color.linear:color);decal.SetFloat("_Opacity",(float)d["opacity"].N);
        decal.SetFloat("_Hardness",(float)d["hardness"].N);decal.SetFloat("_Wrapping",(float)d["wrapping"].N);
        float fade=(float)d["normalFade"].N,front=(float)d["normalFront"].N,back=(float)d["normalBack"].N;
        // SetMaterial passes normalBack before normalFront in its native ABI.
        decal.SetVector("_NormalFront",new Vector4(front-1-fade,1/fade,0,0));decal.SetVector("_NormalBack",new Vector4(1-back+fade,-1/fade,0,0));
        decal.SetMatrix("_TileMatrix",Matrix4x4.identity);decal.SetTexture("_TileTexture",Texture2D.whiteTexture);decal.SetFloat("_TileOpacity",(float)d["tileOpacity"].N);decal.SetFloat("_TileTransition",(float)d["tileTransition"].N);
        decal.SetMatrix("_MaskMatrix",Matrix4x4.identity);decal.SetTexture("_MaskTexture",Texture2D.whiteTexture);decal.SetVector("_MaskChannel",new Vector4(0,0,0,1));decal.SetVector("_MaskStretch",Vector3.one);decal.SetVector("_MaskInvert",new Vector4(0,1,0,0));
        decal.SetTexture("_LocalMaskTexture",Texture2D.whiteTexture);decal.SetVector("_LocalMaskChannel",new Vector4(1,0,0,0));
        decal.SetMatrix("_DepthMatrix",Matrix4x4.identity);decal.SetVector("_DepthData",new Vector4(0,0,0,-1));
        var channels=d["blendMode"]["Channels"];decal.SetVector("_Channels",new Vector4((float)channels["x"].N,(float)channels["y"].N,(float)channels["z"].N,(float)channels["w"].N));
        Mesh mesh=null;Matrix4x4 localToWorld=transform.localToWorldMatrix;
        if(target is SkinnedMeshRenderer skinned){
            if(baked==null)baked=new Mesh();
            // CwMeshModel.jba 0x5ebc4e4 deliberately bakes at local scale 1,
            // restores the transform, then compensates its lossy scale.
            var lossy=transform.lossyScale;var local=transform.localScale;transform.localScale=Vector3.one;skinned.BakeMesh(baked);transform.localScale=local;
            localToWorld=skinned.localToWorldMatrix;
            if(includeScale)localToWorld*=Matrix4x4.Scale(new Vector3(lossy.x==0?0:1/lossy.x,lossy.y==0?0:1/lossy.y,lossy.z==0?0:1/lossy.z));
            mesh=baked;
        }
        else {var filter=GetComponent<MeshFilter>();if(filter!=null)mesh=filter.sharedMesh;}
        if(mesh==null)return;
        var active=RenderTexture.active;var buffer=RenderTexture.GetTemporary(current.descriptor);
        try {
            Graphics.Blit(current,buffer);decal.SetTexture("_Buffer",buffer);decal.SetVector("_BufferSize",new Vector4(current.width,current.height,0,0));
            RenderTexture.active=current;decal.SetPass(0);Graphics.DrawMeshNow(mesh,localToWorld,materialIndex);if(current.useMipMap)current.GenerateMips();
        }
        finally{RenderTexture.active=active;RenderTexture.ReleaseTemporary(buffer);}
    }
    void OnDestroy(){if(current!=null){current.Release();Destroy(current);}if(replace!=null)Destroy(replace);if(decal!=null)Destroy(decal);if(baked!=null)Destroy(baked);}
}
