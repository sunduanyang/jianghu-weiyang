from . import combat_attributes, weapons, equipment, martial, martial_catalog
"""Personal possessions: finite instances, engine-priced transfers and event provenance."""
from copy import deepcopy
import math


GRADES = {9: '凡', 8: '素', 7: '良', 6: '精', 5: '珍',
          4: '秘', 3: '极', 2: '绝', 1: '神'}
GRADE_COLORS = {9: '#8C918C', 8: '#E8E5DA', 7: '#78A85B', 6: '#568FC4',
                5: '#51B8B4', 4: '#AF79BF', 3: '#D4B052', 2: '#DC8644', 1: '#D8584E'}
GRADE_COLOR_NAMES = {9: '灰', 8: '白', 7: '绿', 6: '蓝', 5: '青',
                     4: '紫', 3: '金', 2: '橙', 1: '红'}
GRADE_NAMES = {grade: name+'品' for grade, name in zip(range(9, 0, -1), '九八七六五四三二一')}
CATEGORIES = {'manual': '武功秘籍', 'food': '食品', 'wine': '酒', 'tea': '茶',
              'weapon': '武器', 'shield': '盾牌', 'armor': '护甲', 'accessory': '佩饰', 'tool': '器具',
              'medicine': '丹药', 'material': '材料'}
SLOTS = {'weapon': '主手', 'shield': '副手', 'armor': '护甲'}
CONSUMABLES = ('food', 'wine', 'tea', 'medicine')


def template(name, category, grade, price, description, **kwargs):
    return dict(name=name, category=category, grade=grade, price=price,
                description=description, **kwargs)


