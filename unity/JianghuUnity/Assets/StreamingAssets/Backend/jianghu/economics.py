"""Finite town inventories, household demand and paid production.

Bulk units represent household supplies or service capacity, separately from
individually owned equipment. The engine alone advances production and exchange.
"""
from copy import deepcopy
import math
from . import careers, territory, politics

VERSION = 1
GOODS = {
    'grain': ('谷粮', 2., .01), 'fish': ('鱼鲜', 2.5, .35), 'meat': ('肉食', 3., .22),
    'fiber': ('麻棉', 2., 0.), 'cloth': ('布匹', 5., 0.), 'herb': ('药材', 1.4, .03),
    'medicine': ('成药', 3.2, .01), 'timber': ('木材', 3., 0.), 'iron': ('铁料', 4., 0.),
    'tools': ('日用器具', 8., 0.), 'study': ('文教服务', 4., 1.),
    'leisure': ('游艺服务', 3., 1.), 'transport': ('行旅服务', 3., 1.),
}
FOOD = ('grain', 'fish', 'meat')
RECIPES = {
    '农人': [({'grain': 7., 'fiber': 1.}, {})],
    '渔人': [({'fish': 5.5}, {})], '猎户': [({'meat': 4.8}, {})],
    '短工': [({'timber': 2.7}, {}), ({'iron': 2.2}, {})],
    '药师': [({'herb': 6.}, {}), ({'medicine': 2.5}, {'herb': 2., 'cloth': .1})],
    '匠人': [({'cloth': 3.5}, {'fiber': 2.}), ({'tools': 1.8}, {'timber': .6, 'iron': .4})],
    '书生': [({'study': 3.}, {'timber': .08})],
    '艺人': [({'leisure': 4.}, {})], '镖师': [({'transport': 4.}, {})],
}


def active(w):
    return 'goods_system' in w.economy


def state(w):
    return w.economy['goods_system']


def q(value):
    return round(max(0., value), 4)


def quantity(value):
    """Floor settlement quantities so affordability never rounds upward."""
    return math.floor(max(0., value) * 10000 + 1e-8) / 10000


def prepare(n):
    n.setdefault('economic_life', dict(stock={g: 0. for g in GOODS}, needs={}, satisfaction=1.,
                                      income=0., expenses=0., month=None, records=[]))
    return n['economic_life']


def actor(w, who):
    return w.player if str(who) == 'player' else w.npcs[int(who)]


def place_of(w, who):
    return w.player['protagonist']['place'] if str(who) == 'player' else actor(w, who)['place']


def needs(w, who):
    n = actor(w, who)
    age = w.player['protagonist']['age_months'] if str(who) == 'player' else n['age_months']
    adult = age >= 144
    luxury = .25 if n['wealth'] >= 400 else .12 if n['wealth'] >= 100 else .04
    return dict(food=1. if adult else .52, cloth=.06 if adult else .04,
                medicine=.3 if n.get('illness') or n.get('wound') else .015,
                tools=.035 if adult else 0., study=.07 if age >= 120 else .025,
                leisure=luxury, transport=.06 if adult else .02)


def market(w, place):
    return state(w)['markets'][str(place)]


def targets(w, place):
    residents = [w.npcs[i] for i in w.residents.get(place, []) if w.npcs[i]['alive']]
    count = len(residents)
    return dict(grain=max(1., count * .7), fish=max(.5, count * .2), meat=max(.5, count * .15),
                fiber=max(1., count * .06), cloth=max(.5, count * .06), herb=max(1., count * .05),
                medicine=max(.3, sum(needs(w, n['id'])['medicine'] for n in residents)),
                timber=max(1., count * .04), iron=max(1., count * .025), tools=max(.4, count * .035),
                study=max(.3, count * .065), leisure=max(.4, sum(needs(w, n['id'])['leisure'] for n in residents)),
                transport=max(.5, count * .08))


def total_cash(w):
    return (sum(n['wealth'] for n in w.npcs.values()) + w.player['wealth'] +
            sum(g['fund'] for g in w.governments) + sum(f['fund'] for f in w.factions) +
            sum(c['fund'] for c in w.society['communities'].values()) + w.items['market']['wealth'] +
            w.economy['gaming_house'] + (sum(m['cash'] for m in state(w)['markets'].values()) if active(w) else 0.))


