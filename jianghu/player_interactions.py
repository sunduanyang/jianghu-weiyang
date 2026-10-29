"""Immediate character encounters, using real inventory, ties and physical battles."""
from copy import deepcopy
import math

from . import combat_attributes, crafts, demography, items, martial, martial_manuals, organizations, player_social, protagonist, reactions, sect_life
from . import physical_combat as combat

CATALOG = (
    ('praise', '夸奖', 0, 1, '称赞对方；交情与信任随对方的态度变化，每次消耗1行动力。'),
    ('insult', '羞辱', 0, 1, '当面羞辱对方，降低交情与信任，并损失1声望。'),
    ('gift', '赠送礼物', 0, 1, '从自己的行囊选择一件未装备物品赠予对方，实物交付后增进交情。'),
    ('spar', '切磋', 1, 2, '进入点到为止的物理切磋；每场使用独立战斗状态，仍有装备磨损。'),
    ('teach', '请教', 2, 2, '选择对方掌握的功法，请求授予秘籍；老师可以拒绝。得到秘籍后须在行囊使用学习。'),
    ('confide', '知心而交', 3, 1, '需情分25、信任15。向对方提出结为知交，由其性情和当前心情决定是否接受。'),
    ('marry', '共结连理', 3, 3, '双方须年满18岁、均未婚，情分60、信任40，且对方愿意接受你的心意。'),
    ('swear', '义结金兰', 3, 2, '双方须年满16岁，情分40、信任25；对方接受后登记为义亲。'),
    ('steal', '偷窃', 4, 2, '选择偷取银两或未装备物品，成败由灵巧、身法与对方警觉决定；败露损失交情与3声望。'),
    ('rob', '抢夺', 4, 3, '发起真实物理战斗，损失8声望；仅胜出后夺取对方至多20银、至多四分之一家资。双方承受实际损伤。'),
    ('assault', '袭击', 4, 3, '发起真实物理战斗，损失10声望并结下仇怨；双方承受实际损伤，袭击本身不产生战利品。'),
    ('trade', '交易', 5, 1, '与对方买卖未装备的实物；按明示价格交换物品与银两，每成交一件耗1行动力。'),
    ('join_sect', '拜入门派', 5, 4, '由此门派三品及以上人物引收入门，成为九品弟子；消耗4行动力。'),
    ('learn_craft', '学艺', 2, 2, '付2银向对方学其最擅长的一门技艺；对方造诣须达15且高出你5以上。'),
    ('treat', '诊治', 5, 2, '以你的医术为对方调治最重的伤处；接骨须医术25。交情随疗效增进，医术随之精进。'),
    ('mend', '请人修补', 5, 1, '付工钱请懂锻造的人将你耗损最重的兵器或衣甲修满，对方自备料。'),
)
BY_ID = {row[0]: row for row in CATALOG}
HOSTILE = {'steal', 'rob', 'assault'}
CONVERSATIONS = (
    ('talk', '闲叙近况', 0, '与对方聊聊近况，听取其真实态度与回应。'),
    ('reconcile', '赔礼和解', 0, '备二两银赔礼，商谈缓和旧怨；对方可以接受或拒绝。'),
    ('ask_aid', '求助盘缠', 5, '请求对方援助四两盘缠；须由对方依自己的处境决定。'),
    ('introduce', '请其引荐', 5, '请对方引荐其熟识的人，获准后保留引荐凭据与最近所知去向。'),
    ('help', '接济或回报', 5, '拿出四两银接济对方，也可回报此前受过的援助。'),
    ('swear', '邀其结义', 3, '与对方商谈结义，由其斟酌；拒绝后可选择一种性格继续说服。'),
)


def npc(w, npc_id):
    if isinstance(npc_id, bool) or not isinstance(npc_id, int) or npc_id not in w.npcs:
        raise ValueError('请选择有效人物')
    return w.npcs[npc_id]