CATALOG = {
    'wood_sword': template('枣木剑', 'weapon', 9, 12, '以枣木削成，轻巧趁手，适宜初学。'),
    'iron_sword': template('青锋剑', 'weapon', 7, 48, '百炼青锋，剑身平直，行走江湖的可靠兵刃。'),
    'steel_blade': template('雁翎刀', 'weapon', 6, 86, '刀背厚重，刀尖微翘，出手沉稳。'),
    'cloud_sword': template('流云剑', 'weapon', 3, 320, '锻纹如流云，轻而不浮，非寻常匠作。'),
    'ancient_blade': template('照夜古刀', 'weapon', 2, 720, '寒光映夜，刀铭已不可辨，锋芒历岁未减。'),
    'cloth': template('粗布衣', 'armor', 9, 10, '洗得发白的布衣，能挡些风尘。', defense=1),
    'leather': template('熟皮护衣', 'armor', 7, 42, '熟皮缝制，护住要害，适合行脚。', defense=4),
    'silk_robe': template('云纹锦袍', 'armor', 5, 110, '锦绣藏锋，内衬柔韧丝绵。', defense=7),
    'jade': template('平安玉扣', 'accessory', 8, 24, '小小一枚青玉，寄寓岁岁平安。'),
    'gourd': template('药葫芦', 'accessory', 6, 64, '以药草熏养的葫芦，佩在身边助人调息。'),
    'hoe': template('铁头锄', 'tool', 9, 14, '农家常用的锄具，可供交易与赠送。'),
    'hammer': template('锻铁锤', 'tool', 8, 22, '木柄铁锤，是匠人熟悉的日常器具。'),
    'abacus': template('乌木算盘', 'tool', 7, 38, '算清一笔一笔的往来，买卖才有余地。'),
    'herb_basket': template('采药篓', 'tool', 8, 20, '竹篾细密，可妥善收存山间药草。'),
    'salve': template('金疮药', 'medicine', 9, 6, '外敷伤处，恢复体力并缩短外伤。', heal=18, wound=2),
    'pill': template('养心丸', 'medicine', 7, 18, '缓和病势，调养元气；不能代替长期诊治。', heal=12, illness=12),
    'ration': template('麦饼', 'food', 9, 3, '朴素耐放的行粮，食用恢复少许体力。', heal=6),
    'wine': template('桂花酿', 'wine', 8, 8, '清甜微辛，歇脚时饮一盏，可舒缓心绪。', heal=8, joy=4),
    'basic_manual': template('吐纳入门', 'manual', 9, 20, '从呼吸起落入手，循序习得基本功。', learning=1.2, requirement=0, pages=3),
    'sword_manual': template('青溪剑谱', 'manual', 7, 58, '记录青溪一脉剑式，需些武学根基。', learning=2., requirement=12, pages=4),
    'cloud_manual': template('栖云心法', 'manual', 4, 180, '凝神守一，气息绵长；根基不足者难以领会。', learning=3., requirement=30, pages=5),
    'taixu_manual': template('太虚真解', 'manual', 1, 1200, '字句深奥，须有深厚根基，方能窥见其意。', learning=4.5, requirement=70, pages=6),
    'herb': template('止血草', 'material', 9, 4, '山间常见药草，可制金疮药。'),
    'iron': template('生铁', 'material', 9, 5, '未经精炼的铁料，可用于修补兵刃与衣甲。'),
    'timber': template('硬木料', 'material', 9, 4, '纹理紧实的木料，可用于修补佩饰与器具。'),
}
# Physical weapon profiles feed the native arena; item identities stay stable.
PHYSICAL_TEMPLATES = {
    'iron_dagger': template('铁匕首', 'weapon', 9, 18, '短刃近身，轻快省力。'),
    'hand_axe': template('单手战斧', 'weapon', 8, 32, '斧头着力，柄部无伤。'),
    'spiked_mace': template('钉头铁锤', 'weapon', 7, 56, '锤头重击，压盾有力。'),
    'long_spear': template('白蜡长枪', 'weapon', 8, 36, '双手持杆，仅枪头造成伤害。'),
    'great_sword': template('双手重剑', 'weapon', 6, 90, '双手握持，无法同时举盾。'),
    'great_axe': template('双手战斧', 'weapon', 6, 94, '双手分担重斧，收势较慢。'),
    'war_hammer': template('双手战锤', 'weapon', 6, 98, '双手抡击，冲击沉重。'),
    'wood_shield': template('藤木盾', 'shield', 9, 16, '单手兵器可配盾；双手兵器占用副手。', defense=2),
}
CATALOG.update(PHYSICAL_TEMPLATES)
# Keep template identities stable so existing possessions remain addressable.
ORIGINAL_TEMPLATES = frozenset(CATALOG)
CATALOG.update({
    'iron_practice_sword': template('铁胎练剑', 'weapon', 8, 26, '剑刃未开，铁胎扎实，适宜习练架势。'),
    'autumn_sword': template('秋水剑', 'weapon', 5, 140, '淬火匀净，剑光似秋水，收发自如。'),
    'frost_blade': template('霜华刀', 'weapon', 4, 220, '叠锻霜纹隐现，兼有韧性与锋锐。'),
    'tianheng_sword': template('天衡剑', 'weapon', 1, 1500, '名匠穷年淬成，剑脊如衡，出锋若星。'),
    'scallion_pancake': template('葱香炊饼', 'food', 8, 6, '面香裹葱，外酥内软，是旅店常备的点心。', heal=9),
    'mushroom_noodles': template('山菌汤面', 'food', 7, 12, '山菌熬汤，细面滑口，暖胃解乏。', heal=12),
    'lotus_rice': template('荷叶蒸饭', 'food', 6, 22, '新米与时蔬包入荷叶，蒸得清香。', heal=16),
    'river_shrimp': template('清炒河虾', 'food', 5, 40, '细火急炒，虾肉鲜嫩，佐以时令青蔬。', heal=20),
    'silver_fish': template('银丝鱼脍', 'food', 4, 72, '鲜鱼细切，姜醋相和，刀工十分讲究。', heal=25),
    'golden_crab': template('金膏蒸蟹', 'food', 3, 128, '秋蟹膏满，清蒸留鲜，是席间珍味。', heal=31),
    'mountain_stew': template('山海珍羹', 'food', 2, 240, '山珍海味慢煨成羹，汤色清润，滋养甚厚。', heal=38),
    'nine_treasure_feast': template('九珍玉食', 'food', 1, 480, '九味珍材各得其宜，汇成一席玲珑滋味。', heal=48),
    'village_wine': template('村酿浊酒', 'wine', 9, 4, '村家新酿，酒色微浊，饮来温和。', heal=5, joy=2),
    'green_plum_wine': template('青梅酒', 'wine', 7, 16, '青梅入酒，酸甜相济，略解行路烦忧。', heal=10, joy=5),
    'bamboo_wine': template('竹叶青酿', 'wine', 6, 30, '竹叶浸酿，色泽青润，回味悠长。', heal=12, joy=6),
    'pear_blossom_wine': template('梨花白', 'wine', 5, 55, '酒色澄澈，香如春日梨花，入口绵柔。', heal=15, joy=7),
    'song_lao_wine': template('松醪春', 'wine', 4, 96, '松香与谷香交融，封坛经年方启。', heal=18, joy=8),
    'jade_dew_wine': template('玉壶清露', 'wine', 3, 168, '清泉酿成，以玉色瓷壶封存，香气清冽。', heal=22, joy=9),
    'zixia_wine': template('紫霞陈酿', 'wine', 2, 300, '陈香层叠，开坛如霞，少许便觉醇厚。', heal=27, joy=10),
    'taibai_wine': template('太白遗酿', 'wine', 1, 600, '名酿古法续传，酒香沉静，百味融于一盏。', heal=34, joy=12),
    'coarse_tea': template('山野粗茶', 'tea', 9, 3, '山间粗叶焙成，虽朴素，亦能解渴醒神。', heal=4, joy=1),
    'roasted_tea': template('炒青茶', 'tea', 8, 7, '铁锅炒青，茶汤明亮，香味平和。', heal=6, joy=2),
    'pine_tea': template('松溪春茗', 'tea', 7, 14, '溪畔春芽，轻焙留香，饮后精神一振。', heal=8, joy=3),
    'cloud_tea': template('云岭毛尖', 'tea', 6, 26, '云岭细芽，锋毫匀整，滋味清鲜。', heal=11, joy=4),
    'lotus_tea': template('莲心雪芽', 'tea', 5, 48, '嫩芽如雪，回甘带莲心清意。', heal=14, joy=5),
    'silver_needle_tea': template('白毫银针', 'tea', 4, 85, '芽头肥壮，白毫如银，茶香温润。', heal=18, joy=6),
    'dragon_tea': template('龙团胜雪', 'tea', 3, 150, '细碾团茶，点汤如雪，工序繁复。', heal=22, joy=8),
    'purple_bud_tea': template('紫笋贡茗', 'tea', 2, 280, '紫芽初展时采制，香凝不散，甘醇入喉。', heal=27, joy=10),
    'taihe_tea': template('太和清露', 'tea', 1, 700, '清露初采，慢焙成香。啜饮一盏，澄心静气。', heal=32, joy=12),
    'stance_manual': template('桩功浅说', 'manual', 8, 34, '从立身扎根讲起，附有清楚的行功图。', learning=1.6, requirement=5, pages=3),
    'steps_manual': template('归雁步图', 'manual', 6, 90, '步随身转，进退有节，须先练稳下盘。', learning=2.3, requirement=18, pages=4),
    'river_manual': template('沧浪劲论', 'manual', 5, 128, '论劲如潮，层层相续，宜以已有根基印证。', learning=2.6, requirement=24, pages=5),
    'moon_manual': template('照月剑经', 'manual', 3, 320, '以静照动，借月喻剑，收录名家心悟。', learning=3.5, requirement=42, pages=5),
    'liangyi_manual': template('两仪玄章', 'manual', 2, 640, '阴阳相济，动静互根，字字关乎深厚内功。', learning=4., requirement=55, pages=6),
})
RECIPES = {'salve': {'herb': 2}, 'pill': {'herb': 3}}


