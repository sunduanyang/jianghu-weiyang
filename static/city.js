/* Original isometric town atlas. Browsing a plan never spends world time. */
const cityUI = {
  place: null, data: null, selected: 'gate', origin: 'gate', token: 0, key: '', filter: '',
  async open(id) {
    this.selected = 'market'; this.origin = 'gate'; this.filter = ''; this.key = '';
    $('#city-dialog').showModal();
    await this.load(Number(id));
  },
  async load(id) {
    const token = ++this.token, changing = this.place !== id || !this.data;
    this.place = id;
    if (changing) $('#city-content').innerHTML = '<p class="city-loading">正沿驿道展开城中图…</p>';
    try {
      const data = await api('/api/city?place='+id);
      if (token !== this.token || !$('#city-dialog').open) return;
      this.data = data;
      this.key = [world.database,world.seed,world.month,world.events,id].join('/');
      this.render();
    } catch(e) {
      if (token === this.token) $('#city-content').innerHTML = `<p class="city-loading">${esc(e.message)}</p><button data-city-retry>重新展开</button>`;
    }
  },
  refresh() {
    if (!$('#city-dialog')?.open || this.place === null) return;
    const key = [world.database,world.seed,world.month,world.events,this.place].join('/');
    if (key !== this.key) { this.key = key; this.load(this.place); }
  },
  xy(row,col) { return [400+(col-row)*34, 72+(row+col)*20]; },
  diamond(x,y) { return `${x},${y-20} ${x+34},${y} ${x},${y+20} ${x-34},${y}`; },
  route(start,end) {
    const rows=this.data.rows, size=rows.length;
    const from=[start.row,start.col], goal=`${end.row},${end.col}`;
    const pending=[[0,from]], costs=new Map([[from.join(','),0]]), prior=new Map();
    while (pending.length) {
      pending.sort((a,b)=>b[0]-a[0]);
      const [cost,pos]=pending.pop(), key=pos.join(',');
      if(cost!==costs.get(key))continue;
      if(key===goal){const path=[pos];let cursor=key;while(prior.has(cursor)){cursor=prior.get(cursor);path.unshift(cursor.split(',').map(Number));}return path;}
      for(const [dr,dc] of [[-1,0],[1,0],[0,-1],[0,1]]){
        const r=pos[0]+dr,c=pos[1]+dc;
        if(r<0||r>=size||c<0||c>=size||'~^#'.includes(rows[r][c]))continue;
        const next=`${r},${c}`, step='+='.includes(rows[r][c])?1:rows[r][c]==='t'?3:2;
        if(cost+step<(costs.get(next)??Infinity)){costs.set(next,cost+step);prior.set(next,key);pending.push([cost+step,[r,c]]);}
      }
    }
    return [];
  },
  building(node,x,y) {
    const type=node.kind, tent=['steppe','forest'].includes(this.data.theme), color=this.data.color;
    let shape='';
    if(type==='garden') shape=`<path d="M${x-10} ${y+1} l8 -22 9 23 Z M${x+2} ${y+1} l9 -29 10 30 Z" fill="#667f58"/><path d="M${x-2} ${y+3} v-8 M${x+11} ${y+5} v-12" stroke="#665b3e" stroke-width="3"/>`;
    else if(type==='dock') shape=`<path d="M${x-20} ${y+1} l22 -12 21 12 -22 12 Z" fill="#b29a6a" stroke="#86734f"/><path d="M${x-13} ${y+2} l28 -1 -7 6 -15 -1 Z M${x+2} ${y-1} v-21 l12 15 -12 -2" fill="#e3d4a8" stroke="#786e50"/>`;
    else if(type==='training') shape=`<path d="M${x-22} ${y} l22 -12 22 12 -22 13 Z" fill="#b8b29a" stroke="#827f68"/><path d="M${x-13} ${y-7} v-19 m-4 7 h8 M${x+12} ${y+1} v-20 m-4 7 h8" stroke="#786348" stroke-width="2"/>`;
    else if(type==='gate') shape=`<path d="M${x-18} ${y} v-20 l18 -9 18 9 v20 l-18 10 Z" fill="#b5ac90" stroke="#7e7c65"/><path d="M${x-5} ${y+5} v-16 q5 -8 10 0 v10" fill="#596756"/><path d="M${x-23} ${y-20} l23 -15 23 15 -23 9 Z" fill="#67766b"/>`;
    else if(tent && ['government','residence','inn','market'].includes(type)) shape=`<ellipse cx="${x}" cy="${y+1}" rx="23" ry="11" fill="#d7cfac" stroke="#8f8162"/><path d="M${x-23} ${y} Q${x-12} ${y-19} ${x} ${y-28} Q${x+15} ${y-18} ${x+23} ${y} Z" fill="${type==='government'?color:'#e4d8b7'}" stroke="#8f8162"/><path d="M${x-4} ${y+8} v-13 l8 -3 v12" fill="#776b50"/>`;
    else {
      const grand=['palace','government','temple','faction'].includes(type), w=grand?22:18,h=grand?21:16;
      shape=`<path d="M${x-w} ${y-2} v-${h} l${w} -9 ${w} 9 v${h} l-${w} 10 Z" fill="#d6c7a2" stroke="#9b8c6a"/>
        <path d="M${x} ${y+8} v-${h+1} l${w} -9 v${h} Z" fill="#c2b18b"/>
        <path d="M${x-w-5} ${y-h-2} l${w+5} -16 ${w+5} 16 -${w+5} 12 Z" fill="${type==='government'?color:type==='market'?'#a7755c':grand?'#647c70':'#8d9275'}" stroke="#5f6f5e"/>
        <path d="M${x-4} ${y+5} v-10 l8 -3 v9 M${x+11} ${y-3} v-7" fill="#71664d" stroke="#71664d"/>`;
      if(grand)shape+=`<path d="M${x-16} ${y-h-14} l16 -13 16 13 -16 9 Z" fill="${type==='government'?color:'#778470'}" stroke="#566954"/>`;
    }
    return shape;
  },
  render() {
    const d=this.data;
    if(!d.nodes.some(n=>n.id===this.selected))this.selected='gate';
    if(!d.nodes.some(n=>n.id===this.origin))this.origin='gate';
    const node=d.nodes.find(n=>n.id===this.selected), start=d.nodes.find(n=>n.id===this.origin);
    const route=this.route(start,node), trail=new Set(route.map(q=>q.join(',')));
    const terrainColors={'.':'#d7dab9','+':'#d6c49e','=':'#b5a17a','~':'#99b7af','^':'#a4af99','t':'#b9c5a3',',':'#c8cf9d','#':'#b0ad94'};
    const base=[];
    for(let r=0;r<d.size;r++)for(let c=0;c<d.size;c++){
      const [x,y]=this.xy(r,c), t=d.rows[r][c];
      const detail=t==='~'?`<path d="M${x-16} ${y-1} q8 -5 16 0 t16 0 M${x-9} ${y+6} q8 -4 16 0" stroke="#d6e5dc" fill="none" opacity=".7"/>`:t===','?`<path d="M${x-18} ${y} l20 -11 M${x-9} ${y+5} l20 -11 M${x} ${y+10} l20 -11" stroke="#9da777" stroke-width="2"/>`:'';
      base.push(`<g class="city-ground"><polygon points="${this.diamond(x,y)}" fill="${terrainColors[t]}"/><title>${esc(d.terrain_legend[t])} · ${r+1},${c+1}</title>${detail}${trail.has(`${r},${c}`)?`<polygon class="city-trail-tile" points="${this.diamond(x,y)}"/>`:''}</g>`);
    }
    const overlays=[];
    for(let sum=0;sum<d.size*2;sum++)for(let r=0;r<d.size;r++){
      const c=sum-r;if(c<0||c>=d.size)continue;
      const [x,y]=this.xy(r,c), t=d.rows[r][c], n=d.nodes.find(n=>n.row===r&&n.col===c);
      if(n){const active=this.filter===''||n.kind===this.filter;
        overlays.push(`<g class="city-building ${n.id===this.selected?'selected':''} ${active?'':'muted-node'}" data-city-node="${n.id}" tabindex="0" role="button" aria-label="${esc(n.name)}，${n.residents.length}人">
          <title>${esc(n.name)} · ${esc(n.description)}</title><polygon class="city-building-base" points="${this.diamond(x,y)}"/>${this.building(n,x,y)}
          <text class="city-building-label" x="${x}" y="${y+21}">${esc(n.label)}</text>
          ${n.id===this.origin?`<text class="city-origin" x="${x-25}" y="${y-15}">起</text>`:''}
          ${n.residents.length?`<g class="city-people-count"><circle cx="${x+21}" cy="${y-22}" r="9"/><text x="${x+21}" y="${y-19}">${n.residents.length}</text></g>`:''}</g>`);
      }else if(t==='^')overlays.push(`<path class="city-mountain" d="M${x-28} ${y+5} l28 -46 28 46 -28 13 Z"/><path d="M${x-9} ${y-24} l9 -17 9 17 -8 -4 -4 6 Z" fill="#dce0cc"/>`);
      else if(t==='t')overlays.push(`<path d="M${x-19} ${y+1} l9 -29 11 30 Z M${x-1} ${y+5} l12 -37 12 37 Z" fill="#71886a" stroke="#64785b"/>`);
      else if(t==='#')overlays.push(`<path d="M${x-31} ${y} v-12 l31 -18 31 18 v12 l-31 18 Z" fill="#aaa88e" stroke="#858b73"/><path d="M${x-31} ${y-12} l31 -18 31 18 -31 17 Z" fill="#c8c4a8"/>`);
    }
    const selectedRoute=route.map(([r,c])=>this.xy(r,c).join(',')).join(' ');
    const kinds=[...new Map(d.nodes.map(n=>[n.kind,n.label])).entries()];
    $('#city-title').textContent=d.name+' · 城中图';
    $('#city-content').innerHTML=`<div class="city-summary"><div><span class="city-mode">${d.authored?'重点营造':'因地成图'}</span><b>${esc(d.theme_label)}</b><p>${esc(d.note)}</p></div><span class="city-owner" style="--city-owner:${d.color}">${esc(d.country_name)}${d.occupied?' · 占领中':''}</span></div>
      <div class="city-toolbar"><label>移步别处 <select id="city-selector" aria-label="内部地图地点">${world.places.map(p=>`<option value="${p.id}" ${p.id===d.place?'selected':''}>${p.interior_authored?'◆ ':''}${esc(p.name)}</option>`).join('')}</select></label><label>设施 <select id="city-type" aria-label="内部地图设施筛选"><option value="">全部设施</option>${kinds.map(([id,name])=>`<option value="${id}" ${this.filter===id?'selected':''}>${name}</option>`).join('')}</select></label><span>${d.nodes.length}处设施 · ${d.population}人在世</span></div>
      <div class="city-layout"><section class="city-board theme-${d.theme}" aria-label="${esc(d.name)}内部地图">
        <svg viewBox="70 0 660 455" role="group" aria-label="${esc(d.name)}的街巷设施与步行路线"><defs><filter id="city-shadow"><feDropShadow dx="1" dy="3" stdDeviation="3" flood-color="#4e5942" flood-opacity=".18"/></filter></defs>
        <ellipse cx="400" cy="277" rx="288" ry="134" fill="#72816a" opacity=".09"/>
        <g class="city-tiles">${base.join('')}</g><polyline class="city-trail" points="${selectedRoute}"/>
        <g class="city-structures">${overlays.join('')}</g><text x="400" y="433" class="city-road-name">${esc(d.road_name)}</text>
        <g transform="translate(685 48)" class="city-compass"><path d="M0 -15 l-5 18 5 -5 5 5 Z"/><text y="-22">北</text></g></svg>
        <div class="city-map-key"><span><i class="water"></i>水域</span><span><i class="field"></i>田圃</span><span><i class="woods"></i>山林</span><span><i class="street"></i>街道 / 桥</span><span><i class="path"></i>游览路线</span></div>
        <p class="city-instructions">点选设施查看人物与用途；可更换寻路起点。水域、山岭和城垣不可穿行，跨水须经桥。</p></section>
      <aside class="city-details"><p class="eyebrow">${esc(node.label)} / ${esc(d.country_name)}</p><h3>${esc(node.name)}</h3><p>${esc(node.description)}</p>
        <div class="city-route-info"><span>从${esc(start.name)}</span><b>${route.length?route.length-1:'—'}格</b><small>${route.length?'道路优先的游览路线':'此处无步行通路'}</small></div><button data-city-origin="${node.id}" class="quiet">以此为寻路起点 ↗</button>
        <div class="city-service-actions">${node.kind==='market'||node.kind==='workshop'?'<button data-city-service="market">打开过客货栈 ↗</button>':''}${['training','temple','faction'].includes(node.kind)?'<button data-city-service="martial">功法与闭关 ↗</button>':''}${node.kind==='government'?'<button data-city-service="government">查看所属官府 ↗</button>':''}${['gate','dock'].includes(node.kind)?'<button data-city-service="routes">查看城外行路 ↗</button>':''}</div>
        <div class="section-label">此地人物 <span>${node.residents.length}人</span></div><div class="city-residents">${node.residents.map(n=>`<button data-person="${n.id}"><b>${esc(n.name)}</b><span>${esc(n.job)}</span><small>${n.event_id?when(n.month)+' · 最近行动落点':'乡籍居所'}</small></button>`).join('')||'<p class="note">眼下没有人物在此落脚。</p>'}</div>
        <p class="city-presence-note">行动落点随人物真实行程更新；尚无行动记录的人物列于乡籍居所。</p></aside></div>
      <div class="city-facility-directory" aria-label="城中设施名录">${d.nodes.filter(n=>!this.filter||n.kind===this.filter).map(n=>`<button class="${n.id===this.selected?'active':''}" data-city-node="${n.id}"><i>${esc(n.glyph)}</i><span><b>${esc(n.name)}</b><small>${esc(n.label)} · ${n.residents.length}人</small></span></button>`).join('')}</div>`;
  }
};

