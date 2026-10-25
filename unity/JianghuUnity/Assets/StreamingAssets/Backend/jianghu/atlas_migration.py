"""Conserve version-2 funds while replacing its modern-province political model."""
from collections import Counter
from copy import deepcopy

from . import government, politics
from .atlas import COUNTRIES, PLACES
from .historical_map import LEGACY_V2_OWNERS


def from_v2(w, state):
    old = state['governments']
    w.countries = politics.new()
    w.governments = [government.new(c['id']) for c in COUNTRIES]
    monetary = ('fund', 'opening_fund', 'revenue', 'expenses', 'relief')
    weights = [0.] * len(COUNTRIES)
    for g in w.governments:
        for field in monetary + ('security', 'sanitation', 'tax_rate'):
            g[field] = 0.
    allocations = []
    for prior in old:
        targets = Counter(PLACES[i]['country'] for i, owner in enumerate(LEGACY_V2_OWNERS)
                          if owner == prior['country'])
        count = sum(targets.values())
        for country, amount in targets.items():
            share = amount/count
            g = w.governments[country]
            for field in monetary:
                g[field] += prior[field]*share
            for field in ('security', 'sanitation', 'tax_rate'):
                g[field] += prior[field]*share
            weights[country] += share
        primary = max(targets, key=targets.get)
        for field in ('closed_cases', 'succession_count'):
            w.governments[primary][field] += prior[field]
        for case in prior['cases']:
            country = PLACES[w.npcs[case['victim']]['place']]['country']
            w.governments[country]['cases'].append(deepcopy(case))
        allocations.append({'previous_country': prior['country'], 'fund': prior['fund'],
                            'shares': {str(c): n/count for c, n in targets.items()}})
    for g, weight in zip(w.governments, weights):
        for field in ('security', 'sanitation', 'tax_rate'):
            g[field] = g[field]/weight if weight else government.new(g['country'])[field]
    # Each new polity retains one incumbent per role. Other incumbents are released;
    # their old appointment records remain in the journal and archived snapshot.
    incumbents = []
    for prior in old:
        for role, npc_id in prior['offices'].items():
            if npc_id and w.npcs[npc_id]['alive']:
                incumbents.append((role, npc_id, prior))
    for n in w.npcs.values():
        n['office'] = n['office_country'] = None
    retained = []
    for g in w.governments:
        for role in g['offices']:
            candidates = [(i, p) for r, i, p in incumbents
                          if r == role and PLACES[w.npcs[i]['place']]['country'] == g['country']]
            if not candidates:
                continue
            npc_id, previous = max(candidates, key=lambda item: (w.npcs[item[0]]['reputation'], -item[0]))
            n = w.npcs[npc_id]
            n['office'], n['office_country'] = role, g['country']
            g['offices'][role] = npc_id
            g['appointed'][role] = previous['appointed'].get(role, w.month)
            g['last_appointments'][role] = previous['last_appointments'].get(role)
            g['last_event'] = previous['last_event']
            retained.append(npc_id)
    w.government = w.governments[0]
    w.atlas_history = list(state.get('atlas_history', [])) + [
        {'map_version': 2, 'month': w.month, 'countries': deepcopy(state['countries']),
         'governments': deepcopy(old)}]
    return {'from_version': 2, 'to_version': 3, 'allocations': allocations,
            'fund_before': sum(g['fund'] for g in old),
            'fund_after': sum(g['fund'] for g in w.governments),
            'new_money': 0, 'retained_officials': retained,
            'released_officials': [i for _, i, _ in incumbents if i not in retained]}
