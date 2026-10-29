using System.Linq;
using Jianghu;
using UnityEngine;
using UnityEngine.U2D;
using Random=UnityEngine.Random;

// CustomizeGroundSpriteShapes 0x36c3938: original splines are retained and
// modified with the source point-specific seeds. The official SpriteShape
// package builds the same spline geometry instead of a replacement floor.
public sealed class OriginalGroundSource : MonoBehaviour {
    public int seed;
    public bool snow;
    public float SourceSeed {get;private set;}
    OriginalVisualCatalog catalog;
    J settings;
    OriginalPaintTexture groundPaint;
    SpriteShapeController bloodShape;
    SpriteShape bloodProfile;
    Texture2D readable;
    float refreshInterval=.5f;
    bool initialized;
    public bool IsInitialized=>initialized;
    public bool SkippedInactiveSource {get;private set;}
    string sourceRootName;
    // Source OnEnable computes float(CurrentSeed + this component's sibling).
    // Called synchronously after the stage Instantiate, before big buildings.
    public void ApplyEnvironmentContext(OriginalVisualCatalog visualCatalog,int currentSeed,bool christmasMode,string originalSourceRootName=null) {
        if(initialized)throw new System.InvalidOperationException("Original floor was already customized; instantiate the next source stage before applying its context");
        catalog=visualCatalog;
        string sourceRoot=originalSourceRootName??name;sourceRootName=sourceRoot;
        var record=catalog.Data["scene"].A.FirstOrDefault(x=>x["script"].S=="CustomizeGroundSpriteShapes"&&x["scene_path"].S.StartsWith(sourceRoot+"/",System.StringComparison.Ordinal));
        if(record==null)throw new System.InvalidOperationException("Original floor record is missing: "+sourceRoot);
        var node=OriginalEnvironmentSource.IdentityNode(catalog,OriginalEnvironmentSource.Identity(record),transform,sourceRoot);
        // All four source throne prefabs retain an inactive outdoor Ground.
        // Its native OnEnable does not run and must consume no floor RNG.
        if(!node.gameObject.activeInHierarchy||!record["data"]["m_Enabled"].B){SkippedInactiveSource=true;initialized=true;return;}
        SourceSeed=(float)unchecked(currentSeed+node.GetSiblingIndex());
        seed=OriginalEnviroProp.NativeSeedInteger(SourceSeed);snow=christmasMode;
        Initialize(record);
    }
    void Start() {
        if(initialized)return;
        if(catalog==null)catalog=Resources.Load<OriginalVisualCatalog>("OriginalVisualCatalog");
        var record=catalog.Data["scene"].A.FirstOrDefault(x=>x["script"].S=="CustomizeGroundSpriteShapes"&&x["scene_path"].S.StartsWith(name+"/",System.StringComparison.Ordinal));
        if(record==null)return;sourceRootName=name;
        var node=OriginalEnvironmentSource.IdentityNode(catalog,OriginalEnvironmentSource.Identity(record),transform,sourceRootName);
        if(!node.gameObject.activeInHierarchy||!record["data"]["m_Enabled"].B){SkippedInactiveSource=true;initialized=true;return;}
        SourceSeed=seed;Initialize(record);
    }
    void Initialize(J record) {
        settings=record;initialized=true;
        if(record["data"]["customizeOnEnable"].B)Customize();
        var paintRecord=catalog.Data["scene"].A.FirstOrDefault(x=>x["script"].S=="CwPaintableMeshTexture"&&x["source"]["file"].S+":"+x["source"]["id"].S==record["assets"]["BloodOnTheGroundP3D"].S);
        var paintNode=Node(record,"BloodOnTheGroundPaintableMesh");
        if(paintRecord!=null&&paintNode!=null) {
            paintNode.gameObject.SetActive(true);groundPaint=paintNode.gameObject.AddComponent<OriginalPaintTexture>();
            groundPaint.Initialize(paintNode.GetComponent<Renderer>(),paintRecord["data"],null);
        }
        var customizer=catalog.Data["scene"].A.SingleOrDefault(x=>x["script"].S=="BloodOnTheGroundCustomizer"&&x["scene_path"].S.StartsWith(sourceRootName+"/",System.StringComparison.Ordinal)&&
            x["assets"]["paintableTexture"].S==record["assets"]["BloodOnTheGroundP3D"].S);
        var bloodNode=Node(record,"BloodOnTheGroundSpriteShape");bloodShape=bloodNode==null?null:bloodNode.GetComponent<SpriteShapeController>();
        if(customizer!=null&&bloodShape!=null&&bloodShape.spriteShape!=null) {
            bloodProfile=Instantiate(bloodShape.spriteShape);bloodShape.spriteShape=bloodProfile;refreshInterval=(float)customizer["data"]["refreshInterval"].N;
            InvokeRepeating(nameof(RefreshGroundTexture),0,refreshInterval);
        }
    }
    Transform Node(J record,string key) {
        string identity=record["assets"][key].S;
        if(string.IsNullOrEmpty(identity))return null;
        return OriginalEnvironmentSource.IdentityNode(catalog,identity,transform,sourceRootName);
    }
    void Customize() {
        J d=settings["data"];Random.InitState(seed);
        int count=d["spriteShapeControllers"].A.Count;
        for(int controllerIndex=0;controllerIndex<count;controllerIndex++) {
            var node=Node(settings,"spriteShapeControllers["+controllerIndex+"]");if(node==null)continue;
            var controller=node.GetComponent<SpriteShapeController>();if(controller==null)continue;
            if(snow) {
                string profile=controllerIndex==1?"Floor_Snow_0":controllerIndex==2?"Floor_Snow_Shrubbery":controllerIndex==3?"Floor_Snow_1":controllerIndex==5?"Floor_Snow_2":controllerIndex==6?"Floor_Snow_3":null;
                if(profile!=null)controller.spriteShape=catalog.Get<SpriteShape>(settings["assets"][profile].S);
            }
            var spline=controller.spline;
            for(int point=0;point<7&&point<spline.GetPointCount();point++) {
                var position=spline.GetPosition(point);
                if(point==3)position.y=0;
                else {Random.InitState(OriginalEnviroProp.NativeSeedInteger(SourceSeed+point));position.y+=Random.Range((float)d["pointRandomY"]["min"].N,(float)d["pointRandomY"]["max"].N);}
                position.z=0;spline.SetPosition(point,position);
            }
            if(controllerIndex==5||controllerIndex==6)for(int point=0;point<7&&point<spline.GetPointCount();point++) {
                float height=1;
                if(point!=3){Random.InitState(OriginalEnviroProp.NativeSeedInteger(SourceSeed+point));height=Random.Range((float)d["pointRandomHeight"]["min"].N,(float)d["pointRandomHeight"]["max"].N)/(controllerIndex==6?1.15f:1);}
                spline.SetHeight(point,height);
            }
            controller.RefreshSpriteShape();
        }
        var gradient=OriginalVisualCatalog.Gradient(d["possibleColors"]);
        for(int i=0;i<d["spriteRenderers"].A.Count;i++){var node=Node(settings,"spriteRenderers["+i+"]");if(node!=null){var renderer=node.GetComponent<SpriteShapeRenderer>();if(renderer!=null)renderer.color=gradient.Evaluate(Random.Range(0f,1f));}}
    }
    void RefreshGroundTexture() {
        if(groundPaint==null||groundPaint.current==null||bloodProfile==null)return;
        var texture=groundPaint.current;var active=RenderTexture.active;
        try {
            if(readable!=null)Destroy(readable);
            readable=new Texture2D(texture.width,texture.height,TextureFormat.ARGB32,false){name="Original ground blood readable",filterMode=texture.filterMode,wrapMode=texture.wrapMode};
            RenderTexture.active=texture;readable.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0,false);readable.Apply(false,false);bloodProfile.fillTexture=readable;
        }
        finally{RenderTexture.active=active;}
    }
    void OnDestroy(){if(readable!=null)Destroy(readable);if(bloodProfile!=null)Destroy(bloodProfile);}
}
