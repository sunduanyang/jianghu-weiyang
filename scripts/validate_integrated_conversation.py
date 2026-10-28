"""Isolated native UI fixture and scripted local model transport, not model-quality evidence."""
import argparse
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
from pathlib import Path
import sys
import time

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT))
from jianghu import npc_attention, player_social, protagonist
from jianghu.engine import World
from jianghu.store import Store

LINES=dict(upright='既然只是四两盘缠，还请按江湖道义再衡量一回。',
           kind='我明白你也有顾虑，若还留有余力，可否再想一想？',
           balanced='不必急着定论，请把这次援助与彼此交情一并权衡。',
           defiant='成见也不必事事照旧，何妨凭你自己的心意破例一回？',
           self='我确实想走自己的路，这次求助也是我的主意，请再考虑。')


def fixture(folder):
    folder.mkdir(parents=True,exist_ok=True);path=folder/'world.sqlite'
    if path.exists():raise ValueError('Use a fresh isolated save directory')
    w=World(92,0,Store(path),durable=False)
    try:
        protagonist.perform(w,dict(action='create',revision=0,month=0,name='往来验收',sex='女',age=24,place=45))
        for name in ('陈观澜','柳长宁'):
            n=w.make_npc(place=45);n.update(name=name,age_months=360,health=100.,wealth=80.,time_left=0)
            n['monthly_actions']=[dict(id='work:self',kind='work',month=0,week=1,time_cost=4)]
            w.player['ties'][str(n['id'])]=dict(affinity=40.,trust=21.)
            w.set_goal(n,'mastery',initial=True)
        w.reindex();w.player['wealth']=1000.
        n=w.npcs[1];other=w.npcs[2]
        n['knowledge']['2']=dict(name=other['name'],place=other['place'],seen_month=0,job=other['job'])
        n['relationships']['2']=dict(affinity=40.,trust=30.)
        def settle(npc,action,choice,options=False):
            ctx=player_social.start(w,npc,action,'',player_social.state(w)['revision'])
            result=dict(choice_id=choice,reason='独立界面验收的固定决定',memory_ids=[ctx['request']['event_id']],reply=player_social.factual_reply(next(c for c in ctx['choices'] if c['id']==choice)))
            if options:result['persuasion_options']=LINES.copy()
            assert player_social.apply(w,ctx,result)[0]
        settle(1,'ask_aid','give_aid');settle(1,'introduce','introduce:2')
        settle(1,'ask_aid','refuse',True);settle(2,'ask_aid','refuse')
        for n in w.npcs.values():
            n['last_review_attempt']=n['last_review']=n['last_ai_review']=w.month
            w.npc_attention['attempts'][str(n['id'])]=npc_attention.signature(w,n)
        w.player['protagonist']['points']=30;w.checkpoint()
    finally:w.store.close()


def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--output',type=Path,required=True)
    out=parser.parse_args().output.resolve()
    if not out.is_relative_to(ROOT/'output'):parser.error('Validation output must be under repository output/')
    folder=out/'native-save';fixture(folder);calls=[]
    class Handler(BaseHTTPRequestHandler):
        def log_message(self,*args):pass
        def do_POST(self):
            body=json.loads(self.rfile.read(int(self.headers['Content-Length'])))
            data=json.loads(body['messages'][-1]['content'])
            if 'choices' in data:
                time.sleep(.3)
                key='give_aid' if data.get('reconsideration') else 'warm' if data['request']['action']=='talk' else 'refuse'
                if key not in {c['id'] for c in data['choices']}:key=data['choices'][0]['id']
                anchors=[data['request']['event_id']]
                if data.get('reconsideration'):anchors.append(data['reconsideration']['original_request']['event_id'])
                result=dict(choice_id=key,reason='验收模拟回应，保留真实规则结算',memory_ids=anchors);stage='decision'
            elif 'settlement' in data:
                key=data['decision']['choice_id'];result=dict(choice_id=key,reply=player_social.factual_reply(data['settlement']));stage='reply'
            elif 'request_goal' in data:
                result={k:data[k] for k in ('speaker','listener','request_goal')};result['lines']=LINES.copy();stage='five_styles'
            else:
                self.send_error(503,'Background NPC model requests are outside this UI fixture');return
            calls.append(dict(stage=stage,result=result))
            (out/'scripted-model-calls.json').write_text(json.dumps(calls,ensure_ascii=False,indent=2),encoding='utf-8')
            response=json.dumps(dict(message=dict(content=json.dumps(result,ensure_ascii=False)),prompt_eval_count=10,eval_count=10),ensure_ascii=False).encode()
            self.send_response(200);self.send_header('Content-Type','application/json');self.send_header('Content-Length',str(len(response)));self.end_headers();self.wfile.write(response)
    server=ThreadingHTTPServer(('127.0.0.1',0),Handler)
    config=dict(enabled=True,backend='ollama',url=f'http://127.0.0.1:{server.server_port}',model='scripted-ui-validation',auto_start=False,queue_limit=2,timeout=10)
    (folder/'data').mkdir(exist_ok=True);(folder/'data/ai_config.json').write_text(json.dumps(config),encoding='utf-8')
    (out/'fixture-ready.json').write_text(json.dumps(dict(save_dir=str(folder),model_url=config['url'],scripted_model=True)),encoding='utf-8')
    print('SCRIPTED_UI_FIXTURE_READY '+str(folder),flush=True)
    try:server.serve_forever()
    finally:server.server_close()


if __name__=='__main__':main()
