from . import combat_attributes
"""Nine social estates for people outside martial sects.

Estate follows a person's actual occupation or public office. Wealth, martial
skill, reputation, kinship and professional experience do not grant a new estate.
"""
from . import territory

VERSION = 1
GRADES = {i: f'{s}品' for i, s in enumerate('一二三四五六七八九', 1)}
CLASSES = {
    1: ('王侯', ('皇帝', '天子', '国主', '王侯', '亲王', '部族首领')),
    2: ('公卿', ('公卿', '宰相', '丞相', '尚书', '大将军')),
    3: ('豪门', ('世家家主', '世家子弟', '权贵', '豪商')),
    4: ('乡绅', ('乡绅', '士绅', '郡守', '县令')),
    5: ('士人', ('书生', '药师', '医师', '学者', '主簿')),
    6: ('商旅', ('行商', '商人', '镖师', '捕头')),
    7: ('百工', ('匠人', '工匠', '艺人')),
    8: ('农户', ('农人', '农民', '农夫', '渔人', '猎户')),
    9: ('游民', ('乞丐', '强盗', '盗匪', '流民', '游民', '短工', '无业', '幼童')),
}
OCCUPATION_GRADES = {name: grade for grade, (_, names) in CLASSES.items() for name in names}


def is_sect_member(w, n):
    return n['faction'] is not None and w.factions[n['faction']]['kind'] == 'sect'


def occupation(n):
    # Keep explicit identities (e.g. a legacy beggar) even if an older career
    # migration registered an unrelated fallback trade.
    if n['age_months'] < 120:
        return '幼童'
    if n['job'] == '学徒':
        return n['career']['profession'] or '无业'
    return n['job'] or n['career']['profession'] or '无业'


def grade_for(title):
    return OCCUPATION_GRADES.get(title, 9)


def player_identity(w):
    h = w.player['protagonist']
    if not h['created']:
        return None
    if is_sect_member(w, h):
        from .organizations import rank
        standing = rank(w, dict(h,id=-1,combat_attributes=w.player['combat_attributes']))
        return {**standing, 'grade_label': GRADES[standing['grade']],
                'title': w.factions[h['faction']]['name'] + standing['title'],
                'class_label': None, 'system': 'sect', 'system_label': '门派品级', 'country': None}
    title = (h.get('social_status') or {}).get('title') or h.get('job') or '游民'
    grade = grade_for(title)
    return {'grade': grade, 'grade_label': GRADES[grade], 'title': title,
            'class_label': CLASSES[grade][0], 'system': 'nation', 'system_label': '社会品阶',
            'country': territory.owner(w, h['place'])}


def country_view(w, country, include_roster=False):
    from . import careers
    residents = [n for n in w.npcs.values() if n['alive'] and territory.owner(w, n['place']) == country]
    civil = [(n, careers.identity(w, n)) for n in residents if not is_sect_member(w, n)]
    rows = [{'grade': grade, 'grade_label': GRADES[grade], 'class_label': label,
             'examples': list(names), 'population': sum(rank['grade'] == grade for _, rank in civil)}
            for grade, (label, names) in CLASSES.items()]
    result = {'version': VERSION, 'country': country, 'population': len(civil),
              'sect_population': len(residents)-len(civil), 'grades': rows,
              'description': '一品最高，九品最低。全体非门派人士按实际营生和社会身份分品；门派人士沿用门中品级。',
              'rules': '品阶随真实转业、兴业、乡里认可和正式任免变化；家资、武力与亲属身份不直接授品。家业衰落设宽限期，病伤不视为失职。幼童按九品登记，学徒随所学营生分品。'}
    if include_roster:
        result['roster'] = [{'id': n['id'], 'name': n['name'], 'place': n['place'], 'job': n['job'],
                             'grade': rank['grade'], 'grade_label': rank['grade_label'],
                             'class_label': rank['class_label'], 'title': rank['title']}
                            for n, rank in sorted(civil, key=lambda pair: (pair[1]['grade'], pair[0]['id']))]
    return result