def total_goods(w):
    result = {g: 0. for g in GOODS}
    stocks = [m['stock'] for m in state(w)['markets'].values()]
    stocks += [prepare(n)['stock'] for n in w.npcs.values()] + [prepare(w.player)['stock'], state(w)['estate_stock']]
    for stock in stocks:
        for g, value in stock.items():
            result[g] += value
    return result


def initialize(w):
    if active(w):
        return False
    old_cash = total_cash(w)
    system = dict(version=VERSION, since=w.month, markets={}, produced={g: 0. for g in GOODS},
                  consumed={g: 0. for g in GOODS}, spoiled={g: 0. for g in GOODS},
                  estate_stock={g: 0. for g in GOODS}, destroyed_silver=0., transfers=0, settled_month=None)
    w.economy['goods_system'] = system
    # Reallocate existing trading-house silver; never mint a migration fund.
    capital = min(600., w.items['market']['wealth']); w.items['market']['wealth'] -= capital
    sites = w.active_places; counts = [max(1, len(w.residents.get(p['id'], []))) for p in sites]
    remaining = capital
    for i, (p, count) in enumerate(zip(sites, counts)):
        cash = round(capital * count / sum(counts), 4) if i < len(sites)-1 else remaining
        remaining -= cash
        wanted = targets(w, p['id'])
        stock = {g: q(wanted[g] * (1.5 if g in FOOD else 2.) if GOODS[g][2] < 1 else 0.) for g in GOODS}
        system['markets'][str(p['id'])] = dict(place=p['id'], cash=cash, stock=stock,
            target=wanted, prices={g: GOODS[g][1] for g in GOODS}, made={g: 0. for g in GOODS},
            used={g: 0. for g in GOODS}, lost={g: 0. for g in GOODS}, records=[])
    for n in w.npcs.values():
        life = prepare(n)
        if n['alive']:
            life['stock']['grain'] = .5
    prepare(w.player)
    system['opening_goods'] = total_goods(w)
    system['opening_cash'] = old_cash
    e = w.emit('economy_registered', [], '各城镇登记基础物资、家计需求和市集；现有货栈拨出实有银两作流通资金，期初物资单独立账。', .8,
               facts=dict(initial_condition=True, version=VERSION, transferred_silver=capital,
                          initial_goods=deepcopy(system['opening_goods']), towns=len(sites)))
    system['registration_event'] = e['id']
    return True


# Markets keep a working float. Above it, a cash-rich market narrows its spread:
# it pays producers more and charges households less, so silver returns to people.
# The bid never reaches the ask (at most 94% against at least 95% of list), so no
# one can buy from a market and sell straight back to it at a profit.
BASE_BID = .85
MAX_BID = .94
MAX_DISCOUNT = .05


def float_needed(m):
    # Targets and list prices only change at the start of a month; the value is cached then.
    cached = m.get('float')
    return cached if cached is not None else max(20., sum(m['target'][g] * m['prices'][g] for g in GOODS))


def liquidity(m):
    return m['cash'] / float_needed(m)


def surplus(m):
    return max(0., liquidity(m) - 1.)


def bid(m, g):
    """What the market pays a seller per unit."""
    return m['prices'][g] * min(MAX_BID, BASE_BID + surplus(m) * .15)


def ask(m, g):
    """What a buyer pays the market per unit."""
    return m['prices'][g] * (1 - min(MAX_DISCOUNT, surplus(m) * .05))


WHOLESALE = .95


def wholesale(m, g):
    """Merchants deal in bulk at close to the list price, without the retail spread."""
    return m['prices'][g] * WHOLESALE


def fee(w, place, amount, service=None):
    """Silver paid into a town market. When it pays for a service (an escort, a performance,
    medicine for a patient) the market's purchased capacity is used up accordingly, so the
    providers it bought from were paid for real work."""
    if active(w) and amount:
        m = market(w, place)
        if service:
            units = quantity(min(m['stock'][service], amount / max(.0001, ask(m, service))))
            if units:
                m['stock'][service] = q(m['stock'][service] - units)
                state(w)['consumed'][service] += units; m['used'][service] += units
        m['cash'] += amount


