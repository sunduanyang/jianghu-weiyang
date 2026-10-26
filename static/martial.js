/* Martial UI shares the existing local API and ink/paper visual language. */
let martialState = null, martialBusy = false;
const artKinds = {inner:'内功', attack:'摧破', movement:'身法'};
const martialPanel = document.createElement('section');
martialPanel.className = 'panel martial-panel';
martialPanel.innerHTML = '<div class="panel-heading"><div><p class="eyebrow">练一身本领 · 赴一场江湖</p><h2>过客行武</h2></div><button id="martial-open" class="quiet">功法与闭关 ↗</button></div><div id="martial-summary"></div>';
$('.world-column').insertBefore(martialPanel, $('.society'));
const martialTab = document.createElement('button');
martialTab.dataset.tab = 'martial'; martialTab.textContent = '武学';
$('.tabs').insertBefore(martialTab, $('[data-tab="history"]'));
Object.assign(kinds, {player_training:'闭关', spar_start:'约战'});

function renderMartialSummary(){
 if(!world || !$('#martial-summary'))return;
 const p=world.player,m=p.martial;
 $('#martial-summary').innerHTML=`<div class="martial-summary"><div><strong>${num(p.skill,1)}</strong><small>武学</small></div><div><strong>${Object.keys(m.arts).length}</strong><small>所学功法</small></div><div><strong>${num(m.stamina)}</strong><small>精力 / 100</small></div><div><strong>${num(p.health)}</strong><small>体力 / 100</small></div></div><div class="martial-entry"><span>闭关一月，修习有成；进退之间，招式见真章。</span><button id="battle-resume" ${world.battle_active?'class="primary"':'class="quiet"'}>${world.battle_active?'继续未完的切磋 →':'演武记录 ↗'}</button></div>`;
 $$('[data-months], #auto, #new-open').forEach(el=>el.disabled=world.battle_active);
 if(world.battle_active)$('#job').textContent='演武进行中 · 江湖时间暂歇';
}

function artCard(a, interactive=true){
 if(a.combat_reach)a={...a,reach:a.combat_reach};
 const ready=a.ready, equipped=a.equipped;
 return `<article class="art-card ${equipped?'equipped':''}"><div class="art-head"><span class="art-kind">${artKinds[a.kind]} · ${a.affinity}</span><span class="art-grade">${a.grade}品</span></div><h3>${esc(a.name)} <small>${esc(a.realm_name)}</small></h3><p>${esc(a.description)}</p><div class="art-progress"><i style="width:${a.practice}%"></i><b style="left:${a.gate}%"></b></div><div class="art-progress-label"><span>修习 ${num(a.practice,1)} / ${a.gate}</span><span>${a.realm===3?'已臻化境':ready?'可突破':'尚待打磨'}</span></div>${a.kind==='attack'?`<div class="art-cost">距离 ${a.reach.join('–')} · ${a.damage_type==='internal'?'内伤':'外伤'} · ${a.qi} 内力 · ${a.forms} 式</div>`:''}${interactive?`<div class="art-buttons"><button data-martial-action="${ready?'breakthrough':'practice'}" data-art="${a.id}" ${a.realm===3?'disabled':''}>${ready?'突破关隘':'闭关修习'}</button><button data-martial-action="equip" data-art="${a.id}" class="${equipped?'equipped-button':'quiet'}">${equipped?(a.kind==='attack'?'已装配 · 卸下':'运用中'):'装配'}</button></div>`:''}</article>`;
}

