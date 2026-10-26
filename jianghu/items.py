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
CATEGORIES = {'manual': '书籍', 'food': '食品', 'wine': '酒', 'tea': '茶',
              'weapon': '武器', 'armor': '衣甲', 'accessory': '佩饰', 'tool': '器具',
              'medicine': '丹药', 'material': '材料'}
SLOTS = {'weapon': '兵刃', 'armor': '衣甲', 'accessory': '佩饰', 'tool': '器具'}
CONSUMABLES = ('food', 'wine', 'tea', 'medicine')


def template(name, category, grade, price, weight, description, **kwargs):
    return dict(name=name, category=category, grade=grade, price=price, weight=weight,
                description=description, **kwargs)


CATALOG = {
    'wood_sword': template('枣木剑', 'weapon', 9, 12, 1.2, '以枣木削成，轻巧趁手，适宜初学。', attack=2, training=.08),
    'iron_sword': template('青锋剑', 'weapon', 7, 48, 2.4, '百炼青锋，剑身平直，行走江湖的可靠兵刃。', attack=6, training=.15),
    'steel_blade': template('雁翎刀', 'weapon', 6, 86, 3.2, '刀背厚重，刀尖微翘，出手沉稳。', attack=9, training=.2),
    'cloud_sword': template('流云剑', 'weapon', 3, 320, 1.8, '锻纹如流云，轻而不浮，非寻常匠作。', attack=15, training=.3),
    'ancient_blade': template('照夜古刀', 'weapon', 2, 720, 2.6, '寒光映夜，刀铭已不可辨，锋芒历岁未减。', attack=20, training=.4),
    'cloth': template('粗布衣', 'armor', 9, 10, 1.1, '洗得发白的布衣，能挡些风尘。', defense=1),
    'leather': template('熟皮护衣', 'armor', 7, 42, 3.5, '熟皮缝制，护住要害，适合行脚。', defense=4),
    'silk_robe': template('云纹锦袍', 'armor', 5, 110, 1.6, '锦绣藏锋，内衬柔韧丝绵。', defense=7),
    'jade': template('平安玉扣', 'accessory', 8, 24, .1, '小小一枚青玉，寄寓岁岁平安。', recovery=1),
    'gourd': template('药葫芦', 'accessory', 6, 64, .4, '以药草熏养的葫芦，佩在身边助人调息。', recovery=2),
    'hoe': template('铁头锄', 'tool', 9, 14, 2., '农家常用的锄具，勤作可增收。', income=1.5),
    'hammer': template('锻铁锤', 'tool', 8, 22, 2.5, '木柄铁锤，既可锻打，也可修整工件。', income=2),
    'abacus': template('乌木算盘', 'tool', 7, 38, .8, '算清一笔一笔的往来，买卖才有余地。', income=2.5),
    'herb_basket': template('采药篓', 'tool', 8, 20, .7, '竹篾细密，可妥善收存山间药草。', income=2),
    'salve': template('金疮药', 'medicine', 9, 6, .1, '外敷伤处，恢复体力并缩短外伤。', heal=18, wound=2),
    'pill': template('养心丸', 'medicine', 7, 18, .1, '缓和病势，调养元气；不能代替长期诊治。', heal=12, illness=12),
    'ration': template('麦饼', 'food', 9, 3, .3, '朴素耐放的行粮，食用恢复少许体力。', heal=6),
    'wine': template('桂花酿', 'wine', 8, 8, .5, '清甜微辛，歇脚时饮一盏，可舒缓心绪。', heal=8, joy=4),
    'basic_manual': template('吐纳入门', 'manual', 9, 20, .3, '从呼吸起落入手，循序习得基本功。', learning=1.2, requirement=0, pages=3),
    'sword_manual': template('青溪剑谱', 'manual', 7, 58, .4, '记录青溪一脉剑式，需些武学根基。', learning=2., requirement=12, pages=4),
    'cloud_manual': template('栖云心法', 'manual', 4, 180, .4, '凝神守一，气息绵长；根基不足者难以领会。', learning=3., requirement=30, pages=5),
    'taixu_manual': template('太虚真解', 'manual', 1, 1200, .5, '字句深奥，须有深厚根基，方能窥见其意。', learning=4.5, requirement=70, pages=6),
    'herb': template('止血草', 'material', 9, 4, .2, '山间常见药草，可制金疮药。'),
    'iron': template('生铁', 'material', 9, 5, .8, '未经精炼的铁料，可用于修补兵刃与衣甲。'),
    'timber': template('硬木料', 'material', 9, 4, .6, '纹理紧实的木料，可用于修补佩饰与器具。'),
}
# Keep old template IDs and their effects stable; add the missing grades around them.
ORIGINAL_TEMPLATES = frozenset(CATALOG)
CATALOG.update({
    'iron_practice_sword': template('铁胎练剑', 'weapon', 8, 26, 2.6, '剑刃未开，铁胎扎实，适宜习练架势。', attack=4, training=.11),
    'autumn_sword': template('秋水剑', 'weapon', 5, 140, 2.2, '淬火匀净，剑光似秋水，收发自如。', attack=11, training=.24),
    'frost_blade': template('霜华刀', 'weapon', 4, 220, 2.8, '叠锻霜纹隐现，兼有韧性与锋锐。', attack=13, training=.27),
    'tianheng_sword': template('天衡剑', 'weapon', 1, 1500, 2.1, '名匠穷年淬成，剑脊如衡，出锋若星。', attack=25, training=.5),
    'scallion_pancake': template('葱香炊饼', 'food', 8, 6, .3, '面香裹葱，外酥内软，是旅店常备的点心。', heal=9),
    'mushroom_noodles': template('山菌汤面', 'food', 7, 12, .4, '山菌熬汤，细面滑口，暖胃解乏。', heal=12),
    'lotus_rice': template('荷叶蒸饭', 'food', 6, 22, .4, '新米与时蔬包入荷叶，蒸得清香。', heal=16),
    'river_shrimp': template('清炒河虾', 'food', 5, 40, .3, '细火急炒，虾肉鲜嫩，佐以时令青蔬。', heal=20),
    'silver_fish': template('银丝鱼脍', 'food', 4, 72, .3, '鲜鱼细切，姜醋相和，刀工十分讲究。', heal=25),
    'golden_crab': template('金膏蒸蟹', 'food', 3, 128, .4, '秋蟹膏满，清蒸留鲜，是席间珍味。', heal=31),
    'mountain_stew': template('山海珍羹', 'food', 2, 240, .4, '山珍海味慢煨成羹，汤色清润，滋养甚厚。', heal=38),
    'nine_treasure_feast': template('九珍玉食', 'food', 1, 480, .5, '九味珍材各得其宜，汇成一席玲珑滋味。', heal=48),
    'village_wine': template('村酿浊酒', 'wine', 9, 4, .5, '村家新酿，酒色微浊，饮来温和。', heal=5, joy=2),
    'green_plum_wine': template('青梅酒', 'wine', 7, 16, .5, '青梅入酒，酸甜相济，略解行路烦忧。', heal=10, joy=5),
    'bamboo_wine': template('竹叶青酿', 'wine', 6, 30, .5, '竹叶浸酿，色泽青润，回味悠长。', heal=12, joy=6),
    'pear_blossom_wine': template('梨花白', 'wine', 5, 55, .5, '酒色澄澈，香如春日梨花，入口绵柔。', heal=15, joy=7),
    'song_lao_wine': template('松醪春', 'wine', 4, 96, .5, '松香与谷香交融，封坛经年方启。', heal=18, joy=8),
    'jade_dew_wine': template('玉壶清露', 'wine', 3, 168, .4, '清泉酿成，以玉色瓷壶封存，香气清冽。', heal=22, joy=9),
    'zixia_wine': template('紫霞陈酿', 'wine', 2, 300, .4, '陈香层叠，开坛如霞，少许便觉醇厚。', heal=27, joy=10),
    'taibai_wine': template('太白遗酿', 'wine', 1, 600, .4, '名酿古法续传，酒香沉静，百味融于一盏。', heal=34, joy=12),
    'coarse_tea': template('山野粗茶', 'tea', 9, 3, .1, '山间粗叶焙成，虽朴素，亦能解渴醒神。', heal=4, joy=1),
    'roasted_tea': template('炒青茶', 'tea', 8, 7, .1, '铁锅炒青，茶汤明亮，香味平和。', heal=6, joy=2),
    'pine_tea': template('松溪春茗', 'tea', 7, 14, .1, '溪畔春芽，轻焙留香，饮后精神一振。', heal=8, joy=3),
    'cloud_tea': template('云岭毛尖', 'tea', 6, 26, .1, '云岭细芽，锋毫匀整，滋味清鲜。', heal=11, joy=4),
    'lotus_tea': template('莲心雪芽', 'tea', 5, 48, .1, '嫩芽如雪，回甘带莲心清意。', heal=14, joy=5),
    'silver_needle_tea': template('白毫银针', 'tea', 4, 85, .1, '芽头肥壮，白毫如银，茶香温润。', heal=18, joy=6),
    'dragon_tea': template('龙团胜雪', 'tea', 3, 150, .1, '细碾团茶，点汤如雪，工序繁复。', heal=22, joy=8),
    'purple_bud_tea': template('紫笋贡茗', 'tea', 2, 280, .1, '紫芽初展时采制，香凝不散，甘醇入喉。', heal=27, joy=10),
    'taihe_tea': template('太和清露', 'tea', 1, 700, .1, '清露初采，慢焙成香。啜饮一盏，澄心静气。', heal=32, joy=12),
    'stance_manual': template('桩功浅说', 'manual', 8, 34, .3, '从立身扎根讲起，附有清楚的行功图。', learning=1.6, requirement=5, pages=3),
    'steps_manual': template('归雁步图', 'manual', 6, 90, .3, '步随身转，进退有节，须先练稳下盘。', learning=2.3, requirement=18, pages=4),
    'river_manual': template('沧浪劲论', 'manual', 5, 128, .4, '论劲如潮，层层相续，宜以已有根基印证。', learning=2.6, requirement=24, pages=5),
    'moon_manual': template('照月剑经', 'manual', 3, 320, .4, '以静照动，借月喻剑，收录名家心悟。', learning=3.5, requirement=42, pages=5),
    'liangyi_manual': template('两仪玄章', 'manual', 2, 640, .5, '阴阳相济，动静互根，字字关乎深厚内功。', learning=4., requirement=55, pages=6),
})
RECIPES = {'salve': {'herb': 2}, 'pill': {'herb': 3}}


