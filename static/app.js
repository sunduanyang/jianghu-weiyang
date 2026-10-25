const $ = s => document.querySelector(s);
const $$ = s => [...document.querySelectorAll(s)];
const esc = s => String(s ?? '').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const traits={compassion:'仁厚',ambition:'进取',caution:'谨慎',sociability:'外向',loyalty:'重义'};
const beliefs={trust:'人可相托',justice:'善有善报',tradition:'传承有益'};
const goals={wealth:'安家立业',mastery:'武学有成',belonging:'有所归属',renown:'名动一方',freedom:'行遍江湖',revenge:'讨回公道'};
const deathCauses={old_age:'衰老',illness:'疾病',injury:'伤重',accident:'意外',starvation:'饥饿',disaster:'灾害',childbirth:'难产'};
const kinds={pregnancy:'有孕',pregnancy_loss:'夭折',illness:'染病',recovery:'康复',accident:'意外',epidemic:'时疫',guardianship:'抚养',career:'成长',appointment:'任官',government_founded:'县衙',government_relief:'官济',public_budget:'施政',stipend:'俸银',crime_report:'报案',judgment:'判决',public_service:'公务',family:'育儿',treatment:'求医',death_notice:'讣告',census:'乡籍',birth:'新生',death:'故人',marriage:'姻缘',betrayal:'反目',duel:'旧怨',disaster:'水患',promotion:'推举',goal_complete:'如愿',goal_change:'转志',aid:'接济',mentor:'师承',player_talk:'过客',player_gift:'过客',player_duel:'切磋',join:'入会',friendship:'相交',teaching:'传艺',trade:'合营',court:'情意',rejection:'婉拒',loss:'失银',secret_theft:'隐事',faction_aid:'互济',work:'谋生',rest:'休养',train:'习武',travel:'远行',duty:'尽责',rumor:'见闻'};
const actionNames={family:'筹划育儿',treatment:'求医',report:'报官',public_service:'公务',work:'谋生',rest:'休养',train:'习武',travel:'远行',befriend:'结交',court:'倾诉',propose:'提亲',aid:'接济',trade:'合营',betray:'窃银',duel:'寻仇',mentor:'拜师',teach:'传艺',join:'入会',duty:'尽责',rumor:'交换见闻'};
let world=null, selected=null, npc=null, tab='life', history=[], allEvents=false, fetching=false, selectedBusy=false, lastDetailKey='', modeInitialized=false, timer;
const num=(v,d=0)=>Number(v??0).toFixed(d);
const when=m=>`景和${Math.floor(m/12)+1}年${m%12+1}月`;
const person=id=>world?.npcs.find(n=>n.id===Number(id));
const place=id=>world?.places.find(p=>p.id===Number(id))?.name??'未知';
const shortAction=id=>{const [k,t]=id.split(':');return (actionNames[k]??k)+(t==='self'?'':` · ${k==='travel'?place(t):['join','duty'].includes(k)?world.factions[Number(t)]?.name:k==='report'?'证据 #'+t:person(t)?.name??t}`)};

// Portraits are decorative archetypes; identity and history come from the save.
function portrait(n){
 const female=n.sex==='女', years=Math.floor(n.age_months/12);
 const slot=years<16?(female?5:4):years>=60?(female?3:2):((Number(n.id)%3===0)?(female?7:6):(female?1:0));
 return `<div class="avatar portrait ${n.alive?'':'dead'}" aria-hidden="true" style="--portrait-x:${slot%4/3*100}%;--portrait-y:${slot<4?0:100}%"></div>`;
}

async function api(path,body){const r=await fetch(path,body===undefined?{}:{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});const data=await r.json();if(!r.ok)throw Error(data.error??'请求失败');return data}
function toast(s,error=false){$('#toast').textContent=s;$('#toast').style.background=error?'#8f5344':'#29493d';$('#toast').classList.add('show');clearTimeout(timer);timer=setTimeout(()=>$('#toast').classList.remove('show'),3500)}
async function act(path,body,message){try{const r=await api(path,body);if(message)toast(message);await refresh(true);return r}catch(e){toast(e.message,true)}}