function renderMartial(){
 if(!martialState)return;
 const p=martialState.player,m=p.martial,s=m.stats;
 $('#martial-content').innerHTML=`<div class="martial-overview"><div><strong>${num(p.skill,1)}</strong><small>武学</small></div><div><strong>${num(s.qi)}</strong><small>内力上限</small></div><div><strong>${num(s.outer_guard,1)} / ${num(s.inner_guard,1)}</strong><small>外伤 / 内伤护体</small></div><div><strong>${num(m.stamina)}</strong><small>精力</small></div></div><div class="practice-note"><p>闭关修习、突破、调养均推进整个江湖 <b>一个月</b>。修习与突破各需3银、12精力；每月恢复8精力。关隘为40 / 70 / 100，突破后继续精进。</p><button data-martial-action="rest">调养一月</button></div><div class="section-label">当前运功 <span>${s.affinity} · ${s.inner} · 每次移动${s.step}格 · 行囊${num(p.wealth)}银</span></div><div class="arts-grid">${m.arts.map(a=>artCard(a)).join('')}</div><div class="section-label catalog-heading">手抄武谱 <span>研读12银，不推进月份；最多装配六门摧破</span></div><div class="catalog-grid">${martialState.catalog.map(a=>`<article class="catalog-art"><div><b>${esc(a.name)}</b><small>${artKinds[a.kind]} · ${a.affinity} · 武学需 ${a.requirement}</small></div><button data-martial-action="learn" data-art="${a.id}" ${p.skill<a.requirement?'disabled':''}>研读 · 12银</button></article>`).join('')||'<p class="note">现有武谱已全部习得。</p>'}</div>`;
 $('#martial-content').querySelectorAll('button').forEach(el=>el.disabled=el.disabled||martialBusy||world.battle_active);
}

function renderNPCMartial(){
 const m=npc.martial_view;
 $('#npc-body').innerHTML=`<div class="section-label">所学与运功 <span>精力 ${num(m.stamina)}</span></div><p class="note">${esc(m.stats.inner)} · ${m.stats.affinity} · 内力 ${num(m.stats.qi)}<br>师承和修习均由人物的周行程推进。</p><div class="npc-arts">${m.arts.map(a=>artCard(a,false)).join('')}</div>`;
}
const originalRenderNPC = renderNPC;
renderNPC = function(){ originalRenderNPC(); if(npc&&tab==='martial')renderNPCMartial(); };

function fighterHTML(f, side){
 const percent=Math.max(0,f.vigor/f.vigor_max*100);
 return `<section class="fighter fighter-${side}"><div class="fighter-name"><span class="fighter-seal">${side?'客':'我'}</span><div><small>${side?'应战者':'行脚过客'} · ${esc(f.inner)}</small><h3>${esc(f.name)}</h3></div></div><div class="combat-meter"><div><span>气血</span><b>${num(f.vigor)} / ${num(f.vigor_max)}</b></div><div class="meter-track"><i style="width:${percent}%"></i></div></div><div class="combat-meter qi"><div><span>内力</span><b>${num(f.qi)} / ${num(f.qi_max)}</b></div><div class="meter-track"><i style="width:${Math.max(0,f.qi/f.qi_max*100)}%"></i></div></div><div class="forms">${[0,1,2,3].map(i=>`<i class="${i<f.forms?'filled':''}">式</i>`).join('')}<span>${f.guarding?'守势护体':f.evading?'身法避实':'待招'}</span></div><small>外伤 ${num(f.outer_wounds,1)} · 内伤 ${num(f.inner_wounds,1)}</small></section>`;
}

