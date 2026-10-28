const fs=require('fs'),assert=require('assert'),math=require('./rigmath.js');
const spec=JSON.parse(fs.readFileSync(__dirname+'/../Assets/Traigger/Art/assembly.json','utf8'));
const neutral=math.matrices(spec.bones,{}),bent=math.matrices(spec.bones,{ForearmLeft:35});
let vertices=0,maxRestError=0;
for(const l of spec.layers)for(let y=0;y<=22;y++)for(let x=0;x<=18;x++){
 const p=[l.rect[0]+x/18*l.rect[2],l.rect[1]+y/22*l.rect[3]],w=math.weights(spec.bones,l,p);
 assert(Math.abs(w.reduce((s,a)=>s+a.weight,0)-1)<1e-9);
 assert(w.every(a=>l.bones.includes(a.index)&&a.weight>=0));
 const q=math.deform(spec.bones,neutral,p,w);maxRestError=Math.max(maxRestError,Math.hypot(q[0]-p[0],q[1]-p[1]));vertices++;
}
assert(maxRestError<1e-9);
const arm=spec.layers.find(l=>l.name==='ArmLeft'),a=[arm.rect[0],arm.rect[1]],b=[arm.rect[0]+arm.rect[2],arm.rect[1]+arm.rect[3]];
const aa=math.deform(spec.bones,bent,a,math.weights(spec.bones,arm,a)),bb=math.deform(spec.bones,bent,b,math.weights(spec.bones,arm,b));
const change=Math.abs(Math.hypot(a[0]-b[0],a[1]-b[1])-Math.hypot(aa[0]-bb[0],aa[1]-bb[1]));
assert(change>.001);
console.log(JSON.stringify({scope:'Modelo matemático JS, no ejecución Unity',vertices,maxRestError,endpointDistanceChange:change,normalizedWeights:'PASS',nonRigidDeformation:'PASS'},null,2));