async function refresh(force=false){if(fetching)return;fetching=true;try{world=await api('/api/state');renderWorld();if(selected){const key=`${selected}/${world.month}/${world.ai.successes}/${world.events}/${world.ai.pending}`;if(force||key!==lastDetailKey){await loadNPC();lastDetailKey=key}}}catch(e){$('#job').textContent='连接暂不可用';if(!world)toast(e.message,true)}finally{fetching=false}}
function renderWorld(){
 if(!modeInitialized){$('#fast').checked=!world.ai.config.enabled;modeInitialized=true}
 $('#date').textContent=world.date;$('#job').textContent=world.job.error||(world.ai_budget_wait?'重要决策推理中 · 已留时间预算':`${world.job.label}${world.job.remaining?` ${world.job.completed}/${world.job.total}`:''}`);
 $('#running-dot').classList.toggle('running',world.job.remaining>0||world.autoplay);
 $('#auto').textContent=world.autoplay?'Ⅱ 停止演化':'▶ 自行演化';$('#mode').textContent=world.ai.config.enabled?'AI · 本地共享模型':'规则模式 · 无模型';$('#mode').classList.toggle('ai',world.ai.config.enabled);
 $('#population').textContent=world.population;$('#player-money').textContent=`行囊 ${num(world.player.wealth)} 银`;
 if($('#place-filter').options.length===1)world.places.forEach(p=>$('#place-filter').add(new Option(p.name,p.id)));
 renderPeople();renderMap();renderSociety();
 $('#stats').innerHTML=[['friendships','相知'],['marriages','婚姻'],['feuds','仇怨'],['events','真实事件']].map(([k,l])=>`<div class="stat"><strong>${world[k]>9999?(world[k]/10000).toFixed(1)+'万':world[k]}</strong><small>${l}</small></div>`).join('');
 if(!allEvents)$('#world-events').innerHTML=world.world_events.length?world.world_events.map(eventHTML).join(''):'<p class="note">江湖初立，往后的岁月将留下故事。</p>';
 const ai=world.ai, ms=world.metrics.simulation_ms/Math.max(1,world.metrics.steps);
 $('#performance').innerHTML=`<div class="perf-grid"><div class="perf-cell"><strong>${num(ms,1)}<small> ms</small></strong><small>每月引擎耗时¹</small></div><div class="perf-cell"><strong>${ai.calls}</strong><small>模型调用 / 已采用 ${world.metrics.ai_applied}</small></div><div class="perf-cell"><strong>${num(ai.average_ms/1000,1)}<small> s</small></strong><small>平均推理时间</small></div><div class="perf-cell"><strong>${ai.pending}</strong><small>排队含推理 / 改选 ${world.metrics.ai_changed_actions}</small></div></div>`;
 $('#ai-note').innerHTML=esc(ai.last_error?`推理失败，规则继续运行：${ai.last_error}`:ai.config.enabled?`已完成 ${ai.successes} · 失败 ${ai.failed} · 拒收 ${ai.rejected} · ${ai.prompt_tokens+ai.completion_tokens} tokens。模型仅提交提案，规则结算后果。`:'当前全部行为由规则驱动。启用本地模型后，人物会基于自身经历决定计划并提出心理变化。')+'<br>¹ 不含存档与推理耗时；大跨度预演与规则快进均明确跳过模型调用。';
 $('#footer-status').textContent=`种子 ${world.seed} · ${world.total_people} 份人生档案 · 每月自动存档`;
 if(!selected&&world.npcs.length){selected=world.npcs.find(n=>n.alive&&n.spouse)?.id??world.npcs[0].id;loadNPC()}
}
function renderPeople(){
 if(!world)return;
 const q=$('#search').value.trim(), f=$('#place-filter').value, alive=$('#alive-filter').value, sort=$('#sort').value;
 const list=world.npcs.filter(n=>(alive!=='alive'||n.alive)&&(f===''||n.place===Number(f))&&(!q||n.name.includes(q)||n.job.includes(q)));
 list.sort((a,b)=>sort==='id'?a.id-b.id:b[sort]-a[sort]);
 // Keep focus and portrait DOM stable while a paused world is being polled.
 const key=JSON.stringify([selected,list.map(n=>[n.id,n.name,n.job,n.place,n.age_months,n.alive,n.sex])]);
 if(renderPeople.lastKey===key)return;
 renderPeople.lastKey=key;
 const focusedPerson=document.activeElement?.closest('.person-row')?.dataset.person;
 $('#people').innerHTML=list.map(n=>`<div class="person-row ${n.id===selected?'selected':''}" data-person="${n.id}" tabindex="0" role="button" aria-label="查看${esc(n.name)}">${portrait(n)}<div><b>${esc(n.name)}</b><small>${esc(n.job)} · ${esc(place(n.place))}</small></div><span class="age">${Math.floor(n.age_months/12)}岁${n.alive?'':' · 故'}</span></div>`).join('')||'<p class="note" style="padding:15px">未寻到此人。</p>';
 if(focusedPerson!==undefined)$('#people').querySelector(`[data-person="${Number(focusedPerson)}"]`)?.focus({preventScroll:true});
}

