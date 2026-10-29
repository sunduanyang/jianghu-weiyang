using System;
using System.Linq;
using Jianghu;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Read-only evidence to distinguish hidden UI from camera-capture/layout
// effects. It does not activate objects or alter pivots/fonts/animations.
public static class OriginalHudDiagnostics {
    static J V(Vector3 v)=>J.O("x",v.x,"y",v.y,"z",v.z);
    static J R(Rect r)=>J.O("x",r.x,"y",r.y,"width",r.width,"height",r.height);
    static string Path(Transform node){string path=node.name;while(node.parent!=null){node=node.parent;path=node.name+"/"+path;}return path;}
    public static J Capture(OriginalBattleHud hud) {
        if(hud==null)return J.O("missing",true);
        var transforms=hud.GetComponentsInChildren<RectTransform>(true).Where(rect=> {
            string path=Path(rect);return rect.name=="Canvas"||rect.name=="MainContainer"||rect.name=="TopBar_InGame"||path.Contains("/TopBar_InGame/")||rect.name=="Text_TutorialExplainer_Touch"||rect.name=="TopPart";
        });
        var rows=transforms.Select(rect=> {
            var canvas=rect.GetComponentInParent<Canvas>();var group=rect.GetComponent<CanvasGroup>();var graphic=rect.GetComponent<Graphic>();var text=rect.GetComponent<TMP_Text>();var animation=rect.GetComponent<Animation>();
            var corners=new Vector3[4];rect.GetWorldCorners(corners);Camera camera=canvas==null||canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera;
            var points=corners.Select(point=>RectTransformUtility.WorldToScreenPoint(camera,point)).ToArray();
            var row=J.O("path",Path(rect),"active_self",rect.gameObject.activeSelf,"active_hierarchy",rect.gameObject.activeInHierarchy,
                "rect",R(rect.rect),"pivot",V(rect.pivot),"anchored_position",V(rect.anchoredPosition),"local_scale",V(rect.localScale),"lossy_scale",V(rect.lossyScale),
                "screen_corners",new J(points.Select(point=>V(point)).ToList()),
                "canvas_scale",canvas==null?0:canvas.scaleFactor,"canvas_enabled",canvas!=null&&canvas.enabled,"canvas_mode",canvas==null?"missing":canvas.renderMode.ToString(),
                "group_alpha",group==null?1:group.alpha,"graphic_enabled",graphic!=null&&graphic.enabled,"graphic_alpha",graphic==null?0:graphic.color.a,"graphic_culled",graphic!=null&&graphic.canvasRenderer.cull);
            if(animation!=null)row["animation"]=J.O("enabled",animation.enabled,"playing",animation.isPlaying,"auto",animation.playAutomatically,"default",animation.clip==null?"missing":animation.clip.name,
                "states",new J(animation.Cast<AnimationState>().Select(state=>J.O("clip",state.name,"time",state.time,"length",state.length,"enabled",state.enabled,"weight",state.weight)).ToList()));
            if(text!=null)row["text"]=J.O("value",text.text,"font_size",text.fontSize,"auto_size",text.enableAutoSizing,"size_min",text.fontSizeMin,"size_max",text.fontSizeMax,"wrap",text.textWrappingMode.ToString(),"font",text.font==null?"missing":text.font.name,"material",text.fontSharedMaterial==null?"missing":text.fontSharedMaterial.name,"text_bounds",R(new Rect(text.textBounds.min.x,text.textBounds.min.y,text.textBounds.size.x,text.textBounds.size.y)));
            return row;
        }).ToList();
        var scalers=hud.GetComponentsInChildren<CanvasScaler>(true).Select(s=>J.O("path",Path(s.transform),"enabled",s.enabled,"reference",V(s.referenceResolution),"mode",s.uiScaleMode.ToString(),"match_mode",s.screenMatchMode.ToString()));
        return J.O("frame",Time.frameCount,"mode",hud.preview.GameMode,"state",hud.preview.OriginalState,"time_scale",Time.timeScale,"nodes",new J(rows),"scalers",new J(scalers.ToList()));
    }
}
