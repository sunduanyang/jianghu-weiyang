/* Offline national atlas. World coordinates persist across polling and pointer gestures. */
const atlasUI = {
  geometry: null, loading: false, layer: 'countries', view: [0, 0, 1280, 760],
  site: null, signature: '', drag: null, suppressClick: false,
  country(id) { return world.countries.find(c => c.id === Number(id)); },
  async init() {
    if (this.loading || this.geometry) return;
    this.loading = true;
    try {
      const response = await fetch('/geography.json');
      if (!response.ok) throw Error('舆图数据未能载入');
      this.geometry = await response.json();
      this.signature = '';
      if (world) this.render();
    } catch (e) {
      $('#map').innerHTML = '<p class="note map-error">舆图载入失败，请刷新重试。人物模拟与存档仍可使用。</p>';
    }
  },
  bind() {
    const map = $('#map');
    map.addEventListener('wheel', e => { e.preventDefault(); this.zoom(e.deltaY < 0 ? .8 : 1.25, e); }, {passive: false});
    map.addEventListener('pointerdown', e => {
      if (e.button !== 0) return;
      this.drag = {x: e.clientX, y: e.clientY, view: [...this.view], moved: false};
    });
    map.addEventListener('pointermove', e => {
      if (!this.drag) return;
      const dx = e.clientX-this.drag.x, dy = e.clientY-this.drag.y;
      if (Math.abs(dx)+Math.abs(dy) < 5 && !this.drag.moved) return;
      this.drag.moved = true;
      if (!map.hasPointerCapture(e.pointerId)) map.setPointerCapture(e.pointerId);
      const scale = this.pixelScale();
      this.view[0] = this.drag.view[0]-dx/scale;
      this.view[1] = this.drag.view[1]-dy/scale;
      this.applyView();
    });
    const finish = () => {
      if (this.drag?.moved) { this.suppressClick = true; setTimeout(() => this.suppressClick = false, 0); }
      this.drag = null;
    };
    map.addEventListener('pointerup', finish);
    map.addEventListener('pointercancel', finish);
    map.addEventListener('pointerleave', e => { if (!map.hasPointerCapture(e.pointerId)) finish(); });
    $('#map-zoom-in').onclick = () => this.zoom(.7);
    $('#map-zoom-out').onclick = () => this.zoom(1/.7);
    $('#map-reset').onclick = () => { this.view = [0, 0, 1280, 760]; this.applyView(); };
    $('#map-locate').onclick = () => { const n = person(selected); if (n) this.focusSite(n.place); };
    $('#map-detail').addEventListener('click', async e => {
      const button = e.target.closest('#campaign-launch');
      if (!button) return;
      button.disabled = true;
      await act('/api/campaign', {attacker: Number($('#campaign-attacker').value), place: this.site}, '战事已结算，舆图与公帑已更新');
      this.signature = ''; this.render();
    });
    $$('[data-map-layer]').forEach(button => button.onclick = () => {
      this.layer = button.dataset.mapLayer;
      $$('[data-map-layer]').forEach(b => { b.classList.toggle('active', b === button); b.setAttribute('aria-pressed', b === button); });
      this.signature = ''; this.render();
    });
  },
  pixelScale() {
    const r = $('#map').getBoundingClientRect();
    return Math.min(r.width/this.view[2], r.height/this.view[3]);
  },
  zoom(factor, event) {
    const [x,y,w,h] = this.view, newWidth = Math.max(110, Math.min(1280, w*factor));
    const k = newWidth/w;
    let cx = x+w/2, cy = y+h/2;
    const svg = $('#map svg');
    if (event && svg) {
      const point = new DOMPoint(event.clientX, event.clientY).matrixTransform(svg.getScreenCTM().inverse());
      cx = point.x; cy = point.y;
    }
    this.view = [cx+(x-cx)*k, cy+(y-cy)*k, newWidth, h*k];
    this.applyView();
  },
  applyView() {
    const svg = $('#map svg'); if (!svg) return;
    const [,,w,h] = this.view;
    this.view[0] = Math.max(-80, Math.min(1360-w, this.view[0]));
    this.view[1] = Math.max(-70, Math.min(830-h, this.view[1]));
    svg.setAttribute('viewBox', this.view.join(' '));
    svg.classList.toggle('map-close', w < 490);
    svg.classList.toggle('map-near', w < 250);
    for (const site of svg.querySelectorAll('.atlas-site.capital')) {
      const p = world.places[Number(site.dataset.place)];
      const k = w < 250 ? .28 : w < 490 ? .55 : 1;
      site.querySelector('.site-symbol').setAttribute('transform', `translate(${p.x} ${p.y}) scale(${k}) translate(${-p.x} ${-p.y})`);
    }
    // Dense eastern settlements share little screen space. Keep the most useful labels,
    // while hover/focus always reveals a settlement's full name.
    const occupied = [];
    const sites = [...svg.querySelectorAll('.atlas-site')].sort((a,b) =>
      Number(b.classList.contains('chosen'))*10+Number(b.classList.contains('prominent'))-
      Number(a.classList.contains('chosen'))*10-Number(a.classList.contains('prominent')));
    sites.forEach(site => site.classList.remove('label-suppressed'));
    for (const site of sites) {
      if (site.classList.contains('dimmed')) continue;
      const label = site.querySelector('.atlas-site-label');
      const rect = label.getBoundingClientRect();
      if (!rect.width || rect.bottom < 0 || rect.top > innerHeight) continue;
      if (occupied.some(r => rect.left < r.right+4 && rect.right+4 > r.left && rect.top < r.bottom+3 && rect.bottom+3 > r.top))
        site.classList.add('label-suppressed');
      else occupied.push(rect);
    }
    $('#map-scale').textContent = `${(1280/w).toFixed(1)}×`;
  },
  focusSite(id) {
    const p = world.places.find(p => p.id === Number(id)); if (!p) return;
    this.site = p.id;
    this.view = [p.x-135, p.y-93, 270, 186.55];
    this.signature = ''; this.render();
  },
  chooseCountry(id, focus = true) {
    $('#country-filter').value = String(id);
    $('#place-filter').value = ''; $('#faction-filter').value = ''; $('#circuit-filter').value = '';
    this.site = null;
    if (focus && id === '') this.view = [0, 0, 1280, 760];
    if (focus && id !== '') {
      const points = world.places.filter(p => p.country === Number(id));
      if (points.length) {
        const xs = points.map(p=>p.x), ys = points.map(p=>p.y);
        const w = Math.min(1280, Math.max(230, Math.max(...xs)-Math.min(...xs)+100, (Math.max(...ys)-Math.min(...ys)+100)*1280/760));
        this.view = [(Math.max(...xs)+Math.min(...xs)-w)/2, (Math.max(...ys)+Math.min(...ys)-w*760/1280)/2, w, w*760/1280];
      }
    }
    this.signature = ''; renderPeople(); this.render(); renderFactions(); renderSociety();
  },
  selectSite(id) {
    this.site = Number(id);
    $('#country-filter').value = ''; $('#faction-filter').value = '';
    $('#circuit-filter').value = '';
    $('#place-filter').value = String(id);
    renderPeople(); this.signature = ''; this.render();
  },
  render() {
    if (!world?.countries) return;
    if (!this.geometry) { this.init(); return; }
    const country = $('#country-filter').value;
    const key = [world.seed, world.month, world.events, selected, country, this.site, this.layer, $('#faction-filter').value, $('#circuit-filter').value].join('/');
    if (key === this.signature || this.drag) return;
    this.signature = key;
    const dim = id => country !== '' && Number(country) !== id;
    const shapes = this.geometry.territories.map(p => {
      const c = this.country(p.country);
      return `<path class="map-territory ${c.kind==='诸部'?'tribal-range':''}" d="${p.path}" fill="none"><title>${esc(p.name)} · 开局故界</title></path>`;
    }).join('');
    const cells = this.geometry.territories.map(t => `<g clip-path="url(#native-${t.country})">`+
      this.geometry.cells.filter(cell => cell.native_country === t.country).map(cell => {
        const p = world.places[cell.place], c = this.country(p.country);
        return `<path class="map-control ${p.occupied?'occupied':''} ${dim(c.id)?'dimmed':''}" data-place="${p.id}" data-owner="${c.id}" d="${cell.path}" fill="${c.color}"><title>${esc(p.name)}辖区 · ${esc(c.name)}${p.occupied?'（占领）':''}</title></path>`;
      }).join('')+'</g>').join('');
    const routes = world.routes.map(r => {
      const a = world.places[r.a], b = world.places[r.b];
      return `<path class="atlas-route ${r.sea?'sea':''} ${dim(a.country)&&dim(b.country)?'dimmed':''}" d="M${a.x},${a.y} L${b.x},${b.y}"><title>${esc(a.name)}—${esc(b.name)} · ${r.km}公里</title></path>`;
    }).join('');
    const n = person(selected), site = n ? world.places[n.place] : null;
    const focus = site ? `<circle class="atlas-person-ring" cx="${site.x}" cy="${site.y}" r="13"/><circle cx="${site.x}" cy="${site.y}" r="3" fill="#9e4c37"/>` : '';
    const labels = world.countries.filter(c=>c.settlements).map(c=>{
      const capital = world.places.find(p=>p.name===c.capital);
      const anchor = capital?.country===c.id ? {x:c.label_x,y:c.label_y} : world.places.find(p=>p.country===c.id);
      return `<text class="country-label ${dim(c.id)?'dimmed':''}" data-country="${c.id}" x="${anchor.x}" y="${anchor.y}">${esc(c.name)}</text>`;
    }).join('');
    const circuits = this.geometry.regions.map(r=>`<text class="circuit-label ${dim(r.country)?'dimmed':''}" data-circuit="${esc(r.name)}" data-country="${r.country}" x="${r.x}" y="${r.y}">${esc(r.name)}</text>`).join('');
    const rivers = this.geometry.rivers.map(r=>`<path class="atlas-river" d="${r.path}"><title>${esc(r.name)}</title></path>`).join('');
    const sites = world.places.map(p => {
      const schools = world.factions.filter(f => f.place === p.id), capital = p.kind === 'capital';
      const prominent = capital || p.occupied || (this.layer === 'factions' && schools.length);
      const filtered = $('#faction-filter').value;
      const highlight = this.site === p.id || (filtered !== '' && schools.some(f => f.id === Number(filtered)));
      const label = this.layer === 'factions' && schools.length ? schools.map(f=>f.name).join(' / ') : p.name;
      return `<g class="atlas-site ${capital?'capital':''} ${p.occupied?'occupied':''} ${prominent?'prominent':''} ${dim(p.country)?'dimmed':''} ${highlight?'chosen':''}" style="--site-color:${p.color}" data-place="${p.id}" role="button" tabindex="0" aria-label="${esc(p.name)}，${p.population}人，${esc(this.country(p.country).name)}${p.occupied?'占领':''}">
        <title>${esc(p.name)} · ${esc(this.country(p.country).name)} · ${p.population}人${schools.length?' · '+esc(schools.map(f=>f.name).join('、')):''}</title>
        ${capital?`<path class="site-symbol" d="M${p.x},${p.y-5} l5,5 -5,5 -5,-5 Z"/>`:`<circle class="site-symbol" cx="${p.x}" cy="${p.y}" r="${schools.length&&this.layer==='factions'?4:2.8}"/>`}
        ${p.occupied?`<path class="occupation-flag" d="M${p.x},${p.y-3} v-9 l7,2 -7,3"/>`:''}
        <text class="atlas-site-label" x="${p.x+7}" y="${p.y+4}">${esc(label)}</text>
        <text class="atlas-site-pop" x="${p.x+7}" y="${p.y+17}">${p.population}人</text></g>`;
    }).join('');
    const residentDots = world.places.map(p => world.npcs.filter(n=>n.alive&&n.place===p.id).map((n,i)=>{
      const angle=i*2.399963, r=4+Math.sqrt(i)*1.1;
      return `<circle class="map-npc" data-person="${n.id}" cx="${p.x+Math.cos(angle)*r}" cy="${p.y+Math.sin(angle)*r}" r=".85" fill="${n.id===selected?'#a4513f':'#5e7e68'}"><title>${esc(n.name)} · ${esc(p.name)}</title></circle>`;
    }).join('')).join('');
    $('#map').innerHTML = `<svg class="atlas-svg layer-${this.layer}" viewBox="${this.view.join(' ')}" aria-label="约1200年宋金时期江湖舆图，可缩放拖动，${world.places.length}处地点，包含古代政权疆界与诸部活动范围" role="img">
      <defs><clipPath id="historical-land"><path d="${this.geometry.land}"/></clipPath>${this.geometry.territories.map(t=>`<clipPath id="native-${t.country}"><path d="${t.path}"/></clipPath>`).join('')}<pattern id="map-grid" width="80" height="80" patternUnits="userSpaceOnUse"><path d="M80 0 H0 V80" fill="none" stroke="#bcc8bf" stroke-width=".5" opacity=".22"/></pattern></defs>
      <rect x="-1000" y="-1000" width="4000" height="4000" fill="#e9eee8"/>
      <rect width="1280" height="760" fill="url(#map-grid)"/>
      <path class="atlas-coast" d="${this.geometry.land}"/>
      <g class="atlas-land" clip-path="url(#historical-land)">${cells}<g class="atlas-historical-borders">${shapes}</g></g>
      <g class="atlas-rivers" clip-path="url(#historical-land)">${rivers}</g>
      <path class="song-jin-frontier historical-frontier" d="${this.geometry.frontier}"><title>开局宋金故界：秦岭、大散关—淮水一线（概略）</title></path>
      <g class="atlas-roads">${routes}</g><g class="atlas-regions">${labels}</g>
      <g class="atlas-circuits">${circuits}</g><g class="atlas-residents">${residentDots}</g><g class="atlas-sites">${sites}</g>${focus}
      <g class="atlas-compass" transform="translate(1200 65)"><path d="M0 -22 L-6 2 0 -3 6 2 Z" fill="#567168"/><text x="0" y="-30" text-anchor="middle" font-size="14" fill="#567168">北</text><path d="M0 5 V20 M-12 0 H12" stroke="#9aab9c"/></g>
      <text class="frontier-label" x="${25+(107.3-73)*16.5}" y="${25+(54-34.18)*18.5-8}">大散关</text>
      <text class="sea-label" x="930" y="535">东 海</text><text class="sea-label" x="790" y="718">南 海</text><text class="sea-label" x="1080" y="280">日 本 海</text>
    </svg>`;
    this.applyView();
    $('#country-legend').innerHTML = `<button class="country-chip ${country===''?'active':''}" data-country="">天下</button>`+world.countries.map(c=>`<button class="country-chip ${Number(country)===c.id&&country!==''?'active':''}" data-country="${c.id}"><i style="background:${c.color}"></i>${esc(c.name)} <small>${c.settlements}地</small></button>`).join('');
    $('#atlas-count').textContent = `${world.places.length}处山河 · ${world.countries.length}方政权 / 诸部 · ${world.factions.length}个组织`;
    this.renderDetail();
  },
  renderDetail() {
    const p = this.site !== null ? world.places[this.site] : null;
    const c = p ? this.country(p.country) : ($('#country-filter').value !== '' ? this.country($('#country-filter').value) : null);
    if (!c) {
      const recent = (world.campaigns??[]).slice(-3).reverse();
      $('#map-detail').innerHTML = `<div><b>山海之间，城镇易主</b><p>约1200年开局 · 高丽与镰仓时期。疆域颜色随城镇控制权更新，旗标为被占领地点；「州路」保留开局故界。每季交恶势力可能出兵，点击城镇可推演攻守。</p>${recent.map(e=>`<p class="fact-link campaign-recent" data-event="${e.event_id}">${when(e.month)} · ${esc(this.country(e.attacker).name)}${e.captured?'攻占':'进攻未克'}${esc(place(e.place))} ↗</p>`).join('')}</div><span class="atlas-seal">天下</span>`;
      return;
    }
    const schools = p ? world.factions.filter(f=>f.place===p.id) : [];
    const neighbors = p ? world.routes.filter(r=>r.a===p.id||r.b===p.id).map(r=>world.places[r.a===p.id?r.b:r.a].name).join('、') : '';
    $('#map-detail').innerHTML = `<div><div class="atlas-detail-title"><b>${esc(p?p.name:c.name)}</b><span>${esc(c.name)} · ${esc(p?p.region:c.capital)} · ${p?p.population:c.population}人在世</span></div>
      <p>${p?`${schools.map(f=>esc(f.name)+' · '+esc(f.art)).join(' / ')||'往来行旅与当地居民在此生活。'}<br>通往 ${esc(neighbors)}`:esc(c.ethos)+'。公帑 '+num(c.government.fund,1)+' · 税率 '+num(c.government.tax_rate*100,1)+'%'}</p>
      ${p?`<div class="settlement-status"><span>守备 ${num(p.guard)} / ${p.guard_max}</span><span>安定 ${num(p.stability)}%</span><span>${p.occupied?`原属${esc(this.country(p.native_country).name)} · ${when(p.changed_month)}易主`:'开局辖地'}</span>${p.event_id?`<span class="fact-link" data-event="${p.event_id}">最近战报 ↗</span>`:''}</div><div id="campaign-controls"><small>正在查看相邻势力…</small></div>`:`<p>现辖 ${c.settlements}处 · ${c.settlements?'城镇归属影响征税与通行':'已无辖地，保留国库与旧史'}</p><div class="border-relations">${Object.entries(c.relations).map(([id,status])=>`<span class="relation-${status}">${esc(this.country(id).name)} · ${esc(status)}</span>`).join('')}</div>`}</div><span class="atlas-seal" style="border-color:${c.color}">${esc(c.name.slice(0,2))}</span>`;
    if (p) this.renderCampaign(p.id);
  },
  async renderCampaign(id) {
    const node = $('#campaign-controls');
    try {
      const {options} = await api('/api/campaign?place='+id);
      if (node !== $('#campaign-controls') || this.site !== id) return;
      if (!options.length) { node.innerHTML = '<small>腹地暂未与其他势力接壤。</small>'; return; }
      const paused = !world.autoplay && !world.job.remaining && !world.battle_active;
      node.innerHTML = `<div class="campaign-row"><select id="campaign-attacker" aria-label="进攻势力">${options.map(o=>`<option value="${o.attacker}" ${o.ready?'':'disabled'}>${esc(this.country(o.attacker).name)} · 自${esc(place(o.origin))}${o.sea?'渡海':''} · ${num(o.cost,1)}银${o.ready?'':' · '+esc(o.reason)}</option>`).join('')}</select><button id="campaign-launch" ${paused&&options.some(o=>o.ready)?'':'disabled'}>推演攻城</button></div><small>沙盘推演：非交恶势力将先宣战，支付公帑，胜负取决于守备、安定与海路；${paused?'可以失败，战后须休整。':'请先暂停并结束切磋。'}</small>`;
      const ready = options.find(o=>o.ready);
      $('#campaign-attacker').value = String((ready??options[0]).attacker);
      $('#campaign-attacker').disabled = !ready;
    } catch(e) { if (node === $('#campaign-controls')) node.textContent = e.message; }
  }
};

function renderFactions() {
  if (!world?.countries) return;
  const country = $('#country-filter').value, kind = $('#faction-kind').value;
  const factions = world.factions.filter(f=>(country===''||f.country===Number(country))&&(kind===''||f.kind===kind));
  const labels = {sect:'门派',guild:'商帮',society:'义社'};
  $('#faction-directory').innerHTML = factions.map(f=>`<button class="faction-card ${$('#faction-filter').value===String(f.id)?'active':''}" data-faction="${f.id}">
    <span class="faction-type">${labels[f.kind]}</span><b>${esc(f.name)}</b><span>${esc(f.art)}</span>
    <small>${esc(place(f.place))} · ${f.members}人在世 · ${esc(atlasUI.country(f.country).name)}</small></button>`).join('')||'<p class="note">此处没有该类组织。</p>';
}
