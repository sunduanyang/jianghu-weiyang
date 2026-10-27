"""Fourteen authored nine-grade curricula. Grade one is the highest grade."""
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
 'wudang': '''松溪养息法|内功|调息 耐力 体魄
武当两仪桩|桩功|根基 护体 韧性
踏罡游龙步|身法|身法 灵巧 调息
绵掌化劲诀|拳掌|韧性 根基 劲力
玄门吐纳经|内功|调息 护体 耐力
绕指柔剑|剑法|灵巧 身法 劲力
两仪清虚剑|剑法|劲力 灵巧 根基
纯阳无极功|内功|护体 调息 体魄
太极拳剑真解|拳剑|根基 韧性 劲力''',
 'beggars': '''鹑衣耐劳功|炼体|耐力 体魄 韧性
行脚赶路步|身法|身法 耐力 灵巧
莲花护身桩|桩功|根基 韧性 护体
缠丝擒拿手|擒拿|灵巧 膂力 根基
铜锤破阵拳|拳掌|膂力 劲力 体魄
逍遥游身法|身法|身法 灵巧 耐力
打狗棒法|杖法|灵巧 劲力 根基
混天行气功|内功|耐力 调息 韧性
降龙十八掌|拳掌|劲力 膂力 根基''',
 'shaolin': '''少林扎马桩|桩功|根基 体魄 耐力
小洪拳|拳掌|膂力 根基 韧性
罗汉拳|拳掌|劲力 膂力 体魄
韦陀护寺杖|杖法|根基 护体 耐力
铁布衫功|炼体|护体 体魄 韧性
大力金刚掌|拳掌|膂力 劲力 根基
般若禅掌|拳掌|劲力 调息 灵巧
金刚不坏体|炼体|护体 韧性 体魄
易筋经|内功|体魄 膂力 调息''',
 'quanzhen': '''全真静坐诀|内功|调息 耐力 根基
金雁入门步|身法|身法 灵巧 耐力
三花聚顶掌|拳掌|劲力 根基 调息
全真养气功|内功|耐力 调息 体魄
金雁功|身法|身法 耐力 灵巧
七星剑法|剑法|灵巧 劲力 根基
三清守一诀|内功|护体 调息 韧性
重阳剑意|剑法|劲力 灵巧 韧性
先天功|内功|调息 护体 体魄''',
 'yipin': '''一品堂护卫桩|桩功|根基 体魄 膂力
沙行踏影步|身法|身法 耐力 灵巧
截腕擒拿手|擒拿|灵巧 膂力 韧性
铁鹞碎石拳|拳掌|膂力 劲力 根基
回风快刀|刀法|灵巧 劲力 身法
金城护体功|炼体|护体 体魄 韧性
裂碑穿云掌|拳掌|劲力 膂力 调息
百家归元诀|内功|调息 耐力 护体
一品镇岳真功|内功|劲力 根基 韧性''',
 'tianshan': '''雪岭调息法|内功|调息 耐力 体魄
踏雪寻梅步|身法|身法 灵巧 耐力
冰川剑式|剑法|灵巧 劲力 根基
寒星流转诀|内功|调息 护体 韧性
追风逐雪步|身法|身法 灵巧 根基
游龙回雪剑|剑法|劲力 灵巧 身法
天山折枝手|擒拿|灵巧 韧性 膂力
冰魄凝元功|内功|护体 调息 耐力
天山七剑真解|剑法|劲力 身法 灵巧''',
 'dalun': '''大轮定息法|内功|调息 耐力 根基
伏虎承力桩|桩功|膂力 根基 体魄
雪岭开山掌|拳掌|劲力 膂力 韧性
护寺金轮杖|杖法|根基 护体 膂力
赤焰运息功|内功|调息 体魄 耐力
大轮金刚掌|拳掌|膂力 劲力 根基
火焰刀|掌刀|劲力 灵巧 调息
不动明王身|炼体|韧性 护体 根基
大轮明王真经|内功|劲力 调息 体魄''',
 'tianlong': '''天龙静心诀|内功|调息 耐力 韧性
点苍舒筋功|炼体|体魄 灵巧 根基
枯荣坐禅法|内功|耐力 调息 护体
段氏护身掌|拳掌|根基 劲力 韧性
天南行云步|身法|身法 灵巧 调息
一阳指|指法|劲力 灵巧 根基
枯荣禅功|内功|护体 调息 韧性
天龙通脉经|内功|调息 体魄 耐力
六脉神剑|指法|劲力 灵巧 调息''',
 'bezeklik': '''石窟养息法|内功|调息 耐力 根基
护经行脚功|炼体|耐力 体魄 韧性
绿洲回身步|身法|身法 灵巧 根基
壁画罗汉掌|拳掌|膂力 劲力 体魄
护经杖法|杖法|根基 护体 耐力
千佛静观诀|内功|调息 韧性 护体
金砂伏魔杖|杖法|劲力 膂力 根基
不动经藏身|炼体|韧性 护体 体魄
千佛归元经|内功|耐力 调息 护体''',
 'kereit': '''牧野耐寒功|炼体|体魄 耐力 韧性
开弓定肩法|弓术|膂力 根基 灵巧
逐风猎步|身法|身法 耐力 灵巧
克烈短刀术|刀法|灵巧 劲力 膂力
苍鹰稳息诀|内功|调息 耐力 根基
连珠射艺|弓术|灵巧 膂力 劲力
长杆破阵术|枪术|劲力 根基 膂力
风雪护帐功|炼体|韧性 体魄 护体
苍狼逐日真解|弓术|灵巧 膂力 身法''',
 'dobang': '''都房负重桩|桩功|膂力 根基 体魄
行伍耐劳法|炼体|耐力 韧性 体魄
护卫截击术|擒拿|灵巧 根基 膂力
都房短刀术|刀法|劲力 灵巧 身法
铁甲承击功|炼体|护体 韧性 体魄
破阵长刀|刀法|膂力 劲力 根基
回身迎敌步|身法|身法 根基 灵巧
镇军调息功|内功|调息 耐力 护体
都房镇岳刀经|刀法|劲力 膂力 韧性''',
 'kurama': '''山寺养息法|内功|调息 耐力 韧性
杉间踏叶步|身法|身法 灵巧 根基
鞍马小太刀|刀法|灵巧 劲力 身法
僧兵承力桩|桩功|根基 膂力 耐力
八方转身法|身法|身法 灵巧 韧性
天狗回风刀|刀法|劲力 灵巧 膂力
山岚护身诀|内功|护体 调息 根基
飞叶无踪步|身法|身法 耐力 灵巧
鞍马兵法秘传|刀法|灵巧 劲力 韧性''',
 'haidong': '''海东行猎功|炼体|耐力 体魄 韧性
林径穿枝步|身法|身法 灵巧 耐力
护社短矛术|矛术|膂力 根基 劲力
潮息养身法|内功|调息 体魄 耐力
盘根守社桩|桩功|根基 韧性 护体
回潮连矛|矛术|劲力 灵巧 身法
祖灵护身功|炼体|韧性 护体 体魄
山海巡猎步|身法|身法 耐力 灵巧
山海归潮矛经|矛术|劲力 根基 耐力''',
 'heishui': '''林海耐寒功|炼体|体魄 韧性 耐力
猎迹轻身步|身法|身法 灵巧 耐力
黑水猎刀术|刀法|灵巧 劲力 膂力
神歌长息法|内功|调息 耐力 韧性
伏林藏身诀|身法|身法 根基 灵巧
虎啸断木刀|刀法|膂力 劲力 体魄
守坛磐根功|桩功|根基 护体 韧性
玄林护命诀|内功|韧性 调息 护体
黑水山川真经|内功|体魄 耐力 调息''',
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