document.addEventListener('click', async e=>{
  if(e.target.closest('#map-interior')){await cityUI.open(atlasUI.site??person(selected)?.place??45);return;}
  const open=e.target.closest('[data-city-place]');if(open){await cityUI.open(open.dataset.cityPlace);return;}
  const node=e.target.closest('[data-city-node]');if(node){cityUI.selected=node.dataset.cityNode;cityUI.render();return;}
  const origin=e.target.closest('[data-city-origin]');if(origin){cityUI.origin=origin.dataset.cityOrigin;cityUI.render();return;}
  if(e.target.closest('[data-city-retry]')){await cityUI.load(cityUI.place);return;}
  const service=e.target.closest('[data-city-service]');if(!service)return;
  $('#city-dialog').close();
  if(service.dataset.cityService==='market'){await openInventory('player');packMode='market';packSelected=null;renderInventory();}
  if(service.dataset.cityService==='martial')$('#martial-open').click();
  if(service.dataset.cityService==='government')atlasUI.chooseCountry(cityUI.data.country);
  if(service.dataset.cityService==='routes'){$('[data-map-layer="routes"]').click();atlasUI.focusSite(cityUI.place);}
});
document.addEventListener('change', async e=>{
  if(e.target.id==='city-selector'){cityUI.selected=cityUI.origin='gate';cityUI.filter='';await cityUI.load(Number(e.target.value));}
  if(e.target.id==='city-type'){
    cityUI.filter=e.target.value;
    if(cityUI.filter && cityUI.data.nodes.find(n=>n.id===cityUI.selected)?.kind!==cityUI.filter)
      cityUI.selected=cityUI.data.nodes.find(n=>n.kind===cityUI.filter).id;
    cityUI.render();
  }
});
document.addEventListener('keydown',e=>{
  const n=e.target.closest('.city-building');
  if(n&&['Enter',' '].includes(e.key)){e.preventDefault();cityUI.selected=n.dataset.cityNode;cityUI.render();}
});