def grade_info(grade):
    return {'grade_label': GRADES[grade], 'grade_name': GRADE_NAMES[grade],
            'grade_color': GRADE_COLORS[grade], 'grade_color_name': GRADE_COLOR_NAMES[grade]}


def grade_legend():
    return [{'grade': grade, **grade_info(grade)} for grade in GRADES]


# Old martial manuals are removed together with their retired learning system.
RETIRED_MANUALS = {key for key, value in CATALOG.items() if value['category'] == 'manual'}
for _retired in RETIRED_MANUALS:
    del CATALOG[_retired]

# New manuals reference the current nine-grade arts, never the retired skill system.
MANUAL_TEMPLATES = {}
for _art_id, _art in martial_catalog.ARTS.items():
    _key = 'martial_manual_' + _art_id
    MANUAL_TEMPLATES[_art_id] = _key
    CATALOG[_key] = template('《'+_art['name']+'》秘籍', 'manual', _art['grade'],
        {9:20, 8:34, 7:58, 6:90, 5:128, 4:180, 3:320, 2:640, 1:1200}[_art['grade']],
        f"{_art['school_name']}的{_art['kind']}秘籍。使用后学会{_art['name']}，消耗一本；"
        f"永久保留所学，不受身份变动影响。{_art['description']}。", art=_art_id)


def knows(w, owner, art_id):
    n = actor(w, owner)
    curriculum = martial.resolve(w, n, str(owner) == 'player') or {}
    return art_id in curriculum.get('known', [])


