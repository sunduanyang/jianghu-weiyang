from . import combat_attributes
"""Bounded task decomposition; only the existing engine executes consequences."""
from copy import deepcopy
from . import careers, sect_ranks, llm_policy, npc_attention, temperament

VERSION = 1


def role(w, n):
    if n['age_months'] < 120:
        return dict(key='child', label='童年成长', duties=['play', 'rest'])
    if n['office']:
        return dict(key='official', label=n['office']+'公务', duties=['public_service', 'report'])
    if n['faction'] is not None:
        grade = sect_ranks.standing(w.factions[n['faction']], n)['grade']
        key, label, duties = (('leader', '主持门务', ['sect_service', 'duty', 'donate_relief']) if grade == 1 else
                              ('elder', '传艺护持', ['sect_service', 'duty']) if grade <= 4 else
                              ('steward', '门中修业', ['sect_service', 'seek_teacher', 'work']) if grade <= 6 else
                              ('disciple', '入门求学', ['seek_teacher', 'work']))
        return dict(key=key, label=label, duties=duties)
    if n.get('social_status'):
        return dict(key='notable', label=n['social_status']['title']+'乡里责任',
                    duties=['donate_relief', 'neighbor_care', 'aid'])
    profession = n['career']['profession']
    key, label, duties = (('merchant', '经营与互信', ['trade', 'learn_trade', 'work']) if profession == '行商' else
                          ('healer', '医药与照护', ['neighbor_care', 'care', 'learn_trade']) if profession == '药师' else
                          ('artisan', '精进手艺', ['learn_trade', 'work']) if profession in ('匠人', '书生', '艺人') else
                          ('livelihood', '家计与安身', ['work', 'care', 'learn_trade']))
    return dict(key=key, label=label, duties=duties)


def adjustment(w, n, a):
    profile = role(w, n)
    if a['kind'] not in profile['duties']:
        return 0.
    # A duty is a preference, never permission or immunity from personality.
    return 7. + n['traits']['integrity'] * .025 if profile['key'] in ('official', 'leader', 'elder', 'steward', 'disciple') else 5.


def close(w, n, status, reason, event_id=None):
    p = n.get('plan')
    if not p:
        return
    p.update(status=status, blocker=reason)
    n['planning_history'] = (n.get('planning_history', []) + [dict(
        title=p.get('title', p.get('reason', '短期计划')), source=p['source'],
        created=p['created'], ended=w.month, status=status, reason=reason,
        completed=deepcopy(p.get('completed', [])), event_id=event_id)])[-8:]
    w.metrics['plans_'+('completed' if status == '完成' else 'interrupted')] = w.metrics.get(
        'plans_'+('completed' if status == '完成' else 'interrupted'), 0) + 1
    if p.get('intent', 'sequence') != 'sequence':
        key = 'workflows_completed' if status == '完成' else 'workflows_interrupted'
        w.metrics[key] = w.metrics.get(key, 0) + 1
    n['plan'] = None


def start(w, n, intent, title, steps, **data):
    n['plan_revision'] = n.get('plan_revision', 0) + 1
    n['plan'] = dict(version=VERSION, revision=n['plan_revision'], source='规则', created=w.month,
                     until=w.month + 18, intent=intent, title=title, reason=title,
                     goal=n['goal']['kind'], place=n['place'], faction=n['faction'],
                     steps=steps, completed=[], status='执行中', blocker='',
                     horizon='依真实条件逐步完成，费用与周次在执行时结算', **data)
    w.metrics['plans_started'] = w.metrics.get('plans_started', 0) + 1
    if intent != 'sequence':
        w.metrics['workflows_started'] = w.metrics.get('workflows_started', 0) + 1


def tutor_for(w, n, profession):
    candidates = [w.npcs[i] for i in w.residents.get(n['place'], []) if i != n['id']]
    return next(iter(sorted((q for q in candidates if q['alive'] and q['age_months'] >= 192
                            and q['health'] > 30 and q['career']['profession'] == profession
                            and careers.experience(q) >= max(15, careers.experience(n, profession) + 5)),
                            key=lambda q: (-careers.experience(q), q['id']))), None)


def career_target(w, n):
    if (n['age_months'] < 192 or n['office'] or n['faction'] is not None or n.get('social_status')
            or w.month - n['career']['last_change'] < 24):
        return None
    options = []
    for job, p in careers.PROFESSIONS.items():
        if job == n['career']['profession'] or careers.place_archetype(n['place']) not in p['places']:
            continue
        from . import economics
        gain = (economics.expected_income(w, n, job) - economics.expected_income(w, n)) if economics.active(w) else careers.income(n, profession=job) - careers.income(n)
        if gain < 1.5 or p.get('martial', 0) > combat_attributes.rating(n) + 3:
            continue
        tutor = tutor_for(w, n, job) if careers.experience(n, job) < p['entry'] else None
        if careers.experience(n, job) < p['entry'] and tutor is None:
            continue
        options.append((gain * 8 - p['cost'] * .15 + temperament.lean(n, p['trait']) * .025, job, tutor))
    return max(options, key=lambda x: (x[0], x[1]))[1:] if options else None


