using System;
using System.Linq;
using Jianghu;
using UnityEngine;

public static class OriginalEnvironmentSpecialSource {
    static J source;
    public static J Data {
        get {
            if(source==null) {
                var asset=Resources.Load<TextAsset>("OriginalEnvironmentSpecialSource");
                if(asset==null)throw new InvalidOperationException("OriginalEnvironmentSpecialSource is missing");
                source=J.Parse(asset.text);
            }
            return source;
        }
    }
    public static J Record(string identity)=>Data["records"].A.Single(r=>r["identity"].S==identity);
    public static Transform Resolve(J hierarchy,Transform root,string sourceRoot,string identity) {
        if(!hierarchy.Has||hierarchy["root"].S!=sourceRoot)throw new InvalidOperationException("Original special reference escapes source instance: "+identity);
        var node=root;
        for(int i=0;i<hierarchy["indices"].A.Count;i++) {
            int index=hierarchy["indices"][i].I;
            if(index<0||index>=node.childCount)throw new InvalidOperationException("Original special child index missing: "+identity);
            node=node.GetChild(index);
            if(node.name!=hierarchy["names"][i].S)throw new InvalidOperationException("Original special hierarchy changed: "+identity);
        }
        return node;
    }
    public static Transform Node(J row,string field,Transform root,string sourceRoot) {
        string identity=row["assets"][field].S;
        return Resolve(Data["references"][identity]["hierarchy"],root,sourceRoot,identity);
    }
    public static T Component<T>(J row,string field,Transform root,string sourceRoot) where T:UnityEngine.Component {
        string identity=row["assets"][field].S;var node=Node(row,field,root,sourceRoot);
        int ordinal=Data["references"][identity]["component_index"].I;var components=node.GetComponents<T>();
        if(ordinal>=components.Length)throw new InvalidOperationException("Original special component ordinal missing: "+identity);
        return components[ordinal];
    }
}