def grade_info(grade):
    return {'grade_label': GRADES[grade], 'grade_name': GRADE_NAMES[grade],
            'grade_color': GRADE_COLORS[grade], 'grade_color_name': GRADE_COLOR_NAMES[grade]}


def grade_legend():
    return [{'grade': grade, **grade_info(grade)} for grade in GRADES]


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


def objects(w, owner):
    return [w.items['objects'][str(i)] for i in actor(w, owner)['inventory']]


def get(w, owner, item_id):
    item = w.items['objects'].get(str(item_id))
    if not item or item['owner'] != str(owner):
        raise ValueError('这件物品已不在当前行囊中')
    return item


def spec(item):
    return CATALOG[item['template']]


def weight(w, owner):
    return round(sum(spec(i)['weight'] for i in objects(w, owner)), 2)


def capacity(w, owner):
    n = actor(w, owner)
    return 30 if str(owner) == 'player' else 18 + n['skill'] * .12


def can_receive(w, owner, template_id):
    if str(owner) not in ('market', 'government') and weight(w, owner) + CATALOG[template_id]['weight'] > capacity(w, owner) + .0001:
        raise ValueError('行囊负重不足，请先出售或赠出一些物品')


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
    for key, value in {'health': 100., 'skill': 12., 'alive': True}.items():
        w.player.setdefault(key, value)
    for n in w.npcs.values():
        prepare(n)
    if getattr(w, 'items', None):
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
        return False
    w.items = {'version': 2, 'next_id': 1, 'objects': {},
               'market': {'name': '五地货栈', 'wealth': 800.}, 'government': {'name': '县衙遗物库'}}
    prepare(w.items['market'])
    prepare(w.items['government'])
    for template_id, d in CATALOG.items():
        for _ in range(4 if d['grade'] >= 7 else 1):
            create(w, 'market', template_id, '货栈开张存货')
    for template_id in ('wood_sword', 'cloth', 'jade', 'salve', 'salve', 'ration', 'basic_manual', 'herb', 'herb', 'iron'):
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
    result = dict(attack=0., defense=0., income=0., training=0., recovery=0.)
    for item_id in actor(w, owner)['equipment'].values():
        item = get(w, owner, item_id)
        if item['durability'] <= 0:
            continue
        for key in result:
            result[key] += spec(item).get(key, 0)
    return result


