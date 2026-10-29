using System;
using System.Linq;
using Jianghu;
using UnityEngine;
using Random=UnityEngine.Random;

// CustomizeEnviroProp Start 0x36c3438 / Customize 0x36c356c.
// Each prop owns its source seed; the final flip draw is unconditional.
public sealed class OriginalEnviroProp : MonoBehaviour {
    public int chanesToDeleteOutOf10;
    public SpriteRenderer spriteRenderer;
    public Sprite[] possibleSprites=Array.Empty<Sprite>();
    public bool canBeFlipped;
    public Gradient possibleColors;
    public Transform rootTransform;
    public Vector2 randomHeight,randomPosition;
    public bool AutoCustomize,SeedNotBasedOnSiblingIndex;
    public int CurrentSeed;
    bool bound,started;
    public void Initialize(OriginalVisualCatalog catalog,J record,Transform instanceRoot,string sourceRootPath,int currentSeed) {
        if(bound)throw new InvalidOperationException("Original environment prop was already bound");
        J d=record["data"];chanesToDeleteOutOf10=d["chanesToDeleteOutOf10"].I;
        spriteRenderer=OriginalEnvironmentSource.Component<SpriteRenderer>(catalog,record,"spriteRenderer",instanceRoot,sourceRootPath);
        rootTransform=OriginalEnvironmentSource.Node(catalog,record,"rootTransform",instanceRoot,sourceRootPath);
        possibleSprites=OriginalEnvironmentSource.Sprites(catalog,record,"possibleSprites");
        possibleColors=OriginalVisualCatalog.Gradient(d["possibleColors"]);
        canBeFlipped=d["canBeFlipped"].B;AutoCustomize=d["AutoCustomize"].B;SeedNotBasedOnSiblingIndex=d["SeedNotBasedOnSiblingIndex"].B;
        randomHeight=new Vector2((float)d["randomHeight"]["min"].N,(float)d["randomHeight"]["max"].N);
        randomPosition=new Vector2((float)d["randomPosition"]["min"].N,(float)d["randomPosition"]["max"].N);
        CurrentSeed=currentSeed;bound=true;
    }
    void Start(){RunAutoCustomize();}
    // Unity calls this once at the source Start boundary, after Instantiate/OnEnable.
    public void RunAutoCustomize() {
        if(started)return;started=true;
        if(!AutoCustomize)return;
        var parent=transform.parent;
        if(parent==null)throw new InvalidOperationException("Original environment prop requires its source parent");
        int sibling=SeedNotBasedOnSiblingIndex||parent.childCount==1?parent.GetSiblingIndex():transform.GetSiblingIndex();
        Customize(possibleSprites,(float)unchecked(sibling+CurrentSeed));
    }
    public void Customize(Sprite[] sprites,float seed) {
        int sibling=transform.GetSiblingIndex();
        Random.InitState(unchecked(sibling+NativeSeedInteger(seed)));
        if(chanesToDeleteOutOf10>0&&Random.Range(0,11)<=chanesToDeleteOutOf10&&rootTransform!=null)rootTransform.gameObject.SetActive(false);
        if(sprites==null)throw new InvalidOperationException("Original prop sprite array is null");
        // Native Range runs even for a zero-length array, before the managed
        // bounds check throws. An empty source array is not a no-op branch.
        int selected=Random.Range(0,sprites.Length);
        if(selected<0||selected>=sprites.Length)throw new IndexOutOfRangeException("Original prop sprite array is empty");
        spriteRenderer.sprite=sprites[selected];
        spriteRenderer.color=possibleColors.Evaluate(Random.Range(0f,1f));
        spriteRenderer.sortingOrder=unchecked(spriteRenderer.sortingOrder+sibling);
        spriteRenderer.size+=new Vector2(0,Random.Range(randomHeight.x,randomHeight.y));
        transform.localPosition+=new Vector3(Random.Range(randomPosition.x,randomPosition.y),0,0);
        int flip=Random.Range(0,100);
        if(canBeFlipped&&flip>=51)spriteRenderer.flipX=true;
    }
    // ARM FCVTZS saturates; IL2CPP additionally special-cases positive infinity.
    public static int NativeSeedInteger(float seed) {
        if(float.IsPositiveInfinity(seed))return int.MinValue;
        if(float.IsNaN(seed))return 0;
        if(seed>=2147483648f)return int.MaxValue;
        if(seed<=-2147483648f)return int.MinValue;
        return (int)seed;
    }
}

