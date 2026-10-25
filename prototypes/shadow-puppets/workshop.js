(function () {
  'use strict';
  const api = window.PuppetLab;
  const $ = id => document.getElementById(id);
  let saved;
  try { saved = JSON.parse(localStorage.getItem('jianghu-puppet-v3') || 'null'); } catch (_) { saved = null; }
  let state = api.normalize(saved || api.DEFAULT), mix = 0;
  const names = api.NAMES;
  for (const key of ['head', 'upper', 'lower', 'waist']) {
    $(key).innerHTML = api.TYPES.map(t => `<option value="${t}">${names[t]}</option>`).join('');
    $(key).addEventListener('change', event => update({ [key]: event.target.value }));
  }
  $('presets').innerHTML = api.TYPES.map(t => `<button type="button" class="preset" data-preset="${t}" aria-pressed="false">${api.headThumbnail(t)}<span>${names[t]}</span></button>`).join('');
  $('presets').addEventListener('click', event => {
    const button = event.target.closest('[data-preset]');
    if (button) update(Object.fromEntries(['head', 'upper', 'lower', 'waist'].map(k => [k, button.dataset.preset])));
  });
  $('cycle-mix').addEventListener('click', () => {
    mix = (mix + 1) % 3;
    update({ head: api.TYPES[mix], upper: api.TYPES[(mix + 1) % 3], lower: api.TYPES[(mix + 2) % 3], waist: api.TYPES[mix] });
  });
  for (const key of ['expression', 'facing']) {
    $(key + '-controls').addEventListener('click', event => {
      const button = event.target.closest(`[data-${key}]`);
      if (button) update({ [key]: button.dataset[key] });
    });
  }
  $('pose').addEventListener('change', event => update({ pose: event.target.value }));
  for (const key of ['step', 'wear']) $(key).addEventListener('input', event => update({ [key]: Number(event.target.value) }));
  $('joints').addEventListener('change', event => update({ joints: event.target.checked }));
  $('export').addEventListener('click', () => {
    const svg = api.render({ ...state, joints: false }, { ground: false });
    const blob = new Blob([svg], { type: 'image/svg+xml;charset=utf-8' });
    const link = document.createElement('a');
    const url = URL.createObjectURL(blob);
    link.href = url; link.download = `puppet-${state.head}-${state.upper}-${state.lower}-${state.facing}.svg`;
    link.click(); setTimeout(() => URL.revokeObjectURL(url), 1000);
  });
  function update(patch, save = true) {
    state = api.normalize({ ...state, ...patch });
    for (const key of ['head', 'upper', 'lower', 'waist', 'pose', 'step', 'wear']) $(key).value = state[key];
    $('joints').checked = state.joints;
    document.querySelectorAll('[data-preset]').forEach(b => b.setAttribute('aria-pressed', String(['head', 'upper', 'lower', 'waist'].every(k => state[k] === b.dataset.preset))));
    for (const key of ['expression', 'facing']) document.querySelectorAll(`[data-${key}]`).forEach(b => b.setAttribute('aria-pressed', String(state[key] === b.dataset[key])));
    $('stage').innerHTML = api.render(state);
    $('parts').innerHTML = api.parts(state).map(p => `<figure class="part" data-part="${p.slot}">${p.svg}<figcaption>${p.label}</figcaption></figure>`).join('');
    const matched = ['upper', 'lower', 'waist'].every(k => state[k] === state.head);
    $('character-label').textContent = matched ? names[state.head] : '异身同骨 · 混搭';
    $('direction-label').textContent = `向${state.facing === 'right' ? '右' : '左'}上身 · 独立衣襟`;
    $('step-value').textContent = `${state.step}%`;
    $('wear-value').textContent = state.wear === 0 ? '干净' : `${state.wear}%${state.wear > 35 ? ' · 带补丁' : ''}`;
    const check = api.check(state);
    $('status').innerHTML = check.ok ? '<b>铰点对齐</b>　前后裙片独立旋转 · 腰带单层 · 肢段长度固定' : `<span role="alert">关节异常：${check.errors.join('、')}</span>`;
    if (save) try { localStorage.setItem('jianghu-puppet-v3', JSON.stringify(state)); } catch (_) {}
    window.puppetState = { ...state };
  }
  window.setPuppetState = patch => update(patch);
  update({}, false);
})();
