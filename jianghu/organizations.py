from . import combat_attributes
"""Local organizations: scoped merit, paid duties, teaching and accountable funds."""
from . import martial, relationships, sect_ranks, npc_plans
from .atlas import PLACES

LABELS = {'faction_study': '门中请益', 'faction_guide': '同门授业', 'leave_faction': '辞别门派',
          'sect_service': '门中履职'}
STATE_FIELDS = ('leader', 'fund', 'opening_fund', 'account_since', 'revenue', 'expenses',
                'tasks_completed', 'leader_since', 'leader_review_since', 'vacancy_previous',
                'vacancy_event', 'last_event', 'records', 'relations')


def initialize(w):
    for f in w.factions:
        # Legacy funds become an opening balance; no historical income is invented.
        defaults = dict(opening_fund=f['fund'], account_since=w.month, revenue=0., expenses=0.,
                        tasks_completed=0, leader_since=None, leader_review_since=w.month,
                        vacancy_previous=None, vacancy_event=None, last_event=None, records=[],
                        relations={})
        for k, v in defaults.items():
            f.setdefault(k, v)


def prepare(n):
    n.setdefault('faction_since', None)
    n.setdefault('faction_rejoin_after', -1)
    n.setdefault('membership_history', [])


def rank(w, n):
    if n['faction'] is None:
        return None
    return sect_ranks.standing(w.factions[n['faction']], n)


def float_needed(w, f):
    """Silver a sect keeps in hand before paying stipends and relief out of its treasury."""
    cache = getattr(w, '_sect_members', None)
    if not cache or cache[0] != (w.month, len(w.npcs)):
        counts = {}
        for n in w.npcs.values():
            if n['alive'] and n['faction'] is not None:
                counts[n['faction']] = counts.get(n['faction'], 0) + 1
        cache = w._sect_members = ((w.month, len(w.npcs)), counts)
    return 60. + 4. * cache[1].get(f['id'], 0)


def record(f, e):
    f['last_event'] = e['id']
    f['records'] = (f['records'] + [e['id']])[-24:]


def candidates(w, n, planning=False):
    if n['faction'] is None:
        return []
    f = w.factions[n['faction']]
    options = []
    def add(kind, detail, **extra):
        options.append(dict(id=f"{kind}:{f['id']}", kind=kind, target=f['id'],
                            label=f'{LABELS[kind]} · {detail}', **extra))
    add('leave_faction', f['name'])
    if n['age_months'] >= 192 and n['health'] > 30:
        # Ordinary duty is the service evidence monthly assessment requires.
        at_seat = f['place'] == n['place']
        add('sect_service', f"{f['duty'] if at_seat else '在外传讯护持'}（一周）", at_seat=at_seat)
    if f['place'] != n['place'] or n['age_months'] < 192:
        return options
    if rank(w, n)['grade'] <= 4 and n['health'] > 45 and combat_attributes.rating(n) >= 25:
        students = [w.npcs[i] for i in w.residents.get(n['place'], []) if i != n['id']
                    and w.npcs[i]['faction'] == f['id'] and w.npcs[i]['age_months'] >= 120
                    and w.npcs[i]['health'] > 30 and combat_attributes.rating(n) >= combat_attributes.rating(w.npcs[i]) + 8
                    and n['relationships'].get(str(i), {}).get('affinity', 0) >= -25
                    and relationships.partner_available(w, w.npcs[i], planning)]
        for q in sorted(students, key=lambda q: (combat_attributes.rating(q), q['id']))[:2]:
            options.append(dict(id=f'faction_guide:{q["id"]}', kind='faction_guide', target=q['id'],
                                label='同门授业 · 指点'+q['name']+'（双方各一周）'))
    if n['wealth'] >= 6 and n['contribution'] >= 5 and n['health'] > 30 and combat_attributes.rating(n) >= 12:
        tutors = [w.npcs[i] for i in w.residents.get(n['place'], []) if i != n['id']
                  and w.npcs[i]['faction'] == f['id'] and w.npcs[i]['age_months'] >= 192
                  and w.npcs[i]['health'] > 30 and combat_attributes.rating(w.npcs[i]) > combat_attributes.rating(n) + 8
                  and relationships.partner_available(w, w.npcs[i], planning)]
        if tutors:
            tutor = max(tutors, key=lambda q: (combat_attributes.rating(q), -q['id']))
            add('faction_study', f"向{tutor['name']}请益，2银 / 一周", tutor=tutor['id'])
        elif f['art_id'] not in n['martial']['arts']:
            add('faction_study', f"研习{f['art']}，2银 / 一周", tutor=None)
    return options


