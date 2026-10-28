from copy import deepcopy
import unittest

from jianghu import player_interactions, player_social
import test_player_social as base
import test_persuasion as persuasion


class IntegratedConversationTests(unittest.TestCase):
    def world(self):
        w=base.PlayerSocialTests().world();self.addCleanup(w.store.close);return w

    def refuse(self,w):
        ctx=player_social.start(w,1,'ask_aid','',player_social.state(w)['revision'])
        result=base.decision(ctx,'refuse');result['persuasion_options']=persuasion.five_options()
        self.assertTrue(player_social.apply(w,ctx,result)[0]);return player_social.request_row(w,ctx)

    def test_unified_view_preserves_six_conversation_topics_and_twelve_direct_actions(self):
        w=self.world();before=deepcopy(w.export());events=w.store.count()
        v=player_interactions.view(w,1)
        self.assertEqual(len(v['actions']),12)
        self.assertEqual({r['action'] for r in v['conversations']},set(player_social.ACTIONS))
        self.assertEqual([r['label'] for r in v['conversations'] if r['listed'] and r['category']==0],['闲叙近况','赔礼和解'])
        self.assertEqual([r['label'] for r in v['conversations'] if r['listed'] and r['category']==5],['求助盘缠','请其引荐','接济或回报'])
        self.assertEqual(w.export(),before);self.assertEqual(w.store.count(),events)

    def test_unified_refusal_exposes_five_styles_without_private_model_assessment(self):
        w=self.world();self.refuse(w)
        row=player_interactions.view(w,1)['social']['requests'][-1]
        self.assertEqual([r['label'] for r in row['persuasion_options']],list(player_social.STYLES.values()))
        self.assertTrue(row['can_persuade'])
        for private in ('decision_context','decision','fingerprint','options_fingerprint'):self.assertNotIn(private,row)

    def test_persuasion_from_unified_view_keeps_actual_cost_and_npc_independence(self):
        w=self.world();n=w.npcs[1];n['time_left']=0
        n['monthly_actions']=[dict(id='work:self',kind='work',week=1,time_cost=4,month=w.month)]
        before=deepcopy(n['monthly_actions']);row=self.refuse(w)
        data=player_interactions.view(w,1);points=w.player['protagonist']['points'];money=w.player['wealth']
        ctx=player_social.start_persuasion(w,1,row['id'],'kind',data['social']['revision'])
        answer=base.decision(ctx,'give_aid');answer['memory_ids'].append(ctx['reconsideration']['original_request']['event_id'])
        self.assertTrue(player_social.apply(w,ctx,answer)[0])
        v=player_interactions.view(w,1)
        self.assertEqual(v['social']['requests'][-1]['persuasion_label'],'仁善')
        self.assertEqual(w.player['protagonist']['points'],points-1);self.assertEqual(w.player['wealth'],money+4)
        self.assertEqual(n['time_left'],0);self.assertEqual(n['monthly_actions'],before)
        self.assertFalse(v['social']['requests'][0]['can_persuade'])

    def test_unified_view_does_not_copy_one_persons_history_to_another(self):
        w=self.world();self.refuse(w);other=w.make_npc(place=w.npcs[1]['place']);w.reindex()
        self.assertEqual(player_interactions.view(w,other['id'])['social']['requests'],[])
        w.player['protagonist']['points']=0
        self.assertTrue(all(not r['enabled'] and '行动力' in r['reason'] for r in player_interactions.view(w,1)['conversations']))
