from jianghu import combat_attributes as abilities
from tests.combat_helpers import set_rating, update_actor
from copy import deepcopy
import io
import json
import threading
import unittest
from unittest.mock import patch

from jianghu import ai,npc_attention,careers,items
from jianghu.engine import World


def review_for(ctx):
    actions=ctx['allowed_actions'];chosen=next((a for a in actions if a['kind']=='work'),actions[0])
    other=next((a for a in actions if a['id']!=chosen['id']),chosen)
    ids={m['event_id'] for m in ctx['memories']};event=(ctx.get('player_contact') or {}).get('last_event',0)
    return dict(focus='先稳定生计，再考虑职责与来往',
        alternatives=[dict(action_id=a['id'],benefit='能够处理眼前事务',risk='需要付出时间') for a in ([chosen,other] if other!=chosen else [chosen])],
        fallback_action_id=next((a['id'] for a in actions if a['kind']=='rest'),other['id']),fallback_reason='合作受阻时先休养，不虚构进展',
        player_stance='保留意见' if ctx.get('player_contact') else '暂无直接往来',
        player_consideration='谨慎考虑来者的意见' if ctx.get('player_contact') else '暂无直接往来',
        player_memory_id=event if event in ids else 0,memory_ids=[])


def response_for(ctx,deep=False):
    action=next((a for a in ctx['allowed_actions'] if a['kind']=='work'),ctx['allowed_actions'][0])
    result=dict(action_id=action['id'],plan=[action['id']],goal=ctx['allowed_goals'][0],reason='先顾好眼前事务',
        life_policy=dict(title='稳住家计',priorities=[dict(kind=action['kind'],weight=12)]),
        reflection=dict(event_ids=[],interpretation='暂按自己的处境安排',trait='none',direction=0,belief='none',belief_direction=0))
    if deep:result['_deep_review']=review_for(ctx)
    return result