def register_manuals(w):
    if w.items.get('manual_catalog') == 1:
        return False
    added = [create(w, 'market', key, '武功秘籍启用时登记的货栈存货')
             for key in MANUAL_TEMPLATES.values()]
    w.items['manual_catalog'] = 1
    e = w.emit('item_catalog', [], f'货栈登记武功秘籍{len(added)}册。', .25,
               facts={'initial_condition': True, 'manual_catalog': 1, 'items': [i['id'] for i in added]})
    for item in added:
        item['origin_event'] = item['last_event'] = e['id']
    return True


def retire_manuals(w):
    for item in list(w.items['objects'].values()):
        if item['template'] in RETIRED_MANUALS:
            remove(w, item)
    for n in [w.player, *w.npcs.values()]:
        n['read_books'] = {}


def actor(w, owner):
    if str(owner) == 'player':
        return w.player
    if str(owner) in ('market', 'government'):
        return w.items[str(owner)]
    return w.npcs[int(owner)]


def prepare(n):
    n.setdefault('inventory', [])
    n.setdefault('equipment', {})
    n.setdefault('read_books', {})
    n.pop('weight', None)
    n.pop('capacity', None)


def objects(w, owner):
    return [w.items['objects'][str(i)] for i in actor(w, owner)['inventory']]


def get(w, owner, item_id):
    item = w.items['objects'].get(str(item_id))
    if not item or item['owner'] != str(owner):
        raise ValueError('这件物品已不在当前行囊中')
    return item


def spec(item):
    return CATALOG[item['template']]


def create(w, owner, template_id, source, event_id=None):
    key = str(w.items['next_id'])
    w.items['next_id'] += 1
    item = {'id': key, 'template': template_id, 'owner': str(owner), 'durability': 100,
            'created_month': w.month, 'origin': source, 'origin_event': event_id,
            'last_event': event_id}
    w.items['objects'][key] = item
    actor(w, owner)['inventory'].append(key)
    return item


def transfer(w, item, destination):
    old = actor(w, item['owner'])
    old['inventory'].remove(item['id'])
    old['equipment'] = {k: v for k, v in old['equipment'].items() if v != item['id']}
    actor(w, destination)['inventory'].append(item['id'])
    item['owner'] = str(destination)


def remove(w, item):
    n = actor(w, item['owner'])
    n['inventory'].remove(item['id'])
    n['equipment'] = {k: v for k, v in n['equipment'].items() if v != item['id']}
    del w.items['objects'][item['id']]


def initialize(w, legacy=False):
    """One-time registration; never invent possessions for an already deceased NPC."""
    prepare(w.player)
    w.player.setdefault('mood', {'joy': 15.})
    for key, value in {'health': 100., 'alive': True}.items():
        w.player.setdefault(key, value)
    for n in w.npcs.values():
        prepare(n)
    if getattr(w, 'items', None):
        retire_manuals(w)
        manuals_added = register_manuals(w)
        for item in w.items['objects'].values():
            for retired in ('attack', 'weight', 'capacity'):
                item.pop(retired, None)
        for owner in ['player', *w.npcs]:
            equipment.normalize(w, owner)
        if w.items.get('version', 1) < 2:
            # Explicit one-time stock registration, never monthly replenishment.
            known = {i['template'] for i in w.items['objects'].values()} | ORIGINAL_TEMPLATES
            added = [create(w, 'market', key, '九品物品目录扩充时登记的货栈新货')
                     for key in CATALOG if key not in known]
            w.items['version'] = 2
            e = w.emit('item_catalog', [], f'货栈登记九品新货{len(added)}件，开放书籍、食品、酒、茶与武器分栏。', .25,
                       facts={'initial_condition': True, 'catalog_version': 2, 'items': [i['id'] for i in added]})
            for item in added:
                item['origin_event'] = item['last_event'] = e['id']
            return True
        if not w.items.get('physical_catalog'):
            known = {i['template'] for i in w.items['objects'].values()}
            added = [create(w, 'market', key, '物理战斗兵器登记') for key in PHYSICAL_TEMPLATES if key not in known]
            w.items['physical_catalog'] = 1
            e = w.emit('item_catalog', [], f'货栈登记物理兵器与盾牌{len(added)}件。', .25,
                facts={'initial_condition':True, 'physical_catalog':1, 'items':[i['id'] for i in added]})
            for item in added: item['origin_event'] = item['last_event'] = e['id']
            return True
        return manuals_added
    w.items = {'version': 2, 'physical_catalog': 1, 'manual_catalog': 1, 'next_id': 1, 'objects': {},
               'market': {'name': '五地货栈', 'wealth': 800.}, 'government': {'name': '县衙遗物库'}}
    prepare(w.items['market'])
    prepare(w.items['government'])
    for template_id, d in CATALOG.items():
        for _ in range(4 if d['grade'] >= 7 else 1):
            create(w, 'market', template_id, '货栈开张存货')
    for template_id in ('wood_sword', 'cloth', 'jade', 'salve', 'salve', 'ration', 'herb', 'herb', 'iron'):
        create(w, 'player', template_id, '过客随身旧物' if not legacy else '物品登记时的过客行装')
    for item in objects(w, 'player'):
        if spec(item)['category'] in SLOTS:
            w.player['equipment'][spec(item)['category']] = item['id']
    for n in w.npcs.values():
        if not n['alive']:
            continue
        event_id = n['last_event'] if not legacy else None
        if legacy:
            e = w.emit('item_registration', [n['id']], f"{n['name']}登记现有行装；登记以前的物品流转未补写。", .25,
                       facts={'initial_condition': True, 'registered_month': w.month})
            event_id = e['id']
        loadout = ['cloth', 'ration']
        if n['age_months'] >= 120:
            loadout += ['salve', 'wood_sword']
            tool = {'农人': 'hoe', '匠人': 'hammer', '行商': 'abacus', '药师': 'herb_basket', '镖师': 'iron_sword'}.get(n['job'])
            if tool:
                loadout.append(tool)
        for template_id in loadout:
            item = create(w, n['id'], template_id, '乡籍行装' if not legacy else '物品系统启用时登记', event_id)
            category = spec(item)['category']
            if category in SLOTS:
                n['equipment'][category] = item['id']
    return True