def buy(w, who, good, amount, reserve=0.):
    n = actor(w, who); life = prepare(n); m = market(w, place_of(w, who)); price = ask(m, good)
    amount = quantity(min(amount, m['stock'][good], max(0., n['wealth'] - reserve) / price))
    cost = round(amount * price, 4)
    n['wealth'] -= cost; m['cash'] += cost; m['stock'][good] = q(m['stock'][good] - amount)
    life['stock'][good] = q(life['stock'][good] + amount); life['expenses'] += cost
    if amount:
        state(w)['transfers'] += 1
    return amount, cost


def sell(w, who, good, amount):
    n = actor(w, who); life = prepare(n); m = market(w, place_of(w, who))
    price = bid(m, good)
    room = max(0., m['target'][good] * (1.2 if GOODS[good][2] == 1 else 3) - m['stock'][good])
    amount = quantity(min(amount, life['stock'][good], room, m['cash'] / price))
    value = round(amount * price, 4)
    if value > m['cash'] + 1e-8:
        amount = max(0., amount - .0001); value = round(amount * price, 4)
    life['stock'][good] = q(life['stock'][good] - amount); m['stock'][good] = q(m['stock'][good] + amount)
    m['cash'] -= value; n['wealth'] += value; life['income'] += value
    if amount:
        state(w)['transfers'] += 1
    return amount, value


def room(m, g, expecting=False):
    """How much of a good the market will still buy. Services spoil monthly, so a person
    weighing a trade expects last month's competing supply to return."""
    stock = m['stock'][g]
    if expecting and GOODS[g][2] == 1:
        stock = max(stock, m.get('last_made', {}).get(g, 0.))
    return max(0., m['target'][g] * (1.2 if GOODS[g][2] == 1 else 3) - stock)


def recipe(w, n, profession=None):
    job = profession or n['career']['profession']; m = market(w, n['place'])
    choices = RECIPES.get(job, [])
    def score(row):
        outputs, inputs = row
        revenue = sum(min(amount, room(m, g)) * bid(m, g) for g, amount in outputs.items())
        cost = sum(max(0., amount - prepare(n)['stock'][g]) * ask(m, g) for g, amount in inputs.items())
        feasible = all(prepare(n)['stock'][g] + m['stock'][g] >= amount for g, amount in inputs.items())
        return revenue - cost if feasible else -10000
    return max(choices, key=score) if choices else ({}, {})


def expected_income(w, n, profession=None):
    if not active(w):
        return careers.income(n, profession=profession) * .5
    if (profession or n['career']['profession']) == '行商':
        return max((trip['profit'] for trip in routes(w, n)), default=0.)
    outputs, inputs = recipe(w, n, profession)
    m = market(w, n['place'])
    if any(prepare(n)['stock'][g] + m['stock'][g] < amount for g, amount in inputs.items()):
        return 0.
    revenue = min(m['cash'], sum(min(amount, room(m, g, expecting=profession is not None)) * bid(m, g)
                                for g, amount in outputs.items()))
    cost = sum(max(0., amount - prepare(n)['stock'][g]) * ask(m, g) for g, amount in inputs.items())
    return max(0., revenue - cost)


def routes(w, n):
    options = []; home = n['place']
    for neighbor in w.neighbors.get(home, []):
        for a, b in ((home, neighbor), (neighbor, home)):
            origin, destination = market(w, a), market(w, b)
            journey = politics.journey(w, a, b)
            if not journey.get('allowed', True):
                continue
            toll = journey['toll']
            freight = .6 + politics.journey(w, a, b)['km'] * .001
            for g in GOODS:
                if GOODS[g][2] == 1:
                    continue
                spread = wholesale(destination, g) - origin['prices'][g]
                if spread <= 0:
                    continue
                amount = quantity(min(16., max(0., origin['stock'][g] - origin['target'][g]),
                                 max(0., destination['target'][g] * 2 - destination['stock'][g]),
                                 max(0., n['wealth'] - 4 - toll - freight) / origin['prices'][g],
                                 destination['cash'] / wholesale(destination, g)))
                profit = amount * spread - toll - freight
                if amount >= .1 and profit > .25:
                    options.append(dict(origin=a, destination=b, good=g, quantity=amount, toll=toll, freight=freight, profit=profit))
    return options


