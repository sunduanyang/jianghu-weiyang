"""Motives, limited opportunities and morally mixed choices; no generated consequences."""
from . import combat_attributes, temperament

POLES = temperament.POLES
LABELS = {'steal': '行窃', 'extort': '勒索', 'smear': '造谣中伤', 'embezzle': '侵吞公帑',
          'gamble': '赌局', 'leisure': '消遣', 'care': '照料', 'meditate': '静思', 'play': '玩耍'}
KINDS = set(LABELS)
COSTS = {'work': 2, 'travel': 2, 'duel': 2, 'extort': 2}
LIMITS = {'trade': 1, 'travel': 1, 'duel': 1, 'family': 1, 'report': 1,
          'steal': 1, 'extort': 1, 'smear': 1, 'embezzle': 1, 'gamble': 1, 'join': 1,
          'faction_study': 1, 'leave_faction': 1, 'reconcile': 1, 'swear': 1}
LIMITS.update(donate_relief=1, neighbor_care=1)
LIMITS['faction_guide'] = 1
LIMITS['sect_service'] = 1
LIMITS['seek_teacher'] = 1
CRIMES = {'betrayal', 'theft_exposed', 'extortion', 'extortion_resisted', 'slander_exposed', 'embezzlement_exposed'}


def initialize(w, n):
    # Legacy eight-trait saves convert reproducibly without consuming the world's RNG.
    temperament.migrate(w, n)
    defaults = {'time_left': 4, 'activity_month': w.month, 'action_counts': {},
                'monthly_actions': [], 'choice_source': '规则', 'last_ai_execution': None,
                'pressure': 0., 'loneliness': 25., 'last_review_attempt': -100,
                'last_ai_review':n['reviews'][-1]['month'] if n['reviews'] else None,
                'trigger_events': [], 'settlement_logged': False,'probation_until':-1}
    defaults.update(planning_history=[], plan_revision=0, decision=None)
    for k,v in defaults.items():
        n.setdefault(k,v)


def cost(kind):
    return COSTS.get(kind, 1)


def available(w, n):
    return not w.phase or not any(a['week'] <= w.phase < a['week']+a['time_cost']
                                  for a in n['monthly_actions'])


def activity_capacity(w, n):
    weeks = 4 if n['age_months'] >= 120 else 2
    return min(2,weeks) if n['probation_until']>=w.month else weeks


def reset_activity(w, n):
    """Prepare a new NPC time budget without advancing its life or finances."""
    n['time_left'] = activity_capacity(w, n)
    n['action_counts'] = {}
    n['monthly_actions'] = []
    n['activity_month'] = w.month
    n['settlement_logged'] = False


def reset_month(w, n, keep_activity=False):
    if not keep_activity:
        reset_activity(w, n)
    else:
        n['activity_month'] = w.month
        n['settlement_logged'] = False
    n['pressure'] = min(100., max(0, 35-n['wealth']) + (18 if n['illness'] else 0)
                        + max(0, 50-n['health']) + len([i for i in n['children'] if w.npcs[i]['age_months']<72])*4)
    n['loneliness'] = min(100., n['loneliness']+6)


def actions(w, n, visible):
    result=[]
    def add(kind,target=None,detail=''):
        result.append({'id':f'{kind}:{target if target is not None else "self"}', 'kind':kind,
                       'target':target, 'label':LABELS[kind]+(' · '+detail if detail else '')})
    if n['age_months'] < 120:
        add('play',detail='玩耍与探索')
        return result
    add('meditate',detail='缓解愤怒与不安')
    if n['wealth'] >= 2:
        add('leisure',detail='茶酒游憩，花费2银')
    if n['wealth'] >= 6 and n['age_months'] >= 216:
        add('gamble',detail='押6银，有赢有输')
    if n['office'] and w.local_government(n)['fund'] >= 8:
        add('embezzle',detail='利用职权挪走8银，可能被审计发现')
    for i in visible:
        q=w.npcs[i]
        if n['wealth']>=2 and (q['health']<65 or q['age_months']<120) and (
                i in n['parents']+n['children'] or i==n['spouse'] or n['relationships'].get(str(i),{}).get('affinity',0)>25):
            add('care',i,q['name'])
        if n['age_months']<216 or q['age_months']<216:
            continue
        if q['wealth']>=6:
            add('steal',i,q['name'])
            if combat_attributes.rating(n)>=15 and n['health']>45:
                add('extort',i,q['name'])
        listeners=[j for j in w.residents[n['place']] if j not in (n['id'],i) and
                   w.npcs[j]['age_months']>=144 and str(j) in n['knowledge']]
        r=n['relationships'].get(str(i),{})
        motive=r.get('affinity',0)<-15 or (n['traits']['ambition']>65 and n['traits']['integrity']<50)
        if str(i) in n['knowledge'] and listeners and motive:
            add('smear',i,q['name'])
    return result