def bonuses(w, owner):
    return equipment.bonuses(w, owner)


def wear(w, owner, slots, amount=1):
    changes = []
    active = {entry['slot'] for entry in equipment.resolve(w, owner) if entry['active']}
    for slot in slots:
        if slot not in active:
            continue
        item_id = actor(w, owner)['equipment'].get(slot)
        if item_id:
            item = get(w, owner, item_id)
            before = item['durability']
            item['durability'] = max(0, before-amount)
            if item['durability'] != before:
                changes.append({'item_id': item_id, 'before': before, 'after': item['durability']})
    return changes


def buy_price(item):
    return round(spec(item)['price'] * (.25 + .75 * item['durability']/100), 2)


def sell_price(item):
    return round(buy_price(item)*.55, 2)


def repair_price(item):
    return max(1, math.ceil(spec(item)['price']*(100-item['durability'])/100*.25))


def operations(w, owner, item):
    n, d = actor(w, owner), spec(item)
    result = []
    if (d.get('art') and item['durability'] > 0 and
            (str(owner) != 'player' or n['protagonist']['created']) and not knows(w, owner, d['art'])):
        result.append('use')
    equipped = item['id'] in n['equipment'].values()
    if d['category'] in SLOTS:
        if equipped:
            result.append('unequip')
        elif item['durability'] > 0:
            result.append('equip')
        if item['durability'] < 100:
            result.append('repair')
    if d['category'] in CONSUMABLES and (n['health'] < 100 or
            (d.get('wound') and n.get('wound')) or (d.get('illness') and n.get('illness')) or
            (d.get('joy') and n.get('mood') and n['mood']['joy'] < 30)):
        result.append('use')
    if not equipped:
        result += ['sell', 'gift']
    return result


def describe(item):
    data = {**deepcopy(item), **spec(item)}
    if data['category'] == 'weapon':
        data['weapon_attributes'] = weapons.attributes(item)
    return data


def view(w, owner):
    n = actor(w, owner)
    result = []
    for item in objects(w, owner):
        d = spec(item)
        result.append({**describe(item), **grade_info(d['grade']),
                       'category_label': CATEGORIES[d['category']], 'equipped': item['id'] in n['equipment'].values(),
                       'buy_price': buy_price(item), 'sell_price': sell_price(item), 'repair_price': repair_price(item),
                       'read_progress': n['read_books'].get(item['template'], 0),
                       'art_known': knows(w, owner, d['art']) if d.get('art') and str(owner) not in ('market', 'government') else False,
                       'operations': operations(w, owner, item) if n.get('alive', True) else []})
    return {'owner': str(owner), 'name': n['name'], 'wealth': n.get('wealth', 0), 'health': n.get('health'),
            'skill': n.get('skill'), 'joy': n.get('mood', {}).get('joy'),
            'items': result, 'equipment': n['equipment'].copy(),
            'equipment_state': equipment.snapshot(w, owner),
            'weapon_attribute_catalog': weapons.attribute_catalog(),
            'bonuses': bonuses(w, owner), 'categories': CATEGORIES,
            'slots': SLOTS, 'grades': grade_legend(), 'alive': n.get('alive', True)}


