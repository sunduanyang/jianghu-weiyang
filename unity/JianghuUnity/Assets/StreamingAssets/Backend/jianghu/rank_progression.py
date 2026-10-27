"""Recorded assessments, social mobility and accountable appointments.

Queries never perform assessments. Existing standings are registered as present
conditions; only subsequently settled actions establish service or trade records.
"""
from copy import deepcopy
from . import careers, sect_ranks, social_ranks, territory, economics

VERSION = 1
LABELS = {'establish_trade':'登记商旅营生', 'establish_business':'筹办商号', 'recognize_notable':'立业乡里'}
PROMOTION_MONTHS = {8:2, 7:2, 6:3, 5:3, 4:6, 3:6, 2:12}
PUBLIC = {'aid', 'donate_relief', 'neighbor_care', 'care', 'public_service', 'faction_guide'}
OWN_EVENTS = {'rank_assessment', 'rank_change', 'rank_warning', 'ranks_registered', 'identity_change'}


def active(w):
    return bool(getattr(w, 'rank_system', None))


def body(w, who):
    return w.player['protagonist'] if who == 'player' else w.npcs[int(who)]


def person(w, who):
    if who != 'player':
        return w.npcs[int(who)]
    h = body(w, who)
    return dict(h, id=-1, name=w.player['name'], skill=w.player['skill'], wealth=w.player['wealth'],
                reputation=w.player['reputation'], health=w.player['health'], alive=True,
                last_event=h['records'][-1] if h['records'] else None,
                office=None, job=h.get('job') or '游民', career=h.get('career', {'profession':h.get('job'),'experience':{}}))


def prepare(w, who):
    owner = body(w, who); n = person(w, who)
    p = owner.setdefault('rank_progress', dict(registered=w.month, last_review=w.month, activity={},
        local_place=n['place'], local_since=w.month, failure_months=0, decline_months=0,
        embargo_until=-1, turnover=0., warnings=[], history=[]))
    if n.get('faction') is not None:
        s = p.get('sect')
        if not s or s['faction'] != n['faction']:
            p['sect'] = dict(faction=n['faction'], grade=sect_ranks.merit_grade(n), since=w.month,
                registered=w.month, qualified=0, deficient=0, candidate=None, penalty=0.,
                censure_until=-1, cooldown_until=-1, reason='当前品级登记，后续按实际经历考核')
    return p


def initialize(w):
    if active(w):
        return False
    w.rank_system = dict(version=VERSION, since=w.month, courts={})
    for n in w.npcs.values():
        if n['alive']:prepare(w,n['id'])
    prepare(w,'player')
    for g in w.governments:
        country=g['country']; holders={}
        for n in w.npcs.values():
            status=n.get('social_status') or {}
            if n['alive'] and n['faction'] is None and status.get('country')==country:
                grade=social_ranks.grade_for(status['title'])
                slot='ruler' if grade==1 else 'general' if status['title']=='大将军' else 'minister' if grade==2 else None
                if slot and slot not in holders:holders[slot]=n['id']
        w.rank_system['courts'][str(country)]=dict(holders=holders, established=bool(holders),
            titles={slot:w.npcs[i]['social_status']['title'] for slot,i in holders.items()},
            since={slot:w.month for slot in holders}, extinct_months=0)
    event=w.emit('ranks_registered', [], '品阶升降开始按实际经历考核：门中按月验功，乡里依营生与家业升降，朝廷与掌门之位须真实任命。', .8,
                 facts=dict(version=VERSION,initial_condition=True,people=sum(n['alive'] for n in w.npcs.values())))
    w.rank_system['registration_event']=event['id']
    return True


def months(p, kind, now, horizon=12):
    return [m for m in p.get('activity',{}).get(kind,[]) if now-horizon < m <= now]


def mark(p, kind, month, event_id=None):
    p['activity'][kind]=sorted(set(p['activity'].get(kind,[])+[month]))[-36:]
    if event_id is not None:
        evidence=p.setdefault('evidence',{}).setdefault(kind,[])
        evidence[:]=([r for r in evidence if r['month']!=month]+[dict(month=month,event_id=event_id)])[-36:]


