from copy import deepcopy
from pathlib import Path
import tempfile
import json
import threading
from http.server import ThreadingHTTPServer
from types import SimpleNamespace
from urllib.error import HTTPError
from urllib.request import Request, urlopen
import unittest
from unittest.mock import patch

from jianghu import items, martial, martial_catalog, npc_attention, player_social, player_interactions as encounters, protagonist
from jianghu.engine import World
from jianghu.store import Store
from tests.physical_helpers import frame


class PlayerInteractionTests(unittest.TestCase):
    def setUp(self):
        self.w = w = World(42, 0, durable=False)
        self.addCleanup(w.store.close)
        protagonist.perform(w, dict(action='create', revision=0, month=0, name='来客', sex='女', age=24, place=45))
        self.n = n = w.make_npc(place=45)
        n.update(age_months=360, health=100., wealth=100., time_left=4, spouse=None, orientation='皆可')
        n['traits'].update(integrity=90., sociability=90., kindness=90., boldness=50.)
        n['mood'].update(anger=0., joy=0.)
        w.reindex(); w.checkpoint()

    def body(self, action, **extra):
        return dict(npc_id=self.n['id'], action=action, revision=self.w.player['protagonist']['revision'], month=self.w.month, **extra)

    def act(self, action, **extra):
        return encounters.perform(self.w, self.body(action, **extra))

    def relation(self, affinity=0., trust=0.):
        return self.w.player['ties'].setdefault(str(self.n['id']), dict(affinity=affinity, trust=trust))

    def reject_unchanged(self, body):
        before=deepcopy(self.w.export()); count=self.w.store.count()
        with self.assertRaises(ValueError): encounters.perform(self.w, body)
        self.assertEqual(self.w.export(), before)
        self.assertEqual(self.w.store.count(), count)

    def test_view_is_read_only_and_all_twelve_actions_have_requirements(self):
        before=deepcopy(self.w.export()); count=self.w.store.count()
        view=encounters.view(self.w, self.n['id'])
        self.assertEqual(len(view['actions']), 12)
        self.assertEqual(self.w.export(), before); self.assertEqual(self.w.store.count(), count)
        self.assertFalse(next(a for a in view['actions'] if a['id']=='marry')['enabled'])

    def test_hard_blockers_and_invalid_revisions_are_atomic(self):
        for extra in ({'revision':-1}, {'revision':True}, {'month':True}, {'action':'unknown'}, {'npc_id':True}, {'price':0}):
            body=self.body('praise');body.update(extra);self.reject_unchanged(body)
        for field,value in (('alive',False),('place',46)):
            before=self.n[field];self.n[field]=value;self.reject_unchanged(self.body('praise'));self.n[field]=before
        self.w.player['protagonist']['points']=0
        self.reject_unchanged(self.body('insult'))

    def test_praise_and_insult_repeat_with_player_ap_without_spending_npc_time(self):
        w=self.w;n=self.n;points=w.player['protagonist']['points'];time=n['time_left']
        self.act('praise');r=self.relation()
        self.assertEqual((r['affinity'],r['trust']), (4.,2.))
        self.act('insult')
        self.assertEqual((r['affinity'],r['trust']), (-8.,-8.))
        self.assertEqual(w.player['reputation'],-1);self.assertEqual(n['time_left'],time)
        self.assertEqual(w.player['protagonist']['points'],points-2)
        self.act('praise');self.act('insult')
        self.assertEqual((r['affinity'],r['trust']),(-16.,-16.))
        self.assertEqual(w.player['protagonist']['points'],points-4)
        self.assertEqual(n['time_left'],time);self.assertEqual(n['monthly_actions'],[])

    def test_gift_moves_one_owned_item_and_stale_replay_cannot_duplicate(self):
        w=self.w;item=items.create(w,'player','herb','fixture');body=self.body('gift',item_id=item['id'])
        before=w.player['protagonist']['points'];total=len(w.items['objects'])
        self.act('gift',item_id=item['id'])
        self.assertEqual(item['owner'],str(self.n['id']));self.assertEqual(len(w.items['objects']),total)
        self.assertGreater(self.relation()['affinity'],0);self.assertEqual(w.player['protagonist']['points'],before-1)
        self.reject_unchanged(body)
        gear=items.create(w,'player','wood_shield','fixture');w.player['equipment']['shield']=gear['id']
        self.reject_unchanged(self.body('gift',item_id=gear['id']))

    def test_bidirectional_trade_conserves_silver_instances_and_checks_purses(self):
        w=self.w;n=self.n;item=items.create(w,n['id'],'herb','fixture');silver=w.player['wealth']+n['wealth'];total=len(w.items['objects'])
        before=w.player['wealth'];self.act('trade',direction='buy',item_id=item['id'])
        self.assertEqual(item['owner'],'player');self.assertEqual(w.player['wealth'],before-items.buy_price(item))
        self.act('trade',direction='sell',item_id=item['id'])
        self.assertEqual(item['owner'],str(n['id']));self.assertAlmostEqual(w.player['wealth']+n['wealth'],silver)
        self.assertEqual(len(w.items['objects']),total)
        w.player['wealth']=0;self.reject_unchanged(self.body('trade',direction='buy',item_id=item['id']))
        own=items.create(w,'player','herb','fixture');n['wealth']=0
        self.reject_unchanged(self.body('trade',direction='sell',item_id=own['id']))
        self.reject_unchanged(self.body('trade',direction='buy',item_id=own['id']))

    def test_teaching_hands_over_a_manual_then_inventory_use_learns_it(self):
        w=self.w;n=self.n
        art=next(k for k in martial_catalog.ARTS if not items.knows(w,'player',k))
        n['martial']['learned'].append(art);martial.sync_actor(w,n)
        points=w.player['protagonist']['points'];time=n['time_left']
        self.act('teach',art=art)
        book=next(i for i in items.objects(w,'player') if items.spec(i).get('art')==art)
        self.assertFalse(items.knows(w,'player',art));self.assertEqual(w.player['protagonist']['points'],points-2)
        self.assertEqual(n['time_left'],time)
        self.act('teach',art=art)
        self.assertEqual(sum(items.spec(i).get('art')==art for i in items.objects(w,'player')),2)
        self.assertEqual(w.player['protagonist']['points'],points-4)
        items.perform(w,'player','use',book['id']);self.assertTrue(items.knows(w,'player',art))

    def test_trade_rejects_changed_or_invalid_displayed_prices_without_charging(self):
        item=items.create(self.w,self.n['id'],'herb','fixture')
        for quote in (0,True,float('nan')):
            self.reject_unchanged(self.body('trade',direction='buy',item_id=item['id'],quoted_price=quote))
        self.act('trade',direction='buy',item_id=item['id'],quoted_price=items.buy_price(item))
        self.assertEqual(item['owner'],'player')

    def test_http_routes_enforce_pause_revisions_and_real_settlement(self):
        from jianghu.server import handler_for
        game=SimpleNamespace(world=self.w,lock=threading.RLock(),autoplay=False,job=dict(remaining=0))
        server=ThreadingHTTPServer(('127.0.0.1',0),handler_for(game))
        thread=threading.Thread(target=server.serve_forever,daemon=True);thread.start()
        root=f'http://127.0.0.1:{server.server_port}'
        def post(body):
            return urlopen(Request(root+'/api/interaction',data=json.dumps(body).encode(),headers={'Content-Type':'application/json'}),timeout=10)
        try:
            with urlopen(root+f'/api/interaction?npc={self.n["id"]}',timeout=10) as response:
                self.assertEqual(len(json.load(response)['actions']),12)
            body=self.body('praise')
            with post(body) as response:self.assertTrue(json.load(response)['event']['facts']['accepted'])
            self.assertEqual(self.relation()['affinity'],4)
            before=deepcopy(self.w.export())
            with self.assertRaises(HTTPError) as error:post(body)
            self.assertEqual(error.exception.code,400);self.assertEqual(self.w.export(),before)
            game.autoplay=True
            with self.assertRaises(HTTPError) as error:post(self.body('insult'))
            self.assertEqual(error.exception.code,400);self.assertEqual(self.w.export(),before)
        finally:server.shutdown();thread.join();server.server_close()

    def test_confidant_acceptance_and_independent_refusal(self):
        self.relation(25,15);self.act('confide');self.assertTrue(self.relation()['confidant'])
        self.reject_unchanged(self.body('confide'))
        self.relation().pop('confidant');self.n['mood']['anger']=80
        points=self.w.player['protagonist']['points'];result=self.act('confide')
        self.assertFalse(result['event']['facts']['accepted']);self.assertFalse(self.relation().get('confidant',False))
        self.assertEqual(self.w.player['protagonist']['points'],points-1)

    def test_sworn_bond_is_persistent_and_excludes_marriage(self):
        self.relation(80,70);self.act('swear');self.assertTrue(self.relation()['sworn'])
        self.reject_unchanged(self.body('swear'));self.reject_unchanged(self.body('marry'))
        self.reload_world();self.assertTrue(self.w.player['ties'][str(self.n['id'])]['sworn'])

    def reload_world(self):
        folder=tempfile.TemporaryDirectory();self.addCleanup(folder.cleanup)
        path=Path(folder.name)/'world.sqlite';store=Store(path);self.w.store.db.backup(store.db);store.close()
        self.w=World(store=Store(path),durable=False);self.addCleanup(self.w.store.close)
        self.n=self.w.npcs[self.n['id']]

    def test_marriage_is_exclusive_survives_reload_and_clears_on_death(self):
        self.relation(80,70);self.act('marry')
        self.assertEqual(self.w.player['protagonist']['spouse'],self.n['id']);self.assertTrue(self.n['player_spouse'])
        self.assertTrue(self.relation()['married']);self.reject_unchanged(self.body('marry'))
        self.assertFalse(any(a['kind'] in ('court','propose') for a in self.w.legal_actions(self.n)))
        self.reload_world();self.assertTrue(self.n['player_spouse']);self.assertEqual(self.w.player['protagonist']['spouse'],self.n['id'])
        self.w.die(self.n,'accident')
        self.assertIsNone(self.w.player['protagonist']['spouse']);self.assertFalse(self.relation()['married'])

    def test_marriage_respects_existing_spouse_age_and_attraction(self):
        self.relation(80,70)
        self.n['spouse']=0;self.reject_unchanged(self.body('marry'));self.n['spouse']=None
        self.n['age_months']=200;self.reject_unchanged(self.body('marry'));self.n['age_months']=360
        self.n.update(sex='女',orientation='异性');self.reject_unchanged(self.body('marry'))

    def test_theft_success_is_real_and_undetected_does_not_tell_victim(self):
        w=self.w;n=self.n;self.act('praise');before=n['last_event'];silver=n['wealth']+w.player['wealth']
        with patch.object(w.rng,'random',side_effect=[0.,.99]):result=self.act('steal')
        self.assertTrue(result['event']['facts']['success']);self.assertFalse(result['event']['facts']['detected'])
        self.assertEqual(n['last_event'],before);self.assertEqual(self.relation()['affinity'],4)
        visible=npc_attention.player_view(w,n)
        self.assertEqual(visible['last_event'],before)
        self.assertNotIn('encounter_months',visible['relationship']);self.assertNotIn('encounter_records',player_social.visible_tie(w,n['id']))
        self.assertAlmostEqual(n['wealth']+w.player['wealth'],silver);self.assertEqual(result['event']['facts']['silver'],8.)
        with patch.object(w.rng,'random',side_effect=[0.,.99]):again=self.act('steal')
        self.assertTrue(again['event']['facts']['success'])
        self.assertAlmostEqual(n['wealth']+w.player['wealth'],silver)

    def test_theft_failure_takes_no_goods_and_records_real_hostility(self):
        w=self.w;n=self.n;money=(w.player['wealth'],n['wealth'])
        with patch.object(w.rng,'random',return_value=.99):result=self.act('steal')
        self.assertFalse(result['event']['facts']['success']);self.assertEqual((w.player['wealth'],n['wealth']),money)
        self.assertEqual(w.player['reputation'],-3);self.assertEqual(self.relation()['trust'],-20)

    def test_item_theft_and_equipment_protection(self):
        item=items.create(self.w,self.n['id'],'herb','fixture')
        with patch.object(self.w.rng,'random',side_effect=[0.,.99]):self.act('steal',item_id=item['id'])
        self.assertEqual(item['owner'],'player')

    def finish(self, winner=0):
        w=self.w;b=w.battle;packet=frame(b,180);packet['fighters'][1-winner]['health']=0
        packet['fighters'][winner]['health']*=.7
        packet['hits']=6
        for fighter in packet['fighters']:
            fighter['landed_hits']=3;fighter['attack_count']=3
        revision=b['revision'];result=w.battle_action(b['id'],revision,'complete',packet)
        return result,revision,packet

    def test_spar_uses_existing_physics_and_does_not_spend_world_health(self):
        before=(self.w.player['health'],self.n['health']);self.act('spar')
        self.assertTrue(self.w.battle['spar']);self.finish()
        self.assertEqual((self.w.player['health'],self.n['health']),before)

    def test_robbery_only_pays_on_victory_and_settles_once_after_reload(self):
        w=self.w;n=self.n;money=w.player['wealth']+n['wealth'];before=w.player['wealth']
        self.act('rob');self.assertFalse(w.battle['spar']);self.assertEqual(w.player['wealth'],before)
        self.reload_world();w=self.w;n=self.n
        result,revision,packet=self.finish()
        self.assertEqual(result['battle']['winner'],0);self.assertEqual(w.player['wealth'],before+20)
        self.assertAlmostEqual(w.player['wealth']+n['wealth'],money)
        self.assertLess(n['health'],100);self.assertTrue(n['wound']);self.assertLess(self.relation()['affinity'],0)
        count=w.store.count();after=deepcopy(w.export())
        w.battle_action(w.battle['id'],revision,'complete',packet)
        self.assertEqual(w.export(),after);self.assertEqual(w.store.count(),count)

    def test_losing_robbery_or_winning_assault_does_not_mint_loot(self):
        money=self.w.player['wealth'];self.act('rob');self.finish(1)
        self.assertEqual(self.w.player['wealth'],money)
        self.w.player['protagonist']['points']=30;self.w.player['health']=100;self.n['health']=100
        self.act('assault');self.finish(0)
        self.assertEqual(self.w.player['wealth'],money);self.assertEqual(self.w.player['reputation'],-18)


if __name__=='__main__': unittest.main()
