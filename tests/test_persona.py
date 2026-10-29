"""人物近况与口吻: conversations carry the person's own life, and each person keeps one way of speaking."""
from copy import deepcopy
import json
import unittest
from unittest.mock import patch

from jianghu import ai, persona, player_social, protagonist, reactions
from jianghu.engine import World


class PersonaTests(unittest.TestCase):
    def setUp(self):
        self.w = w = World(population=0, compact=True, durable=False)
        self.addCleanup(w.store.close)
        self.place = w.active_places[0]['id']
        protagonist.perform(w, dict(action='create', name='沈行舟', sex='男', age=24, place=self.place,
                                    month=w.month, revision=0))

    def npc(self, **values):
        n = self.w.make_npc(place=self.place)
        n.update(age_months=360, wealth=40., health=100., faction=None, office=None, job='农人',
                 children=[], spouse=None, social_status=None)
        n.update(values)
        self.w.set_goal(n, 'wealth', initial=True)
        self.w.reindex()
        return n

    def test_life_lists_what_the_person_is_busy_with_their_troubles_and_people(self):
        n, friend, foe = self.npc(wealth=5.), self.npc(), self.npc()
        self.w.link(n, friend, 60, 40); self.w.link(n, foe, -60, -40)
        n['illness'] = dict(severity=.3, remaining=2, event_id=0)
        before = deepcopy(self.w.export())
        life = persona.life(self.w, n)
        self.assertEqual(self.w.export(), before)
        self.assertIn('手头拮据', life['troubles']); self.assertIn('身染疾病', life['troubles'])
        feelings = {p['name']: p['feeling'] for p in life['people']}
        self.assertEqual(feelings[friend['name']], '情深'); self.assertEqual(feelings[foe['name']], '怨恨')
        self.assertEqual(set(life['names']), {friend['name'], foe['name']})
        self.assertTrue(life['busy_with']['title'])

    def test_rule_voice_follows_age_tradition_and_office_and_address_follows_the_tie(self):
        self.assertEqual(persona.rule_voice(self.w, self.npc(age_months=70*12, sex='女'))['self_ref'], '老身')
        monk = self.npc(sex='男', faction=next(f['id'] for f in self.w.factions if f['tradition'] == '佛门'))
        self.assertEqual(persona.rule_voice(self.w, monk)['self_ref'], '贫僧')
        self.assertEqual(persona.rule_voice(self.w, self.npc(office='县令'))['self_ref'], '本官')
        n = self.npc()
        self.assertEqual(persona.voice(self.w, n)['address_player'], '兄台')
        self.w.player['ties'][str(n['id'])] = dict(affinity=40., trust=10.)
        self.assertEqual(persona.voice(self.w, n)['address_player'], '沈行舟')
        self.w.player['ties'][str(n['id'])]['sworn'] = True
        self.assertEqual(persona.voice(self.w, n)['address_player'], '贤弟')

    def test_conversation_context_carries_life_voice_and_a_guard_against_invented_people(self):
        n, friend, stranger = self.npc(), self.npc(), self.npc()
        self.w.link(n, friend, 60, 40)
        ctx = player_social.start(self.w, n['id'], 'talk', '', player_social.state(self.w)['revision'])
        self.assertIn(friend['name'], ctx['own_life']['names'])
        self.assertEqual(ctx['voice']['self_ref'], persona.rule_voice(self.w, n)['self_ref'])
        self.assertIn(stranger['name'], ctx['_name_guard'])
        self.assertNotIn(friend['name'], ctx['_name_guard'])
        decision = dict(choice_id='chat', reason='闲谈', memory_ids=[ctx['request']['event_id']])
        self.assertTrue(player_social.valid_reply(ctx, decision, dict(reply=f"最近总惦记着{friend['name']}，你可见过他？")))
        self.assertFalse(player_social.valid_reply(ctx, decision, dict(reply=f"前日{stranger['name']}还来找过我。")))

    def test_the_model_settles_a_voice_once_and_it_is_kept(self):
        n = self.npc()
        ctx = persona.voice_context(self.w, n)
        self.assertTrue(persona.needs_voice(n))
        for bad in (dict(self_ref='我是一个农人', manner='说话慢', phrases=['唉']),
                    dict(self_ref='俺', manner='说话慢吞吞', phrases=['十两银子'+'5']),
                    dict(self_ref='俺', manner='像模型一样说话', phrases=['好说好说'])):
            self.assertFalse(persona.valid_voice(bad))
        good = dict(self_ref='俺', manner='说话慢吞吞，爱拿庄稼打比方', phrases=['老天赏饭吃', '急不得'])
        self.assertTrue(persona.apply_voice(self.w, ctx, good)[0])
        card = persona.voice(self.w, n)
        self.assertEqual((card['self_ref'], card['source']), ('俺', 'LLM'))
        self.assertIn('「急不得」', persona.view(self.w, n)['text'])
        self.assertFalse(persona.needs_voice(n))
        self.assertFalse(persona.apply_voice(self.w, ctx, dict(good, self_ref='在下'))[0])
        self.assertEqual(n['voice']['self_ref'], '俺')
        # The kept card reaches conversations and reactions.
        conv = player_social.start(self.w, n['id'], 'talk', '', player_social.state(self.w)['revision'])
        self.assertEqual(conv['voice']['phrases'], ['老天赏饭吃', '急不得'])

    def test_the_manager_asks_for_a_voice_once_when_the_player_deals_with_someone(self):
        n = self.npc()
        manager = ai.AIManager(dict(enabled=True)); self.addCleanup(manager.close)
        card = dict(self_ref='俺', manner='说话慢吞吞，爱拿庄稼打比方', phrases=['急不得'])
        answer = lambda model, ctx: (dict(attitude=ctx['attitudes'][0]['id'], reply='俺记下了。', thought='还算客气。'), 1, {}, '')
        with patch.object(persona, 'voice_request', lambda model, ctx: (card, 1, {}, '')), \
                patch.object(reactions, 'model_request', answer):
            since = self.w.store.next_id
            from jianghu import player_interactions
            player_interactions.perform(self.w, dict(npc_id=n['id'], action='praise',
                                                     revision=self.w.player['protagonist']['revision'], month=self.w.month))
            self.assertEqual(manager.react(self.w, since), 1)
            manager.jobs.join(); manager.collect(self.w)
            # The first answer is in the rule voice; the person's own voice is settled for next time.
            self.assertEqual(manager.view()['voice_pending'], 1)
            self.assertEqual(manager.give_voice(self.w, n['id']), 0)  # already asked
            manager.jobs.join(); manager.collect(self.w)
        self.assertEqual(n['voice']['source'], 'LLM')
        self.assertEqual(ai.participation(self.w)['voices'], 1)
        self.assertEqual(manager.give_voice(self.w, n['id']), 0)


