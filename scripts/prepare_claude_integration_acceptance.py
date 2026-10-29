"""Prepare a fresh, isolated save for -claudeIntegrationSmoke (no model calls)."""
import argparse
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
from jianghu import city_maps, combat_attributes, player_interactions, protagonist, rank_progression, sect_life
from jianghu.engine import World
from jianghu.store import Store


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--save-dir', type=Path, required=True)
    folder = parser.parse_args().save_dir.resolve()
    if not folder.is_relative_to(ROOT / 'output'):
        parser.error('Acceptance saves must stay inside repository output')
    folder.mkdir(parents=True, exist_ok=True)
    path = folder / 'world.sqlite'
    if path.exists():
        parser.error('Refusing to overwrite an existing save')
    w = World(91, 0, Store(path), compact=True, durable=False, rank_assessments=True)
    try:
        f = w.factions[0]
        protagonist.perform(w, dict(action='create', revision=0, month=0,
                                    name='融合验收', sex='女', age=24, place=f['place']))
        h = w.player['protagonist']
        elder = w.make_npc(place=f['place'])
        elder.update(name='验收考校前辈', faction=f['id'], contribution=40., faction_since=-24,
                     age_months=480, health=100., wealth=80., spouse=None)
        elder['traits'].update(kindness=85., integrity=90., boldness=45., ambition=55., sociability=65.)
        elder['beliefs'].update(trust=70., justice=80., tradition=85.)
        elder['mood'].update(anger=0., joy=0.)
        combat_attributes.initialize(elder)
        elder['combat_attributes']['base'].update(dict.fromkeys(combat_attributes.KEYS, 45.))
        w.set_goal(elder, 'mastery', initial=True)
        rank_progression.prepare(w, elder['id'])['sect']['grade'] = 3
        outlaw = w.make_npc(place=f['place'])
        outlaw.update(name='验收缉拿恶人', faction=None, age_months=360, health=100.,
                      wealth=60., reputation=-40., spouse=None)
        combat_attributes.initialize(outlaw)
        outlaw['combat_attributes']['base'].update(dict.fromkeys(combat_attributes.KEYS, 20.))
        w.set_goal(outlaw, 'revenge', initial=True)
        w.reindex()
        player_interactions.perform(w, dict(npc_id=elder['id'], action='join_sect',
                                            revision=h['revision'], month=w.month))
        h.update(contribution=40., faction_since=-24, points=30)
        combat_attributes.initialize(w.player)
        w.player['combat_attributes']['base'].update(dict.fromkeys(combat_attributes.KEYS, 65.))
        w.player.update(health=100., wealth=300., reputation=30.)
        progress = rank_progression.prepare(w, 'player')
        progress['sect'].update(grade=7, qualified=1, candidate=6, cooldown_until=-1)
        progress['activity']['service'] = [w.month]
        layout = city_maps.world_layout(w, f['place'])
        seat = next(s for s in layout['settlements'] if s['kind'] == 'sect' and s['faction'] == f['id'])
        h.update(row=seat['row'], col=seat['col'])
        conditions = rank_progression.sect_conditions(w, 'player')
        assert conditions['qualified'] and conditions['completed_months'] == 1, conditions
        trial = next(a for a in player_interactions.view(w, elder['id'])['actions'] if a['id'] == 'sect_trial')
        assert trial['enabled'], trial
        hunt = next(c for c in sect_life.board(w, f) if c['kind'] == 'hunt')
        assert hunt['target'] == outlaw['id'] and not sect_life.accept_reason(w, f, hunt), hunt
        w.checkpoint()
        fixture = dict(faction=f['id'], elder=elder['id'], outlaw=outlaw['id'],
                       hunt=hunt['id'], grade=7, month=w.month,
                       note='Native victory uses explicit acceptance-only damage injection; no balance claim.')
        (folder / 'fixture.json').write_text(json.dumps(fixture, ensure_ascii=False, indent=2), encoding='utf-8')
        print(json.dumps(dict(save_dir=str(folder), **fixture), ensure_ascii=False))
    finally:
        w.store.close()


if __name__ == '__main__':
    main()
