"""Three actual local-model decisions on a copy; never enables the player's AI."""
import json
from pathlib import Path
import sqlite3
import sys
import time
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
from jianghu.ai import context_for, apply_decision, LocalModel, DEFAULT_CONFIG
from jianghu.engine import World
from jianghu.store import Store
from jianghu import npc_plans


def run(folder):
    folder = Path(folder).resolve(); source = folder / 'native-save/world.sqlite'; target = folder / 'model-validation.sqlite'
    if target.exists():
        raise ValueError('模型验证副本已存在')
    with sqlite3.connect(source.as_uri() + '?mode=ro', uri=True) as src, sqlite3.connect(target) as dst:
        src.backup(dst)
    w = World(store=Store(target), compact=True, durable=False)
    try:
        actors = []
        for profile in ('disciple', 'merchant', 'elder'):
            n = next(n for n in w.npcs.values() if n['alive'] and n['age_months'] >= 192 and n['wealth'] >= 8
                     and n['health'] > 60 and npc_plans.role(w, n)['key'] == profile)
            actors.append(n)
        results = []
        for n in actors:
            ctx = context_for(w, n)
            print('REQUEST', n['id'], npc_plans.role(w, n)['label'], flush=True)
            response, latency, usage, raw = LocalModel(DEFAULT_CONFIG).request(ctx)
            applied, reason = apply_decision(w, ctx, response)
            row = dict(npc_id=n['id'], role=npc_plans.role(w, n), response=response, applied=applied,
                       validation=reason, latency_ms=latency, usage=usage,
                       remembered_events=[m['event_id'] for m in ctx['memories']], had_prior_plan=bool(ctx['current_plan']))
            results.append(row); w.store.log_ai(dict(month=w.month, npc_id=n['id'], context=ctx,
                                                     response=response, applied=applied, validation=reason, latency_ms=latency, usage=usage))
            print(json.dumps({k: row[k] for k in ('npc_id', 'applied', 'validation', 'latency_ms')}, ensure_ascii=False), flush=True)
        w.month_step(); w.checkpoint()
        executed = {n['id']: sum(a['source'] == 'AI' for a in n['monthly_actions']) for n in actors}
        report = dict(model='Qwen3-4B-Q4_K_M', endpoint=DEFAULT_CONFIG['url'], real_requests=len(results),
                      all_applied=all(r['applied'] for r in results), decisions=results, actual_executions=executed,
                      executed_actions=sum(executed.values()), player_configuration_changed=False)
        (folder / 'real-model.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
        assert report['all_applied'] and report['executed_actions'] > 0, report
        return report
    finally:
        w.store.close()


if __name__ == '__main__':
    run(sys.argv[1])