PREDATORY = ('steal', 'extort', 'smear', 'betray')


def deterrence(w, n, q):
    """What a would-be offender stands to face: the victim's people and their own ties."""
    if not q:
        return 0.
    kin = [w.npcs[i] for i in q['parents'] + q['children'] + ([q['spouse']] if q['spouse'] else [])
           if i in w.npcs and w.npcs[i]['alive'] and w.npcs[i]['place'] == q['place'] and w.npcs[i]['age_months'] >= 192]
    risk = min(12, 4*len(kin)) + (6 if q['faction'] is not None else 0) + (8 if q.get('office') else 0)
    own = [i for i in n['children'] + ([n['spouse']] if n['spouse'] else []) if i in w.npcs and w.npcs[i]['alive']]
    stake = (5 if n['faction'] is not None else 0) + (4 if own else 0)
    infamy = .7 if n['reputation'] < -20 else 1.
    return round((risk + stake) * (1.3 - n['traits']['boldness']/100) * infamy, 4)


def adjustment(w, n, a):
    t=n['traits']; k=a['kind']; q=w.npcs.get(a['target'])
    repeats=n['action_counts'].get(k,0)
    value=-repeats*15
    if k=='trade':
        # A business opportunity is not an infinite source of risk-free income.
        value-=6 + max(0,n['wealth']-100)*.025
    if k in ('befriend','court','rumor','leisure'):
        value+=n['loneliness']*.12
    if k=='aid' and q:
        value+= (n['traits']['integrity']*.1 if q['id'] in n['parents']+n['children'] else 0)
        value-= (100-t['kindness'])*.16
    if k=='betray':
        value+=(100-t['integrity'])*.24+(100-t['kindness'])*.22 - t['integrity']*.2
    if k=='duel':
        value+=t['boldness']*.2+n['mood']['anger']*.15
    if k=='care':
        value+=t['kindness']*.3+t['integrity']*.15-(100-t['kindness'])*.15
    elif k=='leisure':
        value+=8+n['pressure']*.25+(100-t['ambition'])*.1
    elif k=='meditate':
        value+=2+n['mood']['anger']*.45+n['mood']['fear']*.3+(100-n['health'])*.08
    elif k=='gamble':
        value+=t['ambition']*.2+(100-t['kindness'])*.16+t['boldness']*.19-(100-t['boldness'])*.1-8
    elif k=='steal':
        value+=(100-t['integrity'])*.4+(100-t['kindness'])*.3+n['pressure']*.22-t['kindness']*.15-(100-t['boldness'])*.17-10
    elif k=='extort':
        value+=t['boldness']*.4+(100-t['kindness'])*.45-t['integrity']*.32-(100-t['boldness'])*.08-6
    elif k=='smear':
        relation=n['relationships'].get(str(a['target']),{})
        resentment=max(0,-relation.get('affinity',0))
        value+=(100-t['integrity'])*.25+t['ambition']*.18+(100-t['kindness'])*.1+resentment*.2-t['kindness']*.18-t['integrity']*.25-5
    elif k=='embezzle':
        value+=(100-t['integrity'])*.45+(100-t['kindness'])*.4-t['integrity']*.22-(100-t['boldness'])*.12
    elif k=='play':
        value+=18
    if k in PREDATORY:
        value-=deterrence(w,n,q)
    return value


def expose(w, kind, n, q, amount, summary, causes=None, extra=None):
    w.link(n,q,-35,-35)
    n['reputation']=max(-100,n['reputation']-5)
    # Word spreads to the victim's own people nearby; they think less of the offender.
    kin={i for i in q['parents']+q['children']+[q['spouse']] if i is not None}
    near={int(i) for i,r in q['relationships'].items() if r.get('affinity',0)>=20}|kin
    circle=sorted((w.npcs[i] for i in near if i in w.npcs and i!=n['id'] and w.npcs[i]['alive'] and w.npcs[i]['place']==q['place']),
                  key=lambda p:(p['id'] not in kin,-q['relationships'].get(str(p['id']),{}).get('affinity',0),p['id']))[:6]
    e=w.emit(kind,[n['id'],q['id']],summary,.88,causes,
             {'offender':n['id'],'victim':q['id'],'transfer':amount,'witnessed_by':[p['id'] for p in circle],**(extra or {})})
    for p in circle:
        r=w.relation(p,n['id']);r['affinity']=max(-100,r['affinity']-8);r['trust']=max(-100,r['trust']-6)
        w.know(p,n)
    q['grievance_cause']=e['id']
    return e