def haul(w, n):
    options = routes(w, n)
    if not options:
        return dict(gross=0., tax=0., produced={}, sold={}, inputs={}, note='暂无能覆盖盘缠与关税的邻城货路')
    trip = max(options, key=lambda t: t['profit']); a, b, g = trip['origin'], trip['destination'], trip['good']
    origin, destination = market(w, a), market(w, b); amount = trip['quantity']
    purchase = round(amount * origin['prices'][g], 4); sale = round(amount * wholesale(destination, g), 4)
    freight, toll = round(trip['freight'], 4), trip['toll']
    n['wealth'] += sale - purchase - freight - toll
    origin['cash'] += purchase; destination['cash'] -= sale
    fee(w, a, freight, 'transport')
    origin['stock'][g] = q(origin['stock'][g] - amount); destination['stock'][g] = q(destination['stock'][g] + amount)
    government = w.governments[territory.owner(w, b)]; government['fund'] += toll; government['revenue'] += toll
    politics.record_crossing(w, dict(origin_country=territory.owner(w, a), destination_country=territory.owner(w, b), toll=toll))
    gross = sale - purchase - freight - toll
    from . import government as rules
    tax = rules.tax(w, n, gross) if n['age_months'] >= 192 else 0.
    prepare(n)['income'] += sale; prepare(n)['expenses'] += purchase + freight + toll
    state(w)['transfers'] += 1
    return dict(gross=gross, tax=tax, produced={}, sold={g: amount}, inputs={}, shipment=trip,sales=sale,purchases=purchase,
                note=f"从{w.place_name(a)}转运{GOODS[g][0]}至{w.place_name(b)}，实付货款、运费与关税")


def work(w, n):
    if n['career']['profession'] == '行商':
        result = haul(w, n)
        from . import local_life
        local_life.production(w, n, result)
        return result
    life = prepare(n); m = market(w, n['place']); outputs, inputs = recipe(w, n)
    factor = min(1.5, .85 + careers.experience(n) * .006)
    from . import crafts
    factor *= crafts.work_capacity(n)
    last = life.get('last_needs', {})
    for category, penalty in (('food', .25), ('tools', .15), ('cloth', .08)):
        previous = last.get(category, {})
        factor *= 1 - penalty * previous.get('missing', 0.) / max(.0001, previous.get('required', 1.))
    if n['age_months'] < 192:
        factor *= .45
    job = n['career']['profession']
    if job in ('农人', '渔人', '猎户'):
        factor *= careers.PROFESSIONS[job]['demand'][careers.place_archetype(n['place'])]
        if job == '农人':
            factor *= (1., 1., 1.15, 1.35, 1.35, 1.2, .95, .9, 1.3, 1.3, .85, .75)[w.month % 12]
    produced = {g: q(amount * factor) for g, amount in outputs.items()}
    # Stop unwanted stockpiles; retain enough food for the producer's own table.
    if all(life['stock'][g] >= (2 if g in FOOD else 1) and m['stock'][g] >= m['target'][g] * (1.2 if GOODS[g][2] == 1 else 3) - .0001 for g in outputs):
        produced = {}
    required = {g: q(amount * factor) for g, amount in inputs.items()} if produced else {}
    if any(life['stock'][g] + m['stock'][g] < amount for g, amount in required.items()):
        return dict(gross=0., tax=0., produced={}, sold={}, inputs={}, note='原料不足，未能开工')
    cost = sum(max(0., amount - life['stock'][g]) * ask(m, g) for g, amount in required.items())
    if cost > max(0., n['wealth'] - 2):
        return dict(gross=0., tax=0., produced={}, sold={}, inputs={}, note='备料银两不足，未能开工')
    spent = 0.
    for g, amount in required.items():
        if life['stock'][g] < amount:
            _, paid = buy(w, n['id'], g, amount - life['stock'][g]); spent += paid
        life['stock'][g] = q(life['stock'][g] - amount); state(w)['consumed'][g] += amount
        m['used'][g] += amount
    for g, amount in produced.items():
        life['stock'][g] = q(life['stock'][g] + amount); state(w)['produced'][g] += amount; m['made'][g] += amount
    sold = {}; revenue = 0.
    for g in GOODS:
        retain = .8 if g in FOOD else 0.
        quantity, value = sell(w, n['id'], g, max(0., life['stock'][g] - retain))
        if quantity:
            sold[g] = quantity; revenue += value
    from . import local_life
    delivery = local_life.deliver(w, n, sold, revenue)
    gross = revenue - spent - (delivery['paid'] if delivery else 0.)
    result = dict(produced=produced, sold=sold, inputs=required, local_delivery=delivery)
    local_life.production(w, n, result)
    from . import government
    tax = government.tax(w, n, max(0., gross)) if n['age_months'] >= 192 else 0.
    return dict(gross=gross, tax=tax, **result,
                input_cost=spent, sales=revenue, note='按工地实际产出成交，实物归集费用实付；未售物资留在家计储备')


