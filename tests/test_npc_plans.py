"""Consequences, commitment, personal knowledge and save continuity."""
from copy import deepcopy
import io
import json
import unittest
from unittest.mock import patch

from jianghu import careers, martial, npc_memory, npc_plans, social
from jianghu.ai import context_for, apply_decision, LocalModel, DEFAULT_CONFIG
from jianghu.engine import World
from jianghu.store import Store


class NPCPlanningTests(unittest.TestCase):
    def setUp(self):
        self.w = World(seed=42, population=0, durable=False)
        self.addCleanup(self.w.store.close)

    def person(self, job='短工', **values):
        w = self.w; n = w.make_npc(place=0)
        n.update(age_months=360, wealth=30., health=100., skill=20., job=job)
        n.update(values)
        n['career'].update(profession=job, experience={job: 20.}, last_change=-24)
        w.set_goal(n, 'wealth', initial=True); w.reindex()
        n['identity'] = careers.identity(w, n)
        return n

    def renew_week(self):
        w = self.w; w.month += 1
        for n in w.npcs.values():
            social.reset_month(w, n)

    def run_action(self, n):
        w = self.w; a = w.choose(n, w.legal_actions(n)); e = w.execute(n, a)
        self.assertIsNotNone(e)
        return a, e

    def test_real_career_plan_funds_learning_and_finishes_with_actual_transfers(self):
        w = self.w; n = self.person(wealth=9.); tutor = self.person('药师')
        n['career']['experience']['药师'] = 0
        tutor['career']['experience']['药师'] = 60
        npc_plans.start(w, n, 'career_change', '筹备药师', ['work:self', 'learn_trade:2', 'change_job:药师'],
                        profession='药师', tutor=tutor['id'], original_job='短工')
        kinds = []
        for _ in range(16):
            self.renew_week()
            a, e = self.run_action(n); kinds.append(a['kind'])
            if a['kind'] == 'learn_trade':
                self.assertEqual(e['facts']['payment'], 2)
                self.assertEqual(tutor['time_left'], 3)
            if n['job'] == '药师':
                break
        self.assertEqual(n['job'], '药师')
        self.assertIn('work', kinds); self.assertIn('learn_trade', kinds)
        self.assertEqual(kinds[-1], 'change_job')
        self.assertEqual(n['planning_history'][-1]['status'], '完成')
        self.assertGreaterEqual(n['wealth'], 4)

    def test_choice_does_not_consume_a_step_until_execution(self):
        n = self.person(); w = self.w
        n['plan'] = dict(source='AI', created=0, until=2, steps=['work:self', 'rest:self'], reason='先工作再休息')
        selected = w.choose(n, w.legal_actions(n))
        self.assertEqual(n['plan']['steps'], ['work:self', 'rest:self'])
        event = w.execute(n, selected)
        self.assertEqual(n['plan']['steps'], ['rest:self'])
        self.assertEqual(n['plan']['completed'][0]['event_id'], event['id'])

    def test_emergency_pauses_then_resumes_same_plan(self):
        n = self.person(); w = self.w
        npc_plans.start(w, n, 'sequence', '先做手头事务', ['work:self'])
        revision = n['plan']['revision']; n['health'] = 20.
        a, _ = self.run_action(n)
        self.assertIn(a['kind'], ('use_item', 'treatment', 'rest'))
        self.assertEqual(n['plan']['status'], '暂缓')
        self.assertEqual(n['plan']['completed'], [])
        n['health'] = 100.; self.renew_week()
        a = w.choose(n, w.legal_actions(n))
        self.assertEqual(a['kind'], 'work'); self.assertEqual(n['plan']['revision'], revision)

    def test_unavailable_teacher_is_a_recorded_blocker_then_interrupts(self):
        n = self.person(wealth=100.); tutor = self.person('药师'); w = self.w
        n['career']['experience']['药师'] = 0
        npc_plans.start(w, n, 'career_change', '学药', ['learn_trade:2'], profession='药师',
                        tutor=tutor['id'], original_job='短工')
        tutor['place'] = 1; w.reindex()
        self.assertIsNone(npc_plans.resolve(w, n, w.legal_actions(n)))
        self.assertIn('无法请教', n['plan']['blocker'])
        w.month += 2
        npc_plans.resolve(w, n, w.legal_actions(n))
        self.assertIsNone(n['plan']); self.assertEqual(n['planning_history'][-1]['status'], '中断')

    def test_new_location_invalidates_commitment_without_pretending_completion(self):
        n = self.person(); w = self.w
        npc_plans.start(w, n, 'sequence', '本地事务', ['work:self'])
        n['place'] = 1; w.reindex()
        npc_plans.resolve(w, n, w.legal_actions(n))
        self.assertEqual(n['planning_history'][-1]['status'], '中断')

    def test_death_closes_plan_with_actual_death_event(self):
        n = self.person(); w = self.w
        npc_plans.start(w, n, 'sequence', '本地事务', ['work:self'])
        event = w.die(n, 'old_age')
        self.assertIsNone(n['plan'])
        self.assertEqual(n['planning_history'][-1]['event_id'], event['id'])
        self.assertEqual(n['planning_history'][-1]['status'], '中断')

    def test_leaving_school_closes_workflow_with_actual_departure(self):
        w = self.w
        n = self.person(faction=0, contribution=4., place=w.factions[0]['place'])
        npc_plans.start(w, n, 'sect_training', '修业', ['faction_task:0'], target_contribution=5, target_skill=25)
        event = w.execute(n, {'id': 'leave_faction:0'})
        self.assertIsNone(n['plan']); self.assertIsNone(n['faction'])
        self.assertEqual(n['planning_history'][-1]['event_id'], event['id'])
        self.assertEqual(n['planning_history'][-1]['status'], '中断')

    def test_plan_and_random_state_continue_identically_after_reload(self):
        n = self.person(); w = self.w
        npc_plans.start(w, n, 'career_change', '转业', ['work:self', 'change_job:行商'],
                        profession='行商', tutor=None, original_job='短工')
        w.checkpoint(); db = Store(); self.addCleanup(db.close); w.store.db.backup(db.db)
        restored = World(store=db, durable=False)
        before = deepcopy(w.export()); count = w.store.count(); rng = w.rng.getstate()
        w.npc_detail(n['id']); context_for(w, n)
        self.assertEqual(w.export(), before); self.assertEqual(w.store.count(), count); self.assertEqual(w.rng.getstate(), rng)
        w.month_step(); restored.month_step()
        self.assertEqual(w.npcs, restored.npcs); self.assertEqual(w.rng.getstate(), restored.rng.getstate())

    def test_role_guidance_requires_rank_and_respects_student_refusal_and_both_time_budgets(self):
        w = self.w; n = self.person(faction=0, contribution=40., skill=60., place=w.factions[0]['place'])
        q = self.person(faction=0, contribution=0., skill=15., place=n['place'])
        w.reindex(); before = (n['wealth'], q['wealth'], w.factions[0]['fund'])
        w.relation(q, n['id'])['affinity'] = -80
        e = w.execute(n, {'id': f'faction_guide:{q["id"]}'})
        self.assertFalse(e['facts']['accepted']); self.assertEqual(q['skill'], 15.)
        self.assertEqual(n['time_left'], 3); self.assertEqual(q['time_left'], 3)
        self.assertEqual((n['wealth'], q['wealth'], w.factions[0]['fund']), before)
        self.assertFalse(any(a['kind'] == 'faction_guide' for a in w.legal_actions(q)))
        self.renew_week(); w.relation(q, n['id'])['affinity'] = 0
        e = w.execute(n, {'id': f'faction_guide:{q["id"]}'})
        self.assertTrue(e['facts']['accepted']); self.assertGreater(q['skill'], 15.)
        self.assertEqual(n['contribution'], 41.)
        self.assertFalse(any(a['kind'] == 'faction_guide' for a in w.legal_actions(n)))

    def test_relevant_old_personal_memory_is_recalled_beyond_working_memory(self):
        n = self.person(); other = self.person(); w = self.w
        old = w.emit('betrayal', [n['id']], '有人背叛托银，旧日仇怨未解。', .9)
        for _ in range(45):
            w.month += 1; w.emit('daily', [n['id']], '吃了一顿饭，天色平常。', .1)
        n['memories'] = [m for m in n['memories'] if m['event_id'] != old['id']]
        n['goal']['kind'] = 'revenge'
        recalled = npc_memory.retrieve(w, n)
        self.assertEqual(recalled[0]['event_id'], old['id'])
        self.assertNotIn(old['id'], [m['event_id'] for m in npc_memory.retrieve(w, other)])

    def test_hearsay_keeps_attribution_and_unverified_status(self):
        n = self.person(); w = self.w
        memory = dict(event_id=999, month=0, fact='听人说有人背叛，尚未核实。', interpretation='传闻不足尽信。',
                      source='听闻：行旅', salience=.9, changes=[])
        w.store.experience(n['id'], memory, 0); n['goal']['kind'] = 'revenge'
        found = npc_memory.retrieve(w, n)[0]
        self.assertEqual(found['source'], '听闻：行旅'); self.assertIn('尚未核实', found['fact'])

    def test_model_receives_plan_progress_and_only_personal_recalled_events(self):
        n = self.person(); w = self.w
        w.emit('daily', [n['id']], '为学艺积攒盘缠。', .4)
        npc_plans.start(w, n, 'sequence', '积蓄', ['work:self'])
        n['plan']['blocker'] = '当前盘缠不足'; ctx = context_for(w, n); captured = []
        def fake(request, **kwargs):
            captured.append(json.loads(request.data)); return io.BytesIO(b'{"choices":[{"message":{"content":"{}"}}]}')
        with patch('urllib.request.urlopen', side_effect=fake):LocalModel(DEFAULT_CONFIG).request(ctx)
        wire = json.loads(captured[0]['messages'][1]['content'])
        self.assertEqual(wire['current_plan']['blocker'], '当前盘缠不足')
        self.assertIn('responsibilities', wire); self.assertNotIn('baseline_action', wire)

    def test_archived_memory_reflection_appends_personal_revision(self):
        n = self.person(); w = self.w
        e = w.emit('daily', [n['id']], '学艺时承蒙师傅指点。', .7)
        n['memories'] = []; n['goal']['kind'] = 'mastery'; ctx = context_for(w, n)
        action = ctx['allowed_actions'][0]['id']
        decision = dict(action_id=action, plan=[action], goal=ctx['allowed_goals'][0], reason='照顾眼前生活',
                        reflection=dict(event_ids=[e['id']], interpretation='记起昔日师恩。', trait='none', direction=0,
                                        belief='none', belief_direction=0))
        applied, _ = apply_decision(w, ctx, decision)
        self.assertTrue(applied); self.assertIn(e['id'], n['reflected'])
        self.assertEqual(npc_memory.find(w, n, e['id'])['interpretation'], '记起昔日师恩。')