def wear(w, owner, slots, amount=1):
    changes = []
    for slot in slots:
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
    if d['category'] == 'manual' and n['skill'] >= d['requirement'] and n['skill'] < 100 and item['durability'] > 0:
        if n['read_books'].get(item['template'], 0) < d['pages']:
            result.append('read')
    if not equipped:
        result += ['sell', 'gift']
    return result


def view(w, owner):
    n = actor(w, owner)
    result = []
    for item in objects(w, owner):
        d = spec(item)
        result.append({**deepcopy(item), **d, **grade_info(d['grade']),
                       'category_label': CATEGORIES[d['category']], 'equipped': item['id'] in n['equipment'].values(),
                       'buy_price': buy_price(item), 'sell_price': sell_price(item), 'repair_price': repair_price(item),
                       'read_progress': n['read_books'].get(item['template'], 0),
                       'operations': operations(w, owner, item) if n.get('alive', True) else []})
    return {'owner': str(owner), 'name': n['name'], 'wealth': n.get('wealth', 0), 'health': n.get('health'),
            'skill': n.get('skill'), 'joy': n.get('mood', {}).get('joy'),
            'items': result, 'equipment': n['equipment'].copy(), 'weight': weight(w, owner),
            'capacity': round(capacity(w, owner), 1), 'bonuses': bonuses(w, owner), 'categories': CATEGORIES,
            'slots': SLOTS, 'grades': grade_legend(), 'alive': n.get('alive', True)}


