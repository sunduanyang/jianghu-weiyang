"""模型参与度: a pure summary of what the model decided and voiced."""
import unittest

from jianghu import ai, npc_attention
from jianghu.engine import World


class ParticipationTests(unittest.TestCase):
    def test_counts_model_undertakings_contacts_and_reactions_without_changing_anything(self):
        w = World(91, 0, compact=True, durable=False)
        self.addCleanup(w.store.close)
        n = w.make_npc(place=w.active_places[0]['id']); n['age_months'] = 360; w.reindex()
        npc_attention.initialize(w)
        before = ai.participation(w)
        self.assertEqual((before['model_undertakings'], before['contacted_people']), (0, 0))
        n['plan'] = dict(source='AI', intent='courtship', target=None)
        w.npc_attention['contacts'][str(n['id'])] = dict(count=1, events=[], last_event=1, last_month=0)
        w.metrics['player_reactions'] = 3
        snapshot = w.export()
        after = ai.participation(w)
        self.assertEqual(after['model_undertakings'], 1)
        self.assertEqual((after['contacted_people'], after['contacted_model_led']), (1, 1))
        self.assertEqual(after['reactions_completed'], 3)
        self.assertEqual(w.export(), snapshot)

    def test_a_job_without_a_self_section_does_not_stop_the_worker(self):
        from unittest.mock import patch
        from jianghu import reactions
        w = World(91, 0, compact=True, durable=False); self.addCleanup(w.store.close)
        manager = ai.AIManager(dict(enabled=True)); self.addCleanup(manager.close)
        ctx = dict(_reaction=True, _row=1, npc_id=1, month=0)
        with patch.object(reactions, 'model_request', lambda model, c: (None, 1, {}, '')):
            manager.jobs.put_nowait((0, 1, ctx, manager.config.copy()))
            manager.jobs.join()
        self.assertTrue(manager.thread.is_alive())


if __name__ == '__main__':
    unittest.main()