def emit(w, who, kind, text, facts, causes=()):
    if who == 'player':
        from . import protagonist
        event=protagonist.record(w,kind,text,facts,causes=causes)
    else:
        event=w.emit(kind,[who],text,.75,list(causes),facts)
    p=body(w,who)['rank_progress']
    p['history']=(p['history']+[dict(event_id=event['id'],month=w.month,text=text,kind=kind)])[-20:]
    return event


def on_event(w, event, who=None):
    if not active(w) or event['kind'] in OWN_EVENTS:return
    kind=event['kind'];facts=event['facts']
    who = who if who is not None else facts.get('actor',event['people'][0] if event['people'] else None)
    if who is not None and (who=='player' or who in w.npcs) and (who=='player' or w.npcs[who]['alive']):
        p=prepare(w,who)
        if kind in ('join','hero_sect_join'):
            p.pop('sect',None);prepare(w,who)
        if kind=='work':
            mark(p,'attempt',w.month,event['id'])
            output=facts.get('production',{})
            if output.get('produced') or output.get('sold') or facts.get('gross_income',0)>0:mark(p,'work',w.month,event['id'])
            if facts.get('gross_income',0)>0:mark(p,'profit',w.month,event['id'])
            p['turnover']+=max(0.,output.get('sales',output.get('gross',0.)))
        if kind=='hero_market' and facts.get('operation')=='sell':
            mark(p,'work',w.month,event['id']);mark(p,'trade',w.month,event['id']);p['turnover']+=facts['silver']
        if kind in PUBLIC and facts.get('accepted',True):mark(p,'public',w.month,event['id'])
        if kind in ('faction_task','faction_guide','hero_sect_task') and facts.get('accepted',True):mark(p,'service',w.month,event['id'])
        if kind=='teaching' and who!='player':
            teacher=w.npcs[who]
            if teacher['faction'] is not None and any(i!=who and w.npcs[i]['faction']==teacher['faction'] for i in event['people']):
                mark(p,'service',w.month,event['id'])
        if kind=='player_gift' and who=='player' and facts.get('transfer',10)>0:mark(p,'public',w.month,event['id'])
    if kind != 'judgment':return
    offender=facts['offender'];n=w.npcs[offender];p=prepare(w,offender)
    p['embargo_until']=max(p['embargo_until'],w.month+12)
    mark(p,'conviction',w.month,event['id'])
    s=p.get('sect')
    if n['faction'] is not None and s and s['faction']==n['faction']:
        s['penalty']+=12 if facts.get('public_money') else 6
        s['censure_until']=w.month+12;s['qualified']=0
        from . import organizations
        f=w.factions[n['faction']]
        if f['leader']==offender:organizations.elect(w,f,annual=True)
        change_sect(w,offender,min(9,s['grade']+(2 if facts.get('public_money') else 1)),
                    '已生效判决，门中依违纪事实降阶并暂停晋升一年',event['id'])
    status=n.get('social_status') or {}
    if status.get('title')=='乡绅' or social_ranks.grade_for(status.get('title'))==2:
        revoke_status(w,offender,'生效判决导致撤销乡里资格或公卿职衔',event['id'])


def change_sect(w,who,grade,reason,cause=None):
    from . import organizations
    n=person(w,who);p=prepare(w,who);s=p['sect'];f=w.factions[n['faction']]
    before=sect_ranks.standing(f,n)
    if s['grade']==grade:return
    s.update(grade=grade,since=w.month,qualified=0,deficient=0,candidate=None,reason=reason,cooldown_until=w.month+3)
    after=sect_ranks.standing(f,person(w,who))
    if before['grade']==after['grade']:return
    direction='升阶' if after['grade']<before['grade'] else '降阶'
    event=emit(w,who,'rank_change',f"{n['name']}由{before['grade_label']}{before['title']}{direction}为{after['grade_label']}{after['title']}：{reason}。",
               dict(system='sect',direction=direction,before=before,after=after,reason=reason,faction=f['id']),[cause] if cause else [])
    organizations.record(f,event)


