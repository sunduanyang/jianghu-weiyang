using System.Collections.Generic;
using UnityEngine;

namespace Jianghu {
public partial class JianghuApp {
    struct CityQueueItem {public Vector2Int Cell;public int Cost;public CityQueueItem(Vector2Int cell,int cost){Cell=cell;Cost=cost;}}
    readonly List<CityQueueItem> cityQueue=new List<CityQueueItem>();
    static bool CityQueueBefore(CityQueueItem a,CityQueueItem b)=>a.Cost!=b.Cost?a.Cost<b.Cost:a.Cell.x!=b.Cell.x?a.Cell.x<b.Cell.x:a.Cell.y<b.Cell.y;
    void CityQueuePush(Vector2Int cell,int cost){
        var item=new CityQueueItem(cell,cost);int i=cityQueue.Count;cityQueue.Add(item);
        while(i>0){int parent=(i-1)/2;if(!CityQueueBefore(item,cityQueue[parent]))break;cityQueue[i]=cityQueue[parent];i=parent;}cityQueue[i]=item;
    }
    CityQueueItem CityQueuePop(){
        var result=cityQueue[0];int last=cityQueue.Count-1;var item=cityQueue[last];cityQueue.RemoveAt(last);
        if(last>0){int i=0;while(i*2+1<last){int child=i*2+1;if(child+1<last&&CityQueueBefore(cityQueue[child+1],cityQueue[child]))child++;
            if(!CityQueueBefore(cityQueue[child],item))break;cityQueue[i]=cityQueue[child];i=child;}cityQueue[i]=item;}return result;
    }
    void BuildCityPaths(Vector2Int start,Dictionary<Vector2Int,int> costs,Dictionary<Vector2Int,Vector2Int> previous,Vector2Int? goal=null){
        costs.Clear();previous.Clear();cityQueue.Clear();if(!CityInside(start))return;
        costs[start]=0;CityQueuePush(start,0);
        while(cityQueue.Count>0){var item=CityQueuePop();var p=item.Cell;
            if(costs[p]!=item.Cost)continue;if(goal.HasValue&&p==goal.Value)break;
            foreach(var step in CitySteps){var q=p+step;if(!CityInside(q)||CityBlocked(CityTile(q.x,q.y)))continue;
                int cost=item.Cost+CityStepCost(CityTile(q.x,q.y));if(costs.TryGetValue(q,out int old)&&old<=cost)continue;
                costs[q]=cost;previous[q]=p;CityQueuePush(q,cost);
            }
        }
    }
}
}
