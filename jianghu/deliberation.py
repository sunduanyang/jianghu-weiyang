"""Short, grounded alternative comparisons for two-stage NPC decisions."""


def schema(ctx):
    ids=[a['id'] for a in ctx['allowed_actions']];memories=[m['event_id'] for m in ctx['memories']]
    player=(ctx.get('player_contact') or {}).get('last_event',0)
    player=player if player in memories else 0
    return dict(type='object',properties={
        'focus':dict(type='string',maxLength=90),
        'alternatives':dict(type='array',minItems=min(2,len(ids)),maxItems=min(3,len(ids)),items=dict(type='object',properties={
            'action_id':dict(type='string',enum=ids),'benefit':dict(type='string',maxLength=60),'risk':dict(type='string',maxLength=60)},
            required=['action_id','benefit','risk'],additionalProperties=False)),
        'fallback_action_id':dict(type='string',enum=ids),'fallback_reason':dict(type='string',maxLength=90),
        'player_stance':dict(type='string',enum=['接受建议','保留意见','拒绝建议','寻求合作','暂缓承诺','先顾自身'] if ctx.get('player_contact') else ['暂无直接往来']),
        'player_consideration':dict(type='string',**({'maxLength':90} if ctx.get('player_contact') else {'enum':['暂无直接往来']})),
        'player_memory_id':dict(type='integer',enum=[player]),
        'memory_ids':dict(type='array',maxItems=min(3,len(memories)),items=dict(type='integer',enum=memories or [0]))},
        required=['focus','alternatives','fallback_action_id','fallback_reason','player_stance','player_consideration','player_memory_id','memory_ids'],additionalProperties=False)


def valid(ctx,value):
    if not isinstance(value,dict):return False
    ids={a['id'] for a in ctx['allowed_actions']};known={m['event_id'] for m in ctx['memories']}
    for key,maximum in (('focus',90),('fallback_reason',90),('player_consideration',90)):
        if not isinstance(value.get(key),str) or not 1<=len(value[key])<=maximum:return False
    if value.get('player_stance') not in schema(ctx)['properties']['player_stance']['enum']:return False
    if not ctx.get('player_contact') and value['player_consideration']!='暂无直接往来':return False
    if ctx.get('player_contact') and any(denial in value['player_consideration'] for denial in ('暂无直接往来','没有直接往来','尚无直接往来','未与玩家互动')):return False
    player=(ctx.get('player_contact') or {}).get('last_event',0)
    if value.get('player_memory_id')!=(player if player in known else 0):return False
    rows=value.get('alternatives')
    if not isinstance(rows,list) or not min(2,len(ids))<=len(rows)<=min(3,len(ids)):return False
    if any(not isinstance(row,dict) or row.get('action_id') not in ids or
           any(not isinstance(row.get(key),str) or not 1<=len(row[key])<=60 for key in ('benefit','risk')) for row in rows):return False
    if len({row['action_id'] for row in rows})!=len(rows):return False
    events=value.get('memory_ids')
    return value.get('fallback_action_id') in ids and isinstance(events,list) and len(events)<=3 and all(type(i) is int and i in known for i in events)