def market_view(w):
    # Individual instances remain addressable even when the UI groups identical stock.
    return {'name': '五地货栈', 'wealth': w.items['market']['wealth'],
            'items': [{**describe(i), 'buy_price': buy_price(i),
                       **grade_info(spec(i)['grade']), 'category_label': CATEGORIES[spec(i)['category']]}
                      for i in objects(w, 'market')],
            'grades': grade_legend(), 'categories': CATEGORIES,
            'weapon_attribute_catalog': weapons.attribute_catalog(),
            'recipes': [{'template': k, **CATALOG[k], **grade_info(CATALOG[k]['grade']),
                         'ingredients': {CATALOG[t]['name']: q for t, q in v.items()}}
                        for k, v in RECIPES.items()]}


def perform(w, owner, operation, item_id=None, target=None, recipe=None, action=None):
    """Validate ownership, all costs and prerequisites before changing any state."""
    owner = str(owner)
    if owner != 'player' and owner not in {str(i) for i in w.npcs}:
        raise ValueError('该行囊不能主动操作物品')
    n = actor(w, owner)
    if not n.get('alive', True):
        raise ValueError('故人的遗物不能继续使用')
    if operation not in ('buy', 'sell', 'equip', 'unequip', 'use', 'read', 'repair', 'gift', 'craft'):
        raise ValueError('未知物品操作')
    item = None
    consumed = []
    actor_before = {k: n[k] for k in ('wealth', 'health')}
    facts = {'owner': owner, 'operation': operation}
    if action:
        facts.update(action=action['id'], actor=n['id'], decision_source=n['choice_source'], time_cost=action['time_cost'])
    people = [] if owner == 'player' else [int(owner)]
    settlement = n.get('monthly_settlement', {})
    if settlement.get('month') == w.month and not n.get('settlement_logged', False):
        facts['monthly_settlement'] = settlement.copy()
        if settlement.get('payer') != n.get('id') and settlement.get('paid', 0) > 0:
            people.append(settlement['payer'])
    parents = []
    if operation == 'craft':
        if recipe not in RECIPES:
            raise ValueError('没有这份制药方')
        for template_id, count in RECIPES[recipe].items():
            found = [i for i in objects(w, owner) if i['template'] == template_id][:count]
            if len(found) < count:
                raise ValueError('材料不足，请先备齐药草')
            consumed += found
        if n['wealth'] < 2:
            raise ValueError('制药需要2银工费')
    else:
        item = get(w, 'market' if operation == 'buy' else owner, item_id)
        d = spec(item)
        parents = [item.get('last_event')]
        facts['item_before'] = {**deepcopy(item), 'name': d['name']}
        if operation != 'buy' and operation not in operations(w, owner, item):
            raise ValueError('当前不能这样使用：请检查装备、耐久、体力或研读条件')
    if operation == 'buy':
        cost = buy_price(item)
        if n['wealth'] < cost:
            raise ValueError('银两不足')
        n['wealth'] -= cost
        w.items['market']['wealth'] += cost
        transfer(w, item, owner)
        text = f"{n['name']}从货栈购得{d['name']}，支付{cost:g}银。"
        facts['silver'] = cost
    elif operation == 'sell':
        cost = sell_price(item)
        if w.items['market']['wealth'] < cost:
            raise ValueError('货栈余银不足，暂时无法收购')
        w.items['market']['wealth'] -= cost
        n['wealth'] += cost
        transfer(w, item, 'market')
        text = f"{n['name']}将{d['name']}售予货栈，得银{cost:g}。"
        facts['silver'] = cost
    elif operation in ('equip', 'unequip'):
        slot = d['category']
        facts['replaced'] = n['equipment'].get(slot)
        if operation == 'equip':
            n['equipment'][slot] = item['id']
        else:
            n['equipment'].pop(slot, None)
        text = f"{n['name']}{'装备' if operation == 'equip' else '卸下'}{d['name']}。"
    elif operation == 'use' and d.get('art'):
        art_id = d['art']
        n['martial'].setdefault('learned', []).append(art_id)
        n['martial']['learned'].sort()
        martial.sync_actor(w, n, owner == 'player')
        facts.update(art=art_id, learned=True, consumed=item['id'])
        text = f"{n['name']}使用{d['name']}，学会{martial_catalog.ARTS[art_id]['name']}，秘籍已消耗一册。"
        remove(w, item)
    elif operation == 'use':
        before = n['health']
        joy_before = n.get('mood', {}).get('joy', 0)
        n['health'] = min(100., n['health']+d.get('heal', 0))
        if d.get('wound') and n.get('wound'):
            n['wound']['remaining'] = max(0, n['wound']['remaining']-d['wound'])
        if d.get('illness') and n.get('illness'):
            n['illness']['severity'] = max(0, n['illness']['severity']-d['illness'])
        if d.get('joy') and n.get('mood'):
            n['mood']['joy'] = min(30, n['mood']['joy']+d['joy'])
        joy_gain = n.get('mood', {}).get('joy', 0)-joy_before
        facts.update(health_gain=round(n['health']-before, 4), joy_gain=joy_gain, consumed=item['id'])
        text = f"{n['name']}使用{d['name']}，恢复{n['health']-before:g}体力" + (f'，喜悦增加{joy_gain:g}' if joy_gain else '') + '。'
        remove(w, item)
    elif operation == 'repair':
        cost = repair_price(item)
        material = 'iron' if d['category'] in ('weapon', 'armor') else 'timber'
        supply = next((i for i in objects(w, owner) if i['template'] == material), None)
        if n['wealth'] < cost or not supply:
            raise ValueError(f"修补需{cost}银与一份{CATALOG[material]['name']}")
        n['wealth'] -= cost
        w.items['market']['wealth'] += cost
        facts.update(silver=cost, material=deepcopy(supply))
        parents.append(supply.get('last_event'))
        remove(w, supply)
        item['durability'] = 100
        text = f"{n['name']}用{CATALOG[material]['name']}修好{d['name']}，支付{cost}银工费。"
    elif operation == 'gift':
        try:
            recipient = w.npcs[int(target)]
        except (ValueError, KeyError, TypeError):
            raise ValueError('请选择赠送对象') from None
        if not recipient['alive'] or str(target) == owner:
            raise ValueError('只能赠予另一位在世人物')
        if owner != 'player' and recipient['place'] != n['place']:
            raise ValueError('双方不在同一地点')
        transfer(w, item, recipient['id'])
        people.append(recipient['id'])
        if owner == 'player':
            tie = w.player['ties'].setdefault(str(recipient['id']), {'affinity': 0., 'trust': 0.})
            tie['affinity'] = min(100, tie['affinity']+min(12, 2+d['price']/10))
            tie['trust'] = min(100, tie['trust']+2)
        else:
            w.link(n, recipient, 5, 2)
        facts['recipient'] = recipient['id']
        text = f"{n['name']}将{d['name']}赠给{recipient['name']}，物品已实际交付。"
    elif operation == 'craft':
        facts['materials'] = deepcopy(consumed)
        parents = [i.get('last_event') for i in consumed]
        for supply in consumed:
            remove(w, supply)
        n['wealth'] -= 2
        w.items['market']['wealth'] += 2
        item = create(w, owner, recipe, f"{n['name']}依药方制成")
        text = f"{n['name']}耗用药草制成{CATALOG[recipe]['name']}，支付2银工费。"
        facts['silver'] = 2
    facts['actor_delta'] = {key: round(n[key]-value, 4) for key, value in actor_before.items()}
    facts['item_id'] = item['id']
    facts['item_after'] = deepcopy(item) if item['id'] in w.items['objects'] else None
    e = w.emit('item_'+operation, people, text, .65 if operation == 'gift' else .25, parents, facts)
    if operation == 'gift' and owner == 'player':
        tie['last_event'], tie['last_contact'] = e['id'], w.month
    if item['id'] in w.items['objects']:
        item['last_event'] = e['id']
        if operation == 'craft':
            item['origin_event'] = e['id']
    return e


