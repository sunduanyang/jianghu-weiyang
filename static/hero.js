/* The protagonist uses the existing player's money, inventory and martial arts. */
const heroUI = {
  pending: false, signature: '',
  paused() { return !world.autoplay && !world.job.remaining && !world.battle_active; },
  render() {
    const h=world.hero, p=world.player;
    if(!h)return;
    const key=JSON.stringify([world.database,h,p.wealth,p.health,this.paused(),this.pending]);
    if(key===this.signature)return;
    this.signature=key;
    const locked=!this.paused()||this.pending;
    $('#hero-end-turn').disabled=locked||!h.created;
    $('#hero-end-turn').textContent=this.pending?'正在结算…':'结束本月 →';
    $('#hero-panel').innerHTML=h.created?`
      <div class="hero-heading"><div class="hero-portrait">${portrait({id:1,sex:h.sex,age_months:h.age_months,alive:true})}<span>主角</span></div><div class="hero-identity"><p class="eyebrow">吾身入江湖 / 第${h.turn}回合</p><h2>${esc(h.name)} <small>${h.sex} · ${Math.floor(h.age_months/12)}岁${h.age_months%12?` ${h.age_months%12}个月`:''}</small></h2><p>◎ ${esc(h.place_name)} · ${esc(h.position_name)} <small>（${h.row+1}，${h.col+1}）</small></p></div><div class="hero-assets"><b>${num(p.wealth,2)}<small> 银</small></b><span>气血 ${num(p.health)} / 100</span></div></div>
      <div class="hero-month"><div><b>本月行动力</b><strong>${h.points}<small> / ${h.max_points}</small></strong></div><div class="hero-points" role="progressbar" aria-label="本月剩余行动力" aria-valuenow="${h.points}" aria-valuemin="0" aria-valuemax="${h.max_points}"><i style="width:${h.points/h.max_points*100}%"></i></div><p>一月一回合 · 行走消耗行动力 · 结束本月后恢复至${h.max_points}</p></div>
      <div class="hero-map-actions"><button data-hero-locate>大地图 · 定位主角 ↗</button><button class="primary" data-hero-local>小地图 · 进入${esc(h.place_name)} ↗</button><button data-hero-bag class="quiet">行囊</button></div>
      <div class="hero-destinations"><p class="eyebrow">城外行路 / 相邻地点</p><div>${h.connections.map(r=>`<button data-hero-travel="${r.place}" ${locked||!r.enabled?'disabled':''} title="${esc(r.reason||'抵达目的地城门，不自动推进月份')}"><b>${esc(r.name)} <span>${r.sea?'舟':'驿'}</span></b><small>${r.points}行动力 · ${num(r.silver,2)}银</small></button>`).join('')}</div><p class="hero-hint">${!this.paused()?'世界推演或切磋中，请先暂停并收招。':h.connections[0]?.reason||'从城门出发，沿相邻驿道或航路逐段远行。'}</p></div>
      <details class="hero-journal"><summary>行旅手记 <small>最近${h.journal.length}笔</small></summary>${h.journal.map(e=>`<p><small>${esc(e.date)}</small>${esc(e.summary)}</p>`).join('')}</details>`:`
      <div class="hero-welcome"><span class="hero-seal">吾</span><div><p class="eyebrow">从旁观众生，到亲历江湖</p><h2>立一名主角，走自己的路</h2><p>设定名姓与出身，沿大地图远行，在小地图移步。<br>每月${h.max_points}行动力，由你决定何时结束这一回合。</p></div><button class="primary" data-hero-create ${locked?'disabled':''}>创建主角 →</button></div>`;
    $('#hero-rule').textContent=h.created?`第${h.turn}回合 · 余${h.points}行动力`:'创建主角，开启按月行旅';
  },
  locate() {
    if(!world.hero.created)return;
    const h=world.hero;
    atlasUI.site=h.place;
    $('#country-filter').value='';$('#place-filter').value='';$('#faction-filter').value='';$('#circuit-filter').value='';
    atlasUI.signature='';atlasUI.focusSite(h.place);atlasUI.render();
    $('#map').scrollIntoView({behavior:'smooth',block:'center'});
  },
  async perform(action, extra={}) {
    if(this.pending)return;
    this.pending=true;this.render();
    try {
      await api('/api/hero',{action,revision:world.hero.revision,month:world.month,...extra});
      await refresh(true);
      if(action==='create'){$('#hero-dialog').close();this.locate();toast('主角已入江湖。点击小地图开始行走。');}
      if(action==='travel'){
        this.locate();
        if($('#city-dialog').open){cityUI.selectedTile=null;cityUI.selected='gate';await cityUI.load(world.hero.place);}
        toast(`已抵达${world.hero.place_name}，本月余${world.hero.points}行动力`);
      }
      if(action==='walk'&&$('#city-dialog').open)await cityUI.load(cityUI.place);
      if(action==='end_turn')toast(`已进入${world.date}，行动力恢复至${world.hero.max_points}`);
    } catch(e){toast(e.message,true);await refresh(true);}
    finally{this.pending=false;this.signature='';this.render();if($('#city-dialog').open&&cityUI.data)cityUI.render();}
  }
};

document.addEventListener('click',async e=>{
  if(e.target.closest('[data-hero-create]')){
    const select=$('#hero-birthplace');
    select.innerHTML=locations().map(p=>`<option value="${p.id}">${esc(p.name)} · ${esc(p.region)}</option>`).join('');
    select.value=String(world.hero.place);$('#hero-dialog').showModal();return;
  }
  if(e.target.closest('[data-hero-locate]')){heroUI.locate();return;}
  if(e.target.closest('[data-hero-local]')){cityUI.selectedTile=null;await cityUI.open(world.hero.place);return;}
  if(e.target.closest('[data-hero-bag]')){await openInventory('player');return;}
  const travel=e.target.closest('[data-hero-travel]');
  if(travel){await heroUI.perform('travel',{place:Number(travel.dataset.heroTravel)});return;}
  const walk=e.target.closest('[data-hero-walk]');
  if(walk){await heroUI.perform('walk',{place:cityUI.place,row:Number(walk.dataset.row),col:Number(walk.dataset.col)});return;}
  if(e.target.closest('#hero-end-turn'))await heroUI.perform('end_turn');
});
$('#hero-form').onsubmit=async e=>{
  e.preventDefault();const data=Object.fromEntries(new FormData(e.target));
  await heroUI.perform('create',{...data,age:Number(data.age),place:Number(data.place)});
};
document.addEventListener('keydown',async e=>{
  if(!$('#city-dialog').open||!world?.hero.created||cityUI.place!==world.hero.place||e.target.closest('input,select,textarea,button')||e.altKey||e.ctrlKey||e.metaKey)return;
  const delta={ArrowUp:[-1,0],ArrowDown:[1,0],ArrowLeft:[0,-1],ArrowRight:[0,1]}[e.key];
  if(!delta)return;e.preventDefault();
  const h=world.hero,row=h.row+delta[0],col=h.col+delta[1],rows=cityUI.data?.rows;
  if(!rows?.[row]?.[col]||'~^#'.includes(rows[row][col])){toast('此处不可通行，跨水须经桥。');return;}
  await heroUI.perform('walk',{place:h.place,row,col});
});