def loose(w, owner):
    equipped = set(items.actor(w, owner)['equipment'].values())
    return [i for i in items.objects(w, owner) if i['id'] not in equipped]


def points(w, n, action):
    return sect_life.interaction_points(w, n, action) if action in sect_life.INTERACTIONS else BY_ID[action][3]


def reason(w, n, action):
    sect = action in sect_life.INTERACTIONS
    if action not in BY_ID and not sect:
        return '未知人物交互'
    h = w.player['protagonist']; r = player_social.tie(w, n['id'])
    if not n['alive']:
        return '对方已不在世'
    if not h['created']:
        return '请先创建主角'
    if w.battle and w.battle['status'] == 'active':
        return '请先结束当前交手'
    if h['place'] != n['place']:
        return '请先前往对方所在地区'
    if h['points'] < points(w, n, action):
        return f'需要{points(w, n, action)}行动力'
    if any(player_social.is_pending(row) for row in player_social.state(w)['requests']):
        return '请等待当前交谈回应'
    if sect:
        if action not in {row[0] for row in sect_life.interaction_rows(w, n)}:
            return '此门派事项不适用于对方'
        return sect_life.interaction_reason(w, n, action)
    if action == 'join_sect':
        return organizations.player_join_reason(w, n)
    if action in HOSTILE | {'spar', 'marry'} and min(h['age_months'], n['age_months']) < 216:
        return '双方须年满18岁'
    if action in {'spar', 'rob', 'assault'} and min(w.player['health'], n['health']) <= 25:
        return '双方须体力高于25才能交手'
    if action == 'gift' and not loose(w, 'player'):
        return '行囊没有可赠予的未装备物品'
    if action == 'trade' and r.get('affinity', 0) <= -40:
        return '对方与你交恶，不愿交易'
    if action == 'steal' and n['wealth'] <= 0 and not loose(w, n['id']):
        return '对方没有可偷取的银两或物品'
    if action == 'rob' and n['wealth'] <= 0:
        return '对方已经没有余银可夺'
    if action == 'confide':
        if r.get('confidant'):
            return '你们已经结为知交'
        if r.get('affinity', 0) < 25 or r.get('trust', 0) < 15:
            return '结交知己需要情分25、信任15'
    if action == 'swear':
        if r.get('sworn'):
            return '你们已经结为义亲'
        if r.get('married'):
            return '你们已是夫妻'
        if min(h['age_months'], n['age_months']) < 192:
            return '双方须年满16岁'
        if r.get('affinity', 0) < 40 or r.get('trust', 0) < 25:
            return '结义需要情分40、信任25'
    if action == 'marry':
        if h.get('spouse') is not None or n['spouse'] is not None or n.get('player_spouse'):
            return '双方须均未婚'
        if r.get('sworn'):
            return '义亲之间不能结为夫妻'
        if not demography.attracted(n, h):
            return '对方未对你怀有婚恋之意'
        if r.get('affinity', 0) < 60 or r.get('trust', 0) < 40:
            return '结亲需要情分60、信任40'
    if action == 'learn_craft':
        craft = crafts.best(n)
        if n['age_months'] < 192:
            return '对方年纪尚轻，无从传艺'
        if r.get('affinity', 0) <= -20:
            return '对方与你不睦，不愿传艺'
        if crafts.value(n, craft) < max(15, crafts.value(w.player, craft)+5):
            return '对方没有胜过你的技艺可传'
        if w.player['wealth'] < 2:
            return '需要2银学费'
    if action == 'treat' and not crafts.needs_care(n):
        return '对方眼下没有需要调治的伤病'
    if action == 'mend':
        if crafts.value(n, 'forging') < crafts.SKILLED_REPAIR or n['age_months'] < 192:
            return f'对方锻造造诣不足{crafts.SKILLED_REPAIR}'
        item = crafts.mend_target(w)
        if item is None:
            return '没有需要修补的兵器或衣甲'
        if w.player['wealth'] < crafts.mend_price(item):
            return f'需要{crafts.mend_price(item)}银工钱'
    if action == 'teach':
        mine = h.get('faction')
        if (mine is not None and n['faction'] is not None and n['faction'] != mine
                and sect_life.relation(w, mine, n['faction']) <= -30):
            return '两派交恶，对方不愿传艺'
        offers = martial_manuals.offers(w, n)
        if not any(not offer['reason'] for offer in offers):
            return next((o['reason'] for o in offers), '对方暂无可传授的功法')
    return ''