def candidates(w, n):
    result = []
    for item in objects(w, n['id']):
        d = spec(item)
        ops = operations(w, n['id'], item)
        choices = []
        if 'use' in ops and d.get('art'):
            choices.append(('use_item', 60+(15 if (n.get('goal') or {}).get('kind') == 'mastery' else 0)))
        elif 'use' in ops and n['health'] < (85 if d['category'] == 'medicine' else 75):
            choices.append(('use_item', 38+(100-n['health'])*.7))
        if 'equip' in ops:
            old_id = n['equipment'].get(d['category'])
            old = w.items['objects'].get(old_id)
            if not old or old['durability'] <= 0 or spec(old)['price'] < d['price']:
                choices.append(('equip_item', 23))
        if 'read' in ops and n['health'] > 40:
            choices.append(('read_item', 18+(12 if n['goal']['kind'] == 'mastery' else 0)))
        if ('sell' in ops and n['wealth'] < 10 and d['category'] not in CONSUMABLES
                and w.items['market']['wealth'] >= sell_price(item)):
            choices.append(('sell_item', 45))
        if 'repair' in ops and item['durability'] < 25 and n['wealth'] > repair_price(item)+12:
            material = 'iron' if d['category'] in ('weapon', 'armor') else 'timber'
            if any(i['template'] == material for i in objects(w, n['id'])):
                choices.append(('repair_item', 24))
        for kind, score in choices:
            result.append({'id': f"{kind}:{item['id']}", 'kind': kind, 'target': item['id'],
                           'label': f"{dict(use_item='使用', equip_item='装备', read_item='研读', sell_item='出售', repair_item='修补')[kind]} · {d['name']}",
                           'item_score': score})
    # A small visible market shortlist; scarcity is checked again when executed.
    if n['wealth'] >= 18 and len(n['inventory']) < 9:
        wanted = 'salve' if n['health'] < 80 and not any(spec(i)['category'] == 'medicine' for i in objects(w, n['id'])) else None
        found = next((i for i in objects(w, 'market') if i['template'] == wanted and buy_price(i) <= n['wealth']-10), None)
        if found:
            result.append({'id': f"buy_item:{found['id']}", 'kind': 'buy_item', 'target': found['id'],
                           'label': f"购置 · {spec(found)['name']}", 'item_score': 30 if wanted == 'salve' else 17})
    return result