function renderMap(){
 const currentPlace=$('#place-filter').value;
 const key=`${world.database}/${world.month}/${world.population}/${selected}/${currentPlace}`;
 if(renderMap.lastKey===key)return;
 renderMap.lastKey=key;
 const focusedPlace=document.activeElement?.closest('.map-site')?.dataset.place;
 // Illustration coordinates only; simulation geography remains in world.places.
 const artPlaces=[{x:34,y:78},{x:24,y:18},{x:82,y:52},{x:132,y:86},{x:138,y:28}];
 const at=p=>artPlaces[p.id]??{x:12+p.x*1.36,y:4+p.y*1.06};
 const locations=world.places.map(p=>({...p,...at(p)}));
 let dots='';
 locations.forEach(p=>{
  const locals=world.npcs.filter(n=>n.alive&&n.place===p.id);
  locals.forEach((n,i)=>{
   const angle=i*2.399963, radius=4.5+Math.sqrt(i)*.52;
   const x=p.x+Math.cos(angle)*radius,y=p.y+Math.sin(angle)*radius;
   dots+=`<g data-person="${n.id}"><title>${esc(n.name)} · ${esc(n.goal.title)}</title><circle cx="${x}" cy="${y}" r="1.65" fill="transparent"/><circle class="map-npc ${n.id===selected?'current':''}" cx="${x}" cy="${y}" r="${n.id===selected?1.1:.55}" fill="${n.id===selected?'#994c39':'#54664d'}" opacity="${n.id===selected?1:.72}"/></g>`;
  });
 });
 const buildings=[
  'M-2.4 0 L0-1.8 2.4 0 M-1.9-.3 V2 H1.9 V-.3 M-.4 2 V.6 H.4 V2',
  'M-2.6 0 Q0-1 2.6 0 L0-2.2 Z M-1.6 .1 V2 H1.6 V.1 M-2 1 H2 M0-2.2 V-3',
  'M-2.7 2 V-1 H-1.7 V-.3 H-.8 V-1 H.2 V-.3 H1.2 V-1 H2.7 V2 Z M-.5 2 V.6 H.5 V2',
  'M-3 1 Q0 3 3 1 L2.2 2 H-2.2 Z M0 1 V-2.5 L2 -.2 H0',
  'M-2.7 2 V-1 L-1.5-2 -1-1 V2 M1 2 V-1 L1.6-2 2.7-1 V2 M-1 0 H1 M-.5 2 V1 H.5 V2'
 ];
 const sites=locations.map((p,i)=>`<g class="map-site" data-place="${p.id}" tabindex="0" role="button" aria-label="筛选${esc(p.name)}，${p.population}人"><title>${esc(p.name)} · ${p.population}人在世，点击筛选</title><circle class="site-ring" cx="${p.x}" cy="${p.y}" r="3.8" style="${String(p.id)===currentPlace?'stroke:#994c39;stroke-width:.8':''}"/><path class="site-building" transform="translate(${p.x} ${p.y})" d="${buildings[i%buildings.length]}"/><rect class="site-nameplate" x="${p.x-10}" y="${p.y+8}" width="20" height="9" rx=".3"/><text class="site-label" x="${p.x}" y="${p.y+11.7}" text-anchor="middle">${esc(p.name)}</text><text class="site-count" x="${p.x}" y="${p.y+15}" text-anchor="middle">${p.population}人驻足</text></g>`).join('');
 $('#map').innerHTML=`<div class="map-title">山河故人<small>临川五地舆图</small></div><span class="map-seal" aria-hidden="true">景和山河</span><svg viewBox="0 0 160 106.6667" preserveAspectRatio="xMidYMid meet" role="group" aria-label="五地江湖人物分布图"><path d="${locations.map((p,i)=>`${i?'L':'M'}${p.x} ${p.y}`).join(' ')} Z" fill="none" stroke="#8a7950" stroke-width=".22" stroke-dasharray=".8 1.5" opacity=".5"/>${dots}${sites}</svg><div class="map-legend">● 在世之人 &nbsp; <span style="color:var(--red)">◆</span> 当前人物 &nbsp; · 聚落可筛选</div>`;
 if(focusedPlace!==undefined)$('#map').querySelector(`[data-place="${Number(focusedPlace)}"]`)?.focus();
}

