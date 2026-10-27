"""Replace legacy organizations by name/key, conserving active treasury balances."""
from collections import defaultdict
from copy import deepcopy

from .atlas import FACTIONS, PLACES
from .sect_catalog import VERSION


def restore(w, state):
    from .organizations import STATE_FIELDS
    old = state.get('factions', [])
    by_name = {f['name']: f for f in old}
    by_key = {f['key']: f for f in old if f.get('key')}
    w.sect_version = VERSION
    w.sect_history = deepcopy(state.get('sect_history', []))
    w._sect_migration = None
    mapping, new, retired = {}, [], []
    w.factions = []
    for template in FACTIONS:
        prior = by_key.get(template['key']) or by_name.get(template['name'])
        f = deepcopy(template)
        if prior:
            f.update({k: deepcopy(v) for k, v in prior.items() if k in STATE_FIELDS})
            mapping[prior['id']] = f['id']
        else:
            f.update(fund=0., opening_fund=0., revenue=0., expenses=0.)
            new.append(f)
        w.factions.append(f)
    changed = len(old) != len(FACTIONS) or any(mapping.get(f['id']) != f['id'] for f in old)
    if not changed:
        return
    pools = defaultdict(float)
    for f in old:
        if f['id'] in mapping:
            continue
        retired.append(deepcopy(f))
        country = f.get('country', PLACES[f['place']]['country'])
        pools[country] += f['fund']
    transfers = []
    for country, silver in pools.items():
        recipients = [f for f in new if f['country'] == country]
        if recipients:
            allocated = 0.
            for index, f in enumerate(recipients):
                amount = silver-allocated if index == len(recipients)-1 else round(silver/len(recipients), 4)
                allocated += amount
                f['fund'] += amount
                f['revenue'] += amount
                transfers.append(dict(country=country, faction=f['id'], silver=amount))
        else:
            g = w.governments[country]
            g['fund'] += silver
            g['revenue'] += silver
            transfers.append(dict(country=country, government=country, silver=silver))
    previous = {f['id']: f for f in old}
    released = []
    for n in w.npcs.values():
        for entry in n['membership_history']:
            if entry.get('legacy_faction') is not None:
                continue
            fid = entry.get('faction')
            entry.update(name=previous.get(fid, {}).get('name', entry.get('name', '旧组织')),
                         legacy_faction=fid, faction=mapping.get(fid))
        fid = n['faction']
        if fid is not None and fid not in mapping:
            n['membership_history'].append(dict(faction=None, legacy_faction=fid,
                name=previous.get(fid, {}).get('name', '旧组织'), since=n['faction_since'],
                until=w.month, contribution=n['contribution'], reason='门派重构，旧组织撤销'))
            n.update(faction=None, faction_since=None, contribution=0., faction_rejoin_after=w.month)
            released.append(n['id'])
        elif fid is not None:
            n['faction'] = mapping[fid]
        plan = n.get('plan')
        if plan and any(step.split(':', 1)[0] in
                        ('join', 'duty', 'faction_task', 'faction_study', 'leave_faction')
                        for step in plan.get('steps', [])):
            n['plan'] = None
    for pos in w.city_positions.values():
        if pos.get('node', '').startswith('faction_'):
            fid = int(pos['node'].split('_', 1)[1])
            pos['node'] = f'faction_{mapping[fid]}' if fid in mapping else 'residence'
    report = dict(version=VERSION, month=w.month, retired=retired, transfers=transfers,
                  remapping={str(k): v for k, v in mapping.items()}, released=released,
                  treasury_before=sum(f['fund'] for f in old),
                  treasury_after=sum(f['fund'] for f in w.factions), new_money=0)
    w.sect_history.append(report)
    w._sect_migration = report