def produce(w, n, event):
    """Occasional occupational output, with material/labour costs in the same journal."""
    if (w.month+n['id']) % 6 or len(n['inventory']) >= 9 or n['wealth'] < 2:
        return
    template_id = {'农人': 'ration', '药师': 'herb', '匠人': 'iron'}.get(n['job'])
    if not template_id:
        return
    from . import economics
    materials = {}
    material_cost = 0.
    if economics.active(w):
        good = {'ration':'grain', 'herb':'herb', 'iron':'iron'}[template_id]
        stock = economics.prepare(n)['stock']; market = economics.market(w,n['place'])
        missing = max(0., .5-stock[good])
        if market['stock'][good] < missing or n['wealth'] < 1+missing*market['prices'][good]:
            return
        if missing:
            _, material_cost = economics.buy(w,n['id'],good,missing)
        stock[good] = economics.q(stock[good]-.5)
        economics.state(w)['consumed'][good] += .5
        market['used'][good] += .5
        economics.prepare(n)['expenses'] += 1
        materials = {good:.5}
        economics.fee(w,n['place'],1.)
    n['wealth'] -= 1  # Processing fee; modern mode also consumes real bulk inputs.
    item = create(w, n['id'], template_id, f"{n['name']}劳作所得")
    e = w.emit('item_produce', [n['id']], f"{n['name']}劳作时另备得{CATALOG[template_id]['name']}，耗费1银物料。", .15,
               [event['id']], {'item_id': item['id'], 'template': template_id, 'input_cost': 1+material_cost,
                              'bulk_inputs': materials, 'producer': n['id']})
    item['origin_event'] = item['last_event'] = e['id']


def inherit(w, deceased, heirs, event):
    possessions = list(objects(w, deceased['id']))
    for index, item in enumerate(possessions):
        destination = heirs[index % len(heirs)]['id'] if heirs else 'government'
        transfer(w, item, destination)
        e = w.emit('item_inherit', [int(destination)] if heirs else [],
                   f"{spec(item)['name']}作为{deceased['name']}的遗物，"
                   + (f"由{actor(w, destination)['name']}承继。" if heirs else '交县衙遗物库保管。'), .45,
                   [event['id'], item.get('last_event')],
                   {'deceased': deceased['id'], 'recipient': str(destination), 'item_id': item['id'],
                    'item': deepcopy(item)})
        item['last_event'] = e['id']
