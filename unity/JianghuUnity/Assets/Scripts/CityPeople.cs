using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jianghu {
public partial class JianghuApp {
    readonly Dictionary<Vector2Int,List<J>> cityCellPeople=new Dictionary<Vector2Int,List<J>>();
    readonly Dictionary<int,J> cityPersonViews=new Dictionary<int,J>();
    static readonly List<J> EmptyCityPeople=new List<J>();

    void IndexCityPeople(){
        cityCellPeople.Clear();cityPersonViews.Clear();
        foreach(var person in W["npcs"].A)cityPersonViews[person["id"].I]=person;
        // Each saved weekly position belongs to one real cell.
        var seen=new HashSet<int>();
        foreach(var node in City["nodes"].A){
            foreach(var resident in node["residents"].A){
                var at=new Vector2Int(resident["row"].Has?resident["row"].I:node["row"].I,resident["col"].Has?resident["col"].I:node["col"].I);
                if(!CityInside(at))continue;
                if(!seen.Add(resident["id"].I))continue;
                if(!cityCellPeople.TryGetValue(at,out var people))cityCellPeople[at]=people=new List<J>();
                people.Add(resident);
            }
        }
    }
    List<J> CityPeopleAt(Vector2Int at)=>CityInside(at)&&cityCellPeople.TryGetValue(at,out var people)?people:EmptyCityPeople;
    void SelectCityCell(Vector2Int at){
        if(chosenRow!=at.x||chosenCol!=at.y){rosterScroll=Vector2.zero;mainScroll=Vector2.zero;query="";}
        chosenRow=at.x;chosenCol=at.y;
        if(CityInside(at)){rosterMode="人物";rosterOpen=true;}
    }
    void DrawCityCellRoster(){
        if(cityLoading||!City.Has||City["place"].I!=SelectedPlace){T("正在展开城中街巷…",small);return;}
        var at=new Vector2Int(chosenRow,chosenCol);
        if(!CityInside(at)){T("选择地格查看人物",sub);T("单击地图中的一格，即可查看此格人物。",small);return;}
        var people=CityPeopleAt(at);bool heroHere=CityLocal&&Hero["row"].I==at.x&&Hero["col"].I==at.y;
        T(CityCoordinate(at)+" · 此格人物 · "+(people.Count+(heroHere?1:0))+"人",sub);
        if(heroHere)T(Hero["name"].S+" · 主角在此",small);
        if(people.Count==0){T(heroHere?"此格暂无其他人物":"此格暂无人物",small);return;}
        GUI.SetNextControlName("RosterSearch");string nextQuery=GUILayout.TextField(query,field);
        if(nextQuery!=query){query=nextQuery;rosterScroll=Vector2.zero;}
        var rows=people.Where(p=>string.IsNullOrEmpty(query)||p["name"].S.Contains(query)||p["job"].S.Contains(query)).ToList();
        T(rows.Count+"人 · 点击与其交谈",small);
        if(rows.Count==0){T("没有符合搜索的人物",small);return;}
        const float rowHeight=60;
        rosterScroll=GUILayout.BeginScrollView(rosterScroll);
        // Keep one layout entry regardless of scroll position; changing the
        // visible row count during Repaint otherwise invalidates IMGUI layout.
        Rect content=GUILayoutUtility.GetRect(200,rows.Count*rowHeight,GUILayout.ExpandWidth(true));
        int first=Mathf.Clamp((int)(rosterScroll.y/rowHeight)-1,0,rows.Count-1),last=Mathf.Min(rows.Count,first+14);
        for(int i=first;i<last;i++){
            var resident=rows[i];int id=resident["id"].I;
            Rect row=new Rect(content.x,content.y+i*rowHeight,content.width,rowHeight);
            if(GUI.Button(row,GUIContent.none,id==SelectedPerson?rowOnStyle:rowStyle))TalkToPerson(id);
            float left=row.x+8;
            bool hasPerson=cityPersonViews.TryGetValue(id,out var person);
            if(hasPerson){Portrait(new Rect(row.x+6,row.y+6,38,48),id,person["sex"].S,person["age_months"].I);left=row.x+52;}
            GUI.Label(new Rect(left,row.y+6,row.xMax-left-4,24),resident["name"].S+(hasPerson?"  "+person["age_months"].I/12+"岁":""),sub);
            GUI.Label(new Rect(left,row.y+31,row.xMax-left-4,22),resident["job"].S+(hasPerson?" · "+person["identity"]["grade_label"].S:""),small);
        }
        GUILayout.EndScrollView();
    }
}
}
