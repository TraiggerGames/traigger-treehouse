# Añade huesos a la cola (cadena de 5) y a las orejas, con sus pesos, y exporta el FBX con las formas de la boca.
import bpy, sys, numpy as np, mathutils
args=sys.argv[sys.argv.index('--')+1:]; blend,out,preview=args
bpy.ops.wm.open_mainfile(filepath=blend)
arm=[o for o in bpy.data.objects if o.type=='ARMATURE'][0]; mesh=[o for o in bpy.data.objects if o.type=='MESH'][0]; me=mesh.data
arm.data.pose_position='REST'
# Fuera los parpadeos por deformación (los ojos cerrados van por textura en el shader).
for name in ('BlinkRight','BlinkLeft'):
    k=me.shape_keys.key_blocks.get(name)
    if k: mesh.shape_key_remove(k)
M=mesh.matrix_world; co=np.array([M@v.co for v in me.vertices]); N=len(co)
A=arm.matrix_world; Ainv=A.inverted()
def ss(e0,e1,x): t=np.clip((x-e0)/(e1-e0),0,1); return t*t*(3-2*t)
# --- Cola: vértices detrás del cuerpo y por debajo del pecho.
tail=(co[:,1]>-0.02)&(co[:,2]<0.95)
hips=np.array(A@arm.data.bones['Hips'].head_local)
tc=co[tail]; root_i=np.argmin(np.linalg.norm(tc-[hips[0],hips[1]+0.25,hips[2]],axis=1)); root=tc[root_i]
dist=np.linalg.norm(tc-root,axis=1); L=dist.max()
pts=[root]
for k in range(1,6):
    sel=(dist>L*(k-0.5)/5)&(dist<=L*(k+0.5)/5) if k<5 else dist>L*0.9
    pts.append(tc[sel].mean(0) if sel.any() else pts[-1])
pts=np.array(pts); print("cola: raíz",root.round(3),"punta",pts[-1].round(3),"largo",round(L,3),"vértices",tail.sum())
# --- Orejas
ears={}
for side,sx in (('Left',1),('Right',-1)):
    m=(co[:,2]>1.50)&(co[:,0]*sx>0.09)&(co[:,1]<-0.08)&(co[:,1]>-0.40)
    e=co[m]; tip=e[np.argmax(e[:,2]+np.abs(e[:,0])*0.5)]; base=np.array([e[:,0].mean()*0.6,e[:,1].mean(),1.53])
    ears[side]=(m,base,tip); print("oreja",side,"base",base.round(3),"punta",tip.round(3),"vértices",m.sum())
# --- Huesos
bpy.context.view_layer.objects.active=arm; bpy.ops.object.mode_set(mode='EDIT'); eb=arm.data.edit_bones
parent=eb['Hips']; names=[]
for k in range(5):
    b=eb.new(f"Tail{k+1}"); b.head=Ainv@mathutils.Vector(pts[k]); b.tail=Ainv@mathutils.Vector(pts[k+1]); b.parent=parent; b.use_connect=k>0; parent=b; names.append(b.name)
for side,(m,base,tip) in ears.items():
    b=eb.new(f"{side}Ear"); b.head=Ainv@mathutils.Vector(base); b.tail=Ainv@mathutils.Vector(tip); b.parent=eb['Head']
bpy.ops.object.mode_set(mode='OBJECT')
# --- Pesos
groups={g.name:g for g in mesh.vertex_groups}
def group(n): return groups.get(n) or groups.setdefault(n,mesh.vertex_groups.new(name=n))
def set_weights(i,new):   # new: {grupo: peso}, sustituye todo lo demás
    i=int(i)
    for g in mesh.vertex_groups:
        try: g.remove([i])
        except RuntimeError: pass
    total=sum(new.values())
    for n,w in new.items():
        if w>1e-4: group(n).add([i],w/total,'REPLACE')
idx=np.where(tail)[0]
seg=pts[1:]-pts[:-1]
for j,i in enumerate(idx):
    p=co[i]; best=None
    for k in range(5):
        t=np.clip(np.dot(p-pts[k],seg[k])/max(np.dot(seg[k],seg[k]),1e-9),0,1); d=np.linalg.norm(p-(pts[k]+t*seg[k]))
        if best is None or d<best[0]: best=(d,k,t)
    _,k,t=best; s=(k+t)/5                       # 0 raíz … 1 punta
    w={}
    # mezcla suave entre huesos vecinos
    if t<0.5 and k>0: w[f"Tail{k}"]=0.5-t; w[f"Tail{k+1}"]=0.5+t
    elif t>=0.5 and k<4: w[f"Tail{k+1}"]=1.5-t; w[f"Tail{k+2}"]=t-0.5
    else: w[f"Tail{k+1}"]=1.0
    root_blend=1-ss(0.0,0.12,s)                 # la base sigue a la cadera
    if root_blend>0:
        w={n:v*(1-root_blend) for n,v in w.items()}; w['Hips']=w.get('Hips',0)+root_blend
    set_weights(i,w)
for side,(m,base,tip) in ears.items():
    for i in np.where(m)[0]:
        f=ss(0.0,0.35,np.dot(co[i]-base,tip-base)/max(np.dot(tip-base,tip-base),1e-9))
        if f<=0: continue
        old={g.group:g.weight for g in me.vertices[i].groups}
        new={mesh.vertex_groups[g].name:wt*(1-f) for g,wt in old.items()}
        new[f"{side}Ear"]=new.get(f"{side}Ear",0)+f
        set_weights(i,new)
# Modificador de armadura con los grupos nuevos (ya lo tiene del FBX)
bpy.ops.wm.save_as_mainfile(filepath=preview)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=out,use_selection=True,add_leaf_bones=False,bake_anim=False,use_mesh_modifiers=False,
                         mesh_smooth_type='FACE',use_tspace=True,object_types={'ARMATURE','MESH'},primary_bone_axis='Y',secondary_bone_axis='X')
print("exportado",out)