if __name__ == '__main__':
    unittest.main()


class ImpressionTests(unittest.TestCase):
    def setUp(self):
        self.w = w = World(population=0, compact=True, durable=False)
        self.addCleanup(w.store.close)
        self.place = w.active_places[0]['id']
        protagonist.perform(w, dict(action='create', name='沈行舟', sex='男', age=24, place=self.place,
                                    month=w.month, revision=0))
        self.n = n = w.make_npc(place=self.place)
        n.update(age_months=360, wealth=40., health=100., faction=None, office=None, spouse=None)
        w.set_goal(n, 'wealth', initial=True); w.reindex()

    def react(self, impression):
        from jianghu import player_interactions
        since = self.w.store.next_id
        player_interactions.perform(self.w, dict(npc_id=self.n['id'], action='praise',
                                                 revision=self.w.player['protagonist']['revision'], month=self.w.month))
        [ctx] = reactions.queue(self.w, since)
        result = dict(attitude=ctx['attitudes'][0]['id'], reply='多谢夸奖。', thought='此人嘴甜。', impression=impression)
        self.assertTrue(reactions.apply(self.w, ctx, result)[0])
        return ctx

    def test_each_reaction_updates_the_running_view_and_later_prompts_carry_it(self):
        first = self.react('嘴甜的年轻人，不知根底')
        self.assertIsNone(first['previous_impression'])
        second = self.react('嘴甜，倒也真心')
        self.assertEqual(second['previous_impression'], '嘴甜的年轻人，不知根底')
        tie = self.w.player['ties'][str(self.n['id'])]
        self.assertEqual(tie['impression']['text'], '嘴甜，倒也真心')
        conv = player_social.start(self.w, self.n['id'], 'talk', '', player_social.state(self.w)['revision'])
        self.assertEqual(conv['relationship']['impression']['text'], '嘴甜，倒也真心')
        self.assertNotIn('impression', player_social.options_context(conv, dict(choice_id='refuse'), '不行')['relationship'])
        ctx = ai.context_for(self.w, self.n)
        self.assertEqual(ctx['player_contact']['relationship']['impression']['text'], '嘴甜，倒也真心')

    def test_the_view_shows_only_once_close(self):
        self.react('嘴甜的年轻人，不知根底')
        hidden = persona.impression(self.w, self.n)
        self.assertFalse(hidden['visible']); self.assertIsNone(hidden['text']); self.assertTrue(hidden['hint'])
        self.w.player['ties'][str(self.n['id'])]['affinity'] = 45.
        shown = persona.impression(self.w, self.n)
        self.assertEqual((shown['visible'], shown['text']), (True, '嘴甜的年轻人，不知根底'))
        self.assertFalse(reactions.valid(dict(attitudes=[dict(id='noted')], _name_guard=[]),
                                         dict(attitude='noted', reply='好。', thought='嗯，好。', impression='ok')))
