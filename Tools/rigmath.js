// Modelo numérico del skinning para la vista previa web. Unity usa su SkinnedMeshRenderer.
(function(root){
 function weights(bones,layer,p){
  let a=layer.bones.map(index=>{let b=bones[index],end=b;
   for(const i of layer.bones)if(bones[i].parent===index){end=bones[i];break;}
   let dx=end.x-b.x,dy=end.y-b.y,d=dx*dx+dy*dy;
   let t=d<1e-6?0:Math.max(0,Math.min(1,((p[0]-b.x)*dx+(p[1]-b.y)*dy)/d));
   return {index,weight:1/Math.pow(Math.max(.04,Math.hypot(p[0]-b.x-t*dx,p[1]-b.y-t*dy)),layer.falloff||2)};
  }).sort((a,b)=>b.weight-a.weight).slice(0,4);
  let total=a.reduce((s,x)=>s+x.weight,0);a.forEach(x=>x.weight/=total);return a;
 }
 function matrices(bones,angles){
  return bones.reduce((m,b,i)=>{
   let parent=b.parent<0?{x:0,y:0,angle:0}:m[b.parent],base=b.parent<0?{x:0,y:0}:bones[b.parent];
   let dx=b.x-base.x,dy=b.y-base.y,c=Math.cos(parent.angle),s=Math.sin(parent.angle);
   m.push({x:parent.x+c*dx-s*dy,y:parent.y+s*dx+c*dy,angle:parent.angle+(angles[b.name]||0)*Math.PI/180});return m;
  },[]);
 }
 function deform(bones,matrices,p,w){let x=0,y=0;
  for(const a of w){let b=bones[a.index],m=matrices[a.index],dx=p[0]-b.x,dy=p[1]-b.y,c=Math.cos(m.angle),s=Math.sin(m.angle);
   x+=(m.x+c*dx-s*dy)*a.weight;y+=(m.y+s*dx+c*dy)*a.weight;
  }return [x,y];
 }
 const api={weights,matrices,deform};if(typeof module!=='undefined')module.exports=api;else root.RigMath=api;
})(typeof globalThis!=='undefined'?globalThis:this);
