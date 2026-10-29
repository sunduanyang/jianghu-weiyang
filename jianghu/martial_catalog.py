"""Eight authored nine-grade curricula, one per sect. Grade one is the highest grade."""
from copy import deepcopy
from .sect_catalog import SCHOOLS

VERSION = 1
ATTRIBUTE_KEYS = dict(体魄='constitution', 膂力='strength', 劲力='power', 灵巧='dexterity',
                      身法='footwork', 耐力='endurance', 调息='breathing', 根基='rooting',
                      护体='protection', 韧性='resilience')
BUDGETS = {9: (1,1,0), 8: (2,1,0), 7: (2,1,1), 6: (3,1,1), 5: (3,2,1),
           4: (4,3,1), 3: (5,3,2), 2: (7,4,2), 1: (8,5,3)}
# Rows run from ninth grade to first grade. Each art has its own selected axes.
CURRICULA = {
 'shaolin': '''少林扎马桩|桩功|根基 体魄 耐力
小洪拳|拳掌|膂力 根基 韧性
罗汉拳|拳掌|劲力 膂力 体魄
韦陀护寺杖|杖法|根基 护体 耐力
铁布衫功|炼体|护体 体魄 韧性
大力金刚掌|拳掌|膂力 劲力 根基
般若禅掌|拳掌|劲力 调息 灵巧
金刚不坏体|炼体|护体 韧性 体魄
易筋经|内功|体魄 膂力 调息''',
 'huashan': '''华山养气诀|内功|调息 耐力 根基
苍龙岭步法|身法|身法 灵巧 耐力
华山入门剑|剑法|灵巧 劲力 身法
希夷睡功|内功|调息 韧性 护体
落雁峰身法|身法|身法 灵巧 根基
莲花峰快剑|剑法|灵巧 劲力 身法
太华混元掌|拳掌|劲力 膂力 根基
云台吐纳功|内功|护体 调息 体魄
太华险峰剑意|剑法|劲力 灵巧 身法''',
 'wudang': '''松溪养息法|内功|调息 耐力 体魄
武当两仪桩|桩功|根基 护体 韧性
踏罡游龙步|身法|身法 灵巧 调息
绵掌化劲诀|拳掌|韧性 根基 劲力
玄门吐纳经|内功|调息 护体 耐力
绕指柔剑|剑法|灵巧 身法 劲力
两仪清虚剑|剑法|劲力 灵巧 根基
纯阳无极功|内功|护体 调息 体魄
太极拳剑真解|拳剑|根基 韧性 劲力''',
 'emei': '''峨眉静坐法|内功|调息 耐力 韧性
金顶云步|身法|身法 灵巧 耐力
峨眉十二桩|桩功|根基 韧性 体魄
峨眉刺法|刺法|灵巧 劲力 身法
普贤护身掌|拳掌|根基 护体 韧性
双刺穿花手|刺法|灵巧 身法 劲力
峨眉通臂拳|拳掌|劲力 灵巧 膂力
普贤愿力功|内功|护体 调息 体魄
万佛朝宗刺诀|刺法|劲力 灵巧 身法''',
 'beggars': '''鹑衣耐劳功|炼体|耐力 体魄 韧性
行脚赶路步|身法|身法 耐力 灵巧
莲花护身桩|桩功|根基 韧性 护体
缠丝擒拿手|擒拿|灵巧 膂力 根基
铜锤破阵拳|拳掌|膂力 劲力 体魄
逍遥游身法|身法|身法 灵巧 耐力
打狗棒法|杖法|灵巧 劲力 根基
混天行气功|内功|耐力 调息 韧性
降龙十八掌|拳掌|劲力 膂力 根基''',
 'wudu': '''苗寨辨药法|内功|调息 耐力 体魄
山岚瘴步|身法|身法 耐力 灵巧
蛇形缠手|擒拿|灵巧 膂力 韧性
百草避毒功|炼体|护体 体魄 韧性
蝎尾钩|钩法|灵巧 劲力 身法
蜈蚣百足步|身法|身法 灵巧 根基
蟾蜍吐纳功|内功|调息 护体 耐力
千蛛万毒手|拳掌|劲力 灵巧 膂力
五毒神掌|拳掌|劲力 调息 灵巧''',
 'mingjiao': '''二宗三际诀|内功|调息 耐力 根基
听者行脚功|炼体|耐力 体魄 韧性
明使护身牌|盾术|护体 根基 膂力
新月弯刀|刀法|灵巧 劲力 身法
光明拳|拳掌|膂力 劲力 体魄
藤牌滚刀术|盾术|根基 灵巧 护体
明尊弯刀|刀法|劲力 灵巧 身法
五明子护体功|炼体|护体 韧性 体魄
大明尊真经|内功|劲力 调息 体魄''',
 'mizong': '''密宗定息法|内功|调息 耐力 根基
护法承力桩|桩功|膂力 根基 体魄
金刚拳|拳掌|劲力 膂力 韧性
降魔杵法|杵法|根基 护体 膂力
拙火定|内功|调息 体魄 耐力
金刚大力掌|拳掌|膂力 劲力 根基
护法金刚身|炼体|韧性 护体 根基
胜乐运息诀|内功|调息 护体 耐力
大手印|拳掌|劲力 调息 体魄''',
}

ARTS = {}
SCHOOL_ARTS = {}
for _school in SCHOOLS:
    _rows = CURRICULA[_school['key']].splitlines()
    if len(_rows) != 9:
        raise ValueError(_school['name']+'须有九品功法')
    _ids = []
    for _grade, _row in zip(range(9,0,-1), _rows):
        _name, _kind, _axes = _row.split('|')
        _bonus = {ATTRIBUTE_KEYS[axis]: points for axis, points in zip(_axes.split(), BUDGETS[_grade]) if points}
        _id = f"{_school['key']}_g{_grade}"
        ARTS[_id] = dict(id=_id, name=_name, kind=_kind, grade=_grade,
            grade_label='一二三四五六七八九'[_grade-1]+'品', school=_school['key'],
            school_name=_school['name'], bonuses=_bonus,
            description='；'.join(f'{axis}+{points}' for axis, points in zip(_axes.split(), BUDGETS[_grade]) if points))
        _ids.append(_id)
    SCHOOL_ARTS[_school['key']] = _ids


def bonuses(actor):
    result = dict.fromkeys(ATTRIBUTE_KEYS.values(), 0)
    curriculum = actor.get('martial', {}).get('curriculum', {})
    if curriculum.get('version') != VERSION:
        return result
    for art_id in set(curriculum.get('known', [])):
        for key, value in ARTS.get(art_id, {}).get('bonuses', {}).items():
            result[key] += value
    return result


def school_view(key):
    school = next(s for s in SCHOOLS if s['key'] == key)
    return dict(key=key, name=school['name'], arts=[deepcopy(ARTS[k]) for k in reversed(SCHOOL_ARTS[key])])


def catalog():
    return [school_view(s['key']) for s in SCHOOLS]