class PriorityThinkingTests(unittest.TestCase):
    def test_direct_contact_cannot_be_denied_by_a_deep_review(self):
        from jianghu import deliberation
        w=self.world();n=w.npcs[1];w.intervene(n['id'],'talk','请先顾好自己的生计。')
        ctx=ai.context_for(w,n,npc_attention.profile(w,n));review=review_for(ctx)
        self.assertTrue(deliberation.valid(ctx,review))
        review['player_consideration']='暂无直接往来。'
        self.assertFalse(deliberation.valid(ctx,review))
        review=review_for(ctx);review['player_stance']='暂无直接往来'
        self.assertFalse(deliberation.valid(ctx,review))

    def world(self,count=8):
        w=World(population=0,compact=True,durable=False);self.addCleanup(w.store.close)
        place=w.active_places[0]['id']
        for _ in range(count):
            n=w.make_npc(place=place)
            n.update(age_months=360,wealth=40.,health=100.,job='农人',office=None,office_country=None,
                     faction=None,social_status=None,spouse=None,parents=[],children=[],guardian=None)
            n['career'].update(profession='农人',experience={'农人':20.})
            w.set_goal(n,'wealth',initial=True);n['identity']=careers.identity(w,n)
        w.reindex()
        from jianghu import local_life
        local_life.reconcile(w)
        return w

    def manager(self,limit=1):
        manager=ai.AIManager(dict(enabled=True,queue_limit=limit));self.addCleanup(manager.close);return manager

    def mock_request(self,model,ctx):
        return response_for(ctx,ctx['_thinking']['level']!='standard'),1,{},'unit-test mock'

    def test_only_real_player_contacts_are_registered_including_items_and_sparring(self):
        w=self.world(2);n=w.npcs[1]
        w.npc_detail(1);self.assertIsNone(npc_attention.contact(w,n))
        for action,text in (('talk','想听听你的近况'),('gift',''),('visit','')):w.intervene(1,action,text)
        item=items.create(w,'player','ration','测试携带');items.perform(w,'player','gift',item['id'],1)
        w.start_battle(1)
        row=npc_attention.contact(w,n);self.assertEqual(row['count'],5);self.assertEqual(row['kind'],'spar_start')
        w.emit('item_gift',[2],'另一人物送来物品',facts=dict(owner='1',recipient=2))
        self.assertIsNone(npc_attention.contact(w,w.npcs[2]))

    def test_historical_registration_changes_no_existing_state_or_history(self):
        w=self.world(2);w.intervene(1,'talk','往事留档')
        state=deepcopy(w.export());state.pop('npc_attention');events=w.store.count();w.store.save(state)
        loaded=World(store=w.store,compact=True,durable=False)
        normalize=lambda x:json.loads(json.dumps(x))
        for k,v in state.items():self.assertEqual(normalize(loaded.export()[k]),normalize(v),k)
        self.assertEqual(loaded.store.count(),events);self.assertEqual(npc_attention.contact(loaded,loaded.npcs[1])['count'],1)
        before=deepcopy(loaded.export());self.assertFalse(npc_attention.initialize(loaded));self.assertEqual(loaded.export(),before)

    def test_grade_uses_real_sect_and_social_standing_not_wallet(self):
        w=self.world(3);n=w.npcs[1];n['wealth']=1e9
        self.assertEqual(npc_attention.profile(w,n)['level'],'standard')
        n['social_status']=dict(title='国主');self.assertEqual(npc_attention.grade(w,n),1)
        n['social_status']=None;n['faction']=0;set_rating(n, 100);n['contribution']=999
        n['rank_progress']=dict(sect=dict(faction=0,grade=9,penalty=0.))
        self.assertEqual(npc_attention.grade(w,n),9)
        n['rank_progress']['sect']['grade']=3;self.assertEqual(npc_attention.profile(w,n)['level'],'deep')
        n['rank_progress']['sect']['grade']=9;w.factions[0]['leader']=n['id']
        self.assertEqual(npc_attention.grade(w,n),1)

    def test_recent_interaction_has_more_memory_and_always_retrieves_latest_personal_contact(self):
        w=self.world(2);n=w.npcs[1]
        for _ in range(12):w.emit('daily',[1],'平常劳作',.3)
        event=w.intervene(1,'talk','先照顾好自己的生计')
        profile=npc_attention.profile(w,n);ctx=ai.context_for(w,n,profile)
        self.assertEqual(profile['level'],'intensive');self.assertEqual(profile['memory_limit'],8)
        self.assertEqual(ctx['memories'][0]['event_id'],event['id'])
        self.assertEqual(ctx['player_contact']['last_event'],event['id'])
        self.assertNotIn('people',ctx['player_contact'])
        w.month+=4;self.assertEqual(npc_attention.profile(w,n)['level'],'intensive')
        w._npc_contact_clock[str(n['id'])]-=3600;old=npc_attention.profile(w,n)
        self.assertEqual(old['level'],'deep');self.assertLess(old['memory_limit'],profile['memory_limit'])

    def test_newest_contact_then_high_standing_get_slots_before_general_rotation(self):
        w=self.world(8);w.intervene(2,'talk','较早来往');w.intervene(3,'talk','最新来往');w.npcs[4]['social_status']=dict(title='国主')
        manager=self.manager();chosen=[]
        def mocked(model,ctx):chosen.append(ctx['npc_id']);return self.mock_request(model,ctx)
        with patch.object(ai.LocalModel,'request',mocked):
            for _ in range(4):manager.schedule(w);manager.jobs.join();manager.collect(w)
        self.assertEqual(chosen[:3],[3,4,2]);self.assertNotIn(chosen[3],[2,3,4])

    def test_priority_groups_leave_regular_people_real_rotation_opportunities(self):
        w=self.world(16)
        for i in (1,2,3):w.intervene(i,'talk','有过来往')
        for i in (4,5,6):w.npcs[i]['social_status']=dict(title='国主')
        manager=self.manager();seen=[]
        def mocked(model,ctx):seen.append(ctx['npc_id']);return self.mock_request(model,ctx)
        with patch.object(ai.LocalModel,'request',mocked):
            for _ in range(8):manager.schedule(w);manager.jobs.join();manager.collect(w)
        self.assertTrue(any(i>=7 for i in seen));self.assertGreaterEqual(len(set(seen)),8)

    def test_priority_contact_replaces_an_unstarted_background_job(self):
        w=self.world(5);manager=self.manager(2);gate=threading.Event();started=threading.Event()
        def mocked(model,ctx):started.set();gate.wait(4);return self.mock_request(model,ctx)
        with patch.object(ai.LocalModel,'request',mocked):
            try:
                manager.schedule(w);self.assertTrue(started.wait(2))
                event=w.intervene(3,'talk','刚刚有新的来往')
                self.assertEqual(manager.schedule(w,3),1)
                queued=list(manager.jobs.queue);self.assertEqual(len(queued),1)
                self.assertEqual(queued[0][2]['npc_id'],3);self.assertEqual(queued[0][0],0)
                self.assertEqual(queued[0][2]['player_contact']['last_event'],event['id'])
                self.assertFalse(w.npcs[2]['ai_pending'])
                gate.set();manager.jobs.join();manager.collect(w)
            finally:gate.set()

    def test_new_contact_during_inference_rejects_old_plan_and_gets_fresh_context_next(self):
        w=self.world(3);manager=self.manager();gate=threading.Event();started=threading.Event();contexts=[]
        w.intervene(1,'talk','第一条来往')
        def mocked(model,ctx):contexts.append(ctx);started.set();gate.wait(4);return self.mock_request(model,ctx)
        with patch.object(ai.LocalModel,'request',mocked):
            try:
                manager.schedule(w,1);self.assertTrue(started.wait(2))
                event=w.intervene(1,'talk','第二条来往改变了我的建议');manager.schedule(w,1)
                gate.set();manager.jobs.join();manager.collect(w)
                self.assertEqual(manager.stats['rejected'],1);self.assertIsNone(w.npcs[1].get('llm_policy'))
                manager.schedule(w);manager.jobs.join();manager.collect(w)
                self.assertEqual(contexts[-1]['player_contact']['last_event'],event['id'])
                self.assertEqual(w.npcs[1]['deep_thought']['assessment']['player_memory_id'],event['id'])
            finally:gate.set()

    def test_high_standing_can_upgrade_once_this_month_and_rethink_on_new_major_news(self):
        w=self.world(1);n=w.npcs[1];n['social_status']=dict(title='国主')
        n.update(last_review=w.month,last_review_attempt=w.month)
        manager=self.manager()
        with patch.object(ai.LocalModel,'request',lambda model,ctx:self.mock_request(model,ctx)):
            self.assertEqual(manager.schedule(w),1);manager.jobs.join();manager.collect(w)
            self.assertEqual(manager.schedule(w),0)
            w.emit('disaster',[1],'亲历的重要变化',.9)
            self.assertEqual(manager.schedule(w),1);manager.jobs.join();manager.collect(w)
        self.assertEqual(manager.stats['deep_completed'],2)

    def test_repeated_player_requests_preserve_a_reserved_general_turn(self):
        w=self.world(6);manager=self.manager(2);gates={i:threading.Event() for i in (1,2)};started={i:threading.Event() for i in (1,2)}
        def mocked(model,ctx):
            i=ctx['npc_id']
            if i in gates:started[i].set();gates[i].wait(5)
            return self.mock_request(model,ctx)
        with patch.object(ai.LocalModel,'request',mocked):
            try:
                w.intervene(1,'talk','第一人');manager.schedule(w,1);self.assertTrue(started[1].wait(2))
                w.intervene(2,'talk','第二人');manager.schedule(w,2)
                w.intervene(3,'talk','第三人');manager.schedule(w,3)
                gates[1].set();self.assertTrue(started[2].wait(2));manager.collect(w);manager.schedule(w)
                queued=list(manager.jobs.queue);self.assertEqual(queued[0][2]['npc_id'],4);self.assertEqual(queued[0][0],0)
                w.intervene(5,'talk','继续有新来往');manager.schedule(w,5)
                self.assertEqual(list(manager.jobs.queue)[0][2]['npc_id'],4)
                gates[2].set();manager.jobs.join();manager.collect(w)
            finally:
                for gate in gates.values():gate.set()

    def test_two_model_requests_compare_then_choose_only_compared_alternatives(self):
        w=self.world(2);w.intervene(1,'talk','请按自己的处境考虑');n=w.npcs[1]
        ctx=ai.context_for(w,n,npc_attention.profile(w,n));review=review_for(ctx);result=response_for(ctx);bodies=[]
        def fake(request,**kwargs):
            bodies.append(json.loads(request.data));value=review if len(bodies)==1 else result
            return io.BytesIO(json.dumps(dict(choices=[dict(message=dict(content=json.dumps(value)))],usage=dict(prompt_tokens=100,completion_tokens=20))).encode())
        before=deepcopy(w.export())
        with patch('urllib.request.urlopen',side_effect=fake):value,_,usage,_=ai.LocalModel(ai.DEFAULT_CONFIG).request(ctx)
        self.assertEqual(w.export(),before);self.assertEqual(usage['request_count'],2)
        self.assertEqual(usage['prompt_tokens'],200);self.assertEqual(usage['completion_tokens'],40)
        allowed=bodies[1]['response_format']['json_schema']['schema']['properties']['action_id']['enum']
        self.assertEqual(allowed,[r['action_id'] for r in review['alternatives']])
        wire=json.loads(bodies[0]['messages'][1]['content']);self.assertNotIn('_attention_signature',wire)
        self.assertTrue(ai.apply_decision(w,ctx,value)[0]);self.assertEqual(n['deep_thought']['rounds'],2)

    def test_foreign_memories_in_deep_assessment_are_rejected_before_final_request(self):
        w=self.world(2);w.intervene(1,'talk','有实际来往');foreign=w.emit('daily',[2],'另一个人的私事',.4)
        ctx=ai.context_for(w,w.npcs[1],npc_attention.profile(w,w.npcs[1]));review=review_for(ctx);review['memory_ids']=[foreign['id']]
        before=deepcopy(w.export())
        reply=dict(choices=[dict(message=dict(content=json.dumps(review)))])
        with patch('urllib.request.urlopen',return_value=io.BytesIO(json.dumps(reply).encode())) as call:
            with self.assertRaises(ai.ModelRequestError) as error:ai.LocalModel(ai.DEFAULT_CONFIG).request(ctx)
        self.assertEqual(error.exception.request_count,1);self.assertEqual(call.call_count,1);self.assertEqual(w.export(),before)

    def test_fallback_is_a_real_action_and_does_not_falsely_complete_the_main_step(self):
        w=self.world(2);n,q=w.npcs.values();w.link(n,q,20,20);w.intervene(1,'talk','先安排好自己的日常')
        ctx=ai.context_for(w,n,npc_attention.profile(w,n));result=response_for(ctx,True)
        primary=next(a for a in ctx['allowed_actions'] if a['id']=='befriend:2')
        work=next(a for a in ctx['allowed_actions'] if a['kind']=='work')
        result.update(action_id=primary['id'],plan=[primary['id']])
        result['_deep_review']['alternatives']=[dict(action_id=a['id'],benefit='处理眼前事务',risk='需要时间') for a in (primary,work)]
        self.assertTrue(ai.apply_decision(w,ctx,result)[0]);q['time_left']=0
        action=w.choose(n,w.legal_actions(n));self.assertEqual(action['kind'],'rest')
        event=w.execute(n,action);self.assertIsNotNone(event);self.assertEqual(n['time_left'],3)
        self.assertEqual(n['plan']['steps'],['befriend:2']);self.assertTrue(n['plan']['contingency_active'])
        self.assertEqual(n['plan']['completed'][-1]['id'],'rest:self')


if __name__=='__main__':unittest.main()
