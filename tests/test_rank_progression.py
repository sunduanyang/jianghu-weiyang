from jianghu import combat_attributes as abilities
from tests.combat_helpers import set_rating, update_actor
from copy import deepcopy
import unittest

from jianghu.engine import World
from jianghu import careers, organizations, rank_progression as r, sect_ranks, social, social_ranks, economics
from jianghu.store import Store


class RankProgressionTests(unittest.TestCase):
    def setUp(self):
        self.w=World(population=0,durable=False,compact=True,rank_assessments=False)
        self.addCleanup(self.w.store.close)

    def actor(self,job='农人',faction=None,**values):
        w=self.w;n=w.make_npc(place=w.factions[0]['place'])
        update_actor(n, age_months=360,wealth=500.,health=100.,reputation=50.,job=job,faction=faction,skill=20.,contribution=0.,faction_since=0)
        update_actor(n, values);n['career'].update(profession=job,experience={job:50.},last_change=-24)
        w.set_goal(n,'renown',initial=True);n['identity']=careers.identity(w,n);w.reindex()
        return n

    def enable(self):
        r.initialize(self.w)

    def tick(self,n=None,service=False):
        w=self.w;w.month+=1
        if service:
            # Historical service remains evidence after paid duties are removed.
            r.mark(r.prepare(w, n['id']), 'service', w.month)
        r.monthly(w)

    def test_migration_registers_existing_standings_without_rewriting_assets_or_rng(self):
        n=self.actor(faction=0,contribution=35.,skill=45.)
        old=deepcopy(self.w.export());grade=organizations.rank(self.w,n);events=self.w.store.count()
        self.enable()
        self.assertEqual(organizations.rank(self.w,n),grade)
        self.assertEqual(self.w.rng.getstate(),old['random_state']);self.assertEqual(self.w.month,old['month'])
        self.assertEqual(n['wealth'],old['npcs'][n['id']]['wealth'])
        self.assertEqual(self.w.store.count(),events+1)
        registered=deepcopy(self.w.export());self.assertFalse(r.initialize(self.w));self.assertEqual(self.w.export(),registered)
        self.assertEqual(r.prepare(self.w,n['id'])['activity'],{})

    def test_meeting_thresholds_does_not_instantly_change_confirmed_grade(self):
        n=self.actor(faction=0);self.enable();update_actor(n, contribution=1000.,skill=100.)
        self.assertEqual(sect_ranks.merit_grade(n),2)
        self.assertEqual(organizations.rank(self.w,n)['grade'],9)
        self.tick(n,True);self.assertEqual(organizations.rank(self.w,n)['grade'],9)
        self.tick(n,True);self.assertEqual(organizations.rank(self.w,n)['grade'],8)

    def test_each_review_is_once_per_month_and_never_skips_multiple_grades(self):
        n=self.actor(faction=0);self.enable();update_actor(n, contribution=1000.,skill=100.)
        self.tick(n,True)
        before=deepcopy(self.w.export())
        for _ in range(20):r.monthly(self.w);r.view(self.w,n['id'])
        self.assertEqual(self.w.export(),before)
        self.tick(n,True);self.assertEqual(organizations.rank(self.w,n)['grade'],8)
        self.assertEqual(n['rank_progress']['sect']['qualified'],0)

    def test_donations_alone_cannot_replace_actual_service(self):
        n=self.actor(faction=0);self.enable();update_actor(n, contribution=100.,skill=100.)
        for _ in range(6):self.tick()
        self.assertEqual(organizations.rank(self.w,n)['grade'],9)
        self.assertFalse(r.sect_conditions(self.w,n['id'])['qualified'])

    def test_retired_teaching_cannot_count_as_service(self):
        teacher=self.actor(faction=0,contribution=35.,skill=45.)
        student=self.actor(faction=0,skill=16.);student['mentor']=teacher['id'];self.w.link(teacher,student,40,40)
        self.enable();before=deepcopy(self.w.export())
        self.assertIsNone(self.w.execute(teacher,dict(id=f'teach:{student["id"]}')))
        self.assertEqual(self.w.export(),before)

    def test_continuous_shortfall_demotes_after_three_months_with_hysteresis(self):
        n=self.actor(faction=0,contribution=24.,skill=35.);self.enable()
        set_rating(n, 20.)
        self.tick();self.tick();self.assertEqual(organizations.rank(self.w,n)['grade'],4)
        self.tick();self.assertEqual(organizations.rank(self.w,n)['grade'],5)
        changes=[e for e in self.w.store.history(n['id']) if e['kind']=='rank_change']
        self.assertEqual(changes[0]['facts']['direction'],'降阶')
        self.assertNotIn('升为',changes[0]['summary'])

    def test_small_temporary_threshold_dip_and_illness_do_not_demote(self):
        n=self.actor(faction=0,contribution=24.,skill=35.);self.enable();set_rating(n, 34.)
        for _ in range(3):self.tick()
        self.assertEqual(organizations.rank(self.w,n)['grade'],4)
        set_rating(n, 10.);n['illness']={'remaining':6,'severity':1.}
        for _ in range(6):self.tick()
        self.assertEqual(organizations.rank(self.w,n)['grade'],4)

    def test_only_final_judgment_disciplinarily_demotes_and_freezes_promotion(self):
        n=self.actor(faction=0,contribution=70.,skill=65.);victim=self.actor();self.enable()
        self.w.emit('report',[victim['id'],n['id']],'尚待核实',.4,facts={'offender':n['id']})
        self.assertEqual(organizations.rank(self.w,n)['grade'],2)
        event=self.w.emit('judgment',[victim['id'],n['id']],'证据核实后的生效判决',.9,
                          facts=dict(offender=n['id'],victim=victim['id'],public_money=True))
        self.assertEqual(organizations.rank(self.w,n)['grade'],4)
        self.assertGreater(n['rank_progress']['sect']['penalty'],0)
        self.assertIn('暂停',r.sect_conditions(self.w,n['id'])['blocked'])
        change=next(e for e in self.w.store.history(n['id']) if e['kind']=='rank_change')
        self.assertIn(event['id'],change['causes'])

    def test_unqualified_newcomer_cannot_take_vacant_leadership(self):
        n=self.actor(faction=0);self.enable();organizations.monthly(self.w)
        self.assertIsNone(self.w.factions[0]['leader']);self.assertEqual(organizations.rank(self.w,n)['grade'],9)

    def test_qualified_successor_gets_first_grade_only_after_real_election(self):
        n=self.actor(faction=0,contribution=70.,skill=65.);self.enable()
        self.assertEqual(organizations.rank(self.w,n)['grade'],2)
        organizations.elect(self.w,self.w.factions[0])
        self.assertEqual(self.w.factions[0]['leader'],n['id'])
        self.assertEqual(organizations.rank(self.w,n)['grade'],1)

    def test_rich_skilled_farmer_needs_public_service_and_registration_to_be_notable(self):
        n=self.actor(wealth=100000.,skill=100.,reputation=100.);self.enable()
        self.w.month=12
        option=next(o for o in r.options(self.w,n['id']) if o['kind']=='recognize_notable')
        self.assertFalse(option['enabled']);self.assertEqual(careers.identity(self.w,n)['grade'],8)
        p=n['rank_progress'];proof=[]
        for m in range(7,13):
            self.w.month=m;proof.append(self.w.emit('donate_relief',[n['id']],'真实公益',.4,facts={'actor':n['id']})['id'])
        cash=economics.total_cash(self.w);event=r.execute(self.w,n['id'],'recognize_notable')
        self.assertEqual(careers.identity(self.w,n)['grade'],4)
        self.assertEqual(economics.total_cash(self.w),cash)
        self.assertTrue(set(proof).issubset(event['causes']))

    def test_business_requires_trade_proficiency_and_twelve_real_profit_months(self):
        n=self.actor('行商');self.enable();self.w.month=18
        self.assertFalse(next(o for o in r.options(self.w,n['id']) if o['kind']=='establish_business')['enabled'])
        for m in range(7,19):
            self.w.month=m;self.w.emit('work',[n['id']],'实际营业',.3,facts=dict(actor=n['id'],gross_income=10.,production={'sales':15.,'sold':{'grain':1}}))
        self.assertTrue(next(o for o in r.options(self.w,n['id']) if o['kind']=='establish_business')['enabled'])
        r.execute(self.w,n['id'],'establish_business');self.assertEqual(careers.identity(self.w,n)['grade'],3)

    def test_business_decline_has_grace_period_and_stock_prevents_false_bankruptcy(self):
        n=self.actor('行商',wealth=50.,social_status={'title':'豪商','country':0,'since':0});self.enable()
        for _ in range(5):self.tick()
        self.assertEqual(careers.identity(self.w,n)['grade'],3)
        self.tick();self.assertEqual(careers.identity(self.w,n)['grade'],6)
        n['social_status']={'title':'豪商','country':0,'since':self.w.month};economics.prepare(n)['stock']['grain']=100.
        for _ in range(8):self.tick()
        self.assertEqual(careers.identity(self.w,n)['grade'],3)

    def test_repeated_failed_production_can_close_livelihood_but_sickness_is_protected(self):
        n=self.actor('匠人',wealth=0.);self.enable()
        for _ in range(5):
            self.w.month+=1;self.w.emit('work',[n['id']],'无原料未开工',.2,facts=dict(actor=n['id'],gross_income=0.,production={}));r.monthly(self.w)
        self.assertEqual(n['job'],'匠人')
        self.w.month+=1;self.w.emit('work',[n['id']],'仍无法备料',.2,facts=dict(actor=n['id'],gross_income=0.,production={}));r.monthly(self.w)
        self.assertEqual(n['job'],'短工');self.assertEqual(careers.identity(self.w,n)['grade'],9)

    def test_player_and_npc_sect_use_same_assessment_and_stale_actions_reject(self):
        h=self.w.player['protagonist'];h.update(created=True,faction=0,faction_since=0)
        set_rating(self.w.player, 20.);n=self.actor(faction=0);self.enable()
        update_actor(n, contribution=10.,skill=20.);h['contribution']=10.
        for _ in range(2):
            self.w.month+=1
            self.w.emit('faction_task',[n['id']],'履职',.4,facts={'actor':n['id']})
            from jianghu import protagonist
            protagonist.record(self.w,'hero_sect_task','履职',{})
            r.monthly(self.w)
        self.assertEqual(organizations.player_membership(self.w)['grade'],organizations.rank(self.w,n)['grade'])
        before=deepcopy(self.w.export())
        with self.assertRaises(ValueError):r.player_action(self.w,dict(action='recognize_notable',revision=-1,month=self.w.month))
        self.assertEqual(self.w.export(),before)

    def test_save_reload_does_not_repeat_assessments_or_lose_pending_months(self):
        n=self.actor(faction=0);self.enable();update_actor(n, contribution=20.,skill=30.);self.tick(n,True)
        self.w.checkpoint();before=deepcopy(self.w.export())
        loaded=World(store=self.w.store,durable=False,compact=True,rank_assessments=True)
        self.assertEqual(loaded.export(),before);r.monthly(loaded);self.assertEqual(loaded.export(),before)
        loaded.month+=1;r.monthly(loaded)
        self.assertEqual(organizations.rank(loaded,loaded.npcs[n['id']])['grade'],8)

    def test_political_titles_require_actual_vacancy_not_merely_money(self):
        ruler=self.actor(social_status=dict(title='知府',country=0,since=0))
        minister=self.actor(social_status=dict(title='通判',country=0,since=0))
        rich=self.actor(wealth=100000.,reputation=100.)
        self.enable();self.tick()
        self.assertEqual(careers.identity(self.w,rich)['grade'],8)
        self.assertEqual(careers.identity(self.w,minister)['grade'],2)
        self.w.die(ruler,'old_age');self.tick()
        self.assertEqual(careers.identity(self.w,minister)['grade'],1)
        self.assertEqual(self.w.rank_system['courts']['0']['holders']['ruler'],minister['id'])

    def test_minister_appointment_preserves_underlying_estate_on_dismissal(self):
        old=self.actor(social_status=dict(title='通判',country=0,since=0))
        candidate=self.actor('书生',social_status=dict(title='乡绅',country=0,since=0))
        victim=self.actor();self.enable();self.w.month=12
        for m in range(7,13):r.mark(candidate['rank_progress'],'public',m)
        self.w.die(old,'old_age');r.courts(self.w)
        self.assertEqual(careers.identity(self.w,candidate)['grade'],2)
        self.w.emit('judgment',[victim['id'],candidate['id']],'生效判决',.9,facts=dict(offender=candidate['id'],victim=victim['id']))
        self.assertEqual(careers.identity(self.w,candidate)['grade'],4)
        self.assertEqual(candidate['social_status']['title'],'乡绅')

    def test_extinct_polity_does_not_keep_granting_first_and_second_grades(self):
        ruler=self.actor(social_status=dict(title='知府',country=0,since=0));self.enable()
        for p in self.w.active_places:
            if self.w.territory[p['id']]['owner']==0:self.w.territory[p['id']]['owner']=1
        for _ in range(5):self.tick()
        self.assertEqual(careers.identity(self.w,ruler)['grade'],1)
        self.tick();self.assertEqual(careers.identity(self.w,ruler)['grade'],8)
        self.assertEqual(self.w.rank_system['courts']['0']['holders'],{})

    def test_uncreated_hero_and_newborn_do_not_trigger_rank_transactions(self):
        parent=self.actor();self.enable();before=deepcopy(self.w.export())
        self.assertEqual(r.view(self.w,'player')['options'],[])
        self.assertEqual(self.w.export(),before)
        child=self.w.make_npc([parent['id']],parent['place'])
        self.assertIn('rank_progress',child);self.assertEqual(careers.identity(self.w,child)['grade'],9)

    def test_actual_illness_does_not_turn_failed_production_into_forced_unemployment(self):
        n=self.actor('匠人',wealth=0.,illness={'remaining':12,'severity':1.});self.enable()
        for _ in range(8):
            self.w.month+=1;self.w.emit('work',[n['id']],'病中无法开工',.2,facts=dict(actor=n['id'],gross_income=0.,production={}));r.monthly(self.w)
        self.assertEqual(n['job'],'匠人')

    def test_player_can_be_formally_elected_and_treasury_aid_handles_player_leadership(self):
        n=self.actor(faction=0,wealth=2.)
        h=self.w.player['protagonist'];h.update(created=True,faction=0,faction_since=0,age_months=360,contribution=100.)
        update_actor(self.w.player, skill=75.,reputation=60.);self.enable()
        self.assertEqual(organizations.player_membership(self.w)['grade'],2)
        organizations.elect(self.w,self.w.factions[0])
        self.assertEqual(self.w.factions[0]['leader'],-1)
        self.assertEqual(organizations.player_membership(self.w)['grade'],1)
        organizations.annual(self.w)
        self.assertGreater(n['wealth'],2.)
        self.assertEqual(self.w.faction_detail(0)['leader_name'],self.w.player['name'])
        self.w.checkpoint();loaded=World(store=self.w.store,durable=False,rank_assessments=True)
        self.assertEqual(organizations.player_membership(loaded)['grade'],1)
        from jianghu import city_maps
        f=self.w.factions[0]
        seat=next(s for s in city_maps.world_layout(self.w,f['place'])['settlements'] if s.get('faction')==0)
        h.update(place=f['place'],row=seat['row'],col=seat['col'],points=30)
        organizations.player_action(self.w,'sect_leave',0)
        self.assertIsNone(h['faction']);self.assertIsNone(f['leader'])

    def test_player_leadership_does_not_block_qualified_npc_replacement(self):
        n=self.actor(faction=0,contribution=70.,skill=65.)
        h=self.w.player['protagonist'];h.update(created=True,faction=0,faction_since=0,age_months=360,contribution=100.)
        update_actor(self.w.player, skill=75.,reputation=60.);self.enable();organizations.elect(self.w,self.w.factions[0])
        h['rank_progress']['sect']['leader_deficient']=3
        organizations.monthly(self.w)
        self.assertEqual(self.w.factions[0]['leader'],n['id'])
        self.assertEqual(organizations.player_membership(self.w)['grade'],2)

    def test_leadership_votes_use_effective_merit_after_disciplinary_deduction(self):
        first=self.actor(faction=0,contribution=100.,skill=65.)
        second=self.actor(faction=0,contribution=70.,skill=65.);self.enable()
        first['rank_progress']['sect']['penalty']=50.
        organizations.elect(self.w,self.w.factions[0])
        self.assertEqual(self.w.factions[0]['leader'],second['id'])


if __name__=='__main__':unittest.main()
