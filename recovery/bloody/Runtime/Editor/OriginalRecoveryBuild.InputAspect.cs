using System;
using System.IO;
using Jianghu;
using UnityEngine;

public static partial class OriginalRecoveryBuild {
    public static void ProbeInputAspect() {
        var args=Environment.GetCommandLineArgs();
        int At(string key)=>Array.IndexOf(args,key);
        int w=int.Parse(At("-probeWidth")>=0?args[At("-probeWidth")+1]:"1280");
        int h=int.Parse(At("-probeHeight")>=0?args[At("-probeHeight")+1]:"720");
        var resource=Resources.Load<TextAsset>("OriginalInputSource");var source=J.Parse(resource.text);
        float wf=w,hf=h,threshold=(float)source["tablet_canvas"]["aspect_threshold"].N;
        bool expression=wf/hf<threshold;float ratio=wf/hf;bool local=ratio<threshold;
        int bits=BitConverter.SingleToInt32Bits(ratio);bool narrowed=BitConverter.Int32BitsToSingle(bits)<threshold;
        var input=new OriginalInputAdapter(source);input.SetViewport(w,h);
        string directory=args[At("-probeOutput")+1];Directory.CreateDirectory(directory);
        var result=J.O("unity",Application.unityVersion,"width",w,"height",h,"direct_less",expression,"local_less",local,"narrowed_less",narrowed,
            "ratio_bits",bits,"threshold_bits",BitConverter.SingleToInt32Bits(threshold),"ratio",ratio,"double_ratio",(double)w/h,
            "reference_x",input.ReferenceResolution.x,"canvas_scale",input.CanvasScale);
        File.WriteAllText(Path.Combine(directory,"original-input-aspect.json"),result.ToString());Debug.Log("ORIGINAL_INPUT_ASPECT "+result);
    }
}
