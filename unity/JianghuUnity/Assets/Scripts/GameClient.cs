using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using UnityEngine;
using UnityEngine.Networking;

namespace Jianghu {
public class GameClient : MonoBehaviour {
    public J World = new J();
    public string Status = "正在展开山河…", Error = "", SaveDir;
    public bool Ready, Busy;
    public int Revision;
    public Func<J> PhysicsQuitFrame;
    public int StateRefreshRequests {get;private set;}
    int heroUpdateRevision;
    string url, session;
    Process companion;
    bool closing;
    public static string Arg(string key, string fallback = "") { var a=Environment.GetCommandLineArgs(); for(int i=0;i<a.Length-1;i++)if(a[i]==key)return a[i+1];return fallback; }
    IEnumerator Start() {
        Application.runInBackground = true;
        Application.targetFrameRate = 60;
        SaveDir = Arg("-saveDir", Path.Combine(Application.persistentDataPath,"Saves"));
        Directory.CreateDirectory(SaveDir);
        session=Guid.NewGuid().ToString("N");
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
        url="http://127.0.0.1:"+port;
        string python=Arg("-python",Environment.GetEnvironmentVariable("JIANGHU_PYTHON")??"");
        if(string.IsNullOrEmpty(python)) {
            string bundled=Path.Combine(Application.streamingAssetsPath,"Python","python.exe");
            python=File.Exists(bundled)?bundled:"python";
        }
        string script=Path.Combine(Application.streamingAssetsPath,"Backend","unity_bridge.py");
        try {
            companion=new Process();companion.StartInfo=new ProcessStartInfo(python,"-B -u "+Q(script)+" --port "+port+" --session "+session+" --save-dir "+Q(SaveDir)+" --parent "+Process.GetCurrentProcess().Id) {UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.GetDirectoryName(script),RedirectStandardError=true,RedirectStandardOutput=true};
            companion.StartInfo.EnvironmentVariables["PYTHONUTF8"]="1";
            companion.ErrorDataReceived+=(s,e)=>{if(!string.IsNullOrEmpty(e.Data))File.AppendAllText(Path.Combine(SaveDir,"backend-error.log"),e.Data+"\n");};
            companion.OutputDataReceived+=(s,e)=>{if(!string.IsNullOrEmpty(e.Data))File.AppendAllText(Path.Combine(SaveDir,"backend.log"),e.Data+"\n");};
            companion.Start();companion.BeginErrorReadLine();companion.BeginOutputReadLine();
        } catch(Exception e) {Error="无法启动模拟引擎："+e.Message+"。请设置 JIANGHU_PYTHON。";yield break;}
        float deadline=Time.realtimeSinceStartup+90;
        while(Time.realtimeSinceStartup<deadline) {
            bool ok=false;
            yield return Request("/api/unity/health",null,j=>ok=j["session"].S==session,false);
            if(ok){Ready=true;break;}
            if(companion.HasExited){Error="模拟引擎启动失败，请查看存档目录 backend-error.log";yield break;}
            yield return new WaitForSecondsRealtime(.4f);
        }
        if(!Ready){Error="启动超时，请查看模拟引擎日志。";yield break;}
        Error="";yield return Refresh();Status="江湖已就绪 · 独立存档";
        while(!closing){yield return new WaitForSecondsRealtime(1.5f);if(!Busy&&!GetComponent<JianghuApp>().PhysicsBattleVisible)yield return Refresh();}
    }
    static string Q(string s)=>"\""+s.Replace("\"", "")+"\"";
    public IEnumerator Request(string path,J body,Action<J> done,bool report=true) {
        using(var req=new UnityWebRequest(url+path,body==null?"GET":"POST")) {
            // A full-population month and its SQLite checkpoint can outlast a
            // normal interaction. State reads wait behind that same world lock.
            bool monthEnd=path=="/api/hero"&&body!=null&&body["action"].S=="end_turn";
            req.downloadHandler=new DownloadHandlerBuffer();req.timeout=monthEnd||path=="/api/state"?180:60;
            req.SetRequestHeader("X-Unity-Session",session);
            if(body!=null){req.uploadHandler=new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body.ToString()));req.SetRequestHeader("Content-Type","application/json");}
            yield return req.SendWebRequest();
            J result=new J();
            try{result=J.Parse(req.downloadHandler.text);}catch{if(report)Error="接口返回异常："+req.error;}
            if(req.result!=UnityWebRequest.Result.Success || result["error"].Has){if(report)Error=result["error"].Has?result["error"].S:req.error;}
            else {if(report)Error="";done?.Invoke(result);}
        }
    }
    public IEnumerator Refresh(){
        StateRefreshRequests++;
        int revision=heroUpdateRevision;
        yield return Request("/api/state",null,j=>{
            // Do not let an older snapshot undo a newer walking response.
            if(heroUpdateRevision!=revision)return;
            World=j;Revision++;
        });
    }
    public void ApplyHeroStep(J hero){
        World["hero"]=hero;World["player"]["protagonist"]=hero;
        if(hero["journal"].A.Count>0)World["events"]=new J(Mathf.Max(World["events"].I,hero["journal"][0]["id"].I));
        heroUpdateRevision++;Revision++;
    }
    public void Fetch(string path,Action<J> done) {StartCoroutine(Request(path,null,done));}
    public void Act(string path,J body,Action<J> done=null) {if(!Busy&&Ready)StartCoroutine(Action(path,body,done));}
    public IEnumerator Action(string path,J body,Action<J> done=null) {
        Busy=true;Status="正在结算…";
        yield return Request(path,body,done);
        yield return Refresh();Busy=false;Status=string.IsNullOrEmpty(Error)?"已结算并保存":Error;
    }
    public J HeroBody(string action,params object[] extra) {var j=J.O("action",action,"revision",World["hero"]["revision"],"month",World["month"]);for(int i=0;i<extra.Length;i+=2)j[(string)extra[i]]=extra[i+1] as J??new J(extra[i+1]);return j;}
    void OnApplicationQuit(){StopCompanion();}
    void OnDestroy(){StopCompanion();}
    void SavePhysicsBeforeShutdown(){
        try{
            J payload=PhysicsQuitFrame?.Invoke();if(payload==null)return;
            var read=(HttpWebRequest)WebRequest.Create(url+"/api/martial");read.Headers["X-Unity-Session"]=session;read.Timeout=5000;
            J state;using(var response=read.GetResponse())using(var reader=new StreamReader(response.GetResponseStream()))state=J.Parse(reader.ReadToEnd());
            if(state["battle"]["id"].S!=payload["battle_id"].S||state["battle"]["status"].S!="active")return;
            payload["revision"]=state["battle"]["revision"];
            var req=(HttpWebRequest)WebRequest.Create(url+"/api/battle");req.Method="POST";req.Headers["X-Unity-Session"]=session;req.ContentType="application/json";req.Timeout=5000;
            var bytes=System.Text.Encoding.UTF8.GetBytes(payload.ToString());req.ContentLength=bytes.Length;
            using(var stream=req.GetRequestStream())stream.Write(bytes,0,bytes.Length);using(var response=req.GetResponse()){}
        }catch(Exception e){File.AppendAllText(Path.Combine(SaveDir,"backend-error.log"),"Physics quit checkpoint: "+e.Message+"\n");}
    }
    void StopCompanion() {
        if(closing)return;closing=true;
        if(companion==null)return;
        try{if(companion.HasExited)return;
            SavePhysicsBeforeShutdown();
            var req=(HttpWebRequest)WebRequest.Create(url+"/api/unity/shutdown");req.Method="POST";req.Headers["X-Unity-Session"]=session;req.ContentLength=2;req.Timeout=3000;
            using(var s=req.GetRequestStream()){var b=System.Text.Encoding.UTF8.GetBytes("{}");s.Write(b,0,b.Length);}using(var r=req.GetResponse()){}
            if(!companion.WaitForExit(4000))companion.Kill();
        }catch{try{if(!companion.HasExited)companion.Kill();}catch{}}
        companion.Dispose();
    }
}
}
