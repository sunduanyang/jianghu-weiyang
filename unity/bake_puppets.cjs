// Convert original authored vector parts to native Unity textures, preserving SVG source.
const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto');
const api=require('../prototypes/shadow-puppets/puppet.js');
let sharp;
try { sharp=require('sharp'); }
catch(error) {
  if(!process.env.CODEX_NODE_MODULES)throw new Error('Install asset dependencies first: npm ci', {cause:error});
  sharp=require(require.resolve('sharp',{paths:[process.env.CODEX_NODE_MODULES]}));
}
const dest=path.join(__dirname,'JianghuUnity/Assets/Resources/Puppets');fs.mkdirSync(dest,{recursive:true});
const boxes={head:[-55,-157,112,190],torso:[-50,-185,100,205],upperArmFar:[-35,-12,70,112],upperArmNear:[-35,-12,70,112],forearmFar:[-30,-12,60,110],forearmNear:[-30,-12,60,110],thighFar:[-30,-12,60,136],thighNear:[-30,-12,60,136],shinFar:[-25,-12,50,138],shinNear:[-25,-12,50,138],shoeFar:[-65,-20,130,60],shoeNear:[-65,-20,130,60],skirtFront:[-45,-20,90,164],skirtBack:[-45,-20,90,164],waist:[-50,-35,100,120],weapon:[-40,-30,225,60]};
function groups(svg){const re=/<\/?g\b[^>]*>/g,stack=[],result={};let m;while((m=re.exec(svg))){if(m[0].startsWith('</')){const g=stack.pop();if(g&&g.slot)result[g.slot]=svg.slice(g.start,re.lastIndex);}else{const slot=/data-slot="([^"]+)"/.exec(m[0]);stack.push({slot:slot&&slot[1],start:m.index});}}return result;}
function content(g){return g.slice(g.indexOf('>')+1,g.lastIndexOf('</g>'));}
function wear(g){const re=/<g data-layer="wear"[^>]*>([\s\S]*?)<\/g>/;const m=re.exec(g);return m?m[1]:'';}
const pending=new Map();
function texture(svg,box){if(!svg)return '';const key='part-'+crypto.createHash('sha1').update(svg+box.join(',')).digest('hex').slice(0,16);if(!pending.has(key)){const raw=`<svg xmlns="http://www.w3.org/2000/svg" viewBox="${box.join(' ')}" width="${box[2]*2}" height="${box[3]*2}">${svg}</svg>`;pending.set(key,()=>sharp(Buffer.from(raw)).png().toFile(path.join(dest,key+'.png')));}return key;}
(async()=>{const states={};for(const type of api.TYPES)for(const facing of ['left','right'])for(const expression of ['calm','angry','sad']){
 const s={head:type,upper:type,lower:type,waist:type,facing,expression};const base=groups(api.render({...s,wear:0})),low=groups(api.render({...s,wear:35})),high=groups(api.render({...s,wear:100}));
 states[`${type}-${facing}-${expression}`]=Object.entries(base).map(([slot,g])=>{const b=boxes[slot],svg=content(g),lo=wear(low[slot]),hi=wear(high[slot]);return {slot,box:b,base:texture(svg,b),low:texture(lo,b),high:texture(hi,b),svg,wearLow:lo,wearHigh:hi};});
}for(const run of pending.values())await run();const poses=[];for(const pose of ['idle','walk','lunge'])for(const facing of ['left','right'])for(let step=0;step<=100;step+=5)poses.push({pose,facing,step,geometry:api.geometry({pose,facing,step})});fs.writeFileSync(path.join(dest,'index.json'),JSON.stringify({states,rig:api.RIG}));fs.writeFileSync(path.join(dest,'source-poses.json'),JSON.stringify(poses));console.log(JSON.stringify({states:Object.keys(states).length,textures:pending.size,referencePoses:poses.length}));})().catch(e=>{console.error(e);process.exitCode=1;});