def sect_conditions(w,who):
    n=person(w,who);p=body(w,who).get('rank_progress',{});s=p.get('sect')
    if not s or n['faction']!=s['faction']:return None
    current=sect_ranks.standing(w.factions[n['faction']],n)['grade']
    target=max(2,current-1);contribution,skill=sect_ranks.REQUIREMENTS[target]
    required_service=2 if target<=4 else 1
    required_reputation=30 if target==2 else 20 if target==3 else 10 if target==4 else 0
    tenure=max(0,w.month-(n.get('faction_since') if n.get('faction_since') is not None else s['registered']))
    min_tenure=12 if target<=3 else 6 if target<=5 else 0
    requirements=[dict(label='有效贡献',current=max(0.,n['contribution']-s['penalty']),required=contribution),
                  dict(label='武学',current=n['skill'],required=skill),
                  dict(label='声望',current=n['reputation'],required=required_reputation),
                  dict(label='近半年履职月数',current=len(months(p,'service',w.month,6)),required=required_service),
                  dict(label='入门月数',current=tenure,required=min_tenure),
                  dict(label='年龄（月）',current=n['age_months'],required=192 if target<=5 else 120)]
    for r in requirements:r['met']=r['current']>=r['required']
    blocked='处分期间暂停晋升' if w.month<s['censure_until'] else '品阶变动后观察期' if w.month<s['cooldown_until'] else ''
    return dict(current=current,target=target,requirements=requirements,blocked=blocked,
                qualified=all(r['met'] for r in requirements) and not blocked,
                required_months=PROMOTION_MONTHS[target],completed_months=s['qualified'],
                leader_only=current<=2,penalty=s['penalty'],reason=s['reason'],
                deficient_months=s.get('leader_deficient',0) if current==1 else s['deficient'])


def review_sect(w,who):
    n=person(w,who);p=prepare(w,who);s=p['sect'];conditions=sect_conditions(w,who)
    current=conditions['current'];s['reason']='主持之位须由门中推举' if current<=2 else '尚须满足晋阶条件并持续考核'
    if current>2:
        if s.get('candidate')!=conditions['target']:s['qualified']=0
        s['candidate']=conditions['target']
        s['qualified']=s['qualified']+1 if conditions['qualified'] else 0
        if s['qualified']>=conditions['required_months']:
            change_sect(w,who,conditions['target'],'贡献、本领、履职与声望持续达标，经月考核确认')
            return
    protected=n.get('illness') or n.get('wound') or n['health']<35
    if protected:
        s['deficient']=0;s['leader_deficient']=0;s['reason']='病伤休养期间，履职降阶考核暂停';return
    if len(months(p,'service',w.month,1)) and w.month>=s['censure_until']:
        s['penalty']=max(0.,s['penalty']-1)
    if len(months(p,'service',w.month,6))>=2:s['leader_deficient']=0
    if current==1:
        neglect=w.month-s['registered']>=6 and len(months(p,'service',w.month,6))<2
        s['leader_deficient']=s.get('leader_deficient',0)+1 if neglect else 0
        if neglect:s['reason']='主持近半年履职不足，持续失职将重新推举'
        return
    contribution,skill=sect_ranks.REQUIREMENTS[current]
    inadequate=max(0.,n['contribution']-s['penalty'])<contribution*.8 or n['skill']<skill*.85
    neglect=current<=4 and w.month-s['registered']>=6 and len(months(p,'service',w.month,6))<2
    failing=(inadequate or neglect) and w.month>=s['cooldown_until']
    s['deficient']=s['deficient']+1 if failing else 0
    if failing:
        s['reason']='维持品级的本领或有效贡献持续不足' if inadequate else '高阶成员近半年履职不足'
        if s['deficient']>=3:change_sect(w,who,min(9,current+1),s['reason']+'，连续三月考核未改善')


def solvency(w,who):
    n=person(w,who);wallet=w.player if who=='player' else n
    goods=wallet.get('economic_life',{}).get('stock',{})
    return wallet['wealth']+sum(v*economics.GOODS[g][1]*.85 for g,v in goods.items())