function renderBattle(){
 const b=martialState?.battle;
 if(!b){$('#battle-content').innerHTML='<div class="empty battle-empty"><span>武</span><h2>尚未与人切磋</h2><p>在人物档案中选择一位成年、体力充足的江湖人，<br>点击「切磋」进入演武场。</p></div>';return}
 const finished=b.status==='finished';
 const result=finished?(b.winner===null?'此番平手':`${b.fighters[b.winner].name}胜出`):`第 ${b.round} 合`;
 $('#battle-content').innerHTML=`<div class="battle-caption"><span>${finished?'已收招':'江湖暂停 · 每次行动后对手应一招'}</span><strong>${esc(result)}</strong><span>${finished?esc(b.reason):`对手意图：${esc(b.opponent_intent)}`}</span></div><div class="fighters">${b.fighters.map(fighterHTML).join('')}</div><div class="distance-field"><span class="distance-label">身位距离</span><div class="distance-track">${[0,1,2,3,4,5].map(d=>`<span class="${d===b.distance?'at-distance':''}"><b>${d}</b><small>${['贴身','近身','短距','中距','远距','极远'][d]}</small></span>`).join('')}</div><p>长桥拳 0–2 · 青溪剑 2–4 · 进退触发身法闪避</p></div>${finished?`<div class="battle-result"><p>切磋点到为止，体力与精力已结算，结果记入对方年谱。</p>${b.event_id?`<button data-battle-event="${b.event_id}" class="quiet">查看此战年谱 ↗</button>`:''}<button data-close class="primary">收剑归鞘</button></div>`:`<div class="combat-actions">${b.actions.map(a=>`<button data-battle-action="${a.id}" ${!a.enabled||martialBusy?'disabled':''} title="${esc(a.reason||a.label)}" class="${a.damage_type?'attack-action':a.id==='surrender'?'quiet surrender':'tactic-action'}"><strong>${esc(a.label)}</strong><small>${a.reason?esc(a.reason):a.damage_type?`${a.reach.join('–')}距 · ${a.qi}气 · ${a.forms}式`:({advance:'靠近对手',retreat:'拉开距离',guard:'减伤 · 蓄式',breathe:'恢复12内力',surrender:'结束切磋'}[a.id])}</small></button>`).join('')}</div><p class="battle-help">命中与守势积攒「式」；强招消耗内力与式。守势减伤，调息回气，六十合后按剩余气血比例裁定。关闭窗口可暂存战局。</p>`}<div class="section-label">拆招记录 <span>最近行动在上 · 全部过程随战局保存</span></div><ol class="combat-log">${[...b.log].reverse().map(row=>`<li class="${row.side===0?'player-move':'enemy-move'}"><small>${row.round}合 · ${row.side===0?'我':'客'}</small><span>${esc(row.text)}</span></li>`).join('')||'<li><small>开场</small><span>双方站定，距离三格。请择招先行。</span></li>'}</ol>`;
}

async function openMartial(battle=false){
 try{martialState=await api('/api/martial');if(battle){renderBattle();if(!$('#battle-dialog').open)$('#battle-dialog').showModal()}else{renderMartial();if(!$('#martial-dialog').open)$('#martial-dialog').showModal()}}catch(e){toast(e.message,true)}
}

// Replace the original one-click outcome with an actual bout.
$('#challenge').onclick=async()=>{
 if(martialBusy)return;
 martialBusy=true;
 try{martialState=await api('/api/intervene',{npc_id:selected,action:'challenge'});await refresh(true);renderBattle();$('#battle-dialog').showModal()}catch(e){toast(e.message,true)}finally{martialBusy=false;renderBattle()}
};

document.addEventListener('click',async e=>{
 if(e.target.closest('#martial-open')){await openMartial();return}
 if(e.target.closest('#battle-resume')){await openMartial(true);return}
 const eventButton=e.target.closest('[data-battle-event]');
 if(eventButton){$('#battle-dialog').close();await showEvent(Number(eventButton.dataset.battleEvent));return}
 const artButton=e.target.closest('[data-martial-action]'), battleButton=e.target.closest('[data-battle-action]');
 if((!artButton&&!battleButton)||martialBusy)return;
 martialBusy=true;
 if(artButton)renderMartial(); else renderBattle();
 try{
  if(artButton)martialState=await api('/api/martial',{action:artButton.dataset.martialAction,art:artButton.dataset.art});
  else{const b=martialState.battle;martialState=await api('/api/battle',{battle_id:b.id,round:b.round,action:battleButton.dataset.battleAction})}
  await refresh(true);
 }catch(error){toast(error.message,true);try{martialState=await api('/api/martial')}catch{}}
 finally{martialBusy=false;renderMartial();renderBattle()}
});

// Main script's initial request may finish before this script has loaded.
if(world)renderMartialSummary();
