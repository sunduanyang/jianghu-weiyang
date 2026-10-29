using System;
using System.Collections.Generic;
using Jianghu;
using UnityEngine;

// Source identities remain attached to imported Unity objects. No art is redrawn.
public sealed class OriginalVisualCatalog : ScriptableObject {
    [Serializable] public struct Entry {public string identity;public UnityEngine.Object asset;}
    public Entry[] entries=Array.Empty<Entry>();
    public TextAsset sourceJson;
    public Shader[] recoveredShaders=Array.Empty<Shader>();
    Dictionary<string,UnityEngine.Object> index;
    J data;
    Dictionary<string,Shader> shaderIndex;
    public J Data=>data??(data=J.Parse(sourceJson.text));
    public T Get<T>(string identity) where T:UnityEngine.Object {
        if(index==null){index=new Dictionary<string,UnityEngine.Object>();foreach(var e in entries)index[e.identity]=e.asset;}
        return index.TryGetValue(identity,out var value)?value as T:null;
    }
    public Shader RecoveredShader(string name) {
        if(shaderIndex==null){shaderIndex=new Dictionary<string,Shader>();foreach(var shader in recoveredShaders)if(shader!=null)shaderIndex[shader.name]=shader;}
        if(shaderIndex.TryGetValue(name,out var value))return value;
        throw new InvalidOperationException("Original recovered shader was not bound: "+name);
    }
    public static Color Color(J j)=>new Color((float)j["r"].N,(float)j["g"].N,(float)j["b"].N,(float)j["a"].N);
    public static Gradient Gradient(J j) {
        var colors=new GradientColorKey[j["m_NumColorKeys"].I];
        var alphas=new GradientAlphaKey[j["m_NumAlphaKeys"].I];
        for(int i=0;i<colors.Length;i++)colors[i]=new GradientColorKey(Color(j["key"+i]),(float)j["ctime"+i].N/65535);
        for(int i=0;i<alphas.Length;i++)alphas[i]=new GradientAlphaKey((float)j["key"+i]["a"].N,(float)j["atime"+i].N/65535);
        var result=new Gradient();result.SetKeys(colors,alphas);result.mode=(GradientMode)j["m_Mode"].I;return result;
    }
}
