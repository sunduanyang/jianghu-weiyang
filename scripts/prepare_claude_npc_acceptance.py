"""Prepare an isolated save for native acceptance of Claude PRs 19-26.

Model answers are deterministic fixtures passed through production validators.
No player save or model configuration is changed.
"""
import argparse
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
from jianghu import ai, persona, protagonist, reactions, visits
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
    w = World(91, 120, Store(path), compact=True, durable=False,
              population_profile=True, market_economy=True, rank_assessments=True)
    try:
        place = w.active_places[0]['id']
        protagonist.perform(w, dict(action='create', revision=0, month=0,
                                    name='人物融合验收', sex='女', age=24, place=place))
        actors = []
        for name, sex, money in [('沈清和', '女', 60.), ('陆怀远', '男', 60.), ('许知秋', '男', 5.)]:
            n = w.make_npc(place=place)
            n.update(name=name, sex=sex, age_months=300, health=100., wealth=money,
                     faction=None, office=None, spouse=None, orientation='异性', pregnancy=None)
            n['mood'].update(anger=0., joy=0., fear=0.)
            w.set_goal(n, 'wealth', initial=True)
            actors.append(n)
        n, target, visitor = actors
        w.reindex()
        w.set_goal(n, 'belonging', initial=True)
        w.link(n, target, 20, 40)
        ctx = ai.context_for(w, n)
        card = dict(motto='待人以诚，遇事不屈', values=['公道', '亲情'], bottom_line='不欺弱小',
                    when_wronged='讨回公道', temptation='不为所动', dream='belonging', leanings=[])
        answer = dict(action_id=ctx['allowed_actions'][0]['id'], goal='belonging',
                      undertaking=f"courtship:{target['id']}", note='先成家，再顾生计', creed=card)
        ok, reason = ai.apply_decision(w, ctx, ai.expand(ctx, answer))
        assert ok, reason
        ok, reason = persona.apply_voice(w, persona.voice_context(w, n),
                                        dict(self_ref='我', manner='话语平实，遇事先问清缘由', phrases=['慢慢说来']))
        assert ok, reason
        w.player['ties'][str(n['id'])] = dict(affinity=45., trust=30.)
        event = w.emit('player_praise', [n['id']], '人物融合验收称赞沈清和行事厚道。', .4,
                       facts=dict(target=n['id'], accepted=True))
        [reaction] = reactions.queue(w, event['id'])
        ok, reason = reactions.apply(w, reaction, dict(attitude='grateful', reply='承蒙你这番好意，我记在心里。',
                                                       thought='此人待我诚恳，往后也该以诚相待。',
                                                       impression='你待人真诚，是个可以信赖的人。'))
        assert ok, reason
        w.emit('goal_complete', [n['id']], '沈清和达成旧日心愿，开始重新思量往后的日子。', .85)
        w.player['ties'][str(visitor['id'])] = dict(affinity=30., trust=10.)
        made = visits.monthly(w)
        visit = next(v for v in made if v['npc_id'] == visitor['id'])
        w.checkpoint()
        fixture = dict(person=n['id'], target=target['id'], visitor=visitor['id'],
                       visit_id=visit['id'], month=w.month, wealth=w.player['wealth'],
                       visitor_wealth=visitor['wealth'], participation=ai.participation(w),
                       note='Production validation with deterministic model fixtures; no real inference.')
        (folder/'npc-fixture.json').write_text(json.dumps(fixture, ensure_ascii=False, indent=2), encoding='utf-8')
        (folder/'data').mkdir(exist_ok=True)
        (folder/'data/ai_config.json').write_text(json.dumps(dict(enabled=False)), encoding='utf-8')
        print(json.dumps(fixture, ensure_ascii=False))
    finally:
        w.store.close()


if __name__ == '__main__':
    main()
