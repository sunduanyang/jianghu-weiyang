"""The eight sects of the jianghu: one per region, split into 正、中、邪 camps.

Names follow traditions the whole wuxia genre shares (or historical orders such as
the Manichaean 明教) rather than devices owned by any single novel. Locations resolve
in atlas, never in saves.
"""

VERSION = 2

ALIGNMENTS = {'正': '正道', '中': '中立', '邪': '邪道'}

# Standing feuds. 'bitter' pairs are open enemies; 'rival' pairs share a camp but
# compete for the same leadership of the martial world.
FEUDS = {
    ('shaolin', 'mingjiao'): dict(kind='bitter', value=-30., note='佛门正统与“吃菜事魔”的明教势同水火'),
    ('emei', 'wudu'): dict(kind='bitter', value=-30., note='川滇相邻，峨眉与五毒教争夺西南'),
    ('huashan', 'mizong'): dict(kind='bitter', value=-30., note='关中是挡在河西密宗东进路上的门户'),
    ('shaolin', 'wudang'): dict(kind='rival', value=-22., note='同为正道泰斗，暗中争夺武林盟主之位'),
}


def school(key, name, home, seat, ethos, tradition, ranks, duty, art, art_name,
           prototype, basis, alignment, weight, focus='mastery', public_axis='security'):
    if len(ranks) != 9 or len(set(ranks)) != 9:
        raise ValueError(name+'须提供一至九品的九个不同称谓')
    if alignment not in ALIGNMENTS:
        raise ValueError(name+'须属正、中、邪之一')
    return dict(key=key, name=name, home=home, seat_name=seat, ethos=ethos,
                tradition=tradition, ranks=ranks, duty=duty, art_id=art,
                art=art_name, prototype=prototype, basis=basis, focus=focus,
                public_axis=public_axis, alignment=alignment,
                alignment_label=ALIGNMENTS[alignment], weight=weight, kind='sect')


SCHOOLS = [
    school('shaolin', '少林寺', '洛阳', '嵩山少林寺', '禅武合一，护持众生', '佛门',
           ['方丈', '护法首座', '堂院首座', '监院', '执事', '亲传弟子', '内院弟子', '外院弟子', '入寺弟子'], '护寺济民', 'shaolin_fist', '罗汉拳',
           'bridge_fist', '嵩山少林寺为历史寺院；禅武传统为武侠通行设定，组织玩法为游戏设计。', '正', 1.6),
    school('huashan', '华山派', '长安', '华山剑庐', '剑走险峰，守正不阿', '剑派',
           ['掌门', '护法长老', '长老', '堂主', '执事', '真传弟子', '内门弟子', '外门弟子', '记名弟子'], '护送山道行旅', 'huashan_sword', '华山剑法',
           'river_sword', '华山为道教名山；华山派为武侠通行门派，剑法名目为游戏设计。', '正', 1.3),
    school('wudang', '武当派', '襄阳', '武当山门', '以柔克刚，清修济世', '道门',
           ['掌门', '护法长老', '长老', '堂主', '执事', '真传弟子', '内门弟子', '外门弟子', '记名弟子'], '巡山护民', 'wudang_sword', '武当剑法',
           'river_sword', '武当山为道教名山；武当派为武侠通行门派，归入襄阳一带为游戏安排。', '正', 1.4),
    school('emei', '峨眉派', '成都', '峨眉金顶', '佛道兼修，济弱扶倾', '佛门',
           ['掌门', '护法师太', '长老', '首座', '执事', '亲传弟子', '入室弟子', '俗家弟子', '记名弟子'], '护送香客', 'emei_needle', '峨眉刺法',
           'reed_cut', '峨眉山为佛教名山；峨眉派为武侠通行门派，峨眉刺为传统短兵。', '正', 1.2),
    school('beggars', '丐帮', '苏州', '丐帮总舵', '扶危济困，四海同心', '帮会',
           ['帮主', '传功长老', '执法长老', '总舵主', '分舵主', '七袋弟子', '五袋弟子', '三袋弟子', '一袋弟子'], '赈济行旅', 'beggar_staff', '打狗棒法',
           'reed_cut', '乞丐帮会为武侠通行设定；总舵设在江南为游戏安排。', '中', 1.8, 'belonging'),
    school('wudu', '五毒教', '大理', '五毒教坛', '以毒御敌，护佑苗寨', '巫蛊',
           ['教主', '护法', '长老', '坛主', '香主', '圣使', '内坛弟子', '外坛弟子', '入教弟子'], '采药护寨', 'wudu_palm', '五毒神掌',
           'wave_palm', '取材于西南苗疆的蛊毒传说与武侠通行的五毒教；名称、制度为游戏设计。', '邪', 1.0,
           'mastery', 'sanitation'),
    school('mingjiao', '明教', '泉州', '草庵明尊殿', '明尊在上，患难相恤', '明教',
           ['教主', '拂多诞', '法堂主', '坛主', '香主', '护法使者', '明使', '听者', '入教听者'], '护送教众', 'mingjiao_blade', '明尊弯刀',
           'reed_cut', '取材于历史上的摩尼教（明教）；泉州草庵是现存摩尼教遗迹，拂多诞、法堂主、听者为摩尼教旧称，武学为游戏设计。', '邪', 1.2,
           'belonging'),
    school('mizong', '西域密宗', '凉州', '凉州白塔寺', '密法修身，降伏外道', '密宗',
           ['法王', '大上师', '上师', '经堂首座', '执事', '亲传弟子', '经堂弟子', '护法弟子', '入寺弟子'], '护寺巡行', 'mizong_palm', '大手印',
           'wave_palm', '取材于西夏以来盛行河西的藏传密教，凉州白塔寺为历史寺院；武学与组织为游戏设计。', '邪', 0.9),
]

KEYS = [s['key'] for s in SCHOOLS]


def feud(a, b):
    """The standing feud between two sect keys, or None."""
    return FEUDS.get((a, b)) or FEUDS.get((b, a))
