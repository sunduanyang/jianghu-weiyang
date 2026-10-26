/* Both clients submit the same deterministic combat beats. Closing suspends the clock. */
let combatRunning=false, combatSpeed=1, combatTimer=null, combatSession='';
const classicBattleRender=renderBattle;
const combatFormNames={thrust:'刺',slash:'劈',crush:'崩',focus:'凝'};
function stopCombatClock(){combatRunning=false;clearTimeout(combatTimer);}
function combatButton(a){
 const detail=a.reason||(a.reach?`${a.reach.join('–')}距 · ${a.qi}气 · ${a.forms}式${a.cast?` · 运功${a.cast}拍`:''}`:a.description);
 return `<button data-battle-action="${a.id}" ${!a.enabled||martialBusy?'disabled':''} title="${esc(a.description||a.label)}"><strong>${esc(a.label)}</strong><small>${esc(detail||'')}</small></button>`;
}
function combatFigure(f,side,distance){
 const position=50+(side?1:-1)*(9+distance*4.5),cast=f.casting;
 return `<div class="combat-actor ${side?'opponent':''}" style="left:${position}%"><div class="cast-banner">${cast?`${esc(cast.name)} · 还需${cast.remaining}拍`:esc(f.name)}</div><svg viewBox="0 0 100 160" aria-hidden="true"><ellipse cx="50" cy="155" rx="38" ry="4" opacity=".2"/><path d="M42 19L27 24L49 9L70 24L57 22L58 38L51 43L62 63L87 69L91 75L59 75L57 92L69 119L85 145L76 148L54 127L45 106L37 128L22 150L12 148L29 115L35 86L29 72L11 100L5 95L22 60L39 43Z"/><path d="M61 64L95 49L98 52L64 75Z"/></svg></div>`;
}
renderBattle=function(){
 clearTimeout(combatTimer);
 const b=martialState?.battle;
 if(!b||b.ruleset!==2){$('#battle-dialog').classList.remove('combat-v2');classicBattleRender();return;}
 if(combatSession!==b.id){combatSession=b.id;combatRunning=false;}
 const done=b.status==='finished';if(done)combatRunning=false;
 $('#battle-dialog').classList.add('combat-v2');
 const result=done?(b.winner===null?'此番平手':`${b.fighters[b.winner].name}胜出`):`第 ${b.round} 拍`;
 const hud=b.fighters.map((f,i)=>`<div>${fighterHTML(f,i)}<div class="combat-resources"><span>架势 <b>${num(f.posture)} / ${f.posture_max}</b></span><span>脚力 <b>${num(f.mobility)} / 100</b></span><span>${esc(f.mode_name)} ${f.basic_reach.join('–')}距</span></div><div class="combat-forms">${[0,1,2,3].map(n=>`<i class="${n<f.tokens.length?'on':''}">${combatFormNames[f.tokens[n]]||'·'}</i>`).join('')}<small>${f.casting?'运功中':f.parrying?'拆招待发':f.guarding?'架招护体':f.evading?'身法避实':'静候时机'}</small></div></div>`).join('');
 const choices=b.actions||[];
 const section=(label,kind)=>`<section class="combat-choice-group"><h3>${label}</h3><div class="combat-choices">${choices.filter(a=>a.kind===kind&&a.id!=='surrender'&&a.id!=='wait').map(combatButton).join('')}</div></section>`;
 $('#battle-content').innerHTML=`<div class="combat-clock"><strong>${esc(result)}</strong><span>${done?esc(b.reason):`对手将要：${esc(b.opponent_intent)}${b.opponent_action.reach?` · ${b.opponent_action.reach.join('–')}距`:''}`}</span><div><button data-combat-clock ${done?'disabled':''}>${combatRunning?'Ⅱ 暂停':'▶ 开始'} · 空格</button><button data-combat-speed ${done?'disabled':''}>${combatSpeed}倍速</button></div></div><div class="fighters combat-fighters">${hud}</div><div class="combat-stage">${b.fighters.map((f,i)=>combatFigure(f,i,b.distance)).join('')}<div class="combat-distance">身位 ${b.distance.toFixed(1)} <span>0–2 拳脚 · 2–4 兵刃</span></div></div>${done?`<div class="combat-result"><h3>${esc(result)}</h3>${b.fighters.map((f,i)=>`<p>${esc(f.name)}：命中 ${b.summary[i].hits} · 伤害 ${b.summary[i].damage} · 破势 ${b.summary[i].breaks}</p>`).join('')}<p>点到为止。体力、精力与装备损耗已结算。</p>${b.event_id?`<button data-battle-event="${b.event_id}">查看此战年谱 ↗</button>`:''}<button data-close>收剑归鞘</button></div>`:`<div class="combat-deck">${section('行招 · 普攻积式','basic')}${section('摧破 · 积式运功','art')}${section('进退 · 拆招调息','tactic')}</div><p class="combat-instructions">1 / 2 / 3 普攻 · A / D 进退 · G 架招 · F 拆招 · R 调息。普攻积攒刺、劈、崩，架招积攒可替代的凝式；强招运功期间，进退或架招会中止且不返还消耗。拆招化解普攻，摧破可穿过拆招。<button data-battle-action="surrender" class="quiet" ${martialBusy?'disabled':''}>收招认输</button></p>`}<details class="combat-record" ${done?'open':''}><summary>拆招记录 · ${b.log.length} 次行动</summary><ol class="combat-log">${[...b.log].reverse().map(r=>`<li class="${r.side===0?'player-move':'enemy-move'}"><small>${r.round}拍 · ${r.side===0?'我':'客'}</small><span>${esc(r.text)}</span></li>`).join('')}</ol></details>`;
 if(combatRunning&&!done&&!martialBusy&&$('#battle-dialog').open)combatTimer=setTimeout(combatAutoBeat,1800/combatSpeed);
};
async function combatAutoBeat(){
 if(!combatRunning||martialBusy||!$('#battle-dialog').open)return;
 martialBusy=true;
 try{const b=martialState.battle;martialState=await api('/api/battle',{battle_id:b.id,round:b.round,action:'auto'});await refresh(true)}
 catch(e){stopCombatClock();toast(e.message,true);try{martialState=await api('/api/martial')}catch{}}
 finally{martialBusy=false;renderMartial();renderBattle()}
}
document.addEventListener('click',e=>{
 if(e.target.closest('[data-combat-clock]')){combatRunning=!combatRunning;renderBattle()}
 if(e.target.closest('[data-combat-speed]')){combatSpeed=combatSpeed%3+1;renderBattle()}
});
document.addEventListener('keydown',e=>{
 if(!$('#battle-dialog').open||e.repeat||/INPUT|TEXTAREA|SELECT/.test(e.target.tagName))return;
 if(e.code==='Space'){e.preventDefault();if(martialState?.battle?.status==='active'){combatRunning=!combatRunning;renderBattle()}return}
 const mapping={a:'advance',d:'retreat',g:'guard',f:'parry',r:'breathe','1':'basic_thrust','2':'basic_slash','3':'basic_crush'};
 const id=mapping[e.key.toLowerCase()];if(id){e.preventDefault();$(`[data-battle-action="${id}"]`)?.click()}
});
$('#battle-dialog').addEventListener('close',stopCombatClock);
window.addEventListener('blur',()=>{stopCombatClock();if($('#battle-dialog').open)renderBattle()});
document.addEventListener('visibilitychange',()=>{if(document.hidden){stopCombatClock();if($('#battle-dialog').open)renderBattle()}});