def work_adjustment(w, n, kind):
    income = expected_income(w, n) if kind == 'work' else max((r['profit'] for r in routes(w, n)), default=0.)
    life = prepare(n); m = market(w, n['place'])
    # Utility reflects an affordable sale and self-provision, not theoretical wages.
    own_food = sum(life['stock'][g] for g in FOOD)
    food_job = n['career']['profession'] in ('农人', '渔人', '猎户') and kind == 'work'
    missing = max(0., needs(w, n['id'])['food'] - own_food)
    return min(28., income * 3) - (35 if income < .1 else 0) + (40 * missing if food_job else 0)


def begin_month(w):
    for p in w.active_places:
        m = market(w, p['id']); m['target'] = targets(w, p['id'])
        prior_supply = m['made'].copy()
        m['last_made'] = prior_supply
        m['float'] = None
        for key in ('made', 'used', 'lost'):
            m[key] = {g: 0. for g in GOODS}
        for g in GOODS:
            cover = (prior_supply[g] if GOODS[g][2] == 1 else m['stock'][g]) / max(.1, m['target'][g])
            target_price = GOODS[g][1] * max(.65, min(2.4, 1.65 - cover * .35))
            m['prices'][g] = round(max(GOODS[g][1]*.65, min(GOODS[g][1]*2.4, m['prices'][g]*.75 + target_price*.25)), 4)
        m['float'] = float_needed(m)
    for n in list(w.npcs.values()) + [w.player]:
        life = prepare(n); life['last_needs'] = deepcopy(life['needs'])
        life.update(income=0., expenses=0., month=w.month, needs={})


def consume(w, who, category, amount, payer, reserve=0.):
    n = actor(w, who); life = prepare(n); supplier = actor(w, payer); other = prepare(supplier)
    m = market(w, place_of(w, who)); goods = FOOD if category == 'food' else (category,)
    goods = sorted(goods, key=lambda g: ask(m, g))
    remaining = amount; paid = 0.; used = {}; payments = {}
    # Draw on all household food alternatives before spending new silver.
    for g in goods:
        for holder in (life, other) if other is not life else (life,):
            part = min(remaining, holder['stock'][g]); holder['stock'][g] = q(holder['stock'][g] - part)
            remaining = q(remaining - part); used[g] = used.get(g, 0.) + part
    wallets = [(payer, supplier, other)] + ([(who, n, life)] if other is not life else [])
    for g in goods:
        for wallet_id, wallet, wallet_life in wallets:
            unit = ask(m, g)
            amount_bought = quantity(min(remaining, m['stock'][g], max(0.,wallet['wealth']-reserve) / unit))
            cost = round(amount_bought * unit, 4)
            if cost > max(0.,wallet['wealth']-reserve) + 1e-8:
                amount_bought = max(0., amount_bought - .0001); cost = round(amount_bought*unit, 4)
            wallet['wealth'] -= cost; m['cash'] += cost; m['stock'][g] = q(m['stock'][g] - amount_bought)
            paid += cost; wallet_life['expenses'] += cost; remaining = q(remaining - amount_bought); used[g] += amount_bought
            if cost:
                payments[str(wallet_id)] = round(payments.get(str(wallet_id),0.)+cost,4)
    for g, used_amount in used.items():
        state(w)['consumed'][g] += used_amount; m['used'][g] += used_amount
    reason = '' if remaining < .0001 else '本地库存不足' if sum(m['stock'][g] for g in goods) < remaining else '生活银两不足'
    life['needs'][category] = dict(required=amount, satisfied=q(amount-remaining), missing=remaining, paid=round(paid, 4), payer=str(payer), payments=payments, reason=reason)
    return paid, remaining


