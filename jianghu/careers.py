from . import temperament
from . import combat_attributes
"""Livelihoods and earned social standing; the world still settles every action.

Inspired by the separation of occupation and graded identity in wuxia sandboxes.
These are original, local rules, not a reproduction of another game's data.
"""
from copy import deepcopy
from .atlas import PLACES
from . import social_ranks


PROFESSIONS = {
    '农人': {'base': 9, 'trait': '-boldness', 'places': [0, 1, 2, 3, 4],
           'demand': [1.2, .9, .85, 1.1, 1.05], 'cost': 4, 'entry': 0,
           'description': '耕作供给乡里，青溪与桃花渡较宜农事。'},
    '行商': {'base': 10, 'trait': 'sociability', 'places': [0, 2, 3],
           'demand': [1, .85, 1.2, 1.1, .9], 'cost': 12, 'entry': 0,
           'description': '往来贩运，商埠收益较高，合营时可运用商术。'},
    '药师': {'base': 10, 'trait': 'kindness', 'places': [0, 1, 2],
           'demand': [1, 1.15, 1.1, 1, .95], 'cost': 8, 'entry': 12,
           'description': '采药诊治，须先学医理；熟练度提高诊治效果。'},
    '匠人': {'base': 10, 'trait': '-boldness', 'places': [0, 2, 4],
           'demand': [1.05, .9, 1.15, .9, 1.1], 'cost': 8, 'entry': 8,
           'description': '修器制具，先学手艺再置办工具。'},
    '镖师': {'base': 10, 'trait': 'integrity', 'places': [0, 2, 4],
           'demand': [1, 1, 1.1, 1, 1.15], 'cost': 6, 'entry': 0, 'martial': 20,
           'description': '护送商旅，入行须武学20；习武也能积累护镖经验。'},
    '渔人': {'base': 9, 'trait': '-boldness', 'places': [0, 3],
           'demand': [1.1, .65, .9, 1.3, .65], 'cost': 4, 'entry': 0,
           'description': '临水捕捞，在桃花渡最能施展本领。'},
    '猎户': {'base': 9, 'trait': 'boldness', 'places': [1, 4],
           'demand': [.9, 1.25, .7, .85, 1.2], 'cost': 4, 'entry': 0, 'martial': 12,
           'description': '入山猎获，须武学12，栖云山与寒石寨较宜。'},
    '书生': {'base': 9, 'trait': 'ambition', 'places': [0, 2],
           'demand': [1, .85, 1.25, .85, .8], 'cost': 6, 'entry': 8,
           'description': '抄书授读，须先积累学识。'},
    '艺人': {'base': 9, 'trait': 'sociability', 'places': [0, 2, 3],
           'demand': [1, .75, 1.2, 1.15, .8], 'cost': 4, 'entry': 0,
           'description': '以曲艺娱人，城镇渡口容易谋生。'},
    '短工': {'base': 6, 'trait': '-boldness', 'places': [0, 1, 2, 3, 4],
           'demand': [1, .85, 1.1, 1.1, .9], 'cost': 0, 'entry': 0,
           'description': '没有固定产业，以搬运和杂役糊口，属于九品游民；攒下置业费后可另寻营生。'},
}
GRADES = social_ranks.GRADES
LEVELS = [(0, '初入行'), (15, '熟手'), (35, '行家'), (60, '名家'), (85, '宗匠')]
OFFICE_GRADES = {title: social_ranks.grade_for(title) for title in ('县令', '捕头', '主簿')}


def family_trade(w, n):
    jobs = [w.npcs[i]['job'] for i in n['parents'] if w.npcs[i]['job'] in PROFESSIONS]
    return (jobs or list(PROFESSIONS))[(n['id'] + w.seed) % len(jobs or PROFESSIONS)]


def initialize(w, n, legacy=False):
    if 'career' in n:
        return
    profession = n['job'] if n['job'] in PROFESSIONS else family_trade(w, n) if n['age_months'] >= 120 else None
    # A registration is an initial condition, never an invented series of jobs.
    experience = 0 if legacy or n['age_months'] < 192 else min(30, (n['age_months']-192)/24 + n['id'] % 9)
    n['career'] = {'profession': profession, 'experience': {profession: round(experience, 4)} if profession else {},
                   'since': w.month, 'last_change': w.month-24, 'last_event': None,
                   'source': '旧档登记' if legacy else '初始条件'}