function changeText(c){return `${(c.domain==='belief'?beliefs[c.axis]:traits[c.axis])??c.axis} ${c.delta>0?'+':''}${num(c.delta,2)}`}
function lifeStatus(n){return `${n.death?`<p class="life-status">${when(n.death.month)} · 因${esc(n.death.cause_label)}离世 · <span class="fact-link" data-event="${n.death.event_id}">死因与继承 ↗</span></p>`:''}${n.pregnancy?`<p class="life-status">正在妊娠 · 预计${when(n.pregnancy.due)}生产 · 伴侣 ${esc(person(n.pregnancy.father)?.name)}</p>`:''}${n.illness?`<p class="life-status">染病中 · 病情 ${num(n.illness.severity)} / 100 · <span class="fact-link" data-event="${n.illness.event_id}">起因 ↗</span></p>`:''}`}
function renderSociety(){
 const d=world.demography,g=world.government;if(!d||!g)return;
 const generations=Object.entries(d.generations).map(([k,v])=>`<span>第${Number(k)+1}代 ${v}人</span>`).join('');
 const deaths=Object.entries(d.deaths).map(([k,v])=>`${deathCauses[k]??k} ${v}`).join(' · ')||'尚无死亡';
 $('#society').innerHTML=`<div class="society-body"><div class="demography-grid"><div><strong>${world.metrics.births}</strong><small>累计新生</small></div><div><strong>${world.metrics.deaths}</strong><small>累计故人</small></div><div><strong>${d.children}</strong><small>未成年</small></div><div><strong>${d.pregnancies}</strong><small>在孕家庭</small></div></div><div class="generation-bands">${generations}</div><p class="note">死因：${esc(deaths)}</p><div class="section-label">${esc(g.name)} <span>五地共治 · 税率 ${num(g.tax_rate*100,1)}%</span></div><div class="government-offices">${Object.entries(g.offices).map(([role,id])=>`<div><small>${esc(role)}</small>${id?`<button class="quiet" data-person="${id}">${esc(person(id)?.name)}</button>`:'<span class="muted">待补缺</span>'}</div>`).join('')}</div><p class="note">库银 ${num(g.fund,1)} · 累计收入 ${num(g.revenue,1)} · 支出 ${num(g.expenses,1)}<br>治安 ${num(g.security*100)} · 公医 ${num(g.sanitation*100)} · 官员接替 ${g.succession_count}次 · 已判案 ${g.closed_cases}件</p><div class="institution-list">${world.factions.map(f=>`<div><b>${esc(f.name)}</b><small>${esc(f.ethos)} · ${world.npcs.filter(n=>n.alive&&n.faction===f.id).length}名在世成员 · 公帑 ${num(f.fund)}银</small>${f.leader?`<button class="quiet" data-person="${f.leader}">主持 ${esc(person(f.leader)?.name)}</button>`:''}</div>`).join('')}</div></div>`;
}
function eventHTML(e){return `<div class="event" data-event="${e.id}"><div class="event-time">${esc(e.date)}</div><div class="event-text"><span class="event-kind">${kinds[e.kind]??esc(e.kind)}</span>${esc(e.summary)}${e.perspective?`<blockquote class="historical-thought">“${esc(e.perspective.interpretation)}”<small>${e.perspective.ai?'AI解释':'当时理解'} · ${esc(e.perspective.source)}</small></blockquote>${e.perspective.changes.length?`<small>${e.perspective.changes.map(changeText).join(' · ')}</small>`:''}`:''}</div></div>`}
async function selectNPC(id){selected=Number(id);lastDetailKey='';tab='life';history=[];$$('.tabs button').forEach(b=>b.classList.toggle('active',b.dataset.tab===tab));renderPeople();renderMap();await loadNPC();if(innerWidth<950)$('#npc-content').scrollIntoView({behavior:'smooth',block:'start'})}
async function loadNPC(){if(selectedBusy||!selected)return;selectedBusy=true;const id=selected;try{const n=await api('/api/npc/'+id);if(id!==selected)return;npc=n;history=n.history;$('#npc-empty').hidden=true;$('#npc-content').hidden=false;renderNPC()}catch(e){toast(e.message,true)}finally{selectedBusy=false}}
function renderNPC(){if(!npc)return;const n=npc;$('#npc-heading').innerHTML=`<div class="npc-heading">${portrait(n)}<div><p>${n.alive?'此生仍在继续':'此生已入史册'} / 乡籍 ${String(n.id).padStart(3,'0')}</p><h2>${esc(n.name)}</h2><p>${n.sex} · ${Math.floor(n.age_months/12)}岁 · 第${(n.generation??0)+1}代 · ${esc(n.job)}${n.office?" · "+esc(n.office):""} · ${place(n.place)}${n.faction!==null?' · '+world.factions[n.faction].name:''}</p></div></div><div class="npc-summary"><span>银两<strong>${num(n.wealth)}</strong></span><span>武学<strong>${num(n.skill,1)}</strong></span><span>体力<strong>${num(n.health)}</strong></span><span>声望<strong>${num(n.reputation)}</strong></span></div>`;$('#player-money').textContent=`行囊 ${num(world.player.wealth)} 银`;if(tab==='life')renderLife();if(tab==='mind')renderMind();if(tab==='relations')renderRelations();if(tab==='history')renderHistory();$('#review').textContent=n.age_months<120?'幼年照护中':n.ai_pending?'反思排队中…':'请其反思 ↗';$('#review').disabled=n.ai_pending||!n.alive||n.age_months<120;$('#gift').disabled=!n.alive;$('#challenge').disabled=!n.alive||n.age_months<216||n.health<=30}
function renderLife(){const n=npc,g=n.goal,p=n.plan;$('#npc-body').innerHTML=lifeStatus(n)+`<div class="section-label">一生所求 <span>始于${when(g.since)}</span></div><div class="goal-card"><h3>${esc(g.title)}</h3><p>愿在${g.horizon_years}年间走近此志 · 已坚持${(((n.death?.month??world.month)-g.since)/12).toFixed(1)}年</p><div class="progress"><i style="width:${g.progress*100}%"></i></div><p>当前资源 / 关系进展 ${num(g.progress*100)}% · ${g.kind==='wealth'?'家资期望 '+num(g.target)+' 银':g.kind==='mastery'?'武学期望 '+num(g.target):g.kind==='renown'?'声望期望 '+num(g.target):'须经多年持续追求'}${g.cause?` · <span class="fact-link" data-event="${g.cause}">缘起 #${g.cause} ↗</span>`:''}</p></div><div class="section-label">眼前打算 <span>${p?esc(p.source):'等待下月'}</span></div>${p?`<div class="plan-card"><small>${when(p.created)} · ${p.horizon}</small><p>${esc(p.reason)}</p><div class="plan-steps">${p.steps.map(s=>`<span class="plan-step">${esc(shortAction(s))}</span>`).join('')||'<span class="muted">计划已执行，待下月调整</span>'}</div></div>`:'<p class="note">尚未开始行动。</p>'}<div class="section-label">最近亲历 <span>事实与理解分开保存</span></div>${[...n.memories].sort((a,b)=>b.event_id-a.event_id).slice(0,4).map(memoryHTML).join('')}${n.reviews.length?`<div class="section-label">最近 AI 决策</div>${n.reviews.slice(-1).map(r=>`<div class="plan-card"><p>${esc(r.reason)}</p><small>规则参考：${esc(shortAction(r.baseline))} → AI：${esc(shortAction(r.action_id))}</small></div>`).join('')}`:''}`}
function memoryHTML(m){return `<div class="memory"><p class="fact-link" data-event="${m.event_id}"><small>${when(m.month)} · #${m.event_id} · ${esc(m.source)}</small><br>${esc(m.fact)}</p><blockquote>“${esc(m.interpretation)}”</blockquote><small>${m.ai?'本地AI解释 · 变化由心理机制结算':'规则解释 · 当前为可观察的基础人格机制'}</small>${m.changes.length?`<div class="changes">${m.changes.map(changeText).join(' · ')}</div>`:''}</div>`}
function bar(label,value,origin=null,max=100){return `<div class="trait-row"><span>${label}</span><div class="trait-track"><div class="trait-fill" style="width:${Math.max(0,Math.min(100,value/max*100))}%"></div>${origin!==null?`<i class="trait-origin" style="left:${origin/max*100}%" title="初始值 ${num(origin,1)}"></i>`:''}</div><span class="trait-number">${num(value,1)}${origin!==null?` (${value-origin>=0?'+':''}${num(value-origin,1)})`:''}</span></div>`}
function renderMind(){const n=npc;$('#npc-body').innerHTML=`<div class="mind-section"><div class="section-label">稳定倾向 <span>金线为初始值 · 括号为累计变化</span></div>${Object.entries(traits).map(([k,l])=>bar(l,n.traits[k],n.initial_traits[k])).join('')}</div><div class="mind-section"><div class="section-label">当下情绪 <span>随时间衰减</span></div><div class="mood-row">${Object.entries({joy:'喜悦',anger:'愤怒',fear:'不安'}).map(([k,l])=>`<span>${l} ${num(n.mood[k])}</span>`).join('')}</div></div><div class="mind-section"><div class="section-label">对世界的信念</div>${Object.entries(beliefs).map(([k,l])=>bar(l,n.beliefs[k]*100)).join('')}</div><div class="mind-section"><div class="section-label">长期欲望</div>${Object.entries(goals).map(([k,l])=>bar(l,n.desires[k])).join('')}</div><div class="section-label">塑造此人的经历 <span>按重要性</span></div>${[...n.memories].sort((a,b)=>b.salience-a.salience||b.month-a.month).slice(0,8).map(memoryHTML).join('')}`}
function renderRelations(){const n=npc;const links=[['spouse','伴侣'],['mentor','师承']].filter(([k])=>n[k]).map(([k,l])=>`<p class="note">${l}：<span class="fact-link" data-person="${n[k]}">${esc(person(n[k])?.name)}</span></p>`).join('');$('#npc-body').innerHTML=lifeStatus(n)+links+(n.guardian?`<p class="note">监护人：<span class="fact-link" data-person="${n.guardian}">${esc(person(n.guardian)?.name)}</span></p>`:'')+(n.parents.length?`<p class="note">父母：${n.parents.map(i=>`<span class="fact-link" data-person="${i}">${esc(person(i)?.name)}</span>`).join('、')}</p>`:'')+(n.children.length?`<p class="note">子女：${n.children.map(i=>`<span class="fact-link" data-person="${i}">${esc(person(i)?.name)}</span>`).join('、')}</p>`:'')+`<div class="section-label">此人眼中的关系 <span>${n.relations_view.length}人</span></div><div class="relations-grid">${[...n.relations_view].sort((a,b)=>Math.abs(b.affinity)-Math.abs(a.affinity)).map(r=>`<div class="relation" data-person="${r.other}"><div class="relation-head"><b>${esc(r.name)}${r.alive?'':' · 故'}</b><span>${esc(r.label)}</span></div><div class="relation-values"><span>情分 ${num(r.affinity)}</span><span>信任 ${num(r.trust)}</span><span>爱慕 ${num(r.romance)}</span>${r.last_event?`<span class="fact-link" data-event="${r.last_event}">缘由 ↗</span>`:''}</div></div>`).join('')||'<p class="note">尚未建立个人关系。</p>'}</div>`}
function renderHistory(){const n=npc;$('#npc-body').innerHTML=`<div class="section-label">真实年谱 <span>新到旧 · 原始事件</span></div><p class="note">${n.parents.length?'自出生以来的事件均由模拟生成。':'乡籍是初始条件；登记以前的人生没有补写。'}每条记录可展开共同事实与前因。</p><div id="history-events">${history.map(eventHTML).join('')}</div><button id="more-history" class="quiet" style="width:100%">继续往前翻 ↓</button>`}

async function showEvent(id){try{const e=await api('/api/event/'+id);if(!e)return;$('#event-detail').innerHTML=`<p class="eyebrow">${esc(e.date)} · 事件 #${e.id} · ${kinds[e.kind]??esc(e.kind)}</p><h3>${esc(e.summary)}</h3><div class="event-people">${e.people.map(i=>`<button data-person="${i}">${esc(person(i)?.name??i)}</button>`).join('')}</div><div class="section-label">客观结算 <span>玩家可查看全知账本；人物仅知亲历或听闻</span></div><pre class="event-facts">${esc(JSON.stringify(e.facts,null,2))}</pre><div class="section-label" style="margin-top:20px">前因</div><div class="cause-list">${e.cause_events.map(c=>c?eventHTML(c):'').join('')||'<p class="note">初始条件或外部自然事件，无先行事件。</p>'}</div>`;if(!$('#event-dialog').open)$('#event-dialog').showModal()}catch(e){toast(e.message,true)}}
async function showLogs(){try{const logs=await api('/api/ai/logs');$('#logs').innerHTML=logs.length?logs.map(l=>`<article class="log-card"><div class="section-label">${esc(person(l.npc_id)?.name)} · ${when(l.month)}<span>${num(l.latency_ms/1000,2)}s · ${l.applied?'已采用':l.error?'失败':'被拒收'}</span></div><p>${esc(l.validation??l.error??'')}</p><p>${esc(l.response?.reason??'')}</p><details><summary>查看输入、原始输出与验证记录</summary><pre>${esc(JSON.stringify(l,null,2))}</pre></details></article>`).join(''):'<p>尚无模型调用。启用本地模型后，可对选中人物请求一次反思。</p>';$('#logs-dialog').showModal()}catch(e){toast(e.message,true)}}

document.addEventListener('click',async e=>{
 const event=e.target.closest('[data-event]');if(event){await showEvent(Number(event.dataset.event));return}
 const personEl=e.target.closest('[data-person]');if(personEl){$$('dialog[open]').forEach(d=>d.close());await selectNPC(personEl.dataset.person);return}
 const placeEl=e.target.closest('[data-place]');if(placeEl){const filter=$('#place-filter');filter.value=filter.value===placeEl.dataset.place?'':placeEl.dataset.place;renderPeople();renderMap();return}
 const close=e.target.closest('[data-close]');if(close){close.closest('dialog').close();return}
 const month=e.target.closest('[data-months]');if(month){await act('/api/control',{months:Number(month.dataset.months),fast:$('#fast').checked});return}
 const tb=e.target.closest('[data-tab]');if(tb){tab=tb.dataset.tab;$$('.tabs button').forEach(b=>b.classList.toggle('active',b===tb));renderNPC();return}
 if(e.target.id==='more-history'){const more=await api(`/api/events?npc=${selected}&before=${history.at(-1)?.id??0}`);history.push(...more);renderHistory();if(!more.length)toast('已到此人生的起点。')}
});
document.addEventListener('keydown',e=>{if((e.key==='Enter'||e.key===' ')&&e.target.matches('.person-row,.map-site')){e.preventDefault();e.target.dispatchEvent(new MouseEvent('click',{bubbles:true}))}});
['search','place-filter','alive-filter','sort'].forEach(id=>$('#'+id).addEventListener(id==='search'?'input':'change',()=>{renderPeople();if(id==='place-filter')renderMap()}));
$('#random').onclick=()=>{const living=world.npcs.filter(n=>n.alive);selectNPC(living[Math.floor(Math.random()*living.length)].id)};
$('#auto').onclick=()=>act('/api/control',{action:'auto'});$('#pause').onclick=()=>act('/api/control',{action:'pause'},'江湖已暂停，存档已保存');$('#save').onclick=()=>act('/api/save',{},'已保存到本地 SQLite');
$('#gift').onclick=()=>act('/api/intervene',{npc_id:selected,action:'gift'},'10银已交付，经历已入年谱');$('#challenge').onclick=()=>act('/api/intervene',{npc_id:selected,action:'challenge'},'切磋已由规则结算');
$('#review').onclick=async()=>{const r=await act('/api/ai/review',{npc_id:selected});if(r)toast(r.scheduled?'已进入共享推理队列':'已在排队，稍候可查看计划和解释')};
$('#talk-form').onsubmit=async e=>{e.preventDefault();const r=await act('/api/intervene',{npc_id:selected,action:'talk',text:$('#talk').value});if(r){$('#talk').value='';$('#reply').textContent='你的话已经成为一段真实记忆。此人将按自己的处境继续取舍。'}};
$('#model-open').onclick=()=>{const c=world.ai.config;for(const [k,v] of Object.entries(c)){const el=$('#model-form').elements[k];if(el){if(el.type==='checkbox')el.checked=v;else el.value=v}}$('#model-dialog').showModal()};
$('#model-form').onsubmit=async e=>{e.preventDefault();const body=Object.fromEntries(new FormData(e.target));body.enabled=e.target.elements.enabled.checked;const r=await act('/api/ai/config',body,'模型设置已保存');if(r)$('#model-dialog').close()};
$('#new-open').onclick=()=>$('#new-dialog').showModal();$('#new-form').onsubmit=async e=>{e.preventDefault();const r=await act('/api/new',Object.fromEntries(new FormData(e.target)),'新江湖已建立，旧存档保留');if(r){selected=null;npc=null;lastDetailKey='';$('#new-dialog').close();await refresh(true)}};
$('#logs-open').onclick=showLogs;
$('#events-all').onclick=async()=>{allEvents=!allEvents;const es=await api('/api/events'+(allEvents?'':'?important=1'));$('#world-events').innerHTML=es.map(eventHTML).join('');$('#events-all').textContent=allEvents?'仅看大事 ↗':'所有记录 ↗'};
refresh();setInterval(()=>{if(!document.hidden)refresh()},1600);