def theft_chance(w, n):
    own = combat_attributes.ratings(w.player); other = combat_attributes.ratings(n)
    return round(max(.1, min(.9, .5 + (own['dexterity']+own['footwork']-other['dexterity']-other['footwork'])/300
                              + (n['traits']['boldness']-50)/250)), 3)


def goods(w, owner, operation=None):
    result = []
    for item in loose(w, owner):
        row = {**items.describe(item), **items.grade_info(items.spec(item)['grade'])}
        if operation:
            row['price'] = items.buy_price(item) if operation == 'buy' else items.sell_price(item)
            payer = w.player if operation == 'buy' else w.npcs[int(owner)] if owner != 'player' else None
            # When selling, the caller supplies the actual NPC's purse separately in view().
            row['reason'] = '银两不足' if payer and payer['wealth'] < row['price'] else ''
        result.append(row)
    return result


def view(w, npc_id):
    n = npc(w, npc_id); h = w.player['protagonist']; r = player_social.tie(w, npc_id)
    social = player_social.view(w, npc_id)
    conversations = [dict(id='social:'+key, action=key, label=label, category=category, points=1,
                          description=description, mode='conversation', listed=key != 'swear',
                          enabled=not social['reason'], reason=social['reason'])
                     for key, label, category, description in CONVERSATIONS]
    rows = [row for row in CATALOG if row[0] != 'join_sect' or organizations.recruiting_faction(w, n) is not None]
    rows += sect_life.interaction_rows(w, n)
    actions = [dict(id=key, label=label, category=category, points=cost, description=description,
                    reason=(why := reason(w, n, key)), enabled=not why)
               for key, label, category, cost, description in rows]
    sell = goods(w, 'player', 'sell')
    for row in sell:
        row['reason'] = '对方余银不足' if n['wealth'] < row['price'] else ''
    return dict(npc_id=npc_id, revision=h['revision'], month=w.month, actions=actions,
                conversations=conversations, social=social,
                gifts=goods(w, 'player'), buy=goods(w, npc_id, 'buy'), sell=sell,
                steal=goods(w, npc_id), manual_offers=martial_manuals.offers(w, n),
                theft_chance=theft_chance(w, n), player_wealth=w.player['wealth'], npc_wealth=n['wealth'],
                reactions=reactions.view(w, npc_id),
                relationship=deepcopy(r), records=[e for i in reversed(r.get('encounter_records', [])[-8:]) if (e := w.store.event(i))])


def change_tie(r, affinity, trust):
    for key, delta in (('affinity', affinity), ('trust', trust)):
        r[key] = max(-100., min(100., r.get(key, 0)+delta))


def event(w, n, action, text, facts=None, aware=True):
    facts = dict(facts or {}, action=action, target=n['id'], player_points=BY_ID[action][3], npc_time_cost=0)
    r = w.player['ties'].setdefault(str(n['id']), dict(affinity=0., trust=0.))
    if aware:
        result = w.emit('player_'+action, [n['id']], text, .7 if action in HOSTILE | {'marry', 'swear'} else .45,
                        [n['last_event'], r.get('last_event')], facts)
        r.update(last_event=result['id'], last_contact=w.month)
        w.player['protagonist']['records'].append(result['id'])
    else:
        result = protagonist.record(w, 'player_'+action, text, facts)
    return result