def experience(n, profession=None):
    return n['career']['experience'].get(profession or n['career']['profession'], 0)


def level(n):
    return next(row for row in reversed(LEVELS) if experience(n) >= row[0])


def gain(n, profession, amount):
    before = experience(n, profession)
    after = round(min(100, before+amount), 4)
    n['career']['experience'][profession] = after
    return {'profession': profession, 'before': before, 'after': after, 'gain': round(after-before, 4)}


def place_archetype(place):
    if place < 5:
        return place
    site = PLACES[place]
    if site['kind'] == 'mountain':
        return 1
    if site['kind'] in ('port', 'island'):
        return 3
    return 2 if site['trade'] >= 1.1 else 0 if site['trade'] >= .95 else 4


def income(n, place=None, profession=None):
    profession = profession or n['career']['profession']
    if profession not in PROFESSIONS:
        return 0.
    p = PROFESSIONS[profession]
    aptitude = .85 + temperament.lean(n, p['trait']) * .003
    gross = (p['base'] + experience(n, profession)*.09) * p['demand'][place_archetype(n['place'] if place is None else place)] * aptitude
    if profession in ('镖师', '猎户'):
        gross += combat_attributes.rating(n)*.035
    return round(gross * (.45 if n['age_months'] < 192 else 1), 4)


def entry_blockers(w, n, profession):
    p = PROFESSIONS[profession]
    reasons = []
    if n['age_months'] < 192:
        reasons.append('年满16岁后方可独立转业')
    if not n['alive']:
        reasons.append('已故人物不能转业')
    if n['health'] <= 30:
        reasons.append('体力须高于30')
    if n['office']:
        reasons.append('在任官吏须先完成任期')
    if n['career']['profession'] == profession:
        reasons.append('已从事此业')
    if w.month-n['career']['last_change'] < 24:
        reasons.append('距上次转业须满24个月')
    if place_archetype(n['place']) not in p['places']:
        reasons.append('此地缺少入行机会')
    if n['wealth'] < p['cost'] + 4:
        reasons.append(f"需{p['cost']}银置业并留4银生活费")
    if experience(n, profession) < p['entry']:
        reasons.append(f"此业熟练度须达{p['entry']}")
    if combat_attributes.rating(n) < p.get('martial', 0):
        reasons.append(f"战斗能力须达{p['martial']}")
    return reasons


def actions(w, n, planning=False):
    from . import relationships
    if not n['alive'] or n['age_months'] < 120 or n['health'] <= 30:
        return []
    options = []
    for job in PROFESSIONS:
        if not entry_blockers(w, n, job):
            options.append({'id': f'change_job:{job}', 'kind': 'change_job', 'target': job,
                            'label': f'转业 · {job}（{PROFESSIONS[job]["cost"]}银）'})
    if n['wealth'] >= 6:
        # Local tradespeople are publicly visible; no private traits are disclosed.
        tutors = [w.npcs[i] for i in w.residents.get(n['place'], []) if i != n['id']]
        best = {}
        for tutor in tutors:
            job = tutor['career']['profession']
            if (not tutor['alive'] or tutor['age_months'] < 192 or tutor['health'] <= 30
                    or not relationships.partner_available(w, tutor, planning)
                    or job not in PROFESSIONS or experience(tutor) < max(15, experience(n, job)+5)):
                continue
            if job not in best or experience(tutor) > experience(best[job]):
                best[job] = tutor
        for job, tutor in best.items():
            options.append({'id': f'learn_trade:{tutor["id"]}', 'kind': 'learn_trade', 'target': tutor['id'],
                            'profession': job, 'label': f'学艺 · 向{tutor["name"]}学{job}（2银）'})
    return options


def note_income(n, earned):
    """Remember what work has actually paid lately (per working week), not one lucky or idle week."""
    c = n['career']
    c['income_ema'] = round(earned if c.get('income_ema') is None else .7*c['income_ema'] + .3*earned, 4)


