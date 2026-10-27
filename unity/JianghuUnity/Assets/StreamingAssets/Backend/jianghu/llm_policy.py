"""Model-authored, bounded preferences that survive a short action plan."""
from copy import deepcopy


def current(w, n):
    p=n.get('llm_policy')
    return p if (p and w.month<p['until'] and p['place']==n['place']
                 and p['faction']==n['faction'] and p['job']==n['job']) else None


def validate(ctx, value):
    if value is None:return True
    if not isinstance(value,dict) or not isinstance(value.get('title'),str) or not 1<=len(value['title'])<=90:return False
    rows=value.get('priorities');allowed={a['kind'] for a in ctx['allowed_actions']}
    if not isinstance(rows,list) or not 1<=len(rows)<=4:return False
    if any(not isinstance(r,dict) or r.get('kind') not in allowed or type(r.get('weight')) is not int or not -18<=r['weight']<=18 or r['weight']==0 for r in rows):return False
    return len({r['kind'] for r in rows})==len(rows)


def apply(w,n,ctx,value):
    if value is None:return
    n['llm_policy']=dict(title=value['title'],priorities=deepcopy(value['priorities']),
                         created=w.month,until=w.month+12,place=n['place'],job=n['job'],
                         faction=n['faction'],request_month=ctx['month'],source='LLM')


def adjustment(w,n,a):
    policy=current(w,n)
    return next((r['weight'] for r in policy['priorities'] if r['kind']==a['kind']),0) if policy else 0


def participates(w,n,actions):
    return bool(current(w,n) and any(adjustment(w,n,a)!=0 for a in actions))


def view(w,n):
    policy=deepcopy(n.get('llm_policy'))
    if policy:
        from .engine import ACTIONS
        policy['active']=current(w,n) is not None
        for row in policy['priorities']:row['label']=ACTIONS.get(row['kind'],row['kind'])
    return policy