def close_month(w):
    if state(w)['settled_month'] == w.month:
        return
    state(w)['settled_month'] = w.month
    people = [n['id'] for n in w.npcs.values() if n['alive']]
    people.sort(key=lambda i: (w.npcs[i]['age_months'] >= 192, (i + w.month * 37) % max(1, len(w.npcs))))
    if w.player['protagonist']['created']:
        people.append('player')
    payers = {}
    for who in people:
        n = actor(w, who); payer = who
        if who != 'player' and n['age_months'] < 192:
            guardian = w.npcs.get(n['guardian'])
            if guardian and guardian['alive'] and guardian['place'] == n['place'] and (guardian['wealth'] >= 2 or sum(prepare(guardian)['stock'][g] for g in FOOD) >= 1):
                payer = guardian['id']
        payers[who] = payer
    for category in ('food', 'medicine', 'cloth', 'tools', 'study', 'leisure', 'transport'):
        for who in people:
            quantity = needs(w, who)[category]
            consume(w, who, category, quantity, payers[who], reserve=6. if category in ('study', 'leisure', 'transport') else 0.)
    from . import society
    for who in people:
        n = actor(w, who); life = prepare(n); food_missing = life['needs']['food']['missing']
        meaningful = [v for v in life['needs'].values() if v['required'] > 0]
        life['satisfaction'] = round(sum(v['satisfied']/v['required'] for v in meaningful) / max(1,len(meaningful)), 4)
        paid = sum(v['paid'] for v in life['needs'].values())
        if who == 'player':
            n['health'] = max(0., n['health'] - food_missing * 9)
        if who != 'player':
            n['monthly_settlement'] = dict(month=w.month, payer=payers[who], paid=round(paid, 4), food_shortfall=round(food_missing*2., 4))
            if food_missing:
                n['health'] = max(0., n['health'] - food_missing * 7)
            n['pressure'] = min(100., n['pressure'] + food_missing * 12 + max(0., .6-life['satisfaction'])*4)
            if n.get('illness') or n.get('wound'):
                medicine = life['needs']['medicine']
                n['health'] = max(0., n['health'] - 2 * medicine['missing'] / medicine['required'])
            n['mood']['joy'] = min(100., n['mood']['joy'] + 3 * life['needs']['leisure']['satisfied'])
            society.settle_hardship(w, n)
        event = w.emit('household_consumption', [] if who == 'player' else [who],
                       f"{n['name']}本月家计消费{paid:.2f}银，食物需求{'已满足' if not food_missing else '尚缺'+str(round(food_missing,2))+'份'}。",
                       .15 if not food_missing else .65, [n.get('last_event')],
                       dict(month=w.month, needs=deepcopy(life['needs']), payer=str(payers[who]), paid=round(paid, 4)))
        life['records'] = (life['records'] + [event['id']])[-12:]
    spend_gaming_surplus(w)
    # Perishable goods and unused service capacity are explicitly written off.
    holders = [(m['stock'], m) for m in state(w)['markets'].values()]
    holders += [(prepare(n)['stock'], market(w,n['place'])) for n in w.npcs.values() if n['alive']]
    holders += [(prepare(w.player)['stock'], market(w, place_of(w, 'player'))), (state(w)['estate_stock'], None)]
    for stock, m in holders:
        for g, (_, _, rate) in GOODS.items():
            lost = round(stock[g]*rate, 4); stock[g] = q(stock[g]-lost); state(w)['spoiled'][g] += lost
            if m:
                m['lost'][g] += lost


GAMING_FLOAT = 300.


def spend_gaming_surplus(w):
    """Gaming houses pay rent, servants and suppliers: half of any takings above their float
    goes into the town markets by population, where the market spread returns it to people."""
    spare = round((w.economy['gaming_house'] - GAMING_FLOAT) * .5, 4)
    if spare <= 0:
        return 0.
    sites = [(p['id'], max(1, len(w.residents.get(p['id'], [])))) for p in w.active_places]
    total = sum(c for _, c in sites); paid = 0.
    for i, (place, count) in enumerate(sites):
        part = round(spare * count / total, 4) if i < len(sites)-1 else round(spare - paid, 4)
        market(w, place)['cash'] += part; paid += part
    w.economy['gaming_house'] = round(w.economy['gaming_house'] - paid, 4)
    state(w)['gaming_spent'] = round(state(w).get('gaming_spent', 0.) + paid, 4)
    return paid


