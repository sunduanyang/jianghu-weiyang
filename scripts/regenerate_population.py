"""Create a separate nine-grade world, retaining the protagonist from a snapshot.

The source is opened read-only; the output must not exist. No running game is
modified by this command. Import the verified result with the Unity save loader.
"""
import argparse
from copy import deepcopy
import hashlib
import json
from pathlib import Path
import sqlite3
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
from jianghu import items, population, city_maps, settlements
from jianghu.engine import World
from jianghu.store import Store


def retain_player(w, previous, source):
    old = deepcopy(previous['player'])
    # The cast has new identities: an old NPC id must never become a new friend,
    # opponent, reward target or journal reference merely because numbers match.
    for key in list(w.player['inventory']):
        w.items['objects'].pop(str(key))
    old_items = [deepcopy(previous['items']['objects'][str(key)]) for key in old['inventory']]
    equipment = deepcopy(old['equipment'])
    old.update(ties={}, spar_rewards={}, inventory=[], equipment={})
    h = old['protagonist']
    h.update(records=[], revision=h['revision'] + 1)
    w.player = old
    w.battle = None
    remap = {}
    for old_item in old_items:
        item = items.create(w, 'player', old_item['template'], '重生世界时保留的主角原有物品')
        remap[str(old_item['id'])] = item['id']
        item.update(durability=old_item['durability'], created_month=old_item['created_month'],
                    previous_world=dict(source=str(source), item=old_item))
    w.player['equipment'] = {slot: remap[str(key)] for slot, key in equipment.items()}
    h['place'] = settlements.canonical(w, h['place'])
    city = city_maps.world_layout(w, h['place'])
    if not (0 <= h['row'] < city['size'] and 0 <= h['col'] < city['size']
            and city['rows'][h['row']][h['col']] not in '~^#'):
        gate = next(n for n in city['nodes'] if n['id'] == 'gate')
        h.update(row=gate['row'], col=gate['col'])
    e = w.emit('population_rebuilt', [],
               f"世界按社会九品与门派九品重新登记{len(w.npcs)}名人物；"
               f"{w.player['name']}保留原有本领、财物、行装和本月余下行程，旧世界人际与年谱留在原存档。",
               .9, facts=dict(initial_condition=True, source=str(source),
                              player_retained=True, prior_month=previous['month'],
                              prior_records=previous['player']['protagonist']['records'],
                              prior_contact_count=len(previous['player'].get('ties', {})),
                              npc_references_reset=True))
    h['records'] = [e['id']]
    w.population_generation['source'] = str(source)
    w.population_generation['player_retained'] = True
    return remap


def regenerate(source, output, count=1800, seed=20261027):
    source, output = Path(source).resolve(strict=True), Path(output).resolve()
    if output.exists():
        raise ValueError('输出已存在，请使用新的存档名称')
    with sqlite3.connect(source.as_uri() + '?mode=ro', uri=True) as db:
        previous = json.loads(db.execute('SELECT data FROM checkpoint WHERE id=1').fetchone()[0])
    if previous.get('battle') and previous['battle']['status'] == 'active':
        raise ValueError('请先结束当前切磋，再保留主角重生世界')
    w = World(seed=seed, population=count, store=Store(output), durable=False, compact=True,
              population_profile=True, start_month=previous['month'])
    try:
        retain_player(w, previous, source)
        w.checkpoint()
        result = {**population.census(w), 'database': str(output), 'source': str(source),
                  'month': w.month, 'seed': seed, 'generation': w.population_generation,
                  'source_sha256': {p.name: hashlib.sha256(p.read_bytes()).hexdigest()
                                    for p in (ROOT / 'jianghu').glob('*.py')}}
        output.with_suffix('.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
        return result
    finally:
        w.store.close()


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--population', type=int, default=1800)
    parser.add_argument('--seed', type=int, default=20261027)
    args = parser.parse_args()
    result = regenerate(args.source, args.output, args.population, args.seed)
    print(json.dumps({k: result[k] for k in ('database', 'population', 'civil_grades', 'month')}, ensure_ascii=False))
