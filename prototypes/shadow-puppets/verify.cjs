/* Structural regression: verify the actual serialized rigid transforms, not screenshots alone. */
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const api = require('./puppet.js');
const destination = path.join(__dirname, 'validation');
fs.mkdirSync(destination, { recursive: true });
let cases = 0, maxEndpointError = 0;
const slots = api.RIG.slots;
const endpointPairs = [
  ['upperArmFar', 'forearmFar', 88], ['upperArmNear', 'forearmNear', 88],
  ['forearmNear', 'weapon', 80], ['thighFar', 'shinFar', 110],
  ['thighNear', 'shinNear', 110], ['shinFar', 'shoeFar', 112], ['shinNear', 'shoeNear', 112]
];
function transformMap(svg) {
  const map = {};
  const re = /<g data-slot="([^"]+)" data-asset="([^"]+)" transform="translate\(([-\d.]+) ([-\d.]+)\)(?: rotate\(([-\d.]+)\))?"/g;
  let match;
  while ((match = re.exec(svg))) {
    assert(!map[match[1]], `Duplicate slot ${match[1]}`);
    map[match[1]] = { asset: match[2], x: +match[3], y: +match[4], degrees: +(match[5] || 0) };
  }
  return map;
}
for (const head of api.TYPES) for (const upper of api.TYPES) for (const lower of api.TYPES) for (const waist of api.TYPES) {
  for (const expression of api.RIG.expressions) for (const facing of api.RIG.directions) for (const pose of ['idle', 'walk', 'lunge']) for (const step of [0, 25, 50, 75, 100]) {
    const state = { head, upper, lower, waist, expression, facing, pose, step, wear: 0 };
    const svg = api.render(state), map = transformMap(svg);
    assert.deepEqual(Object.keys(map).sort(), [...slots].sort());
    assert.equal((svg.match(/data-slot-owner="waist"/g) || []).length, 1, 'Exactly one belt owner');
    assert.equal((svg.match(/data-slot="weapon"/g) || []).length, 1, 'Exactly one weapon');
    assert(svg.includes(`data-garment="${upper}-${facing}-youren"`), 'Facing-specific collar asset');
    assert(!svg.includes('data-rod'), 'No rods');
    assert(!svg.includes('filter='), 'No procedural dirty base texture');
    assert(!svg.includes('data-layer="wear"'), 'Clean base has no wear layer');
    for (const [parent, child, length] of endpointPairs) {
      const a = map[parent], b = map[child], rad = a.degrees * Math.PI / 180;
      const error = Math.hypot((a.x - Math.sin(rad) * length) - b.x, (a.y + Math.cos(rad) * length) - b.y);
      maxEndpointError = Math.max(error, maxEndpointError);
      assert(error < .00004, `${parent} / ${child} connection error: ${error}`);
    }
    // Front/rear skirt pivots follow the two FIXED torso-local hinges, not moving knee points.
    for (const [slot, offset] of [['skirtFront', 7], ['skirtBack', -7]]) {
      const torso = map.torso, skirt = map[slot], rad = torso.degrees * Math.PI / 180;
      const x = torso.x + offset * Math.cos(rad) * (facing === 'right' ? 1 : -1);
      const y = torso.y + offset * Math.sin(rad) * (facing === 'right' ? 1 : -1);
      assert(Math.hypot(skirt.x - x, skirt.y - y) < .00002, `${slot} detached from waist`);
    }
    assert(api.check(state).ok);
    ++cases;
  }
}
for (const type of api.TYPES) {
  const base = { head: type, upper: type, lower: type, waist: type };
  const idle = api.parts({ ...base, pose: 'idle' });
  const lunge = api.parts({ ...base, pose: 'lunge' });
  assert.deepEqual(idle, lunge, 'Pose must never change the actual leather pieces');
  for (const expression of ['angry', 'sad']) {
    const changed = api.parts({ ...base, expression });
    assert.notEqual(changed[0].svg, idle[0].svg);
    assert.deepEqual(changed.slice(1), idle.slice(1), 'Expressions change only the interchangeable head');
  }
  const worn = api.render({ ...base, wear: 80 });
  assert(worn.includes('data-layer="wear"'));
  assert.notEqual(api.parts({ ...base, facing: 'right' })[1].svg, api.parts({ ...base, facing: 'left' })[1].svg);
}
const report = {
  date: '2026-10-26', source: 'puppet.js rigid vector parts',
  headTorsoLowerCombinations: 27, includingIndependentWaist: 81,
  expressions: 3, directions: 2, poses: 3, phaseSamples: [0, 25, 50, 75, 100], checkedStates: cases,
  maximumSerializedEndpointError: maxEndpointError, tolerance: .00004,
  checks: ['one owner per slot', 'one waist belt', 'one sword', 'serialized limb endpoints meet', 'two fixed independent waist hinges', 'left and right collar asset selection', 'pose-independent piece shapes', 'head-only expression swapping', 'clean base / separate wear overlay', 'no rods'],
  limits: ['No polygon collision solver or exhaustive aesthetic acceptance.', 'Left/right lapel artwork is a separate asset-selection prototype, not historical costume certification.', 'The page is independent from saved NPC state and the game animation runtime.']
};
fs.writeFileSync(path.join(destination, 'structure.json'), JSON.stringify(report, null, 2));
fs.writeFileSync(path.join(__dirname, 'rig-spec.json'), JSON.stringify(api.RIG, null, 2));
const previews = path.join(__dirname, 'previews');
fs.mkdirSync(previews, { recursive: true });
for (const type of api.TYPES) fs.writeFileSync(path.join(previews, `${type}.svg`), api.render({ head: type, upper: type, lower: type, waist: type }, { ground: false }));
function sheet(mixed) {
  let content = '<rect width="1500" height="820" fill="#f6edd7"/><text x="65" y="65" fill="#4b4030" font-size="25" font-family="KaiTi,serif">江湖未央 · 皮影分件试样</text><text x="65" y="101" fill="#8b7655" font-size="14" font-family="Microsoft YaHei,sans-serif">前后裙片独立铰接 / 腰饰单槽 / 全场景无签</text>';
  for (let i = 0; i < 3; ++i) {
    const type = api.TYPES[i];
    const state = mixed ? { head: type, upper: api.TYPES[(i + 1) % 3], lower: api.TYPES[(i + 2) % 3], waist: type, pose: 'lunge' } : { head: type, upper: type, lower: type, waist: type };
    const svg = api.render(state, { ground: false, viewBox: mixed ? '-215 -310 680 580' : '-175 -315 440 580' });
    content += `<g transform="translate(${i * 480 + 30} 155)">${svg.replace('<svg ', '<svg x="0" y="0" width="480" height="590" ')}<text x="240" y="620" text-anchor="middle" fill="#594a34" font-size="19" font-family="KaiTi,serif">${api.NAMES[type]}${mixed ? ' · 混搭' : ''}</text></g>`;
  }
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1500 820" role="img" aria-label="三个人物的皮影${mixed ? '交叉互换' : '原装'}试样">${content}</svg>`;
}
fs.writeFileSync(path.join(previews, 'three-characters.svg'), sheet(false));
fs.writeFileSync(path.join(previews, 'cross-swap.svg'), sheet(true));
console.log(JSON.stringify(report, null, 2));