def score(w, n, a):
    if a['kind'] == 'leave_faction':
        leader = w.factions[n['faction']]['leader']
        resentment = max(0, -n['relationships'].get(str(leader), {}).get('affinity', 0))
        return (-20 + (100 - n['traits']['integrity']) * .15
                + (35 + min(30, resentment * .3) if leader != n['id'] and resentment > 25 else 0)
                + (15 if n['beliefs']['tradition'] < 30 else 0))
    if a['kind'] == 'sect_service':
        return (13 + n['traits']['integrity'] * .12 + (4 if a.get('at_seat') else 0)
                + (14 if n['goal']['kind'] == 'renown' else 0))
    if a['kind'] == 'faction_study':
        return 16 + n['traits']['ambition'] * .1 + (25 if n['goal']['kind'] == 'mastery' else 0)
    if a['kind'] == 'faction_guide':
        return 15 + n['traits']['integrity'] * .1 + n['traits']['kindness'] * .08
    return (16 + n['traits']['integrity'] * .1 + max(0, 30 - n['wealth']) * .3
            + (20 if n['goal']['kind'] == 'renown' else 0))


def execute(w, n, a, facts):
    f = w.factions[n['faction']]
    before = {k: n[k] for k in ('wealth', 'health')}
    prior_rank = rank(w, n)
    people, causes = [n['id']], [n['last_event'], f['last_event']]
    kind = a['kind']
    facts['faction'] = f['id']
    if kind not in LABELS:
        raise ValueError('门派事项已移除或不存在')
    if kind == 'sect_service':
        at_seat = f['place'] == n['place']
        gain = 1. if at_seat else .5
        n['contribution'] += gain
        n['reputation'] = min(100., n['reputation'] + .3)
        # A sect with silver to spare pays a small stipend for duty, from its real treasury.
        stipend = 1. if f['fund'] - 1. >= float_needed(w, f) else 0.
        if stipend:
            f['fund'] = round(f['fund'] - stipend, 4); f['expenses'] = round(f['expenses'] + stipend, 4)
            n['wealth'] = round(n['wealth'] + stipend, 4)
        facts.update(accepted=True, contribution=gain, at_seat=at_seat, duty=f['duty'], stipend=stipend)
        f['tasks_completed'] += 1
        text = (f"{n['name']}在{f['seat_name']}{f['duty']}，记门中贡献{gain:g}" if at_seat else
                f"{n['name']}在外为{f['name']}传讯护持，记门中贡献{gain:g}") + (f"，领津贴{stipend:g}银。" if stipend else "。")
    elif kind == 'faction_study':
        q = w.npcs[a['tutor']] if a.get('tutor') else None
        if q:
            people.append(q['id'])
            causes.append(q['last_event'])
        n['wealth'] -= 2
        if q:
            q['wealth'] += 1
        f['fund'] += 1 if q else 2
        f['revenue'] += 1 if q else 2
        gain = min(1.5, (combat_attributes.rating(q) - combat_attributes.rating(n)) * .06) if q else .5
        pass  # Retired martial proficiency is no longer stored.
        if q:
            w.link(n, q, 3, 3)
        if f['art_id'] not in n['martial']['arts']:
            martial.learn(n, f['art_id'])
            transmission = dict(art=f['art_id'], name=f['art'], learned=True)
        else:
            transmission = martial.transmit(q, n) if q else None
        facts.update(tutor=q['id'] if q else None, payment=2, tutor_income=1 if q else 0,
                     treasury_income=1 if q else 2, transmission=transmission,
                     skill_gain=gain, partner_time_cost=1 if q else 0)
        text = (f"{n['name']}在{f['name']}请益{f['art']}，付银2两，武学增长{gain:.2f}。")
    elif kind == 'faction_guide':
        q = w.npcs[a['target']]
        prior_student_rank = rank(w, q)
        people.append(q['id']); causes.append(q['last_event'])
        reciprocal = w.relation(q, n['id'])
        accepted = reciprocal['affinity'] >= -25 and reciprocal['trust'] >= -25 and q['mood']['anger'] < 65
        gain = min(1., (combat_attributes.rating(n) - combat_attributes.rating(q)) * .025) if accepted else 0.
        transmission = None
        if accepted:
            pass  # Retired martial proficiency is no longer stored.
            n['contribution'] += 1
            n['health'] -= .5
            w.link(n, q, 3, 2)
            if f['art_id'] in n['martial']['arts'] and f['art_id'] not in q['martial']['arts'] and combat_attributes.rating(q) >= 12:
                martial.learn(q, f['art_id'])
                transmission = f['art_id']
        facts.update(student=q['id'], accepted=accepted, skill_gain=gain, transmission=transmission,
                     contribution=1 if accepted else 0, partner_time_cost=1)
        n['cooldowns'][a['id']] = w.month + 1
        text = f"{n['name']}邀{q['name']}同门授业，" + (f"对方领受指点，武学增长{gain:.2f}。" if accepted else '对方因旧怨或盛怒暂不愿受教。')
    else:
        n['membership_history'].append({'faction': f['id'], 'name': f['name'], 'since': n['faction_since'],
                                        'until': w.month, 'contribution': n['contribution']})
        facts.update(previous_contribution=n['contribution'], rejoin_after=w.month + 12)
        n['faction'], n['faction_since'], n['contribution'] = None, None, 0.
        n['faction_rejoin_after'] = w.month + 12
        if f['leader'] == n['id']:
            f['leader'] = None
            f['vacancy_previous'] = n['id']
        text = f"{n['name']}辞别{f['name']}，旧门贡献留入履历，一年后方可另择组织。"
    facts['actor_delta'] = {k: round(n[k] - v, 4) for k, v in before.items()}
    e = w.emit(kind, people, text, .65 if kind == 'leave_faction' else .45, causes, facts)
    record(f, e)
    if kind == 'faction_study' and q:
        relationships.reserve_partner(w, q, a, e, '同门授业')
    if kind == 'faction_guide':
        relationships.reserve_partner(w, q, a, e, '领受指点' if accepted else '婉拒授业')
        promote(w, q, prior_student_rank, e['id'])
    if kind == 'leave_faction':
        npc_plans.close(w, n, '中断', '已辞别原门派，原门中安排终止', e['id'])
        n['membership_history'][-1]['event_id'] = e['id']
        if f['vacancy_previous'] == n['id']:
            f['vacancy_event'] = e['id']
        elect(w, f)
    else:
        promote(w, n, prior_rank, e['id'])
    return e