def begin(w, n, chosen):
    profile = role(w, n)
    if n['goal']['kind'] == 'wealth' and chosen['kind'] in ('work', 'learn_trade', 'change_job'):
        target = career_target(w, n)
        if target:
            job, tutor = target
            start(w, n, 'career_change', '筹备转业为'+job+'：积蓄、学艺、置业',
                  ['work:self'] + ([f'learn_trade:{tutor["id"]}'] if tutor else []) + ['change_job:'+job],
                  profession=job, tutor=tutor['id'] if tutor else None, original_job=n['career']['profession'])
        else:
            start(w, n, 'sequence', '围绕'+n['goal']['title']+'安排眼前事务', [chosen['id']])
    else:
        start(w, n, 'sequence', profile['label']+'：'+chosen['label'], [chosen['id']])


def blocked(w, n, reason):
    p = n['plan']
    p.update(status='受阻', blocker=reason)
    if p.get('blocked_since') is None:
        p['blocked_since'] = w.month
    if w.month - p['blocked_since'] >= 2:
        close(w, n, '中断', reason+'，两月未能继续')
    return None


def resolve(w, n, actions):
    p = n.get('plan')
    if not p:
        return None
    if w.month > p['until']:
        close(w, n, '中断', '计划期限已到，重新审视处境')
        return None
    if p.get('version') and (p['goal'] != n['goal']['kind'] or p['place'] != n['place'] or p['faction'] != n['faction']):
        close(w, n, '中断', '心愿、所在地点或门派发生变化')
        return None
    by_id = {a['id']: a for a in actions}
    p['contingency_active']=False
    def pick(kind, predicate=lambda a: True):
        return next((a for a in actions if a['kind'] == kind and predicate(a)), None)
    choice = None
    intent = p.get('intent', 'sequence')
    if intent == 'career_change':
        job = p['profession']; spec = careers.PROFESSIONS[job]
        if n['career']['profession'] == job:
            close(w, n, '完成', '已置办生计并转入'+job, n['last_event'])
            return None
        if n['career']['profession'] != p['original_job']:
            close(w, n, '中断', '营生已另作安排')
            return None
        if w.month - n['career']['last_change'] < 24:
            return blocked(w, n, '距上次转业不足两年')
        fee = spec['cost'] + 4 + (4 if careers.experience(n, job) < spec['entry'] else 0)
        if n['wealth'] < max(8, fee):
            choice = pick('work')
        elif combat_attributes.rating(n) < spec.get('martial', 0):
            choice = pick('train')
        elif careers.experience(n, job) < spec['entry']:
            tutor = w.npcs.get(p.get('tutor'))
            if not tutor or not tutor['alive'] or tutor['place'] != n['place'] or tutor['career']['profession'] != job:
                return blocked(w, n, '原定手艺师傅当前无法请教')
            choice = pick('learn_trade', lambda a: a['target'] == tutor['id'] and a['profession'] == job)
        else:
            choice = by_id.get('change_job:'+job)
    elif intent == 'sect_training':
        close(w, n, '中断', '旧门中修业与差事已移除')
        return None
    else:
        if not p['steps']:
            close(w, n, '完成', '已完成安排的行动', n['last_event'])
            return None
        choice = by_id.get(p['steps'][0])
        if choice is None and p.get('source')=='AI' and p.get('fallback_id') in by_id:
            choice=by_id[p['fallback_id']];p['contingency_active']=True
        if choice is None and any(a['id'] == p['steps'][0] for a in w.legal_actions(n, planning=True)):
            return blocked(w, n, '本周时间或合作对象的时段不足，等待下次机会')
    if choice is None:
        return blocked(w, n, '当前条件不满足或目标对象无法接触')
    p.update(status='执行中', blocker='', next_action=choice['label'], next_id=choice['id'], blocked_since=None)
    return choice


