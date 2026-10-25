/* Original, editable cut-leather pieces. Every pose reuses these rigid shapes. */
(function (root, factory) {
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.PuppetLab = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  'use strict';
  const TYPES = ['traveller', 'daoist', 'escort'];
  const NAMES = { traveller: '行路侠客', daoist: '山中老道', escort: '女镖师' };
  const COLORS = {
    traveller: { ink: '#302a24', cloth: '#293c49', red: '#a54834', amber: '#c28e42', skin: '#dfb367', hair: '#302923' },
    daoist: { ink: '#36342c', cloth: '#566468', red: '#884b37', amber: '#b99d65', skin: '#d8ad6e', hair: '#ded2b3' },
    escort: { ink: '#302c25', cloth: '#294752', red: '#a63e31', amber: '#cd984b', skin: '#dfb572', hair: '#292c29' }
  };
  const RIG = Object.freeze({
    version: 3, units: 'SVG user units', profile: 'strict side profile', rods: 'none in every context',
    slots: ['head', 'torso', 'upperArmFar', 'forearmFar', 'upperArmNear', 'forearmNear', 'thighFar', 'shinFar', 'shoeFar', 'thighNear', 'shinNear', 'shoeNear', 'skirtBack', 'skirtFront', 'waist', 'weapon'],
    joints: { neck: [0, -160], shoulderFar: [-7, -137], shoulderNear: [6, -137], hip: [0, 0], skirtBack: [-7, 0], skirtFront: [7, 0], waist: [0, 0] },
    lengths: { upperArm: 88, forearm: 80, thigh: 110, shin: 112 },
    waistOwner: 'waist slot exclusively; torso and skirt have no belt',
    skirtRule: 'two rigid independent pieces rotate about their own fixed waist hinges',
    garmentFacings: ['right: independently authored torso/collar', 'left: independently authored torso/collar'],
    directions: ['left', 'right'], expressions: ['calm', 'angry', 'sad'], identities: TYPES,
    wearRule: 'separate local overlays; base paths and fills never change'
  });
  const DEFAULT = { head: 'traveller', upper: 'traveller', lower: 'traveller', waist: 'traveller', expression: 'calm', pose: 'idle', step: 100, facing: 'right', wear: 0, joints: false };
  const num = n => Number(n.toFixed(5));
  const fmt = p => `${num(p.x)} ${num(p.y)}`;
  const radians = d => d * Math.PI / 180;
  const degrees = r => r * 180 / Math.PI;
  const lerp = (a, b, t) => a + (b - a) * t;
  const point = (x, y) => ({ x, y });
  const add = (p, q) => point(p.x + q.x, p.y + q.y);
  const dist = (a, b) => Math.hypot(a.x - b.x, a.y - b.y);
  const rotated = (p, d) => point(p.x * Math.cos(radians(d)) - p.y * Math.sin(radians(d)), p.x * Math.sin(radians(d)) + p.y * Math.cos(radians(d)));
  const angle = (a, b) => degrees(Math.atan2(a.x - b.x, b.y - a.y));
  const local = (a, d, length) => add(a, rotated(point(0, length), d));
  function normalize(input) {
    const s = { ...DEFAULT, ...input };
    for (const k of ['head', 'upper', 'lower', 'waist']) if (!TYPES.includes(s[k])) s[k] = DEFAULT[k];
    if (!['calm', 'angry', 'sad'].includes(s.expression)) s.expression = 'calm';
    if (!['idle', 'walk', 'lunge'].includes(s.pose)) s.pose = 'idle';
    s.facing = s.facing === 'left' ? 'left' : 'right';
    for (const k of ['step', 'wear']) s[k] = Number.isFinite(Number(s[k])) ? Math.max(0, Math.min(100, Number(s[k]))) : DEFAULT[k];
    s.joints = !!s.joints;
    return s;
  }
  // Knee is solved from the two fixed leather lengths, never by stretching a part.
  function solveLeg(hip, ankle, bend) {
    const a = RIG.lengths.thigh, b = RIG.lengths.shin;
    const dx = ankle.x - hip.x, dy = ankle.y - hip.y;
    const d = Math.hypot(dx, dy);
    if (d >= a + b || d <= Math.abs(a - b)) throw new Error('Unreachable leg target');
    const along = (a * a - b * b + d * d) / (2 * d);
    const height = Math.sqrt(Math.max(0, a * a - along * along));
    return point(hip.x + along * dx / d + bend * height * dy / d, hip.y + along * dy / d - bend * height * dx / d);
  }
  function geometry(input) {
    const s = normalize(input), f = s.facing === 'right' ? 1 : -1, t = s.step / 100;
    let hip = point(0, 0), lean = 0, farX = -32, nearX = 37;
    let upperNear = -17, elbowNear = -21, upperFar = 11, elbowFar = 8, swordAngle = 51;
    if (s.pose === 'lunge') {
      hip = point(lerp(0, 12, t), lerp(0, 62, t)); lean = 17 * t;
      farX = lerp(-32, -131, t); nearX = lerp(37, 156, t);
      upperNear = lerp(-17, -88, t); elbowNear = lerp(-21, -2, t);
      upperFar = lerp(11, 83, t); elbowFar = lerp(8, 4, t); swordAngle = lerp(51, 0, t);
    } else if (s.pose === 'walk') {
      const wave = Math.sin(t * Math.PI * 2), stride = wave * 77;
      hip = point(0, 16 + 10 * Math.abs(wave)); lean = 3;
      farX = -18 - stride; nearX = 18 + stride;
      upperNear = -17 + wave * 22; elbowNear = -21;
      upperFar = 11 - wave * 22; elbowFar = 8; swordAngle = 54;
    }
    const torsoPoint = p => add(hip, rotated(p, lean));
    const neck = torsoPoint(point(...RIG.joints.neck));
    const shoulderFar = torsoPoint(point(...RIG.joints.shoulderFar));
    const shoulderNear = torsoPoint(point(...RIG.joints.shoulderNear));
    const elbowN = local(shoulderNear, upperNear + lean, RIG.lengths.upperArm);
    const wristN = local(elbowN, upperNear + elbowNear + lean, RIG.lengths.forearm);
    const elbowF = local(shoulderFar, upperFar + lean, RIG.lengths.upperArm);
    const wristF = local(elbowF, upperFar + elbowFar + lean, RIG.lengths.forearm);
    const ankleNear = point(nearX, 213), ankleFar = point(farX, 213);
    const kneeNear = solveLeg(hip, ankleNear, 1), kneeFar = solveLeg(hip, ankleFar, 1);
    const skirtFront = torsoPoint(point(...RIG.joints.skirtFront));
    const skirtBack = torsoPoint(point(...RIG.joints.skirtBack));
    const result = { hip, neck, shoulderFar, shoulderNear, elbowNear: elbowN, elbowFar: elbowF, wristNear: wristN, wristFar: wristF, kneeNear, kneeFar, ankleNear, ankleFar, skirtFront, skirtBack };
    for (const p of Object.values(result)) p.x *= f;
    result.lean = lean * f;
    result.frontSkirtAngle = angle(result.hip, result.kneeNear) * .72;
    result.backSkirtAngle = angle(result.hip, result.kneeFar) * .72;
    result.swordAngle = s.facing === 'right' ? swordAngle : 180 - swordAngle;
    return result;
  }
  const path = (d, fill, stroke = '#362b22', width = 1.65, extra = '') => `<path d="${d}" fill="${fill}" stroke="${stroke}" stroke-width="${width}" stroke-linejoin="round" stroke-linecap="round" ${extra}/>`;
  const line = (d, color = '#b68b4f', width = 1.2, extra = '') => path(d, 'none', color, width, extra);
  const pin = (x, y, size = 5) => `<circle cx="${x}" cy="${y}" r="${size}" fill="#b28b48" stroke="#392e23" stroke-width="1.5"/><circle cx="${x}" cy="${y}" r="${size * .37}" fill="#51422c"/>`;
  const hole = (x, y, r = 2.8) => `<circle cx="${x}" cy="${y}" r="${r}" fill="#f6e9ca"/>`;
  function wearLayer(kind, wear) {
    if (!wear) return '';
    const alpha = (wear / 100).toFixed(3);
    const marks = {
      torso: line('M-22 -117l4 14m-3 25l2 20M21 -59l-2 24', '#d7bc82', 2),
      upperArm: line('M-16 48l3 13m26 10l-5 13', '#d9bf88', 2),
      forearm: line('M-12 44l3 14M12 66l-5 4', '#d9bf88', 1.8),
      thigh: line('M-13 43l4 18m19 31l-5 10', '#d9bf88', 2),
      shin: line('M-10 39l2 12M9 83l-3 12', '#d9bf88', 2),
      skirtFront: line('M-18 57l2 20M19 61l-3 12', '#d9bf88', 2) + (wear > 35 ? path('M-9 56L9 53 11 73 -7 76Z', '#bc8d50', '#573e29', 1) + line('M-12 58l7 1m-7 6l7 1m-6 6l6 1m16-17l7-1m-7 8l7-1m-6 8l6-1M-4 50l1 7m7-8l1 7m-6 17l1 7m7-8l1 6', '#ede0b7', 1) : ''),
      skirtBack: line('M-19 62l3 19M18 44l-2 18', '#d9bf88', 2),
      shoe: line('M12 8l15 2', '#d4b777', 2)
    };
    return `<g data-layer="wear" opacity="${alpha}">${marks[kind] || ''}</g>`;
  }
  function head(type, expression = 'calm', facing = 'right') {
    const c = COLORS[type], f = facing === 'right' ? 1 : -1;
    let hair = '', details = '';
    if (type === 'daoist') {
      hair = path('M-30-63Q-37-94-16-111L-14-132 11-132 14-109Q32-99 28-78L16-83-15-74Z', c.hair, c.ink) + path('M-17-132L12-132 14-114-19-114Z', c.cloth, c.ink) + line('M-12-124H7M-10-119H9', c.amber, 1.8);
      details = path('M21-31L35-30 39-7 33 30 27 13 22 42 17 14 11 31 13-9Z', '#ddd0af', c.ink, 1.2) + line('M20-17L23 17M27-19L29 13M15-12L17 13', '#9e9176', 1) + line('M23-33q6-3 13 0', c.hair, 2.8) + line('M-11-127q-4 3 0 5t6-3q-1-4 4-3t3 5', c.amber, 1) + line('M12-77l9 2M10-73l8 2', '#a5824e', .8);
    } else if (type === 'escort') {
      hair = path('M-31-56Q-45-85-25-108L-10-115Q-24-139-8-144 8-144 9-126L19-111Q34-104 31-83L19-82-20-65Z', c.hair, c.ink) + path('M-28-70L-39-29-30-7-23-34-18-61Z', c.hair, c.ink) + line('M-32-52l6 6m-9 6l7 6m-9 5l7 6', '#aa844a', 1.2);
      details = line('M-14-114Q-1-121 16-109M-14-109Q-1-116 17-104', c.amber, 1.2) + path('M0-123L4-130 8-123 4-116Z', c.red, c.ink, 1) + line('M-23-98q13-5 32-1M-24-94q14-5 32-1M-24-90q14-4 31-1', '#987c48', .9) + line('M-9-113l2 5m4-6l2 5m4-5l2 5', '#f0d19a', .8);
    } else {
      hair = path('M-29-58Q-43-94-19-108L-6-113Q-23-130-13-141 0-152 13-139 24-125 10-115L20-109Q37-101 31-82L17-80-14-66Z', c.hair, c.ink) + path('M-28-73L-37-35-25-42-23-21-12-60Z', c.hair, c.ink);
      details = path('M-29-94Q-1-101 29-91L29-82Q0-92-29-86Z', c.red, c.ink, 1) + line('M-23-91q23-5 45 1', c.amber, 1) + path('M-6-116L10-119 11-113-4-110Z', c.red, c.ink, 1) + line('M-13-131q4-10 13-8M-10-126q5-8 14-6M-25-102q17-7 36-1', c.amber, 1);
    }
    const face = path('M-20-89Q6-103 25-86L27-64 43-46 32-42 35-31 27-27 20-10 8-2 10 20-9 20-11-10Q-30-33-25-62Z', c.skin, c.ink, 1.6);
    const ear = path('M-9-59Q-18-69-21-55-22-39-11-36L-8-42Q-17-42-15-54L-10-50', c.skin, c.ink, 1.2);
    const eye = expression === 'angry' ? path('M12-62L30-55 15-53Z', c.ink, c.ink, .7) + line('M10-70L31-60', c.ink, 2) : expression === 'sad' ? path('M13-56Q20-50 30-54L16-51Z', c.ink, c.ink, .5) + line('M11-67L30-61', c.ink, 1.7) : path('M12-60Q23-62 31-55L15-54Z', c.ink, c.ink, .6) + line('M11-68Q21-70 30-63', c.ink, 1.6);
    const mouth = expression === 'angry' ? line('M28-28l6-1', c.ink, 1.8) : expression === 'sad' ? line('M27-27q5-4 8-1', c.ink, 1.5) : line('M28-29l7-1', c.ink, 1.2);
    return `<g data-head="${type}-${expression}" transform="scale(${f} 1)">${hair}${face}${ear}${eye}${mouth}${details}</g>`;
  }
  function torso(type, facing, wear) {
    const c = COLORS[type];
    // These are separate facing assets, including separate overlap order. The torso is never mirrored.
    const right = [
      path('M-10-169L8-169 25-145 30-113 25-50 29 8-27 8-25-67-30-133-20-151Z', c.cloth, c.ink),
      path('M-10-168L4-164 25-128 23-105 13-116-4-144-18-151Z', c.amber, c.ink, 1),
      path('M8-169L18-157-7-107-20-36-26-13-23-68-14-115Z', c.red, c.ink, 1),
      line('M8-165L-9-115-22-45', '#e0bb70', 1)
    ];
    const left = [
      path('M10-169L-8-169-25-145-31-115-26-47-29 8 27 8 25-67 30-133 20-151Z', c.cloth, c.ink),
      path('M-8-169L-20-154 8-108 20-40 26-16 23-73 16-117Z', c.amber, c.ink, 1),
      path('M10-168L-3-163-25-128-22-105-12-116 5-143 18-151Z', c.red, c.ink, 1),
      line('M9-164L-2-154-22-119', '#e0bb70', 1)
    ];
    let ornament = '';
    if (type === 'daoist') {
      ornament = facing === 'right' ? line('M3-151q-9-6-6 4t-8 7M-2-138q-9-5-6 4t-8 7', '#dfc99b', 1.4) : line('M2-150q9-6 6 4t8 7M9-137q8-5 5 4t7 7', '#dfc99b', 1.4);
    } else if (type === 'escort') {
      ornament = facing === 'right' ? line('M4-150l-7 6 2 6m0-1l-7 6 1 5', '#ead09a', 1.5) : line('M4-151l7 6-2 6m1-1l7 6-1 5', '#ead09a', 1.5);
    }
    return `<g data-garment="${type}-${facing}-youren" data-slot-owner="torso">${(facing === 'right' ? right : left).join('')}${ornament}${wearLayer('torso', wear)}</g>`;
  }
  function upperArm(type, wear) {
    const c = COLORS[type];
    const contour = type === 'daoist' ? 'M-12-5Q0-15 12-5L21 28 32 79 16 94-30 94-27 43Z' : type === 'escort' ? 'M-12-5Q0-15 12-5L14 30 16 78 9 90-15 90-16 28Z' : 'M-12-5Q0-15 12-5L18 27 22 78 10 91-19 91-20 31Z';
    const trim = type === 'daoist' ? line('M-27 78H28M-26 83H25', c.amber, 1.3) : line('M-13 75H14', c.amber, 1.5);
    const guard = type === 'escort' ? path('M-11 12H11L13 38-10 42Z', c.amber, c.ink, 1) + line('M-7 23H8M-6 30H8', c.red, 1.1) : '';
    return path(contour, c.cloth, c.ink) + trim + guard + hole(-8, 83, 1.7) + hole(0, 83, 1.7) + hole(8, 83, 1.7) + wearLayer('upperArm', wear);
  }
  function forearm(type, wear, holding) {
    const c = COLORS[type];
    const hand = holding ? path('M-7 75L9 75 12 84 8 92 0 93-9 85Z', c.skin, c.ink, 1.1) + line('M-3 80l1 7m4-7l1 7', c.ink, .8) : path('M-6 75L7 75 14 87 11 90 4 83 8 97 4 99-1 88 0 100-4 100-6 87-9 96-12 94-8 81Z', c.skin, c.ink, 1.1);
    return path('M-11-5Q0-12 11-5L12 57 10 73-10 73-13 45Z', c.cloth, c.ink) + path('M-12 53H12L10 72H-10Z', c.red, c.ink, .9) + hole(-5, 62, 1.7) + hole(3, 62, 1.7) + wearLayer('forearm', wear) + hand;
  }
  function thigh(type, wear) {
    const c = COLORS[type];
    return path('M-17-3Q0-13 17-3L16 51 11 109Q0 121-11 109L-16 44Z', c.cloth, c.ink) + line('M-12 93L10 85', c.amber, 1.4) + wearLayer('thigh', wear);
  }
  function shin(type, wear) {
    const c = COLORS[type];
    return path('M-10-3Q0-12 10-3L12 57 8 112H-8L-12 54Z', c.cloth, c.ink) + line('M-9 25L9 33M-10 48L10 56M-8 81L8 89', c.amber, 1.3) + wearLayer('shin', wear);
  }
  function shoe(type, facing, wear) {
    const c = COLORS[type], f = facing === 'right' ? 1 : -1;
    return `<g transform="scale(${f} 1)">${path('M-9-8H8L11 1 28 8Q38 11 39 5L43 8Q45 17 34 18H-14L-13 8Z', c.ink, c.ink, 1.2)}${line('M-11 12H34M7-1L18 5 11 8', c.amber, 1.1)}${wearLayer('shoe', wear)}</g>`;
  }
  function skirt(type, which, wear) {
    const c = COLORS[type];
    const front = which === 'front';
    const variants = {
      traveller: { front: 'M-19-6L17-6 24 95 13 101-23 96-25 56Z', back: 'M-17-6L18-6 23 93-15 98-24 90-22 35Z', frontTrim: 'M-19 92L18 96', backTrim: 'M-19 87L18 91' },
      daoist: { front: 'M-19-6H18L28 115 22 124-28 123-29 62Z', back: 'M-18-6H18L28 119 15 127-25 122-28 57Z', frontTrim: 'M-24 116L23 117', backTrim: 'M-23 116L22 121' },
      escort: { front: 'M-18-6H18L20 79 6 89-20 81-22 38Z', back: 'M-16-6H16L20 80 5 87-20 76-20 30Z', frontTrim: 'M-16 76L6 83 15 77', backTrim: 'M-16 72L4 82 15 77' }
    };
    const v = variants[type];
    const body = path(v[which], front ? c.cloth : c.amber, c.ink, 1.6);
    const border = line(front ? v.frontTrim : v.backTrim, front ? c.amber : c.red, 1.5);
    return body + border + wearLayer(front ? 'skirtFront' : 'skirtBack', wear);
  }
  function waist(type) {
    const c = COLORS[type];
    let accessory = '';
    if (type === 'daoist') {
      accessory = line('M-17 7L-22 32', c.ink, 2) + path('M-22 23Q-34 24-31 35-44 54-24 59-6 57-15 38-10 25-22 23Z', c.amber, c.ink, 1.3) + line('M-30 37L-15 39', c.red, 2);
    } else if (type === 'escort') {
      accessory = line('M-18 9V33', c.red, 2) + path('M-27 28L-13 26-9 47-24 50Z', c.amber, c.ink, 1.2) + line('M-22 32v12m-4-8h10m-7 5h8', c.ink, 1);
    } else {
      accessory = line('M-17 9L-22 28', c.ink, 1.7) + path('M-27 27L-16 28-11 43Q-21 54-34 42Z', c.amber, c.ink, 1.2) + line('M-28 31L-16 32', c.red, 2);
    }
    return `<g data-slot-owner="waist">${path('M-31-9H30L28 10H-29Z', type === 'daoist' ? c.amber : c.red, c.ink, 1.5)}${line('M-27-4H26M-26 5H25', '#d6b574', .8)}${path('M-2-6H8V7H-2Z', c.amber, c.ink, 1.1)}${accessory}</g>`;
  }
  function sword() {
    return path('M-23-4H0V4H-23Z', '#48382b', '#302a24', 1.1) + path('M3-14Q-2-15-3-9L-2 9Q1 15 5 13L7 7 7-7Z', '#b78b43', '#302a24', 1.4) + path('M8-4H150L169 0 150 4H8Z', '#cbbd94', '#42382a', 1.1) + line('M12 0H156', '#75694f', .7);
  }
  function partGroup(slot, asset, transform, content) {
    return `<g data-slot="${slot}" data-asset="${asset}" transform="${transform}">${content}</g>`;
  }
  function segment(slot, type, a, b, content, endPin = false) {
    const len = dist(a, b);
    return partGroup(slot, `${type}-${slot.replace(/Far|Near/, '')}`, `translate(${fmt(a)}) rotate(${num(angle(a, b))})`, content + pin(0, 0) + (endPin ? pin(0, len, 4.3) : ''));
  }
  function jointGuides(g) {
    const keys = ['neck', 'shoulderNear', 'elbowNear', 'wristNear', 'shoulderFar', 'elbowFar', 'wristFar', 'hip', 'kneeNear', 'kneeFar', 'ankleNear', 'ankleFar', 'skirtFront', 'skirtBack'];
    const chain = ['wristFar', 'elbowFar', 'shoulderFar', 'neck', 'shoulderNear', 'elbowNear', 'wristNear'];
    const linePath = chain.map((k, i) => `${i ? 'L' : 'M'}${fmt(g[k])}`).join(' ') + `M${fmt(g.neck)}L${fmt(g.hip)}L${fmt(g.kneeNear)}L${fmt(g.ankleNear)}M${fmt(g.hip)}L${fmt(g.kneeFar)}L${fmt(g.ankleFar)}`;
    return `<g class="joint-guide"><path d="${linePath}"/>${keys.map(k => `<circle data-joint="${k}" cx="${num(g[k].x)}" cy="${num(g[k].y)}" r="4"/>`).join('')}</g>`;
  }
  function render(input, options = {}) {
    const s = normalize(input), g = geometry(s), p = [];
    const gt = pt => `translate(${fmt(pt)})`;
    p.push(partGroup('skirtBack', `${s.lower}-skirt-back`, `${gt(g.skirtBack)} rotate(${num(g.backSkirtAngle)})`, skirt(s.lower, 'back', s.wear) + pin(0, 0, 4.2)));
    p.push(segment('thighFar', s.lower, g.hip, g.kneeFar, thigh(s.lower, s.wear)));
    p.push(segment('shinFar', s.lower, g.kneeFar, g.ankleFar, shin(s.lower, s.wear)));
    p.push(partGroup('shoeFar', `${s.lower}-shoe-${s.facing}`, gt(g.ankleFar), shoe(s.lower, s.facing, s.wear) + pin(0, 0, 3.5)));
    p.push(segment('upperArmFar', s.upper, g.shoulderFar, g.elbowFar, upperArm(s.upper, s.wear)));
    p.push(segment('forearmFar', s.upper, g.elbowFar, g.wristFar, forearm(s.upper, s.wear, false), true));
    p.push(segment('thighNear', s.lower, g.hip, g.kneeNear, thigh(s.lower, s.wear)));
    p.push(segment('shinNear', s.lower, g.kneeNear, g.ankleNear, shin(s.lower, s.wear)));
    p.push(partGroup('shoeNear', `${s.lower}-shoe-${s.facing}`, gt(g.ankleNear), shoe(s.lower, s.facing, s.wear) + pin(0, 0, 3.5)));
    p.push(partGroup('skirtFront', `${s.lower}-skirt-front`, `${gt(g.skirtFront)} rotate(${num(g.frontSkirtAngle)})`, skirt(s.lower, 'front', s.wear) + pin(0, 0, 4.2)));
    p.push(partGroup('torso', `${s.upper}-torso-${s.facing}`, `${gt(g.hip)} rotate(${num(g.lean)})`, torso(s.upper, s.facing, s.wear)));
    p.push(partGroup('head', `${s.head}-${s.expression}-${s.facing}`, `${gt(g.neck)} rotate(${num(g.lean)})`, head(s.head, s.expression, s.facing) + pin(0, 0, 3.8)));
    p.push(partGroup('waist', `${s.waist}-waist`, `${gt(g.hip)} rotate(${num(g.lean)})`, waist(s.waist)));
    p.push(segment('upperArmNear', s.upper, g.shoulderNear, g.elbowNear, upperArm(s.upper, s.wear)));
    p.push(partGroup('weapon', 'shared-single-jian', `${gt(g.wristNear)} rotate(${num(g.swordAngle)})`, sword()));
    p.push(segment('forearmNear', s.upper, g.elbowNear, g.wristNear, forearm(s.upper, s.wear, true), true));
    const viewBox = options.viewBox || '-450 -330 900 620';
    const title = options.title || `${NAMES[s.head]}的平面皮影，${s.facing === 'right' ? '朝右' : '朝左'}`;
    return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="${viewBox}" role="img" aria-label="${title}"><title>${title}</title><desc>固定长度的分件皮影，前后裙片独立铰接，单一腰饰槽，独立左右交领，无操纵签。</desc>${options.ground === false ? '' : line('M-330 233H330', '#a9916338', 1)}<g data-puppet-facing="${s.facing}" fill-opacity=".94">${p.join('')}</g>${s.joints ? jointGuides(g) : ''}</svg>`;
  }
  function headThumbnail(type) {
    return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="-55 -157 110 183" aria-hidden="true">${head(type)}</svg>`;
  }
  function parts(input) {
    const s = normalize(input), wrap = (label, box, content, slot) => ({ label, slot, svg: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="${box}" role="img" aria-label="${label}">${content}</svg>` });
    return [
      wrap('头茬', '-51 -155 107 184', head(s.head, s.expression, s.facing) + pin(0, 0, 3.8), 'head'),
      wrap('上身 · 无腰带', '-45 -179 90 198', torso(s.upper, s.facing, s.wear) + pin(0, -160, 3.8), 'torso'),
      wrap('前裙片', '-38 -17 76 151', skirt(s.lower, 'front', s.wear) + pin(0, 0), 'skirtFront'),
      wrap('后裙片', '-38 -17 76 151', skirt(s.lower, 'back', s.wear) + pin(0, 0), 'skirtBack'),
      wrap('腰饰 · 独占腰带', '-45 -28 90 105', waist(s.waist), 'waist'),
      wrap('臂片 × 2', '-64 -17 120 125', `<g transform="translate(-28 0)">${upperArm(s.upper, s.wear)}${pin(0, 0)}</g><g transform="translate(28 0)">${forearm(s.upper, s.wear, false)}${pin(0, 0)}</g>`, 'arms'),
      wrap('腿片 × 2', '-50 -17 100 142', `<g transform="translate(-25 0)">${thigh(s.lower, s.wear)}${pin(0, 0)}</g><g transform="translate(25 0)">${shin(s.lower, s.wear)}${pin(0, 0)}</g>`, 'legs'),
      wrap('单剑', '-40 -30 225 60', sword(), 'weapon')
    ];
  }
  function check(input) {
    const s = normalize(input), g = geometry(s), lengths = RIG.lengths;
    const measures = [
      ['upper arm near', g.shoulderNear, g.elbowNear, lengths.upperArm], ['upper arm far', g.shoulderFar, g.elbowFar, lengths.upperArm],
      ['forearm near', g.elbowNear, g.wristNear, lengths.forearm], ['forearm far', g.elbowFar, g.wristFar, lengths.forearm],
      ['thigh near', g.hip, g.kneeNear, lengths.thigh], ['thigh far', g.hip, g.kneeFar, lengths.thigh],
      ['shin near', g.kneeNear, g.ankleNear, lengths.shin], ['shin far', g.kneeFar, g.ankleFar, lengths.shin]
    ];
    const errors = measures.filter(([, a, b, length]) => Math.abs(dist(a, b) - length) > 1e-7).map(([label]) => label);
    const maxLengthError = Math.max(...measures.map(([, a, b, length]) => Math.abs(dist(a, b) - length)));
    return { ok: !errors.length, errors, maxLengthError, limbLengths: Object.fromEntries(measures.map(([label, a, b]) => [label, num(dist(a, b))])), anchors: g };
  }
  return { TYPES, NAMES, COLORS, RIG, DEFAULT, normalize, geometry, render, headThumbnail, parts, check };
});