def execute(w, n, a, facts):
    k=a['kind'];q=w.npcs.get(a['target']);parents=[n['last_event'],n['goal']['cause']]
    before={key:n[key] for key in ('wealth','health')}
    def settled():
        return {**facts,'actor_delta':{key:round(n[key]-v,4) for key,v in before.items()}}
    if k=='steal':
        amount=min(12,q['wealth']*.3)
        n['wealth']+=amount;q['wealth']-=amount
        if w.rng.random()<.25+w.local_government(n)['security']*.3:
            return expose(w,'theft_exposed',n,q,amount,f"{n['name']}窃取{q['name']}{amount:.1f}银，被当场发现。",parents,settled())
        e=w.emit('secret_theft',[n['id']],f"{n['name']}暗中窃取{q['name']}{amount:.1f}银，未被发现。",.65,parents,
                 {**settled(),'secret':True,'offender':n['id'],'victim':q['id'],'transfer':amount})
        w.emit('loss',[q['id']],f"{q['name']}发现遗失{amount:.1f}银，不知是谁所为。",.65,[e['id']],{'amount':amount})
        return e
    if k=='extort':
        submitted=combat_attributes.rating(n)+w.rng.uniform(-10,12)>combat_attributes.rating(q)+w.rng.uniform(-8,15)
        amount=min(10,q['wealth']*.25) if submitted else 0
        n['wealth']+=amount;q['wealth']-=amount
        if not submitted:n['health']=max(0,n['health']-5)
        return expose(w,'extortion' if submitted else 'extortion_resisted',n,q,amount,
                      f"{n['name']}威逼{q['name']}交银，"+(f"强索{amount:.1f}银。" if submitted else '遭到抵抗。'),parents,settled())
    if k=='smear':
        audience=next(w.npcs[i] for i in w.residents[n['place']] if i not in (n['id'],q['id']) and
                      w.npcs[i]['age_months']>=144 and str(i) in n['knowledge'])
        secret=w.emit('slander',[n['id']],f"{n['name']}故意向{audience['name']}编造{q['name']}不守信用的传言。",.6,parents,
                      {**settled(),'secret':True,'offender':n['id'],'subject':q['id'],'listener':audience['id']})
        w.emit('hearsay',[audience['id']],f"{audience['name']}从{n['name']}处听闻「{q['name']}不守信用」，此说尚未核实。",.45,[secret['id']],
               {'speaker':n['id'],'subject':q['id'],'unverified':True})
        r=w.relation(audience,q['id']);r['affinity']=max(-100,r['affinity']-5);r['trust']=max(-100,r['trust']-4)
        q['reputation']=max(-100,q['reputation']-2)
        n['reputation']=min(100,n['reputation']+1)
        if w.rng.random()<.2+w.local_government(n)['security']*.2:
            return expose(w,'slander_exposed',n,q,0,f"{n['name']}中伤{q['name']}的传言被核实为编造，双方反目。",[secret['id']],settled())
        return secret
    if k=='embezzle':
        amount=8
        w.local_government(n)['fund']-=amount;w.local_government(n)['expenses']+=amount;n['wealth']+=amount
        e=w.emit('embezzlement',[n['id']],f"{n['name']}利用{n['office']}职权将公帑8银据为己有。",.7,parents,
                 {**settled(),'offender':n['id'],'amount':8,'secret':True})
        w.local_government(n).setdefault('corruption_records',[]).append({'event_id':e['id'],'offender':n['id'],'amount':8,'audited':False})
        return e
    if k=='gamble':
        n['wealth']-=6;w.economy['gaming_house']+=6
        won=w.rng.random()<.28
        paid=min(18,w.economy['gaming_house']) if won else 0
        w.economy['gaming_house']-=paid;n['wealth']+=paid
        return w.emit('gamble',[n['id']],f"{n['name']}押6银入赌局，"+(f"赢回{paid:.1f}银。" if won else '输掉赌注。'),.35,parents,
                      {**settled(),'stake':6,'payout':paid,'won':won})
    if k=='care':
        n['wealth']-=2;q['health']=min(100,q['health']+5)
        from . import economics
        economics.fee(w, n['place'], 2)
        w.link(n,q,3,2)
        return w.emit('care',[n['id'],q['id']],f"{n['name']}花2银照料{q['name']}，对方恢复5体力。",.4,parents,
                      {**settled(),'cost':2,'health_gain':5})
    if k=='leisure':
        n['wealth']-=2;n['health']=min(100,n['health']+3)
        from . import economics
        economics.fee(w, n['place'], 2)
        n['pressure']=max(0,n['pressure']-8);n['mood']['joy']=min(100,n['mood']['joy']+8)
        text=f"{n['name']}花2银在茶肆听曲消遣，暂缓心中压力。"
    elif k=='meditate':
        n['mood']['anger']*=.65;n['mood']['fear']*=.7;n['pressure']=max(0,n['pressure']-4)
        text=f"{n['name']}独坐静思，压下眼前的愤怒与不安。"
    else:
        n['mood']['joy']=min(100,n['mood']['joy']+6);n['health']=min(100,n['health']+2)
        text=f"{n['name']}在住处附近玩耍探索。"
    return w.emit(k,[n['id']],text,.15,parents,settled())