def promote(w, n, before, cause):
    from . import rank_progression
    if rank_progression.active(w):
        return
    after = rank(w, n)
    if before != after:
        f = w.factions[n['faction']]
        e = w.emit('faction_rank', [n['id']], f"{n['name']}凭门中贡献与本领，由{before['grade_label']}{before['title']}升为{f['name']}{after['grade_label']}{after['title']}。",
                   .7, [cause], {'faction': f['id'], 'before': before, 'after': after})
        record(f, e)


def elect(w, f, annual=False):
    from . import rank_progression
    members = [n for n in w.npcs.values() if n['alive'] and n['faction'] == f['id'] and n['age_months'] >= 192]
    h=w.player['protagonist']
    if rank_progression.active(w) and h['created'] and h['faction']==f['id']:
        members.append(rank_progression.person(w,'player'))
    old = f['leader'] if f['leader'] is not None else f['vacancy_previous']
    current = rank_progression.person(w,'player') if old==-1 and h['created'] else w.npcs.get(old)
    valid = current and current['alive'] and current['faction'] == f['id'] and current['age_months'] >= 192
    if valid and rank_progression.active(w):
        progress=current.get('rank_progress',{}).get('sect',{})
        valid = w.month >= progress.get('censure_until',-1) and progress.get('leader_deficient',0)<3
    if valid and not annual:
        return
    members = [n for n in members if rank_progression.leader_eligible(w,n)]
    if not members:
        if valid:
            return
        f['leader'] = None
        if current and current['alive'] and current['faction']==f['id']:
            e=w.emit('leadership_vacancy',[current['id']] if current['id']>0 else [],f"{f['name']}主持之位因资格中止暂缺，候选人须具备实际资历后再推举。",.8,
                     [f['last_event']],dict(faction=f['id'],previous=current['id'],reason='无合资格主持'))
            record(f,e)
            if old==-1:rank_progression.emit(w,'player','rank_change',f"{w.player['name']}因持续失职失去{f['name']}主持之位，按原有门中资历重新分品。",dict(system='sect',faction=f['id'],reason='主持资格中止'),[e['id']])
        return
    merit = lambda n: max(0.,n['contribution']-n.get('rank_progress',{}).get('sect',{}).get('penalty',0.)) + n['reputation'] * .3 + combat_attributes.rating(n) * .15
    leader = max(members, key=lambda n: (merit(n), -n['id']))
    if valid and (w.month - f['leader_review_since'] < 72
                  and merit(leader) < merit(current) + 15):
        return
    if old == leader['id']:
        return
    f['leader'], f['leader_since'], f['leader_review_since'] = leader['id'], w.month, w.month
    people = [n['id'] for n in members if n['id']>0]
    if current and current['alive'] and old>0 and old not in people:
        people.append(old)
    e = w.emit('promotion', people, f"{f['name']}按贡献、声望与本领推举{leader['name']}主持事务。", .8,
               [f['last_event'], f['vacancy_event'], leader['last_event']], {'faction': f['id'], 'leader': leader['id'], 'previous': old,
                                                       'merit': round(merit(leader), 4)})
    record(f, e)
    if old==-1 or leader['id']==-1:
        rank_progression.emit(w,'player','rank_change',f"{f['name']}正式推举{leader['name']}主持事务，{w.player['name']}的门中职衔据此调整。",
                              dict(system='sect',faction=f['id'],leader=leader['id'],previous=old),[e['id']])
    f['vacancy_previous'], f['vacancy_event'] = None, None


