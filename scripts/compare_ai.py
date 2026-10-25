"""Small real-model intervention study. It measures behavior, not literary quality."""
import argparse
from copy import deepcopy
import hashlib
import json
from pathlib import Path
import sys
import time

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT))
from jianghu.engine import World
from jianghu.ai import LocalModel, DEFAULT_CONFIG, context_for, apply_decision
from jianghu.store import Store


def main(samples):
    stamp=time.time_ns()
    baseline=World(42,100,Store(ROOT/'reports'/f'ai-control-{stamp}.sqlite'),durable=False)
    for _ in range(60):baseline.month_step()
    baseline.checkpoint()
    target_store=Store(ROOT/'reports'/f'ai-treatment-{stamp}.sqlite')
    baseline.store.db.backup(target_store.db)
    ai=World(store=target_store,durable=False)
    model=LocalModel(DEFAULT_CONFIG)
    # Diverse existing lives, selected before observing model output.
    ids=[]
    for kind in ['wealth','mastery','belonging','renown','freedom']:
        candidates=[n for n in ai.npcs.values() if n['alive'] and n['goal']['kind']==kind and n['age_months']>=216]
        if candidates:ids.append(candidates[0]['id'])
    ids += [n['id'] for n in ai.npcs.values() if n['alive'] and n['id'] not in ids and n['age_months']>=216]
    rows=[]
    for index,i in enumerate(ids[:samples]):
        ctx=context_for(ai,ai.npcs[i]);record={'npc_id':i,'name':ai.npcs[i]['name'],'context':ctx}
        print(f'Real local inference {index+1}/{samples}, NPC {i}',flush=True)
        try:
            response,ms,usage,raw=model.request(ctx)
            applied,reason=apply_decision(ai,ctx,response)
            record.update({'response':response,'raw':raw,'latency_ms':ms,'usage':usage,'applied':applied,'validation':reason,
                           'changed_from_reference':applied and response['action_id']!=ctx['baseline_action']})
        except Exception as e:
            record['error']=str(e)
        rows.append(record)
        ai.store.log_ai(record)
        (ROOT/'reports'/'ai_comparison.json').write_text(json.dumps({'model':'Qwen3-4B-Q4_K_M','backend':'llama.cpp Vulkan','seed':42,'population':100,'prehistory_months':60,'decisions':rows},ensure_ascii=False,indent=2),encoding='utf-8')
    ai.checkpoint()
    # Advance one month while the accepted proposals are still current; inspect actual engine events.
    baseline.month_step();ai.month_step()
    for row in rows:
        i=row['npc_id'];row['control_action']=baseline.npcs[i]['last_action'];row['ai_action']=ai.npcs[i]['last_action']
        row['control_event']=baseline.store.event(baseline.npcs[i]['last_action']['event_id'])
        row['ai_event']=ai.store.event(ai.npcs[i]['last_action']['event_id'])
    for _ in range(11):baseline.month_step();ai.month_step()
    baseline.checkpoint();ai.checkpoint()
    report={'model':'Qwen3-4B-Q4_K_M','backend':'llama.cpp Vulkan','seed':42,'population':100,
            'source_sha256':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in (ROOT/'jianghu').glob('*.py')},
            'prehistory_months':60,'followup_months':12,'decisions':rows,
            'calls':len(rows),'valid_applied':sum(bool(r.get('applied')) for r in rows),
            'changed_from_reference':sum(bool(r.get('changed_from_reference')) for r in rows),
            'actual_next_action_differences':sum(r['control_action']['id']!=r['ai_action']['id'] for r in rows),
            'control_summary':{k:v for k,v in baseline.summary().items() if k not in ('npcs','places','factions','player')},
            'ai_summary':{k:v for k,v in ai.summary().items() if k not in ('npcs','places','factions','player')},
            'limitation':'Small selected-cohort behavior probe, not a blinded quality evaluation or proof of improved realism.'}
    (ROOT/'reports'/'ai_comparison.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps({k:report[k] for k in ('calls','valid_applied','changed_from_reference','actual_next_action_differences')},ensure_ascii=False),flush=True)
    baseline.store.close();ai.store.close()


if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--samples',type=int,default=6);args=p.parse_args();main(args.samples)