def choose(w, n, actions):
    # Preserve a committed plan while responding to an immediate survival need.
    emergency = None
    if n['health'] < 45:
        emergency = [a for a in actions if a['kind'] in ('use_item', 'treatment', 'rest')]
    elif n['wealth'] < 8:
        emergency = [a for a in actions if a['kind'] in ('work', 'sell_item')]
    if emergency:
        chosen = max(emergency, key=lambda a: w.score(n, a))
        if n.get('plan'):
            n['plan'].update(status='暂缓', blocker='先处理伤病' if n['health'] < 45 else '先筹足眼前生活费')
        n['decision'] = dict(source='生计应急', reason='先处理伤病' if n['health'] < 45 else '家资不足8银，先维持生计',
                             action=chosen['label'], action_id=chosen['id'], month=w.month)
        n['choice_source'] = '规则'
        return chosen
    chosen = resolve(w, n, actions)
    if chosen:
        n['choice_source'] = n['plan']['source']
        n['decision'] = dict(source=n['choice_source'], reason=n['plan'].get('title', n['plan']['reason']),
                             action=chosen['label'], action_id=chosen['id'], month=w.month)
        if n['plan'].get('contingency_active'):
            n['decision']['reason']=n['plan'].get('fallback_reason','先按预案处理眼前事务')
        if n['plan'].get('policy_origin'):
            n['decision'].update(policy_influence=True,policy_title=n['plan']['policy_origin']['title'])
        return chosen
    scored = [(w.score(n, a) + w.rng.uniform(-9, 9), a) for a in actions]
    chosen = max(scored, key=lambda row: row[0])[1]
    baseline=max(scored,key=lambda row:row[0]-llm_policy.adjustment(w,n,row[1]))[1]
    policy=llm_policy.current(w,n) if llm_policy.participates(w,n,actions) else None
    if n.get('plan') is None:
        begin(w, n, chosen)
        if policy and n.get('plan'):
            n['plan']['policy_origin']=dict(title=policy['title'],created=policy['created'])
        planned = resolve(w, n, actions)
        if planned:
            chosen = planned
    n['choice_source'] = '规则'
    n['decision'] = dict(source='规则', reason=(n['plan']['blocker'] if n.get('plan') and n['plan'].get('blocker') else
                                               n['plan']['reason'] if n.get('plan') else '根据眼前处境安排事务'),
                         action=chosen['label'], action_id=chosen['id'], month=w.month)
    if policy:
        n['decision'].update(policy_influence=True,policy_title=policy['title'],
                             policy_ranking_changed=max(scored,key=lambda row:row[0])[1]['id']!=baseline['id'])
    return chosen


def record(w, n, a, event):
    p = n.get('plan')
    if not p or n.get('choice_source') != p['source'] or p.get('next_id') != a['id'] or p.get('status') != '执行中':
        return
    p['completed'] = (p.get('completed', []) + [dict(action=a['label'], id=a['id'], month=w.month, event_id=event['id'])])[-12:]
    if p.get('intent', 'sequence') == 'sequence' and p['steps'] and p['steps'][0] == a['id']:
        p['steps'].pop(0)
        if not p['steps']:
            close(w, n, '完成', '已完成安排的行动', event['id'])
    elif p.get('intent') == 'career_change' and n['career']['profession'] == p['profession']:
        close(w, n, '完成', '已置办生计并转入'+p['profession'], event['id'])
    elif (p.get('intent') == 'sect_training' and n['contribution'] >= p['target_contribution']
          and combat_attributes.rating(n) >= p['target_skill'] and w.factions[n['faction']]['art_id'] in n['martial']['arts']):
        close(w, n, '完成', '本阶段贡献与武学目标已达到', event['id'])


def view(w, n):
    p = deepcopy(n.get('plan'))
    if p:
        if p.get('intent') == 'career_change':
            spec = careers.PROFESSIONS[p['profession']]
            fee = spec['cost'] + 4 + (4 if careers.experience(n, p['profession']) < spec['entry'] else 0)
            p['next_description'] = ('筹足至少'+str(max(8, fee))+'银' if n['wealth'] < max(8, fee) else
                                     '练足入行所需武学' if combat_attributes.rating(n) < spec.get('martial', 0) else
                                     '向约定师傅学艺，达到入行熟练度' if careers.experience(n, p['profession']) < spec['entry'] else
                                     '检查时机并支付置业费用，正式转业')
        elif p.get('intent') == 'sect_training':
            p['next_description'] = ('办差积累贡献至'+str(p['target_contribution']) if n['contribution'] < p['target_contribution'] else
                                     '备足请益费用与生活余银' if n['wealth'] < 8 else
                                     '练好基本功，达到武学12' if combat_attributes.rating(n) < 12 else '向同门请益或继续习武')
        else:
            p['next_description'] = p.get('next_action', '按约定步骤继续，执行前再核对条件')
    return dict(version=VERSION, role=role(w, n), current=p, decision=deepcopy(n.get('decision')),life_policy=llm_policy.view(w,n),
                attention=npc_attention.profile(w,n),deep_thought=deepcopy(n.get('deep_thought')),
                history=deepcopy(n.get('planning_history', [])),
                description='行动依据真实条件和周预算执行；受阻时会等待或调整，伤病与生计危机可暂缓计划。')