def monthly(w):
    from . import sect_life
    for f in w.factions:
        elect(w, f)
    if w.month % 3 == 0:
        for f in w.factions:
            relieve(w, f, 6., 15.)
    sect_life.monthly(w)


def relieve(w, f, amount, below):
    """Quarterly relief for struggling members while the treasury is above its working float."""
    reserve = float_needed(w, f)
    needy = sorted((n for n in w.npcs.values() if n['alive'] and n['faction'] == f['id'] and n['wealth'] < below),
                   key=lambda n: (n['wealth'], n['id']))
    for n in needy:
        if f['fund'] - amount < reserve:
            break
        f['fund'] = round(f['fund'] - amount, 4); f['expenses'] = round(f['expenses'] + amount, 4)
        n['wealth'] = round(n['wealth'] + amount, 4)
        people = list(dict.fromkeys([n['id']] + ([f['leader']] if f['leader'] and f['leader'] > 0 else [])))
        e = w.emit('faction_aid', people, f"{f['name']}以库中余银{amount:g}两接济门人{n['name']}。", .55,
                   [n['last_event'], f['last_event']], {'faction': f['id'], 'receiver': n['id'], 'silver': amount,
                                                       'reserve': round(reserve, 4), 'quarterly': True})
        record(f, e)


def annual(w):
    for f in w.factions:
        elect(w, f, annual=True)
        needy = sorted((n for n in w.npcs.values() if n['alive'] and n['faction'] == f['id'] and n['wealth'] < 15),
                       key=lambda n: (n['wealth'], n['id']))
        for n in needy:
            if f['fund'] < 8:
                break
            f['fund'] -= 8
            f['expenses'] += 8
            n['wealth'] += 8
            people = list(dict.fromkeys([n['id']] + ([f['leader']] if f['leader'] and f['leader']>0 else [])))
            e = w.emit('faction_aid', people, f"{f['name']}拨库银8银救助{n['name']}。", .65,
                       [n['last_event'], f['last_event']], {'faction': f['id'], 'receiver': n['id'], 'silver': 8})
            record(f, e)


def view(w, f):
    members = [n for n in w.npcs.values() if n['alive'] and n['faction'] == f['id']]
    hero = w.player['protagonist']
    player_member = hero.get('faction') == f['id']
    grades = sect_ranks.ladder(f)
    for n in members:
        row = grades[rank(w, n)['grade']-1]
        row['population'] = row.get('population', 0)+1
    for row in grades:
        row.setdefault('population', 0)
    if player_member:
        grades[player_membership(w)['grade']-1]['population'] += 1
    ranks = {row['title']: row['population'] for row in grades}
    leader_name=w.player['name'] if f['leader']==-1 else w.npcs[f['leader']]['name'] if f['leader'] in w.npcs else '暂缺'
    return {**f, 'leader_name':leader_name,'place_name': PLACES[f['place']]['name'],
            'members': len(members)+int(player_member), 'player_member': player_member,
            'local_members': sum(n['place'] == f['place'] for n in members)+int(player_member and hero['place']==f['place']),
            'ranks': ranks, 'rank_system': dict(version=sect_ranks.VERSION, grades=grades,
                description='一品最高，九品入门；主持须真实推举。启用品阶考核后，其余品级逐阶按月考核贡献、本领、声望与履职，违纪或持续失职可降阶。')}