def market_view(w):
    # Individual instances remain addressable even when the UI groups identical stock.
    return {'name': '五地货栈', 'wealth': w.items['market']['wealth'],
            'items': [{**deepcopy(i), **spec(i), 'buy_price': buy_price(i),
                       **grade_info(spec(i)['grade']), 'category_label': CATEGORIES[spec(i)['category']]}
                      for i in objects(w, 'market')],
            'grades': grade_legend(), 'categories': CATEGORIES,
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
    actor_before = {k: n[k] for k in ('wealth', 'health', 'skill')}
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
        after_weight = weight(w, owner)-sum(spec(i)['weight'] for i in consumed)+CATALOG[recipe]['weight']
        if after_weight > capacity(w, owner):
            raise ValueError('行囊负重不足')
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
        can_receive(w, owner, item['template'])
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
    elif operation == 'read':
        progress = n['read_books'].get(item['template'], 0)+1
        before = n['skill']
        n['read_books'][item['template']] = progress
        n['skill'] = min(100, n['skill']+d['learning'])
        item['durability'] = max(0, item['durability']-5)
        text = f"{n['name']}研读{d['name']}至第{progress}篇，武学增长{n['skill']-before:.2f}。"
        facts.update(skill_gain=n['skill']-before, progress=progress, total=d['pages'])
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
        can_receive(w, recipient['id'], item['template'])
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
        if 'use' in ops and n['health'] < (85 if d['category'] == 'medicine' else 75):
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
        if not wanted and n['goal']['kind'] == 'mastery' and n['wealth'] > 45:
            if n['read_books'].get('basic_manual', 0) < 3 and not any(i['template'] == 'basic_manual' for i in objects(w, n['id'])):
                wanted = 'basic_manual'
        found = next((i for i in objects(w, 'market') if i['template'] == wanted and buy_price(i) <= n['wealth']-10), None)
        if found and weight(w, n['id'])+spec(found)['weight'] <= capacity(w, n['id']):
            result.append({'id': f"buy_item:{found['id']}", 'kind': 'buy_item', 'target': found['id'],
                           'label': f"购置 · {spec(found)['name']}", 'item_score': 30 if wanted == 'salve' else 17})
    return result


def produce(w, n, event):
    """Occasional occupational output, with material/labour costs in the same journal."""
    if (w.month+n['id']) % 6 or len(n['inventory']) >= 9 or n['wealth'] < 2:
        return
    template_id = {'农人': 'ration', '药师': 'herb', '匠人': 'iron'}.get(n['job'])
    if not template_id or weight(w, n['id'])+CATALOG[template_id]['weight'] > capacity(w, n['id']):
        return
    n['wealth'] -= 1  # Input cost, in addition to the month's labour opportunity.
    item = create(w, n['id'], template_id, f"{n['name']}劳作所得")
    e = w.emit('item_produce', [n['id']], f"{n['name']}劳作时另备得{CATALOG[template_id]['name']}，耗费1银物料。", .15,
               [event['id']], {'item_id': item['id'], 'template': template_id, 'input_cost': 1, 'producer': n['id']})
    item['origin_event'] = item['last_event'] = e['id']


def inherit(w, deceased, heirs, event):
    possessions = list(objects(w, deceased['id']))
    for index, item in enumerate(possessions):
        destination = heirs[index % len(heirs)]['id'] if heirs else 'government'
        # Inheritance may exceed capacity; no possession is silently destroyed.
        transfer(w, item, destination)
        e = w.emit('item_inherit', [int(destination)] if heirs else [],
                   f"{spec(item)['name']}作为{deceased['name']}的遗物，"
                   + (f"由{actor(w, destination)['name']}承继。" if heirs else '交县衙遗物库保管。'), .45,
                   [event['id'], item.get('last_event')],
                   {'deceased': deceased['id'], 'recipient': str(destination), 'item_id': item['id'],
                    'item': deepcopy(item), 'over_capacity_allowed': True})
        item['last_event'] = e['id']