def entrants(w, n, job, months=6):
    """Neighbours who have just switched into this trade: the market they leave behind is thinner."""
    return sum(1 for i in w.residents.get(n['place'], []) if i != n['id'] and w.npcs[i]['alive']
               and w.npcs[i]['career']['profession'] == job and w.npcs[i]['career'].get('source') == '自主转业'
               and w.month - w.npcs[i]['career'].get('last_change', -999) < months)


def switch_gain(w, n, job):
    """Expected gain from switching trades, as one person would judge it.

    The current trade is valued by what it has really paid lately as well as today's
    market, so a single bad week does not empty a trade; the new trade is discounted
    for neighbours who have just crowded into it.
    """
    from . import economics
    if not economics.active(w):
        return income(n, profession=job) - income(n)
    today = economics.expected_income(w, n)
    remembered = n['career'].get('income_ema')
    current = today if remembered is None else max(today, .6*remembered + .4*today)
    # A trade is judged by what neighbours in it actually earn, when there are any to watch.
    peers = sorted(w.npcs[i]['career']['income_ema'] for i in w.residents.get(n['place'], [])
                   if i != n['id'] and w.npcs[i]['alive'] and w.npcs[i]['career']['profession'] == job
                   and w.npcs[i]['career'].get('income_ema') is not None)
    # With no one to watch, an untried trade is only a guess and is discounted.
    prospect = peers[len(peers)//2] if len(peers) >= 2 else economics.expected_income(w, n, job) * .6
    return prospect / (1 + .2*entrants(w, n, job)) - current


def review_month(w, n):
    """People reconsider their trade on their own rhythm, not all in the same month."""
    return (n['id'] + w.month) % 3 == 0


def score(w, n, a):
    if a['kind'] == 'change_job':
        job = a['target']
        settled = n['career'].get('income_ema') is not None
        if not settled or not review_month(w, n):
            # Off one's own rhythm, or before knowing what one's trade pays: not now.
            return -40 - PROFESSIONS[job]['cost']*.35
        difference = switch_gain(w, n, job)
        # Switching costs, attachment to one's craft and a two-year commitment discourage wandering.
        # Switching only appeals when the new trade really looks better; a wish for wealth
        # sharpens a real gain but does not invent one.
        eager = min(8., difference*2) if n['goal']['kind'] == 'wealth' and difference > 0 else 0.
        return difference*5 + eager - 6 - PROFESSIONS[job]['cost']*.35 - experience(n)*.06
    job = a['profession']
    return (6 + n['traits']['ambition']*.08 + (10 if n['career']['profession'] == job else 0)
            + (6 if n['goal']['kind'] == 'wealth' else 0))


def grow(w, n):
    if n['age_months'] not in (120, 192):
        return
    before = n['job']
    c = n['career']
    job = c['profession'] or family_trade(w, n)
    c['profession'] = job
    c['experience'].setdefault(job, 0.)
    n['job'] = '学徒' if n['age_months'] == 120 else job
    c.update({'since': w.month, 'last_change': w.month, 'source': '成长入行'})
    e = w.emit('career', [n['id']], f"{n['name']}长成，由{before}转为{n['job']}，研习{job}之道。", .65,
               [n['last_event']], {'before': before, 'after': n['job'], 'profession': job,
                                   'experience': experience(n), 'entry': '成年承接学徒营生，免置业费'})
    c['last_event'] = e['id']
    from . import npc_plans
    npc_plans.close(w, n, '中断', '成长后营生职责改变，重新安排事务', e['id'])


def identity(w, n):
    if not n['alive'] and n.get('final_identity'):
        return deepcopy(n['final_identity'])
    occupation = social_ranks.occupation(n)
    social_grade = social_ranks.grade_for(occupation)
    roles = []
    if n['age_months'] < 120:
        roles.append({'kind': 'life', 'title': '幼童', 'grade': 9})
    elif n['age_months'] < 192:
        roles.append({'kind': 'livelihood', 'title': f'{occupation}学徒', 'grade': social_grade})
    else:
        title = f'{occupation}·{level(n)[1]}' if occupation in PROFESSIONS else occupation
        roles.append({'kind': 'livelihood', 'title': title, 'grade': social_grade})
    if n['faction'] is not None:
        from .organizations import rank
        f = w.factions[n['faction']]
        standing = rank(w, n)
        roles.append({'kind': 'faction', 'title': f'{f["name"]}{standing["title"]}', 'grade': standing['grade']})
    if n['office']:
        roles.append({'kind': 'office', 'title': n['office'], 'grade': social_ranks.grade_for(n['office'])})
    if n.get('social_status'):
        title = n['social_status']['title']
        roles.append({'kind': 'estate', 'title': title, 'grade': social_ranks.grade_for(title)})
    if n['spouse']:
        roles.append({'kind': 'family', 'title': '已婚', 'grade': 9})
    if n['children']:
        roles.append({'kind': 'family', 'title': '母亲' if n['sex'] == '女' else '父亲', 'grade': 9})
    if n['mentor']:
        roles.append({'kind': 'mentorship', 'title': '门徒', 'grade': 9})
    if any(w.npcs[int(i)]['mentor'] == n['id'] for i in n['relationships']):
        roles.append({'kind': 'mentorship', 'title': '授业师父', 'grade': 8})
    sect_member = social_ranks.is_sect_member(w, n)
    primary = (next(r for r in roles if r['kind'] == 'faction') if sect_member else
               min((r for r in roles if r['kind'] in ('life', 'livelihood', 'office', 'estate')),
                   key=lambda r: (r['grade'], r['kind'] != 'office')))
    return {'grade': primary['grade'], 'grade_label': GRADES[primary['grade']],
            'title': primary['title'], 'roles': roles,
            'class_label': None if sect_member else social_ranks.CLASSES[primary['grade']][0],
            'system': 'sect' if sect_member else 'nation',
            'system_label': '门派品级' if sect_member else '社会品阶',
            'country': None if sect_member else social_ranks.territory.owner(w, n['place'])}


def sync_identity(w, n, cause):
    if not n['alive']:
        return
    current = identity(w, n)
    before = n.get('identity')
    n['identity'] = current
    if before and before != current:
        old_roles = {r['title'] for r in before['roles']}
        new_roles = {r['title'] for r in current['roles']}
        changes = ([f'新增「{title}」' for title in sorted(new_roles-old_roles)]
                   + [f'卸下「{title}」' for title in sorted(old_roles-new_roles)])
        if before.get('system') != current['system']:
            changes.append(f"转入{current['system_label']}")
        if before.get('country') != current['country'] and current['country'] is not None:
            changes.append(f"归属{w.countries[current['country']]['name']}")
        direction = '转系' if before.get('system')!=current['system'] else '升阶' if current['grade']<before['grade'] else '降阶' if current['grade']>before['grade'] else '同阶改衔'
        summary = (f"{n['name']}的身份由{before['grade_label']}「{before['title']}」{direction}为"
                   f"{current['grade_label']}「{current['title']}」。"
                   if (before['grade'], before['title']) != (current['grade'], current['title'])
                   else f"{n['name']}的身份关系更新：{'，'.join(changes)}。")
        e = w.emit('identity_change', [n['id']],
                   summary, .6,
                   [cause, n.get('identity_event')], {'before': before, 'after': current,'direction':direction})
        n['identity_event'] = e['id']
        from . import rank_progression
        rank_progression.note_identity(w,n,before,current,e)


def view(w, n):
    from . import economics
    c = n['career']
    xp = experience(n)
    next_level = next((v for v in LEVELS if v[0] > xp), None)
    return {**deepcopy(c), 'experience_current': xp, 'level': level(n)[1],
            'next_level': {'experience': next_level[0], 'title': next_level[1]} if next_level else None,
            'gross_income_estimate': economics.expected_income(w, n) if economics.active(w) else income(n),
            'income_basis': '当前市集库存与实有银两，每次谋生估算' if economics.active(w) else '传统营生估算',
            'identity': identity(w, n)}


def catalog(w, n):
    from . import economics
    return [{'name': job, **deepcopy(p), 'experience': experience(n, job),
             'income_estimate': economics.expected_income(w, n, job) if economics.active(w) else income(n, profession=job), 'blockers': entry_blockers(w, n, job)}
            for job, p in PROFESSIONS.items()]