def perform(w, body):
    allowed = {'npc_id', 'action', 'revision', 'month', 'item_id', 'direction', 'art', 'quoted_price'}
    if not isinstance(body, dict) or set(body)-allowed:
        raise ValueError('人物交互只接受既定事项与物品编号')
    n = npc(w, body.get('npc_id')); key = body.get('action'); h = w.player['protagonist']
    if not isinstance(key, str) or key not in BY_ID and key not in sect_life.INTERACTIONS:
        raise ValueError('未知人物交互')
    if (protagonist.integer(body.get('revision'), '行程版本') != h['revision'] or
            protagonist.integer(body.get('month'), '月份') != w.month):
        raise ValueError('处境已变化，请刷新后再操作')
    why = reason(w, n, key)
    if why:
        raise ValueError(why)
    item = None; direction = body.get('direction'); art = body.get('art')
    if key in {'gift', 'trade'} or key == 'steal' and body.get('item_id') is not None:
        if key == 'trade' and direction not in ('buy', 'sell'):
            raise ValueError('请选择购买或出售')
        owner = n['id'] if key == 'steal' or key == 'trade' and direction == 'buy' else 'player'
        item = items.get(w, owner, body.get('item_id'))
        if item not in loose(w, owner):
            raise ValueError('装备中的物品不能赠送、买卖或偷取')
    if key == 'trade':
        price = items.buy_price(item) if direction == 'buy' else items.sell_price(item)
        if 'quoted_price' in body:
            quote = body['quoted_price']
            if isinstance(quote, bool) or not isinstance(quote, (int, float)) or not math.isfinite(quote) or abs(quote-price) > .0001:
                raise ValueError('报价已变化，请重新查看物品')
        payer, payee = (w.player, n) if direction == 'buy' else (n, w.player)
        if payer['wealth'] < price:
            raise ValueError('你的银两不足' if direction == 'buy' else '对方余银不足')
    if key == 'teach':
        why = martial_manuals.reason(w, 'player', n, art)
        if why:
            raise ValueError(why)
    if key == 'steal' and item is None and n['wealth'] <= 0:
        raise ValueError('对方没有余银可偷取，请选择一件物品')

    if key in sect_life.INTERACTIONS:
        result, battle = sect_life.perform_interaction(w, n, key)
        h['revision'] += 1
        r = w.player['ties'].setdefault(str(n['id']), dict(affinity=0., trust=0.))
        r['encounter_records'] = (r.get('encounter_records', [])+[result['id']])[-24:]
        if battle:
            w.battle = battle
        w.checkpoint()
        response = dict(event=result, interaction=view(w, n['id']))
        if battle:
            response['martial'] = w.martial_view()
        return response
    r = w.player['ties'].setdefault(str(n['id']), dict(affinity=0., trust=0.))
    name = w.player['name']; target = n['name']; aware = True
    if key == 'join_sect':
        f = organizations.recruiting_faction(w, n)
        h.update(faction=f['id'], faction_since=w.month, contribution=0.)
        text = f"{name}经{target}引收，拜入{f['name']}，成为九品{f['ranks'][8]}。"
        result = event(w, n, key, text, dict(actor='player', faction=f['id'], key=f['key'],
                                           recruiter=n['id'], recruiter_grade=organizations.rank(w, n)['grade']))
        organizations.record(f, result)
        sect_life.on_join(w, f, result)
        martial.sync_actor(w, w.player, True)
    elif key == 'gift':
        result = items.perform(w, 'player', 'gift', item['id'], n['id'])
        h['records'].append(result['id'])
    elif key == 'trade':
        payer['wealth'] = round(payer['wealth']-price, 4); payee['wealth'] = round(payee['wealth']+price, 4)
        previous = item['last_event']; items.transfer(w, item, 'player' if direction == 'buy' else n['id'])
        text = f"{name}{'向'+target+'购得' if direction == 'buy' else '将物品售予'+target+'：'}{items.spec(item)['name']}，成交价{price:g}银。"
        result = event(w, n, key, text, dict(direction=direction, silver=price, item_id=item['id'], previous_event=previous))
        item['last_event'] = result['id']
    elif key == 'teach':
        result = martial_manuals.request(w, 'player', n['id'], art)
    elif key == 'learn_craft':
        craft = crafts.best(n)
        w.player['wealth'] = round(w.player['wealth']-2, 4); n['wealth'] = round(n['wealth']+2, 4)
        growth = crafts.gain(w.player, craft, crafts.teaching_gain(n, craft))
        change_tie(r, 2, 2)
        text = f"{name}付2银向{target}学习{crafts.NAMES[craft]}，造诣增长{growth['gain']:.2f}。"
        result = event(w, n, key, text, dict(craft=craft, teacher_skill=crafts.value(n, craft), payment=2, practice=growth))
    elif key == 'treat':
        salve = next((i for i in loose(w, 'player') if i['template'] == 'salve'), None)
        care = crafts.treat(w.player, n, 1.3 if salve else 1.)
        if salve:
            care['salve'] = deepcopy(salve); items.remove(w, salve)
        care['practice'] = crafts.practice(w.player, 'medicine', 1.)
        part = care['part']
        warmth = 3 + (6 if part and part['outcome'] in ('set', 'treated') else 1 if part else 2)
        change_tie(r, warmth, 3)
        n['mood']['joy'] = min(30., n['mood']['joy']+2)
        text = f"{name}为{target}诊治，对方恢复{care['healing']:g}体力"
        if part:
            text += '，' + {'relieved': f"{part['part_name']}骨伤未能接正，只得止痛", 'set': f"接正{part['part_name']}骨伤",
                            'treated': f"处理{part['part_name']}伤处"}[part['outcome']]
        text += ('，敷用一份金疮药' if salve else '') + '。'
        result = event(w, n, key, text, dict(care=care, affinity_gain=warmth))
    elif key == 'mend':
        item = crafts.mend_target(w); price = crafts.mend_price(item); before = item['durability']
        w.player['wealth'] = round(w.player['wealth']-price, 4); n['wealth'] = round(n['wealth']+price, 4)
        item['durability'] = 100
        growth = crafts.practice(n, 'forging', .3)
        change_tie(r, 1, 1)
        text = f"{name}付{price}银请{target}修补{items.name(item)}，耐久由{before:g}修至100。"
        result = event(w, n, key, text, dict(item_id=item['id'], before=before, after=100, silver=price, practice=growth))
        item['last_event'] = result['id']
    elif key in {'spar', 'rob', 'assault'}:
        spar = key == 'spar'
        martial.sync_actor(w, w.player, True); martial.sync_actor(w, n)
        battle_id = f'{w.seed}-{w.month}-{w.store.next_id}'
        battle = combat.start(w.player, n, w.rng.getrandbits(63), battle_id, spar=spar,
                              equipment=(combat.equipment(w, 'player'), combat.equipment(w, n['id'])))
        battle['encounter'] = key
        if not spar:
            change_tie(r, -35 if key == 'rob' else -45, -30)
            w.player['reputation'] -= 8 if key == 'rob' else 10
            battle['loot_cap'] = round(min(20., n['wealth']*.25), 4) if key == 'rob' else 0
        text = f"{name}与{target}约定点到为止，摆开切磋架势。" if spar else f"{name}向{target}发起{'抢夺' if key == 'rob' else '袭击'}，双方拔兵交手。"
        result = event(w, n, key, text, dict(battle_id=battle_id, hostile=not spar))
        battle['cause'] = result['id']; w.battle = battle
    elif key == 'steal':
        success = w.rng.random() < theft_chance(w, n)
        aware = not success or w.rng.random() < (100-n['traits']['boldness'])/200
        amount = 0.
        if success:
            if item:
                items.transfer(w, item, 'player')
            else:
                amount = round(min(n['wealth'], max(1., min(8., n['wealth']*.15))), 4)
                n['wealth'] = round(n['wealth']-amount, 4); w.player['wealth'] = round(w.player['wealth']+amount, 4)
        if aware:
            change_tie(r, -18, -20); w.player['reputation'] -= 3
        loot = items.spec(item)['name'] if item else f'{amount:g}银'
        text = f"{name}偷取{target}的{loot}得手" if success else f"{name}向{target}行窃未遂"
        text += '，当场败露，结下怨隙。' if aware else '，未被察觉。'
        result = event(w, n, key, text, dict(success=success, detected=aware, silver=amount, item_id=item['id'] if item and success else None), aware)
        if item and success:
            item['last_event'] = result['id']
    else:
        accepted = n['mood']['anger'] < 65 and (r.get('affinity', 0)+r.get('trust', 0)+n['traits']['integrity']+n['traits']['sociability'] >= 100)
        if key == 'praise':
            accepted = n['mood']['anger'] < 65 and r.get('affinity', 0) > -40
            change_tie(r, 4 if accepted else -1, 2 if accepted else 0)
            n['mood']['joy'] = min(30., n['mood']['joy']+(3 if accepted else 0))
            text = f"{name}称赞{target}，"+('对方欣然领受，交情增进。' if accepted else '对方心存芥蒂，未领这份好意。')
        elif key == 'insult':
            change_tie(r, -12, -10); n['mood']['anger'] = min(100., n['mood']['anger']+8)
            w.player['reputation'] -= 1
            text = f"{name}当面羞辱{target}，对方愤怒，情分与信任下降。"
        else:
            if accepted:
                change_tie(r, 8, 5)
                if key == 'confide': r.update(confidant=True, confidant_month=w.month)
                elif key == 'swear': r.update(sworn=True, sworn_month=w.month)
                else:
                    r.update(married=True, married_month=w.month)
                    h['spouse'] = n['id']; n['player_spouse'] = True
            label = BY_ID[key][1]
            text = f"{name}向{target}提出{label}，"+('对方应允，双方郑重定下约定。' if accepted else '对方此刻不愿应允，婉拒了这份邀约。')
        result = event(w, n, key, text, dict(accepted=accepted))
    if key in sect_life.SEVERITY:
        sect_life.after_hostile(w, key, n, result, aware)
    if key != 'teach':
        h['points'] -= BY_ID[key][3]
    h['revision'] += 1
    r['encounter_records'] = (r.get('encounter_records', [])+[result['id']])[-24:]
    w.checkpoint()
    response = dict(event=result, interaction=view(w, n['id']))
    if key in {'spar', 'rob', 'assault'}:
        response['martial'] = w.martial_view()
    return response


def settle_hostile(w, battle, n):
    """Called once by the existing battle settlement, including after reload."""
    key = battle.get('encounter')
    if key in ('sect_trial', 'sect_hunt', 'sect_feud'):
        return sect_life.settle_battle(w, battle, n)
    if key not in {'rob', 'assault'}:
        return None
    loot = 0.
    if key == 'rob' and battle['winner'] == 0:
        loot = round(min(n['wealth'], battle.get('loot_cap', 0)), 4)
        n['wealth'] = round(n['wealth']-loot, 4); w.player['wealth'] = round(w.player['wealth']+loot, 4)
    crafts.settle_battle(battle, (w.player, n))
    result = '双方未分胜负' if battle['winner'] is None else battle['fighters'][battle['winner']]['name']+'胜出'
    text = f"{w.player['name']}与{n['name']}因{'抢夺' if key == 'rob' else '袭击'}交手，{result}。"
    text += f'夺得{loot:g}银。' if loot else '未获得战利品。'
    return dict(kind='player_'+key+'_result', summary=text, loot=loot)
