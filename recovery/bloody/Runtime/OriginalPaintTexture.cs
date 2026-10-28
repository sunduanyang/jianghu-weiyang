using Jianghu;
using UnityEngine;

// PaintCore.CwPaintableTexture.Replace 0x5e95b40: size from source,
// CwCommandReplace: textureLod(_Texture,uv,0)*_Color, then regenerate mips.
public sealed class OriginalPaintTexture : MonoBehaviour {
    public RenderTexture current;
    public Texture sourceTexture;
    public Color sourceColor;
    public Renderer target;
    J config;
    Material replace;
    int materialIndex;
    string property;
    public void Initialize(Renderer renderer,J settings,Texture texture) {
        target=renderer;config=settings;property=settings["slot"]["Name"].S;materialIndex=settings["slot"]["Index"].I;
        replace=new Material(Shader.Find("Hidden/PaintCore/CwReplace"));
        // CwMaterialCloner makes the paint slot unique to this model.
        var materials=renderer.sharedMaterials;for(int i=0;i<materials.Length;i++)materials[i]=new Material(materials[i]);renderer.sharedMaterials=materials;
        Replace(texture,OriginalVisualCatalog.Color(settings["color"]));
    }
    public void Replace(Texture texture,Color color) {
        sourceTexture=texture;sourceColor=color;
        int width=texture!=null?texture.width:config["width"].I,height=texture!=null?texture.height:config["height"].I;
        if(current==null||current.width!=width||current.height!=height) {
            if(current!=null){current.Release();Destroy(current);}
            current=new RenderTexture(width,height,0,RenderTextureFormat.ARGB32){name=target.name+" original paint",filterMode=(FilterMode)config["filter"].I,
                wrapModeU=(TextureWrapMode)config["wrapU"].I,wrapModeV=(TextureWrapMode)config["wrapV"].I,anisoLevel=config["aniso"].I,useMipMap=config["mipMaps"].I!=0,autoGenerateMips=false};
            current.Create();
        }
        replace.SetTexture("_Texture",texture!=null?texture:Texture2D.whiteTexture);replace.SetColor("_Color",color);
        Graphics.Blit(texture,current,replace);if(current.useMipMap)current.GenerateMips();
        target.sharedMaterials[materialIndex].SetTexture(property,current);
    }
    void OnDestroy(){if(current!=null){current.Release();Destroy(current);}if(replace!=null)Destroy(replace);}
}
