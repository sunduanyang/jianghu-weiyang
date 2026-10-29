using System;
using System.Linq;
using Jianghu;
using UnityEngine;
using UnityEngine.U2D;

// Active throne-room counterpart of BloodOnTheGroundCustomizer. Its outdoor
// Ground is inactive; identify this surface by paintableTexture, never path
// prefix order. Ordinary stage ground recovery remains in OriginalGroundSource.
public sealed class OriginalInteriorBloodSurface : MonoBehaviour {
    OriginalPaintTexture paint;
    SpriteShape profile;
    Texture2D readable,originalFill;
    float interval;
    bool initialized;
    public string SourceIdentity {get;private set;}
    public bool HasStarted {get;private set;}
    public static OriginalInteriorBloodSurface[] Attach(OriginalVisualCatalog catalog,Transform stageRoot,string sourceRoot) {
        return catalog.Data["scene"].A.Where(r=>r["script"].S=="BloodOnTheGroundCustomizer"&&r["scene_path"].S.StartsWith(sourceRoot+"/",StringComparison.Ordinal)).Select(row=> {
            var node=OriginalEnvironmentSource.IdentityNode(catalog,OriginalEnvironmentSource.Identity(row),stageRoot,sourceRoot);
            if(!node.gameObject.activeInHierarchy||!row["data"]["m_Enabled"].B)return null;
            var component=node.GetComponent<OriginalInteriorBloodSurface>()??node.gameObject.AddComponent<OriginalInteriorBloodSurface>();
            component.Initialize(catalog,row,stageRoot,sourceRoot);return component;
        }).Where(c=>c!=null).ToArray();
    }
    void Initialize(OriginalVisualCatalog catalog,J row,Transform root,string sourceRoot) {
        if(initialized)throw new InvalidOperationException("Original interior blood surface already initialized");
        SourceIdentity=OriginalEnvironmentSource.Identity(row);
        string identity=row["assets"]["paintableTexture"].S;
        var texture=catalog.Data["scene"].A.Single(r=>r["script"].S=="CwPaintableMeshTexture"&&OriginalEnvironmentSource.Identity(r)==identity);
        var target=OriginalEnvironmentSource.IdentityNode(catalog,identity,root,sourceRoot);var renderer=target.GetComponent<Renderer>();
        if(renderer==null)throw new InvalidOperationException("Original interior paint Renderer missing: "+identity);
        paint=target.GetComponent<OriginalPaintTexture>()??target.gameObject.AddComponent<OriginalPaintTexture>();
        if(paint.current==null)paint.Initialize(renderer,texture["data"],null);
        var sourceProfile=catalog.Get<SpriteShape>(row["assets"]["spriteShapeProfile"].S);
        if(sourceProfile==null)throw new InvalidOperationException("Original interior blood SpriteShape profile missing");
        var shapes=root.GetComponentsInChildren<SpriteShapeController>(true).Where(s=>s.spriteShape==sourceProfile).ToArray();
        if(shapes.Length==0)throw new InvalidOperationException("Original interior blood profile is not used by any retained SpriteShapeController");
        profile=Instantiate(sourceProfile);foreach(var shape in shapes)shape.spriteShape=profile;
        originalFill=profile.fillTexture;interval=(float)row["data"]["refreshInterval"].N;initialized=true;
    }
    // Native Start 0x36c0dec invokes BeginRefreshing after .05 scaled seconds.
    void Start(){if(initialized){Invoke(nameof(BeginRefreshing),.05f);HasStarted=true;}}
    public void BeginRefreshing(){InvokeRepeating(nameof(RefreshGroundTexture),0,interval);}
    public void RefreshGroundTexture() {
        if(paint==null||paint.current==null||profile==null)return;
        if(readable!=null)Destroy(readable);
        var current=paint.current;var prior=RenderTexture.active;
        try {
            readable=new Texture2D(current.width,current.height,TextureFormat.ARGB32,false){name="Original interior ground blood readable",filterMode=current.filterMode,wrapMode=current.wrapMode};
            RenderTexture.active=current;readable.ReadPixels(new Rect(0,0,current.width,current.height),0,0,false);readable.Apply(false,false);profile.fillTexture=readable;
        }
        finally {RenderTexture.active=prior;}
    }
    void OnDestroy(){CancelInvoke();if(profile!=null){profile.fillTexture=originalFill;Destroy(profile);}if(readable!=null)Destroy(readable);}
}