def player_membership(w):
    h = w.player['protagonist']
    if h.get('faction') is None:
        return None
    f = w.factions[h['faction']]
    standing = rank(w, dict(h,id=-1,combat_attributes=w.player['combat_attributes']))
    return dict(faction=f['id'], key=f['key'], name=f['name'], **standing,
                contribution=h['contribution'], since=h['faction_since'])


def recruiting_faction(w, recruiter):
    faction_id = recruiter.get('faction')
    if not recruiter['alive'] or faction_id is None:
        return None
    faction = w.factions[faction_id]
    return faction if faction['kind'] == 'sect' and rank(w, recruiter)['grade'] <= 3 else None


def player_join_reason(w, recruiter):
    if recruiting_faction(w, recruiter) is None:
        return '只有本门派三品及以上人物可以引收入门'
    h = w.player['protagonist']
    if h.get('faction') is not None:
        return '已经加入门派，请先辞别原门派'
    if w.month < h.get('faction_rejoin_after', -1):
        return '辞别门派满一年后方可另择门派'
    if combat_attributes.rating(w.player) < 12:
        return '战斗能力须达到12'
    return ''


def player_options(w, faction_id):
    from . import city_maps, sect_life
    h = w.player['protagonist']; f = w.factions[faction_id]
    city = city_maps._world_layout(w, h['place'])
    area = city_maps.city_regions.area_at(city, h['row'], h['col'])
    at_seat = area is not None and area.get('faction') == faction_id
    counts = h.get('sect_counts', {}) if h.get('sect_activity_month') == w.month else {}
    choices = [('donate', '捐助门中 · 4银 / 贡献1.5', 2),
               ('leave', '辞别'+f['name'], 2)] if h.get('faction') == faction_id else []
    result = []
    for action, label, points in choices:
        reason = '请先创建主角' if not h['created'] else '请先走入这座门派驻地' if not at_seat else ''
        if not reason and h['points'] < points:
            reason = '本月行动力不足'
        if not reason and counts.get(action, 0) and action == 'donate':
            reason = '本月已办理此事'
        if not reason and action == 'donate' and w.player['wealth'] < 4:
            reason = '银两不足'
        result.append(dict(action='sect_'+action, faction=faction_id, label=label,
                           points=points, enabled=not reason, reason=reason))
    # Commissions and penance come first on the page; donation and leaving stay last.
    return sect_life.options(w, f) + result


def player_action(w, action, faction_id, choice=None):
    from . import protagonist, sect_life
    if type(faction_id) is not int or not 0 <= faction_id < len(w.factions):
        raise ValueError('门派不存在')
    if choice is not None and not isinstance(choice, str):
        raise ValueError('差事编号无效')
    option = next((o for o in player_options(w, faction_id) if o['action'] == action
                   and o.get('choice') == choice), None)
    if option is None:
        raise ValueError('当前门派身份不允许此操作' if choice is None else '差事已变化，请刷新门派页')
    if not option['enabled']:
        raise ValueError(option['reason'])
    h = w.player['protagonist']; f = w.factions[faction_id]
    if 'choice' in option:
        event, points = sect_life.perform_option(w, f, action, choice)
        h['points'] -= points
        return event
    kind = action.removeprefix('sect_'); facts = dict(faction=faction_id, key=f['key'], points=option['points'])
    if kind == 'donate':
        w.player['wealth'] -= 4; f['fund'] += 4; f['revenue'] += 4
        h['contribution'] += 1.5
        facts.update(payment=4, contribution=1.5)
        text = f"{w.player['name']}向{f['name']}捐助4银，积累贡献1.5点。"
        event = protagonist.record(w, 'hero_'+action, text, facts)
        record(f, event)
    else:
        event = sect_life.depart(w, f)
    h['points'] -= option['points']
    if h.get('sect_activity_month') != w.month:
        h['sect_activity_month'] = w.month; h['sect_counts'] = {}
    h['sect_counts'][kind] = h['sect_counts'].get(kind, 0)+1
    return event