// Source component references resolve inside a particular imported instance.
// Never fall back to similarly named objects elsewhere in the scene.
public static class OriginalEnvironmentSource {
    public static string Identity(J record)=>record["source"]["file"].S+":"+record["source"]["id"].S;
    public static Transform Path(Transform root,string sourceRootPath,string path) {
        if(path==sourceRootPath)return root;
        string prefix=sourceRootPath+"/";
        if(!path.StartsWith(prefix,StringComparison.Ordinal))throw new InvalidOperationException("Original environment reference escapes instance: "+path);
        var node=root.Find(path.Substring(prefix.Length));
        if(node==null)throw new InvalidOperationException("Original environment object is missing: "+path);
        return node;
    }
    public static Transform Node(OriginalVisualCatalog catalog,J record,string key,Transform root,string sourceRootPath) {
        string identity=record["assets"][key].S;
        if(string.IsNullOrEmpty(identity))return null;
        return IdentityNode(catalog,identity,root,sourceRootPath);
    }
    public static Transform IdentityNode(OriginalVisualCatalog catalog,string identity,Transform root,string sourceRootPath) {
        J h=catalog.Data["hierarchy"][identity];
        if(!h.Has)throw new InvalidOperationException("Original hierarchy identity is missing: "+identity+"; regenerate the source hierarchy catalog");
        if(h["root"].S!=sourceRootPath)throw new InvalidOperationException("Original environment identity escapes instance: "+identity);
        Transform node=root;
        for(int i=0;i<h["indices"].A.Count;i++) {
            int child=h["indices"][i].I;
            if(child<0||child>=node.childCount)throw new InvalidOperationException("Original environment child index is missing: "+identity);
            node=node.GetChild(child);
            if(node.name!=h["names"][i].S)throw new InvalidOperationException("Original environment hierarchy changed: "+identity+" / "+node.name);
        }
        return node;
    }
    public static T Component<T>(OriginalVisualCatalog catalog,J record,string key,Transform root,string sourceRootPath) where T:UnityEngine.Component {
        var node=Node(catalog,record,key,root,sourceRootPath);
        var result=node==null?null:node.GetComponent<T>();
        if(result==null)throw new InvalidOperationException("Original environment component is missing: "+key);
        return result;
    }
    public static Sprite[] Sprites(OriginalVisualCatalog catalog,J record,string key) {
        return DataField(record["data"],key).A.Select((_,i)=>catalog.Get<Sprite>(record["assets"][key+"["+i+"]"].S)).ToArray();
    }
    // Asset binding keys are flattened (stages[1].bigBuildings.center[0]),
    // while serialized data remains nested. J's indexer is a literal key.
    public static J DataField(J data,string path) {
        J value=data;int cursor=0;
        while(cursor<path.Length) {
            if(path[cursor]=='.'){cursor++;continue;}
            if(path[cursor]=='[') {
                int end=path.IndexOf(']',cursor+1);
                if(end<0||!int.TryParse(path.Substring(cursor+1,end-cursor-1),out int index))throw new InvalidOperationException("Invalid original serialized array path: "+path);
                value=value[index];cursor=end+1;
            } else {
                int end=cursor;while(end<path.Length&&path[end]!='.'&&path[end]!='[')end++;
                value=value[path.Substring(cursor,end-cursor)];cursor=end;
            }
            if(!value.Has)throw new InvalidOperationException("Original serialized field is missing: "+path);
        }
        return value;
    }
    public static OriginalEnviroProp Attach(OriginalVisualCatalog catalog,J record,Transform root,string sourceRootPath,int currentSeed) {
        var node=IdentityNode(catalog,Identity(record),root,sourceRootPath);
        var prop=node.GetComponent<OriginalEnviroProp>();
        if(prop==null){prop=node.gameObject.AddComponent<OriginalEnviroProp>();prop.Initialize(catalog,record,root,sourceRootPath,currentSeed);}
        else prop.CurrentSeed=currentSeed;
        return prop;
    }
    public static void AttachAutoProps(OriginalVisualCatalog catalog,Transform root,string sourceRootPath,int currentSeed) {
        foreach(var record in catalog.Data["scene"].A.Where(r=>r["script"].S=="CustomizeEnviroProp"&&(r["scene_path"].S==sourceRootPath||r["scene_path"].S.StartsWith(sourceRootPath+"/",StringComparison.Ordinal))))
            Attach(catalog,record,root,sourceRootPath,currentSeed);
    }
}
