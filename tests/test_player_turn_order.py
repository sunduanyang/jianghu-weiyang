from copy import deepcopy
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from jianghu import items, martial, martial_catalog, player_interactions, player_social, protagonist, social
from jianghu.engine import World
from jianghu.store import Store


class PlayerFirstTurnTests(unittest.TestCase):
    def setUp(self):
        self.w = w = World(42, 4, durable=False)
        self.addCleanup(w.store.close)
        place = w.npcs[1]['place']
        for n in w.npcs.values():
            n.update(place=place, age_months=360, health=100., time_left=4)
            n['traits'].update(loyalty=90., compassion=90.)
        w.reindex()
        protagonist.perform(w, dict(action='create', name='先行人', age=24, sex='女', place=place, month=0, revision=0))
        self.n=w.npcs[1]

    def act(self, action, **extra):
        w=self.w;h=w.player['protagonist']
        return player_interactions.perform(w,dict(npc_id=self.n['id'],action=action,revision=h['revision'],month=w.month,**extra))

    def end(self):
        w=self.w;h=w.player['protagonist']
        return protagonist.perform(w,dict(action='end_turn',revision=h['revision'],month=w.month))

    def test_npcs_do_not_execute_until_the_player_finishes(self):
        w=self.w;before=deepcopy(w.npcs)
        for _ in range(3):
            player_interactions.view(w,1);w.summary();protagonist.view(w)
        self.assertEqual(w.npcs,before)
        self.act('praise')
        self.assertEqual(w.month,0);self.assertEqual(self.n['time_left'],4)
        self.assertEqual(self.n['monthly_actions'],[])

    def test_social_and_teaching_leave_all_four_npc_weeks_for_their_own_actions(self):
        w=self.w;n=self.n
        art=next(k for k in martial_catalog.ARTS if not items.knows(w,'player',k))
        n['martial']['learned'].append(art);martial.sync_actor(w,n)
        self.act('praise');self.act('teach',art=art)
        self.assertEqual(n['monthly_actions'],[]);self.assertEqual(n['time_left'],4)
        weeks=[];execute=w.execute
        def run(actor,action):
            if actor['id']==1:weeks.append(w.phase)
            return execute(actor,action)
        with patch.object(w,'choose',side_effect=lambda actor,actions:next(a for a in actions if a['kind']=='rest')),patch.object(w,'execute',side_effect=run):
            self.end()
        self.assertEqual(weeks,[1,2,3,4])
        old=n['previous_month_actions']
        self.assertTrue(all(a['kind']!='player_social' for a in old))
        self.assertEqual(sum(a['time_cost'] for a in old),4)
        self.assertEqual(n['time_left'],4);self.assertEqual(n['monthly_actions'],[])
        self.assertIsNone(player_social.blocker(w,n) or None)
        self.assertEqual(w.player['protagonist']['points'],30)

    def test_next_turn_begins_free_and_another_invitation_is_possible(self):
        w=self.w
        for expected in (1,2,3):
            self.act('praise')
            with patch.object(w,'choose',side_effect=lambda actor,actions:next(a for a in actions if a['kind']=='rest')):
                self.end()
            self.assertEqual(w.month,expected)
            self.assertEqual(w.player['protagonist']['npc_budget_month'],expected)
            self.assertTrue(all(n['time_left']==4 and not n['monthly_actions'] for n in w.npcs.values() if n['alive']))
            self.assertTrue(next(a for a in player_interactions.view(w,1)['actions'] if a['id']=='praise')['enabled'])

    def test_reload_does_not_refill_a_partially_booked_current_turn(self):
        w=self.w;self.act('praise')
        self.n['time_left']=2;self.n['monthly_actions']=[dict(id='rest:self',kind='rest',month=w.month,week=1,time_cost=2,event_id=self.n['last_event'])]
        before=deepcopy(w.export());w.checkpoint()
        loaded=self.reload(w)
        self.assertEqual(loaded.player,w.player)
        self.assertEqual(loaded.npcs[1]['time_left'],2)
        self.assertEqual(loaded.npcs[1]['monthly_actions'],w.npcs[1]['monthly_actions'])
        self.assertFalse(protagonist.prepare_npc_turn(loaded));self.assertEqual(w.export(),before)

    def reload(self,w):
        folder=tempfile.TemporaryDirectory();self.addCleanup(folder.cleanup)
        path=Path(folder.name)/'turn.sqlite';target=Store(path);w.store.db.backup(target.db);target.close()
        loaded=World(store=Store(path),durable=False);self.addCleanup(loaded.store.close)
        return loaded

    def test_old_exhausted_save_reopens_only_the_current_time_budget(self):
        w=self.w;n=self.n;h=w.player['protagonist'];h.pop('npc_budget_month')
        self.act('praise')
        booking=dict(id='player_social:encounter',kind='player_social',month=w.month,week=1,time_cost=1,event_id=n['last_event'])
        n['monthly_actions']=[booking]
        n['monthly_actions'] += [dict(id='work:self',kind='work',month=w.month,week=2,time_cost=2,event_id=n['last_event']),
                                 dict(id='rest:self',kind='rest',month=w.month,week=4,time_cost=1,event_id=n['last_event'])]
        n['time_left']=0
        points=h['points'];money=w.player['wealth'];rng=deepcopy(w.rng.getstate());events=w.store.count();w.checkpoint()
        loaded=self.reload(w);after=loaded.npcs[1]
        self.assertEqual(after['time_left'],4);self.assertEqual(after['monthly_actions'],[])
        self.assertEqual(loaded.month,w.month);self.assertEqual(loaded.player['wealth'],money)
        self.assertEqual(loaded.player['protagonist']['points'],points)
        self.assertEqual(loaded.rng.getstate(),rng);self.assertEqual(loaded.store.count(),events)
        for key in ('wealth','health','age_months','inventory','equipment','last_event','memories'):
            self.assertEqual(after[key],n[key])

    def test_player_can_take_more_than_four_lessons_without_spending_npc_time(self):
        w=self.w;n=self.n
        arts=[k for k in martial_catalog.ARTS if not items.knows(w,'player',k)][:5]
        n['martial']['learned'].extend(arts);martial.sync_actor(w,n)
        for art in arts:self.act('teach',art=art)
        self.assertEqual(n['time_left'],4);self.assertEqual(n['monthly_actions'],[])
        self.assertEqual(w.player['protagonist']['points'],20)
        self.assertEqual('',player_social.blocker(w,n))
        self.assertFalse(protagonist.prepare_npc_turn(w));self.assertEqual(n['time_left'],4)

    def test_old_player_reservations_are_refunded_once_without_erasing_npc_actions(self):
        w=self.w;n=self.n;h=w.player['protagonist'];h.pop('interaction_time_policy')
        own=dict(id='rest:self',kind='rest',month=w.month,week=1,time_cost=1,event_id=n['last_event'])
        booked=dict(id='player_social:self',kind='player_social',month=w.month,week=2,time_cost=1,event_id=n['last_event'])
        n.update(time_left=2,monthly_actions=[own,booked]);points=h['points'];w.checkpoint()
        loaded=self.reload(w)
        self.assertEqual(loaded.npcs[1]['time_left'],3);self.assertEqual(loaded.npcs[1]['monthly_actions'],[own])
        self.assertEqual(loaded.player['protagonist']['points'],points)
        self.assertFalse(protagonist.prepare_npc_turn(loaded));self.assertEqual(loaded.npcs[1]['time_left'],3)

    def test_non_player_simulation_keeps_its_normal_monthly_budget(self):
        w=World(33,3,durable=False);self.addCleanup(w.store.close)
        for n in w.npcs.values():n['time_left']=0
        with patch.object(w,'choose',side_effect=lambda actor,actions:next(a for a in actions if a['kind']=='rest')):
            w.month_step()
        self.assertEqual(w.month,1)
        self.assertTrue(all(n['monthly_actions'] for n in w.npcs.values() if n['alive']))
        self.assertNotIn('npc_budget_month',w.player['protagonist'])


if __name__=='__main__':unittest.main()