def options(w,who):
    if not active(w):return []
    if who=='player' and not w.player['protagonist']['created']:return []
    n=person(w,who);p=body(w,who).get('rank_progress',{})
    if not p or not n['alive'] or n['age_months']<192 or n['faction'] is not None:return []
    status=n.get('social_status') or {};current=(social_ranks.player_identity(w) if who=='player' else careers.identity(w,n))
    rows=[]
    for action,title,cost,conditions in [
        ('establish_trade','行商',12.,[('真实售货月数',len(months(p,'trade',w.month)),3),('累计售货',p['turnover'],12.)]),
        ('recognize_notable','乡绅',60.,[('声望',n['reputation'],30.),('乡里公益月数',len(months(p,'public',w.month)),6),('定居月数',w.month-p['local_since'],12)]),
        ('establish_business','豪商',120.,[('商旅熟练度',careers.experience(n) if who!='player' else len(months(p,'trade',w.month))*4,45.),
                                       ('实际经营月数',len(months(p,'profit' if who!='player' else 'trade',w.month,18)),12),('声望',n['reputation'],30.),('累计营业收入',p['turnover'],120.)])]:
        if action=='establish_trade' and (who!='player' or current['grade']<=6):continue
        if action=='recognize_notable' and current['grade']<=4:continue
        if action=='establish_business' and (status or {}).get('title')=='豪商':continue
        if action=='establish_business' and (n['career']['profession'] if who!='player' else n['job'])!='行商':continue
        if action=='establish_business' and current['grade']<=3:continue
        minimum={'establish_trade':16.,'recognize_notable':160.,'establish_business':300.}[action]
        tests=[dict(label=label,current=value,required=required,met=value>=required) for label,value,required in conditions]
        tests.append(dict(label='可用银两',current=n['wealth'],required=minimum,met=n['wealth']>=minimum))
        reasons=[r['label']+'尚未达标' for r in tests if not r['met']]
        if w.month<p['embargo_until']:reasons.append('判决后一年内不得晋阶')
        rows.append(dict(id=action,kind=action,target=None,label=LABELS[action],title=title,cost=cost,
                         requirements=tests,enabled=not reasons,reason='；'.join(reasons),time_cost=2))
    return rows


def candidates(w,n):
    return [dict(o,id=o['id']+':self') for o in options(w,n['id']) if o['enabled']]


def adjustment(w,n,action):
    if not active(w) or n['faction'] is None:return 0.
    p=n.get('rank_progress',{});s=p.get('sect',{})
    grade=sect_ranks.standing(w.factions[n['faction']],n)['grade']
    if grade<=4 and action['kind'] in ('faction_task','faction_guide','teach') and len(months(p,'service',w.month,6))<2:
        return 18+n['traits']['loyalty']*.08
    return 0.


def execute(w,who,action):
    option=next((o for o in options(w,who) if o['kind']==action and o['enabled']),None)
    if not option:raise ValueError('当前未满足此项身份进阶条件')
    n=person(w,who);owner=body(w,who);wallet=w.player if who=='player' else n
    cost=option['cost'];wallet['wealth']-=cost
    if economics.active(w):economics.fee(w,n['place'],cost)
    else:
        g=w.governments[territory.owner(w,n['place'])];g['fund']+=cost;g['revenue']+=cost
    if action=='establish_trade':owner['job']='行商'
    else:
        owner['social_status']=dict(title=option['title'],country=territory.owner(w,n['place']),since=w.month,
            place=n['place'],source='实绩考核与身份登记',basis=deepcopy(option['requirements']))
    p=prepare(w,who);p['decline_months']=0;p['warning']=''
    facts=dict(system='nation',action=action,title=option['title'],cost=cost,requirements=option['requirements'])
    if who!='player':facts.update(actor=who,time_cost=2,decision_source=n.get('choice_source','规则'))
    evidence=[r['event_id'] for k,rows in p.get('evidence',{}).items() if k in ('public','profit','trade') for r in rows if w.month-18<r['month']<=w.month]
    facts['evidence']=evidence[-24:]
    event=emit(w,who,'rank_change',f"{n['name']}经实绩核验，花费{cost:.0f}银{'筹办商号' if action=='establish_business' else '立业乡里' if action=='recognize_notable' else '置办商旅营生'}，登记为{option['title']}。",facts,causes=evidence[-24:])
    if owner.get('social_status'):owner['social_status']['event_id']=event['id']
    return event


