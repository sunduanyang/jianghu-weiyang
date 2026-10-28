from copy import deepcopy
import unittest

from jianghu import items, martial, martial_catalog, player_interactions, player_social, protagonist
from jianghu.engine import World
from tests.test_player_social import decision


class InteractionActionPointTests(unittest.TestCase):
    def setUp(self):
        self.w=w=World(42,0,durable=False);self.addCleanup(w.store.close)
        protagonist.perform(w,dict(action='create',name='往来人',sex='女',age=24,place=45,month=0,revision=0))
        self.n=n=w.make_npc(place=45)
        n.update(age_months=360,health=100.,wealth=100.,time_left=0)
        n['traits'].update(loyalty=80.,compassion=80.,sociability=80.)
        n['mood'].update(anger=0.,joy=0.)
        n['monthly_actions']=[dict(id='work:self',kind='work',month=0,week=1,time_cost=4,event_id=n['last_event'])]
        w.set_goal(n,'mastery',initial=True);w.reindex()

    def act(self,action,**extra):
        h=self.w.player['protagonist']
        return player_interactions.perform(self.w,dict(action=action,npc_id=self.n['id'],month=self.w.month,revision=h['revision'],**extra))

    def model(self,action,choice):
        ctx=player_social.start(self.w,self.n['id'],action,'',player_social.state(self.w)['revision'])
        self.assertTrue(player_social.apply(self.w,ctx,decision(ctx,choice))[0])
        return ctx

    def test_legacy_monthly_flags_and_busy_npc_do_not_limit_ten_praises(self):
        w=self.w;n=self.n;before=deepcopy(n['monthly_actions']);points=w.player['protagonist']['points']
        w.player['ties'][str(n['id'])]=dict(affinity=0.,trust=0.,social_slot_month=0,encounter_months=dict(praise=0,insult=0))
        for _ in range(10):self.act('praise')
        self.assertEqual(w.player['protagonist']['points'],points-10)
        self.assertEqual(n['time_left'],0);self.assertEqual(n['monthly_actions'],before)
        self.assertEqual(w.player['ties'][str(n['id'])]['affinity'],40.)
        self.assertTrue(next(a for a in player_interactions.view(w,n['id'])['actions'] if a['id']=='praise')['enabled'])

    def test_player_can_spend_all_thirty_ap_and_then_is_blocked_without_side_effects(self):
        w=self.w
        for _ in range(30):self.act('praise')
        self.assertEqual(w.player['protagonist']['points'],0)
        before=deepcopy(w.export());events=w.store.count()
        with self.assertRaisesRegex(ValueError,'行动力'):self.act('praise')
        self.assertEqual(w.export(),before);self.assertEqual(w.store.count(),events)
        self.assertEqual(self.n['time_left'],0)

    def test_six_same_art_lessons_ignore_old_cooldowns_and_do_not_use_teacher_time(self):
        w=self.w;n=self.n;before=deepcopy(n['monthly_actions'])
        key=next(k for k in martial_catalog.ARTS if not items.knows(w,'player',k))
        n['martial']['learned'].append(key);martial.sync_actor(w,n)
        w.player['martial'].setdefault('requests',{})[f'{n["id"]}:{key}']=w.month
        for _ in range(6):self.act('teach',art=key)
        self.assertEqual(sum(items.spec(i).get('art')==key for i in items.objects(w,'player')),6)
        self.assertEqual(w.player['protagonist']['points'],18)
        self.assertEqual(n['time_left'],0);self.assertEqual(n['monthly_actions'],before)

    def test_refused_relationship_requests_can_be_repeated_with_another_ap(self):
        w=self.w;n=self.n;n['mood']['anger']=80
        w.player['ties'][str(n['id'])]=dict(affinity=60.,trust=40.,encounter_months=dict(confide=0))
        for _ in range(3):self.assertFalse(self.act('confide')['event']['facts']['accepted'])
        self.assertEqual(w.player['protagonist']['points'],27);self.assertEqual(n['time_left'],0)

    def test_model_aid_and_refused_presets_can_repeat_without_npc_week_cost(self):
        w=self.w;n=self.n;money=w.player['wealth'];total=money+n['wealth'];schedule=deepcopy(n['monthly_actions'])
        for _ in range(3):self.model('ask_aid','give_aid')
        for _ in range(2):self.model('ask_aid','refuse')
        self.assertEqual(w.player['wealth'],money+12);self.assertAlmostEqual(w.player['wealth']+n['wealth'],total)
        self.assertEqual(w.player['protagonist']['points'],25)
        self.assertEqual(n['time_left'],0);self.assertEqual(n['monthly_actions'],schedule)
        self.assertTrue(next(o for o in player_social.view(w,n['id'])['options'] if o['id']=='ask_aid')['enabled'])

    def test_unrelated_npc_schedule_change_does_not_invalidate_a_waiting_reply(self):
        w=self.w;n=self.n
        ctx=player_social.start(w,n['id'],'talk','',0)
        n['time_left']=4;n['monthly_actions']=[]
        self.assertTrue(player_social.apply(w,ctx,decision(ctx,'warm'))[0])
        self.assertEqual(n['time_left'],4);self.assertEqual(n['monthly_actions'],[])

    def test_legacy_visit_and_spar_entry_points_also_repeat_for_ap(self):
        w=self.w;n=self.n;schedule=deepcopy(n['monthly_actions'])
        for _ in range(2):w.intervene(n['id'],'visit')
        for _ in range(3):
            result=w.intervene(n['id'],'challenge');battle=result['battle']
            w.battle_action(battle['id'],battle['revision'],'surrender')
        self.assertEqual(w.player['protagonist']['points'],22)
        self.assertEqual(n['time_left'],0);self.assertEqual(n['monthly_actions'],schedule)


if __name__=='__main__':unittest.main()
