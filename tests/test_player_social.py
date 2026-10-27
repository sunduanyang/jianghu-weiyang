from copy import deepcopy
import json
import threading
import unittest
from unittest.mock import patch

from jianghu import ai, careers, npc_attention, player_social, protagonist
from jianghu.engine import World


def decision(ctx, choice='chat', reply=None):
    if reply is None:reply='这事我眼下不愿答应。' if choice=='refuse' else '此事容我再想想。' if choice=='defer' else '我听见了，咱们慢慢说。'
    return dict(choice_id=choice, reason='按眼前处境与已有交情作此决定',
                memory_ids=[ctx['request']['event_id']], reply=reply)


class PlayerSocialTests(unittest.TestCase):
    def world(self, count=3):
        w = World(population=0, compact=True, durable=False)
        self.addCleanup(w.store.close)
        place = w.active_places[0]['id']
        for _ in range(count):
            n = w.make_npc(place=place)
            n.update(age_months=360, wealth=40., health=100., faction=None, office=None,
                     job='农人', children=[], spouse=None, guardian=None, social_status=None)
            n['career'].update(profession='农人', experience={'农人':20.})
            w.set_goal(n,'wealth',initial=True)
            n['identity'] = careers.identity(w,n)
        w.reindex()
        protagonist.perform(w, dict(action='create', name='沈行舟', sex='男', age=24, place=place,
                                    month=w.month, revision=0))
        return w

    def start(self,w,action='talk',text='',npc=1):
        return player_social.start(w,npc,action,text,player_social.state(w)['revision'])

    def apply(self,w,action,choice,text=''):
        ctx=self.start(w,action,text)
        self.assertTrue(player_social.apply(w,ctx,decision(ctx,choice))[0])
        return ctx

    def test_readonly_views_and_old_world_need_no_migration(self):
        w=self.world();before=deepcopy(w.export());events=w.store.count()
        for _ in range(2):w.npc_detail(1);player_social.view(w,1)
        self.assertEqual(w.export(),before);self.assertEqual(w.store.count(),events)
        self.assertNotIn('social',w.player)

    def test_model_can_refuse_despite_sufficient_silver_and_no_transfer_occurs(self):
        w=self.world();money=w.player['wealth'];npc_money=w.npcs[1]['wealth']
        ctx=self.apply(w,'ask_aid','refuse')
        self.assertIn('give_aid',[c['id'] for c in ctx['choices']])
        self.assertEqual(w.player['wealth'],money);self.assertEqual(w.npcs[1]['wealth'],npc_money)
        self.assertEqual(player_social.state(w)['favors'],[])

    def test_real_aid_is_conserved_bounded_idempotent_and_reciprocated(self):
        w=self.world();total=w.player['wealth']+w.npcs[1]['wealth'];money=w.player['wealth']
        ctx=self.apply(w,'ask_aid','give_aid')
        self.assertEqual(w.player['wealth'],money+4)
        self.assertEqual(w.player['wealth']+w.npcs[1]['wealth'],total)
        before=deepcopy(w.export());self.assertFalse(player_social.apply(w,ctx,decision(ctx,'give_aid'))[0])
        self.assertEqual(w.export(),before)
        next_ctx=self.start(w,'ask_aid','')
        self.assertNotIn('give_aid',[c['id'] for c in next_ctx['choices']])
        self.assertTrue(player_social.apply(w,next_ctx,decision(next_ctx,'refuse'))[0])
        self.apply(w,'help','receive_aid')
        self.assertEqual(w.player['wealth'],money)
        self.assertEqual(player_social.state(w)['favors'][0]['status'],'returned')

    def test_family_reserve_and_age_prevent_unsupported_aid(self):
        w=self.world();n=w.npcs[1];n['wealth']=18.;n['children']=[2];w.npcs[2]['age_months']=60
        ctx=self.start(w,'ask_aid','')
        self.assertNotIn('give_aid',[c['id'] for c in ctx['choices']])
        before=w.player['wealth'];self.assertFalse(player_social.apply(w,ctx,decision(ctx,'give_aid'))[0])
        self.assertEqual(w.player['wealth'],before);self.assertEqual(n['wealth'],18.)

    def test_generated_words_cannot_add_unsupported_game_effects(self):
        w=self.world();ctx=self.start(w);result=decision(ctx)
        result['silver']=999;before=w.player['wealth']
        self.assertFalse(player_social.apply(w,ctx,result)[0]);self.assertEqual(w.player['wealth'],before)
        ctx=self.start(w);result=decision(ctx);result['memory_ids']=[999999]
        self.assertFalse(player_social.apply(w,ctx,result)[0])

    def test_current_request_must_be_in_model_memory_citations(self):
        w=self.world();ctx=self.start(w)
        result=decision(ctx);result.pop('reply');result['memory_ids']=[]
        self.assertFalse(player_social.valid_decision(ctx,result))
        self.assertEqual(ctx['memories'][0]['event_id'],ctx['request']['event_id'])
        self.assertEqual(npc_attention.profile(w,w.npcs[1])['level'],'intensive')

    def test_spoken_money_must_match_real_outcome_before_any_transfer(self):
        w=self.world();ctx=self.start(w,'ask_aid','')
        for text in ('给你三两银子。','我给你３两。','送你九十九银。'):
            result=decision(ctx,'give_aid',text)
            self.assertFalse(player_social.valid_reply(ctx,result,dict(reply=text)))
        for text in ('这四两银子给你。','我给你4银。','路上多保重。'):
            result=decision(ctx,'give_aid',text)
            self.assertTrue(player_social.valid_reply(ctx,result,dict(reply=text)))
        money=w.player['wealth'];points=w.player['protagonist']['points']
        self.assertFalse(player_social.apply(w,ctx,decision(ctx,'give_aid','这三两银子你收下。'))[0])
        self.assertEqual(w.player['wealth'],money);self.assertEqual(w.player['protagonist']['points'],points)

    def test_inconsistent_speech_is_rewritten_once_and_real_calls_are_counted(self):
        w=self.world();ctx=self.start(w,'ask_aid','');calls=[]
        def complete(model,messages,schema,budget,stage):
            calls.append(stage)
            if len(calls)==1:
                result=decision(ctx,'give_aid');result.pop('reply')
            elif len(calls)==2:result=dict(reply='给你三两银子。')
            else:result=dict(reply='这四两银子你收下，路上多保重。')
            return result,json.dumps(result),dict(prompt_tokens=10,completion_tokens=12),1.
        with patch.object(ai.LocalModel,'complete',complete):
            result,_,usage,_=ai.LocalModel(ai.continuous_config()).interact(ctx)
        self.assertEqual(usage['request_count'],3);self.assertEqual(usage['completion_tokens'],36)
        self.assertTrue(player_social.apply(w,ctx,result)[0])

    def test_snapshot_changes_invalidate_without_charging_either_side(self):
        for mutation in ('month','wealth','move','relationship','health'):
            with self.subTest(mutation=mutation):
                w=self.world();ctx=self.start(w,'ask_aid','')
                if mutation=='month':w.month+=1
                elif mutation=='wealth':w.npcs[1]['wealth']=10.
                elif mutation=='move':w.player['protagonist']['place']+=1
                elif mutation=='health':w.npcs[1]['health']-=5
                else:w.player['ties']['1']=dict(affinity=-30,trust=-40)
                points=w.player['protagonist']['points'];money=w.player['wealth'];npc=w.npcs[1]['wealth']
                self.assertFalse(player_social.apply(w,ctx,decision(ctx,'give_aid'))[0])
                self.assertEqual(w.player['wealth'],money);self.assertEqual(w.npcs[1]['wealth'],npc)
                self.assertEqual(w.player['protagonist']['points'],points)

    def test_multiturn_memory_monthly_relation_limit_and_shared_time_slot(self):
        w=self.world();n=w.npcs[1];points=w.player['protagonist']['points'];time=n['time_left']
        self.apply(w,'talk','warm','')
        ctx=self.start(w,'talk','')
        self.assertEqual(ctx['history'][-1]['player'],player_social.DEFAULT_TEXT['talk'])
        self.assertTrue(player_social.apply(w,ctx,decision(ctx,'warm'))[0])
        self.assertEqual(n['time_left'],time-1)
        self.assertEqual(w.player['protagonist']['points'],points-2)
        self.assertEqual(player_social.tie(w,1)['affinity'],2.)
        self.assertEqual(player_social.tie(w,1)['trust'],1.)
        self.assertEqual(len([a for a in n['monthly_actions'] if a['kind']=='player_social']),1)

    def test_introduction_uses_observed_contacts_and_does_not_force_friendship(self):
        w=self.world();n=w.npcs[1];q=w.npcs[2];w.link(n,q,30,10)
        seen=deepcopy(n['knowledge']['2']);q['wealth']=999.;q['place']+=1
        ctx=self.start(w,'introduce','')
        option=next(c for c in ctx['choices'] if c['id']=='introduce:2')
        self.assertEqual(option['person']['place'],seen['place'])
        self.assertNotIn('wealth',option['person'])
        self.assertTrue(player_social.apply(w,ctx,decision(ctx,'introduce:2'))[0])
        self.assertNotIn('2',w.player['ties'])
        self.assertEqual(player_social.state(w)['introductions'][0]['target'],2)
        w.player['protagonist']['place']=q['place']
        ctx=self.start(w,'talk','',2)
        self.assertEqual(ctx['presented_introductions'][0]['by'],1)

    def test_sworn_and_reconciliation_require_real_relationships(self):
        w=self.world();ctx=self.start(w,'swear','')
        self.assertNotIn('swear',[c['id'] for c in ctx['choices']])
        player_social.apply(w,ctx,decision(ctx,'refuse'))
        w=self.world()
        for _ in range(2):w.intervene(1,'talk','往来已久')
        w.player['ties']['1'].update(affinity=60,trust=40)
        self.apply(w,'swear','swear');self.assertTrue(player_social.tie(w,1)['sworn'])
        w.player['ties']['1'].update(affinity=-60,trust=-50)
        self.apply(w,'reconcile','reconcile')
        self.assertLess(player_social.tie(w,1)['affinity'],0)
        self.assertTrue(player_social.tie(w,1)['sworn'])

    def test_npc_can_request_help_without_receiving_money_until_player_agrees(self):
        w=self.world();w.npcs[1]['wealth']=5.;before=w.player['wealth']
        self.apply(w,'talk','seek_help','')
        self.assertEqual(w.npcs[1]['wealth'],5.);self.assertEqual(w.player['wealth'],before)
        self.apply(w,'help','receive_aid');self.assertEqual(w.npcs[1]['wealth'],9.)
        self.assertEqual(w.player['wealth'],before-4)

    def test_saved_dialogue_and_pending_restart_do_not_repeat_transfers(self):
        w=self.world();self.apply(w,'ask_aid','give_aid')
        self.start(w,'talk','');w.checkpoint();money=w.player['wealth']
        loaded=World(store=w.store,compact=True,durable=False)
        self.assertTrue(player_social.recover(loaded));self.assertFalse(player_social.recover(loaded))
        self.assertEqual(loaded.player['wealth'],money)
        rows=player_social.view(loaded,1)['requests']
        self.assertEqual(rows[-1]['status'],'failed');self.assertEqual(rows[-2]['status'],'completed')

    def test_duplicate_submission_disabled_model_and_absence_are_rejected(self):
        w=self.world();manager=ai.AIManager(dict(enabled=False));self.addCleanup(manager.close)
        before=deepcopy(w.export())
        with self.assertRaises(ValueError):manager.interact(w,1,'talk','你好',0)
        self.assertEqual(w.export(),before)
        self.start(w)
        with self.assertRaises(ValueError):self.start(w)
        with self.assertRaises(ValueError):player_social.start(w,1,'talk','你好',0)

    def test_two_model_requests_decide_first_then_generate_grounded_speech(self):
        w=self.world();ctx=self.start(w,'ask_aid','');calls=[]
        def complete(model,messages,schema,budget,stage):
            calls.append((messages,schema,stage))
            if len(calls)==1:
                result=decision(ctx,'defer');result.pop('reply')
            else:result=dict(reply='我也得留着家里的口粮，眼下帮不上你。')
            return result,json.dumps(result),dict(prompt_tokens=10,completion_tokens=12),1.
        with patch.object(ai.LocalModel,'complete',complete):
            result,_,usage,_=ai.LocalModel(ai.continuous_config()).interact(ctx)
        self.assertEqual(usage['request_count'],2);self.assertEqual(usage['completion_tokens'],24)
        self.assertEqual(json.loads(calls[1][0][1]['content'])['settlement']['id'],'defer')
        self.assertTrue(player_social.apply(w,ctx,result)[0])

    def test_priority_queue_collect_logs_and_disable_inflight_are_safe(self):
        w=self.world();manager=ai.AIManager(dict(enabled=True,queue_limit=1));self.addCleanup(manager.close)
        gate=threading.Event();started=threading.Event()
        def mocked(model,ctx):
            started.set();gate.wait(4)
            return decision(ctx,'give_aid'),1.,dict(request_count=2), 'mock conversation'
        with patch.object(ai.LocalModel,'interact',mocked):
            manager.interact(w,1,'ask_aid','',0);self.assertTrue(started.wait(2))
            manager.config['enabled']=False;money=w.player['wealth'];gate.set();manager.jobs.join();manager.collect(w)
        self.assertEqual(w.player['wealth'],money);self.assertFalse(manager.dialogue_pending)
        self.assertEqual(player_social.view(w,1)['requests'][-1]['status'],'failed')
        log=json.loads(w.store.db.execute('SELECT data FROM ai_log ORDER BY id DESC LIMIT 1').fetchone()[0])
        self.assertEqual(log['task'],'player_interaction');self.assertFalse(log['applied'])


if __name__=='__main__':unittest.main()