def inherit(w, n, heirs):
    if not active(w):
        return
    source = prepare(n)['stock']
    destination = prepare(heirs[0])['stock'] if heirs else state(w)['estate_stock']
    for g, value in source.items():
        destination[g] = q(destination[g] + value); source[g] = 0.


def audit(w):
    e = state(w); goods = total_goods(w)
    residuals = {g: round(goods[g] - (e['opening_goods'][g]+e['produced'][g]-e['consumed'][g]-e['spoiled'][g]), 6) for g in GOODS}
    return dict(cash=total_cash(w), cash_residual=round(total_cash(w) - (e['opening_cash']-e['destroyed_silver']), 6),
                goods_residuals=residuals, negative_stock=any(v < -1e-6 for m in e['markets'].values() for v in m['stock'].values()),
                negative_market_cash=any(m['cash'] < -1e-6 for m in e['markets'].values()))


def view(w, place=None, who=None):
    if not active(w):
        return dict(enabled=False)
    place = place if place is not None else place_of(w, 'player')
    m = market(w, place)
    result = dict(enabled=True, version=VERSION, place=place, name=w.place_name(place), cash=round(m['cash'], 4),
        since=state(w)['since'], month=w.month,
        towns=[dict(id=p['id'], name=p['name']) for p in w.active_places],
        liquidity=round(liquidity(m), 4),
        goods=[dict(id=g, name=label, price=round(ask(m, g), 4), sell_price=round(bid(m, g), 4), list_price=m['prices'][g], stock=m['stock'][g],
                    target=m['target'][g], produced=m['made'][g], consumed=m['used'][g], spoiled=m['lost'][g]) for g,(label,_,_) in GOODS.items()])
    if who is not None:
        result['household'] = deepcopy(prepare(actor(w, who)))
        from .engine import date
        result['settlement_date'] = date(result['household']['month']) if result['household']['month'] is not None else ''
    return result


def perform(w, body):
    """Player trades use current location, finite balances and a versioned turn."""
    from . import protagonist
    if not active(w):
        raise ValueError('此存档尚未登记市集经济')
    h = w.player['protagonist']
    if not h['created'] or (w.battle and w.battle['status'] == 'active'):
        raise ValueError('请先创建主角并结束切磋')
    if protagonist.integer(body.get('revision'), '行程版本') != h['revision'] or protagonist.integer(body.get('month'), '月份') != w.month:
        raise ValueError('家计或行程已变化，请刷新后再操作')
    if 'place' in body and protagonist.integer(body['place'], '市集地点') != h['place']:
        raise ValueError('请先沿地图抵达该城镇，再与当地市集交易')
    operation, good = body.get('operation'), body.get('good')
    if operation not in ('buy', 'sell') or good not in GOODS or GOODS[good][2] == 1:
        raise ValueError('请选择可储存的生活物资与买卖方式')
    amount = body.get('amount', 1)
    if isinstance(amount, bool) or not isinstance(amount, (int, float)) or not math.isfinite(amount) or not 0 < amount <= 20:
        raise ValueError('每笔买卖数量须大于0且不超过20份')
    if h['points'] < 1:
        raise ValueError('本月行动力不足，买卖需1点')
    actual, silver = (buy if operation == 'buy' else sell)(w, 'player', good, amount)
    if actual <= 0:
        raise ValueError('物资、银两或市集收购需求不足，未成交')
    h['points'] -= 1
    event = protagonist.record(w, 'hero_market', f"{w.player['name']}在{w.place_name(h['place'])}{'购入' if operation == 'buy' else '售出'}{GOODS[good][0]}{actual:.2f}份，成交{silver:.2f}银。",
                               dict(operation=operation, good=good, quantity=actual, silver=silver, place=h['place']))
    h['revision'] += 1
    w.checkpoint()
    return dict(event=event, market=view(w, h['place'], 'player'))