def revoke_status(w,who,reason,cause=None):
    owner=body(w,who);status=owner.pop('social_status',None)
    if not status:return
    if status.get('underlying_status'):
        owner['social_status']=deepcopy(status['underlying_status'])
    emit(w,who,'rank_change',f"{person(w,who)['name']}失去「{status['title']}」身份：{reason}；现按实际营生与其他在任身份分品。",
         dict(system='nation',previous_status=status,reason=reason),[cause] if cause else [])
    p=prepare(w,who);p['decline_months']=0;p['warning']=''


def review_civil(w,who):
    n=person(w,who);p=prepare(w,who)
    if p['local_place']!=n['place']:p.update(local_place=n['place'],local_since=w.month)
    status=n.get('social_status') or {};title=status.get('title')
    if title and social_ranks.grade_for(title)>=3:
        assets=solvency(w,who)
        if title in ('世家家主','世家子弟','权贵'):
            family=set(n.get('parents',[])+n.get('children',[])+([n['spouse']] if n.get('spouse') else []))
            assets+=sum(solvency(w,i) for i in family if i in w.npcs and w.npcs[i]['alive'])
        limit=120 if title in ('豪商','世家家主','世家子弟','权贵') else 40
        distressed=assets<limit and not n.get('office')
        if title=='乡绅' and status.get('place') is not None and status['place']!=n['place'] and w.month-p['local_since']>=12:distressed=True
        p['decline_months']=p['decline_months']+1 if distressed else 0
        duration=6 if title=='豪商' else 12 if title=='乡绅' else 24
        if p['decline_months']>=duration:revoke_status(w,who,'家业或乡里根基长期无法维持，经过宽限期后确认衰落')
        elif distressed:p['warning']=f'家业维持不足，宽限考察 {p["decline_months"]}/{duration} 月'
        else:p['warning']=''
    elif who!='player' and n['faction'] is None and not n.get('office') and n['age_months']>=192:
        failed=w.month in months(p,'attempt',w.month,1) and w.month not in months(p,'work',w.month,1) and solvency(w,who)<2
        failed=failed and not n.get('illness') and not n.get('wound') and n['health']>=35 and n['job']!='短工'
        p['failure_months']=p['failure_months']+1 if failed else 0
        p['warning']=f'营生连续停摆 {p["failure_months"]}/6 月' if failed else ''
        if p['failure_months']>=6:
            previous=n['job'];n['job']='短工'
            n['career'].update(profession='短工',since=w.month,last_change=w.month,source='营生长期停摆后歇业')
            n['career']['experience'].setdefault('短工',0.)
            event=emit(w,who,'rank_change',f"{n['name']}的{previous}营生连续半年无从开工且无法备料，歇业后以短工维生。",
                       dict(system='nation',previous_job=previous,job='短工',reason='实际谋生持续失败，家计储备不足'))
            from . import npc_plans
            npc_plans.close(w,n,'中断','原营生已经歇业',event['id']);p['failure_months']=0


def leader_eligible(w,n):
    if not active(w):return True
    p=n.get('rank_progress',{});s=p.get('sect',{})
    return (n['age_months']>=252 and n['skill']>=45 and n['contribution']-s.get('penalty',0)>=35
            and n['reputation']>=20 and w.month>=s.get('censure_until',-1)
            and s.get('leader_deficient',0)<3 and sect_ranks.standing(w.factions[n['faction']],n)['grade']<=3)


