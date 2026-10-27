from copy import deepcopy
import json
from pathlib import Path
import tempfile
import threading
import time
import unittest
from unittest.mock import patch

from jianghu import ai,llm_policy,npc_plans,protagonist,server
from jianghu.engine import World
from jianghu.store import Store


def proposal(ctx,kind='work'):
    action=next((a for a in ctx['allowed_actions'] if a['kind']==kind),ctx['allowed_actions'][0])
    return dict(action_id=action['id'],plan=[action['id']],goal=ctx['allowed_goals'][0],reason='先维持家计，再顾及乡里',
        life_policy=dict(title='先稳住家计与生计',priorities=[dict(kind=action['kind'],weight=18)]),
        reflection=dict(event_ids=[],interpretation='暂按眼前处境安排',trait='none',direction=0,belief='none',belief_direction=0))


class LLMAgencyTests(unittest.TestCase):
    def world(self,count=12):
        w=World(population=count,compact=True,durable=False)
        self.addCleanup(w.store.close)
        for n in w.npcs.values():n.update(age_months=300,health=100.,wealth=40.)
        return w

    def test_policy_is_model_authored_persistent_and_does_not_change_assets(self):
        w=self.world(1);n=w.npcs[1];ctx=ai.context_for(w,n);before=deepcopy(w.export());result=proposal(ctx)
        self.assertTrue(ai.apply_decision(w,ctx,result)[0])
        self.assertEqual(n['llm_policy']['title'],result['life_policy']['title'])
        self.assertEqual(n['wealth'],before['npcs'][1]['wealth']);self.assertEqual(w.month,before['month'])
        for key in ('economy','items','governments','city_positions','random_state'):
            self.assertEqual(w.export()[key],before[key],key)
        w.checkpoint();again=World(store=w.store,compact=True,durable=False)
        self.assertEqual(again.npcs[1]['llm_policy'],n['llm_policy'])

    def test_invalid_policy_rejects_the_entire_proposal_before_any_change(self):
        w=self.world(1);n=w.npcs[1];ctx=ai.context_for(w,n)
        for priorities in ([dict(kind='mint_silver',weight=18)], [dict(kind='work',weight=100)],
                           [dict(kind='work',weight=True)], [dict(kind='work',weight=18)]*2):
            result=proposal(ctx);result['life_policy']['priorities']=priorities;before=deepcopy(w.export())
            self.assertFalse(ai.apply_decision(w,ctx,result)[0]);self.assertEqual(w.export(),before)

    def test_policy_has_expiry_and_invalidates_when_real_circumstances_change(self):
        w=self.world(1);n=w.npcs[1];ctx=ai.context_for(w,n);ai.apply_decision(w,ctx,proposal(ctx))
        self.assertIsNotNone(llm_policy.current(w,n));job=n['job'];n['job']='不同营生'
        self.assertIsNone(llm_policy.current(w,n));n['job']=job
        w.month=n['llm_policy']['until'];self.assertIsNone(llm_policy.current(w,n))

    def test_restart_recovers_a_thought_interrupted_before_any_result_arrived(self):
        w=self.world(1);n=w.npcs[1];n.update(ai_pending=True,last_review_attempt=w.month)
        w.checkpoint();again=World(store=w.store,compact=True,durable=False)
        self.assertFalse(again.npcs[1]['ai_pending'])
        self.assertLess(again.npcs[1]['last_review_attempt'],again.month)

    def test_changed_location_invalidates_an_inflight_proposal(self):
        w=self.world(1);n=w.npcs[1];ctx=ai.context_for(w,n);n['place']=next(p['id'] for p in w.active_places if p['id']!=n['place'])
        before=deepcopy(w.export());self.assertFalse(ai.apply_decision(w,ctx,proposal(ctx))[0]);self.assertEqual(w.export(),before)

    def test_context_contains_own_home_work_and_policy_but_no_other_private_addresses(self):
        w=self.world();n=w.npcs[1];ctx=ai.context_for(w,n)
        self.assertTrue(ctx['settlement_life']['enabled']);self.assertIn('work',ctx['settlement_life'])
        self.assertNotIn('people',ctx['settlement_life']);self.assertNotIn('npcs',ctx)
        self.assertEqual(ctx['situation']['place'],n['place'])

    def test_policy_changes_daily_ranking_after_short_plan_finishes_and_is_audited(self):
        w=self.world(1);n=w.npcs[1];ctx=ai.context_for(w,n);ai.apply_decision(w,ctx,proposal(ctx))
        n['plan']=None;n['office']=None;n['faction']=None;n['health']=100.;n['wealth']=40.
        # Freeze equal baseline utilities, so the model-authored preference is decisive.
        actions=[dict(id='work:self',kind='work',target=None,label='谋生'),dict(id='rest:self',kind='rest',target=None,label='休养')]
        def score(actor,action):return llm_policy.adjustment(w,actor,action)
        with patch.object(w,'score',side_effect=score),patch.object(w.rng,'uniform',return_value=0),patch.object(npc_plans,'begin'):
            selected=w.choose(n,actions)
        self.assertEqual(selected['kind'],'work');self.assertTrue(n['decision']['policy_influence'])
        self.assertEqual(n['choice_source'],'规则')

    def test_emergency_can_pause_a_model_plan_without_erasing_its_policy(self):
        w=self.world(1);n=w.npcs[1];ctx=ai.context_for(w,n);ai.apply_decision(w,ctx,proposal(ctx));policy=deepcopy(n['llm_policy'])
        n['health']=20.;choice=w.choose(n,w.legal_actions(n));self.assertIn(choice['kind'],('rest','treatment','use_item'))
        self.assertEqual(n['llm_policy'],policy);self.assertNotIn('policy_influence',n['decision'])

    def test_a_manually_executed_different_action_is_not_counted_as_model_influence(self):
        w=self.world(1);n=w.npcs[1]
        n['decision']=dict(action_id='work:self',month=w.month,policy_influence=True,policy_ranking_changed=True)
        event=w.execute(n,dict(id='rest:self'));self.assertIsNotNone(event)
        self.assertEqual(w.metrics.get('ai_policy_actions',0),0)
        self.assertEqual(w.metrics.get('ai_policy_ranking_changes',0),0)

    def manager(self,config):
        manager=ai.AIManager(config);self.addCleanup(manager.close);return manager

    def test_background_keeps_thinking_past_64_people_without_advancing_the_month(self):
        w=self.world(90)
        def mocked(model,ctx):return proposal(ctx),1,{},'unit-test mock'
        with patch.object(ai.LocalModel,'request',mocked):
            manager=self.manager(dict(enabled=True,reviews_per_month=1,queue_limit=3))
            counts=[]
            for _ in range(25):
                counts.append(manager.schedule(w));manager.jobs.join();manager.collect(w)
                self.assertLessEqual(len(manager.pending),3)
            self.assertEqual(sum(counts),75);self.assertEqual(manager.stats['calls'],75)
            self.assertEqual(w.month,0)
            self.assertEqual(sum(n['last_ai_review']==w.month for n in w.npcs.values()),75)
            self.assertTrue(all(n.get('llm_policy') for n in w.npcs.values() if n['last_ai_review']==w.month))
            self.assertNotIn('reviews_per_month',manager.config)

    def test_old_month_snapshots_are_refreshed_without_canceling_the_inflight_person(self):
        w=self.world(8);gate=threading.Event();started=threading.Event()
        def mocked(model,ctx):started.set();gate.wait(4);return proposal(ctx),1,{},'unit-test mock'
        with patch.object(ai.LocalModel,'request',mocked):
            manager=self.manager(dict(enabled=True,queue_limit=2))
            try:
                self.assertEqual(manager.schedule(w),2);self.assertTrue(started.wait(2))
                w.month+=1;self.assertEqual(manager.refresh_queue(w),1)
                self.assertEqual(len(manager.pending),1)
                self.assertEqual(manager.schedule(w),1)
                self.assertTrue(all(item[2]['month']==w.month for item in list(manager.jobs.queue)))
                gate.set();manager.jobs.join();manager.collect(w)
                self.assertFalse(manager.pending)
            finally:gate.set()

    def test_obsolete_month_limits_and_wait_preferences_are_ignored(self):
        config=ai.continuous_config(dict(enabled=True,reviews_per_month=1,turn_wait_seconds=120,turn_min_decisions=16))
        self.assertEqual(config['config_version'],6)
        self.assertTrue(config['enabled'])
        for key in ('reviews_per_month','turn_wait_seconds','turn_min_decisions'):self.assertNotIn(key,config)

    def test_disabling_model_does_not_apply_late_responses(self):
        w=self.world(1);manager=self.manager(dict(enabled=True,reviews_per_month=1));gate=threading.Event()
        def mocked(model,ctx):gate.wait(2);return proposal(ctx),1,{},'unit-test mock'
        with patch.object(ai.LocalModel,'request',mocked):
            manager.schedule(w);manager.config['enabled']=False;gate.set();manager.jobs.join();manager.collect(w)
        self.assertIsNone(w.npcs[1].get('llm_policy'));self.assertFalse(manager.pending)

    def test_legacy_wait_check_never_holds_up_month_progression(self):
        manager=type('Manager',(),dict(config=dict(enabled=True),submitted_months={1:0}))()
        self.assertFalse(server.game_ai_wait(manager,1));self.assertFalse(server.game_ai_wait(manager,100))

    def test_normal_end_turn_can_defer_without_spending_points_or_advancing_twice(self):
        w=self.world();h=w.player['protagonist'];h.update(created=True,started_month=w.month)
        before=deepcopy(w.export());called=[]
        body=dict(action='end_turn',month=w.month,revision=h['revision'])
        result=protagonist.perform(w,body,lambda:called.append('queued') or protagonist.view(w))
        self.assertEqual(called,['queued']);self.assertEqual(w.export(),before)
        self.assertEqual(result['points'],h['points'])

    def test_real_server_advances_even_while_model_request_is_blocked(self):
        gate=threading.Event();folder=tempfile.TemporaryDirectory();self.addCleanup(folder.cleanup)
        root=Path(folder.name);(root/'data').mkdir();path=root/'world.sqlite'
        w=World(population=4,compact=True,store=Store(path),durable=False)
        w.player['protagonist'].update(created=True,started_month=w.month);w.checkpoint();w.store.close()
        (root/'data/ai_config.json').write_text(json.dumps(dict(enabled=True,config_version=4,reviews_per_month=2,queue_limit=2,turn_wait_seconds=10,turn_min_decisions=1)),encoding='utf-8')
        def mocked(model,ctx):gate.wait(5);return proposal(ctx),1,{},'unit-test mock'
        game=None
        with patch.object(server,'ROOT',root),patch.object(ai.LocalModel,'request',mocked):
            try:
                game=server.Game(path);deadline=time.monotonic()+2
                while not game.ai.pending and time.monotonic()<deadline:time.sleep(.01)
                with game.lock:
                    h=game.world.player['protagonist'];month=game.world.month
                    protagonist.perform(game.world,dict(action='end_turn',month=month,revision=h['revision']),game.defer_hero_turn)
                    self.assertEqual(game.world.month,month);self.assertEqual(game.job['remaining'],1)
                deadline=time.monotonic()+3
                while game.world.month==month and time.monotonic()<deadline:time.sleep(.01)
                with game.lock:
                    self.assertEqual(game.world.month,month+1);self.assertEqual(game.job['remaining'],0)
                    self.assertFalse(game.job.get('thinking',False));self.assertEqual(game.ai.stats['successes'],0)
                    events=[e for e in game.world.store.history(limit=100) if e['kind']=='hero_end_turn']
                    self.assertEqual(len(events),1)
                gate.set();deadline=time.monotonic()+3
                while game.ai.stats['successes']==0 and time.monotonic()<deadline:time.sleep(.01)
                self.assertGreater(game.ai.stats['successes'],0)
            finally:
                gate.set()
                if game:
                    game.closed=True;game.thread.join(3);game.ai.close();game.world.store.close()


if __name__=='__main__':unittest.main()
