"""Teachers hand over real manuals; learning is a separate inventory action."""
from . import items, martial, martial_catalog, social, player_social


def held(w, owner, art_id):
    return any(items.spec(i).get('art') == art_id for i in items.objects(w, owner))


def reason(w, owner, teacher, art_id):
    pupil = items.actor(w, owner)
    player = str(owner) == 'player'
    h = w.player['protagonist']
    if w.battle and w.battle['status'] == 'active':
        return '请先结束切磋'
    if not teacher or not teacher['alive'] or not pupil.get('alive', True):
        return '只能向在世人物求师'
    if str(teacher['id']) == str(owner):
        return '不能向自己求师'
    if player and not h['created']:
        return '请先创建主角'
    if (h['place'] if player else pupil['place']) != teacher['place']:
        return '请先前往老师所在地区'
    if teacher['age_months'] < 192 or teacher['health'] <= 30:
        return '对方尚不便授业'
    if not isinstance(art_id, str) or art_id not in martial_catalog.ARTS or not items.knows(w, teacher['id'], art_id):
        return '老师尚未掌握这门功法'
    if items.knows(w, owner, art_id):
        return '已经掌握这门功法'
    if not player and held(w, owner, art_id):
        return '行囊已有对应秘籍，请先使用学习'
    key = f"{teacher['id']}:{art_id}"
    if not player and pupil['martial'].get('requests', {}).get(key) == w.month:
        return '本月已向这位老师求取过此秘籍'
    if player and h['points'] < 2:
        return '求师需要2行动力'
    if not player and (pupil['time_left'] < 1 or not social.available(w, pupil)):
        return '本月暂无空闲求师'
    if not player and (player_social.free_week(w, teacher) is None or not social.available(w, teacher)):
        return '老师本月的授业时间已排满，请下月再来'
    return ''


def offers(w, teacher):
    known = (martial.resolve(w, teacher) or {}).get('known', [])
    return [dict(art=key, name=martial_catalog.ARTS[key]['name'],
                 reason=reason(w, 'player', teacher, key)) for key in known]


def candidates(w, pupil, teacher):
    if pupil['age_months'] < 120 or pupil['relationships'].get(str(teacher['id']), {}).get('affinity', 0) < 6:
        return []
    known = (martial.resolve(w, teacher) or {}).get('known', [])
    for key in sorted(known, key=lambda k: (-martial_catalog.ARTS[k]['grade'], k)):
        if not reason(w, pupil['id'], teacher, key):
            return [dict(id=f"seek_teacher:{teacher['id']}:{key}", kind='seek_teacher',
                         target=teacher['id'], art=key,
                         label=f"求师 · {teacher['name']} · {martial_catalog.ARTS[key]['name']}")]
    return []


def request(w, owner, teacher_id, art_id, action=None):
    teacher = w.npcs.get(teacher_id)
    blocked = reason(w, owner, teacher, art_id)
    if blocked:
        raise ValueError(blocked)
    pupil = items.actor(w, owner)
    player = str(owner) == 'player'
    relation = (w.player['ties'].get(str(teacher_id), {}) if player else
                pupil['relationships'].get(str(teacher_id), {}))
    accepted = (relation.get('affinity', 0) > -10 and relation.get('trust', 0) > -10 and
                teacher['traits']['loyalty'] + teacher['traits']['compassion'] + relation.get('affinity', 0) >= 65)
    facts = dict(teacher=teacher_id, pupil=str(owner), art=art_id, accepted=accepted, npc_time_cost=0 if player else 1)
    if action:
        facts.update(action=action['id'], actor=pupil['id'], decision_source=pupil['choice_source'], time_cost=action['time_cost'])
    item = None
    if accepted:
        item = next((i for i in items.objects(w, teacher_id) if items.spec(i).get('art') == art_id and i['durability'] > 0), None)
        if item is None:
            item = items.create(w, teacher_id, items.MANUAL_TEMPLATES[art_id], f"{teacher['name']}依所学抄录，授予{pupil['name']}")
            facts['copied'] = True
        facts.update(item_id=item['id'], previous_owner=item['owner'], previous_event=item.get('last_event'))
        items.transfer(w, item, owner)
        if player:
            r = w.player['ties'].setdefault(str(teacher_id), dict(affinity=0., trust=0.))
            r.update(affinity=min(100, r['affinity']+9), trust=min(100, r['trust']+8))
            if not w.player['protagonist'].get('mentor'):
                w.player['protagonist']['mentor'] = teacher_id
        else:
            if not pupil['mentor']:
                pupil['mentor'] = teacher_id
            w.link(pupil, teacher, 9, 8, label='师徒')
    if not player:
        pupil['martial'].setdefault('requests', {})[f'{teacher_id}:{art_id}'] = w.month
    name = martial_catalog.ARTS[art_id]['name']
    text = (f"{pupil['name']}向{teacher['name']}求学{name}，"
            + (f"获授《{name}》秘籍一册，使用秘籍后即可学会。" if accepted else '老师未允传授。'))
    people = [teacher_id] if player else [pupil['id'], teacher_id]
    e = w.emit('manual_teaching', people, text, .65,
               [teacher['last_event'], item.get('last_event') if item else None], facts)
    if item:
        item['last_event'] = e['id']
        if facts.get('copied'):
            item['origin_event'] = e['id']
    if player:
        h = w.player['protagonist']
        h['points'] -= 2
        h['records'].append(e['id'])
        if accepted:
            r.update(last_event=e['id'], last_contact=w.month)
    if not player:
        week = player_social.free_week(w, teacher)
        teacher['time_left'] -= 1
        teacher['monthly_actions'].append(dict(id=f'manual_teaching:{owner}', kind='manual_teaching',
            target=pupil['id'], month=w.month, week=week, time_cost=1, event_id=e['id'], source='NPC求师'))
    return e