def audit(w, g=None):
    if g is None:
        for court in w.governments:
            audit(w, court)
        return

    records=g.get('corruption_records',[])
    for row in records:
        if row['audited'] or w.rng.random()>.6:
            continue
        row['audited']=True
        offender=w.npcs[row['offender']]
        auditor=next((w.npcs[i] for i in g['offices'].values() if i and i!=offender['id'] and w.npcs[i]['alive']),None)
        if not auditor or not offender['alive']:
            continue
        e=expose(w,'embezzlement_exposed',offender,auditor,row['amount'],
                 f"县衙查账发现{offender['name']}侵吞公帑{row['amount']:.1f}银。",[row['event_id']],{'public_money':True})
        g['last_event']=e['id']


def interpretation(w, n, e):
    """Actor/victim roles and values matter; success does not always teach virtue."""
    k=e['kind'];t=n['traits'];f=e['facts'];changes=[]
    culprit=f.get('offender')==n['id']
    if k in ('secret_theft','slander','embezzlement'):
        if t['integrity']<45:
            text='规矩管不住所有机会，这次得手值得记住。'
            changes=[w.drift(n,'integrity',-.06,event_id=e['id']),w.drift(n,'kindness',-.05,event_id=e['id'])]
        else:
            text='眼前得利，却不敢细想自己跨过了哪条线。'
            n['mood']['fear']=min(100,n['mood']['fear']+6)
            changes=[w.drift(n,'integrity',-.03,event_id=e['id'])]
    elif k in CRIMES or k=='judgment':
        victim = f.get('victim')==n['id']
        if k=='judgment' and not culprit:
            if victim:
                n['beliefs']['justice']=min(100,n['beliefs']['justice']+1.5)
                return '讨回了部分公道，往后仍要看人行事。',[]
            return '办成一桩案子，也会得罪一方的人。',[]
        if culprit:
            text='只是这回被抓住了；下次要更会掩饰。' if t['integrity']<45 else '后果落到身上，才知侥幸不能长久。'
            changes=[w.drift(n,'boldness',-.15,event_id=e['id'])]
            if k=='judgment':n['beliefs']['justice']=max(0,n['beliefs']['justice']-2.5)
        else:
            text='这笔账不能轻轻揭过。' if t['boldness']>55 else '与其逞强，不如留下证据再作打算。'
            n['beliefs']['trust']=max(0,n['beliefs']['trust']-3.5)
            n['mood']['anger']=min(100,n['mood']['anger']+12)
            if k!='judgment':
                n['desires']['revenge']=min(100,n['desires']['revenge']+30+(100-t['kindness'])*.6+t['ambition']*.12)
                n['grievance_cause']=e['id']
            changes=[w.drift(n,'kindness',-.1 if t['kindness']<50 else -.025,event_id=e['id'])]
    elif k=='hearsay':
        text='闲话未必可信，先记下来源。' if t['boldness']<50 else '这番话听来有理，往来时得留意此人。'
    elif k=='gamble':
        text=('赢过一次，也许还会再赢。' if f['won'] else '不甘心就这样认输，还想把本钱拿回来。') if t['ambition']>55 else (
              '输赢都是代价，往后应量力。')
        changes=[w.drift(n,'ambition',.035 if t['ambition']>55 else -.025,event_id=e['id'])]
    elif k in ('leisure','meditate','play','care'):
        text={'leisure':'人生不能只有账本和前程。','meditate':'忍一时，并不意味着旧事已经放下。',
              'play':'今日的好奇与快乐也是生活。','care':'身边人的安危，会改变自己的取舍。'}[k]
        if k=='meditate':changes=[w.drift(n,'boldness',-.015,event_id=e['id'])]
    else:
        return None
    return text,[c for c in changes if c]