def courts(w):
    for country_key,court in w.rank_system['courts'].items():
        country=int(country_key)
        if not court['established']:continue
        occupied=any(territory.owner(w,p['id'])==country for p in w.active_places)
        court['extinct_months']=0 if occupied else court['extinct_months']+1
        if court['extinct_months']>=6:
            for who in list(court['holders'].values()):
                if who in w.npcs and w.npcs[who]['alive']:revoke_status(w,who,'所属政权失去全部辖地已满半年，原在任职衔终止')
            court['holders'].clear();continue
        if not occupied:continue
        for slot in ('ruler','minister','general'):
            if slot not in court['titles']:continue
            current=w.npcs.get(court['holders'].get(slot))
            valid=current and current['alive'] and current.get('social_status',{}).get('title')==court['titles'][slot]
            if valid:continue
            eligible=[]
            for n in w.npcs.values():
                if not n['alive'] or n['faction'] is not None or not 252<=n['age_months']<=840 or territory.owner(w,n['place'])!=country:continue
                p=n.get('rank_progress',{});rank=careers.identity(w,n)
                if w.month<p.get('embargo_until',-1) or n['reputation']<30:continue
                if slot=='ruler':
                    if rank['grade']!=2:continue
                else:
                    if not 3<=rank['grade']<=5 or len(months(p,'public',w.month,24))<6:continue
                    if slot=='general' and n['skill']<45:continue
                    if slot=='minister' and careers.experience(n)<30:continue
                eligible.append(n)
            if not eligible:continue
            chosen=max(eligible,key=lambda n:(n['reputation']+n['skill']*.2,-n['id']))
            for other,i in list(court['holders'].items()):
                if i==chosen['id']:court['holders'].pop(other)
            previous=deepcopy(chosen.get('social_status'))
            underlying=previous if previous and social_ranks.grade_for(previous['title'])>=3 else (previous or {}).get('underlying_status')
            chosen['social_status']=dict(title=court['titles'][slot],country=country,since=w.month,source='真实空缺任命',underlying_status=underlying)
            court['holders'][slot]=chosen['id'];court['since'][slot]=w.month
            event=emit(w,chosen['id'],'rank_change',f"{w.countries[country]['name']}在真实空缺后正式推任{chosen['name']}为{court['titles'][slot]}。",
                dict(system='nation',country=country,slot=slot,previous=current['id'] if current else None,previous_status=previous,title=court['titles'][slot]))
            chosen['social_status']['event_id']=event['id']


def monthly(w):
    if not active(w):return
    people=[n['id'] for n in w.npcs.values() if n['alive']]
    if w.player['protagonist']['created']:people.append('player')
    for who in people:
        p=prepare(w,who)
        if p['last_review']>=w.month:continue
        if p['last_review']!=w.month-1:
            if p.get('sect'):p['sect'].update(qualified=0,deficient=0)
            p.update(failure_months=0,decline_months=0)
        p['last_review']=w.month
        if person(w,who)['faction'] is not None:review_sect(w,who)
        else:review_civil(w,who)
    courts(w)


def view(w,who):
    if not active(w):return dict(enabled=False)
    n=person(w,who);p=body(w,who).get('rank_progress',{})
    if not p:return dict(enabled=True,history=[],description='已故人物保持生前最终身份。')
    from .engine import date
    return dict(enabled=True,version=VERSION,next_assessment=date(w.month+1),sect=sect_conditions(w,who) if n['faction'] is not None else None,
        options=options(w,who),warning=p.get('warning',''),history=deepcopy(p['history']),
        description='门派逐阶考核，病伤可休养；社会品阶随真实营生、家业与任职改变。查看界面不推进考核。')


def note_identity(w,n,before,after,event):
    if not active(w) or (before['grade'],before['system'])==(after['grade'],after['system']):return
    p=n.get('rank_progress')
    if p:p['history']=(p['history']+[dict(event_id=event['id'],month=w.month,text=event['summary'],kind='identity_change')])[-20:]


def player_action(w,body_data):
    from . import protagonist
    h=w.player['protagonist']
    if not h['created'] or (w.battle and w.battle['status']=='active'):raise ValueError('请先创建主角并结束切磋')
    if protagonist.integer(body_data.get('revision'),'行程版本')!=h['revision'] or protagonist.integer(body_data.get('month'),'月份')!=w.month:
        raise ValueError('行程已变化，请刷新后重试')
    if h['points']<4:raise ValueError('身份事务需要4点行动力')
    event=execute(w,'player',body_data.get('action'))
    h['points']-=4;h['revision']+=1;w.checkpoint()
    return dict(event=event,progress=view(w,'player'))
