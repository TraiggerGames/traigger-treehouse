import bpy, sys, numpy as np, mathutils
from mathutils.kdtree import KDTree
args=sys.argv[sys.argv.index('--')+1:]; src,out,tex=args
bpy.ops.wm.read_factory_settings(use_empty=True); bpy.ops.import_scene.fbx(filepath=src)
mesh=[o for o in bpy.data.objects if o.type=='MESH'][0]; me=mesh.data
M=mesh.matrix_world; Minv=M.inverted().to_3x3()
co=np.array([M@v.co for v in me.vertices]); N=len(co)
# Capas delanteras (mechones de pelo sobre la cara): hay superficie justo detrás.
kd=KDTree(N)
for i,p in enumerate(co): kd.insert((p[0],0,p[2]),i)
kd.balance()
front=(co[:,1]<-0.30)&(co[:,2]>1.18)&(co[:,2]<1.45)&(np.abs(co[:,0])<0.16)
hair=np.zeros(N,bool)
for i in np.where(front)[0]:
    for (_,j,_) in kd.find_range((co[i,0],0,co[i,2]),0.006):
        if co[j,1]>co[i,1]+0.008 and co[j,1]<-0.25: hair[i]=True; break
# Color del pelo en la textura: castaño o mechón azul claro (los ojos son blanco, turquesa oscuro y negro).
img=bpy.data.images.load(tex); TW,TH=img.size; px=np.array(img.pixels[:],dtype=np.float32).reshape(TH,TW,4)
vuv=np.zeros((N,2)); uvl=me.uv_layers.active.data
for loop in me.loops: vuv[loop.vertex_index]=uvl[loop.index].uv
c=px[np.clip((vuv[:,1]*TH).astype(int),0,TH-1),np.clip((vuv[:,0]*TW).astype(int),0,TW-1),:3]
r,g,b=c[:,0],c[:,1],c[:,2]
brown=(r>0.12)&(r<0.55)&(r>b+0.05)&(g<r)
lightblue=(b>0.7)&(r>0.25)&(g>0.6)
# Piezas (islas de la malla) de pelo: la mayoría de sus vértices tienen color de pelo.
import bmesh
bm=bmesh.new(); bm.from_mesh(me); bm.verts.ensure_lookup_table()
island=np.full(N,-1); n=0
for v in bm.verts:
    if island[v.index]>=0: continue
    stack=[v]; island[v.index]=n
    while stack:
        a_=stack.pop()
        for e in a_.link_edges:
            b_=e.other_vert(a_)
            if island[b_.index]<0: island[b_.index]=n; stack.append(b_)
    n+=1
haircolor=(brown|lightblue).astype(float)
frac=np.bincount(island,weights=haircolor)/np.maximum(np.bincount(island),1)
hair=frac[island]>0.5
print("capas delanteras (pelo) excluidas:",hair.sum())
EXCLUDE=True
def ss(e0,e1,x): t=np.clip((x-e0)/(e1-e0),0,1); return t*t*(3-2*t)
def ellipse(cx,cz,rx,rz): return np.sqrt(((co[:,0]-cx)/rx)**2+((co[:,2]-cz)/rz)**2)
basis=mesh.shape_key_add(name='Basis',from_mix=False)
def add(name,delta,exclude=False):
    if exclude: delta[hair]=0
    k=mesh.shape_key_add(name=name,from_mix=False)
    for i in np.where(np.abs(delta).sum(1)>1e-7)[0]:
        k.data[i].co=me.vertices[i].co+Minv@mathutils.Vector(delta[i])
    print(name,"vértices movidos:",int((np.abs(delta).sum(1)>1e-7).sum()))
facing=co[:,1]<-0.30
# Parpadeo: el ojo pintado se aplasta hacia una línea un poco por debajo del centro (como en el anime).
for name,cx in (('BlinkRight',-0.073),('BlinkLeft',0.070)):
    # El párpado de arriba (pestañas, z≈1.385) baja hasta el de abajo (z≈1.312); lo de debajo apenas se mueve.
    lid=1.314
    r=ellipse(cx,1.350,0.050,0.045); w=(1-ss(0.80,1.0,r))*facing
    above=co[:,2]>lid
    d=np.zeros((N,3)); d[:,2]=np.where(above,(lid-co[:,2])*0.97,(lid-co[:,2])*0.8)*w
    add(name,d,exclude=True)
# Boca cerrada: el labio de abajo sube hasta el de arriba. Solo se mueve la abertura de la boca: la barbilla
# (por debajo del labio inferior, z<1.222) se queda quieta para no hundir la papada.
top=1.268
bottom=1.208+0.016*np.clip(co[:,0]/0.042,-1,1)**2   # el fondo de la boca baja en el centro (forma de U)
# Los dos labios van hacia la línea media; por encima (hasta la nariz, z 1.288) y por debajo (hasta el borde de la
# barbilla, z 1.176) el desplazamiento se reparte poco a poco, así la piel se estira igual y no se hunde nada.
# El labio de arriba baja hasta el de abajo (el morro blanco acaba justo debajo de la boca: si sube el labio de
# abajo, arrastra la barbilla). Por debajo del labio inferior no se mueve nada.
# Entre abierta y cerrada (la boca cerrada del todo va por textura): los labios se juntan hacia una línea un poco
# por encima del centro. El borde del morro (z≈1.205) queda fijo para no tirar de la barbilla.
mid=top-(top-bottom)*0.75
z=co[:,2]
low=bottom+0.012   # el labio de abajo y lo de debajo no se mueven
dz=np.where(z>top,(mid-top)*(1-ss(top,1.288,z)),np.where(z<low,0.0,(mid-z)*ss(low,low+0.010,z)))
inx=1-ss(0.70,1.05,np.abs(co[:,0])/0.044)
w=inx*facing*(z>1.204)*(z<1.295)
d=np.zeros((N,3)); d[:,2]=dz*w*0.97
add('MouthClosed',d)
# Boca en «o»: las comisuras se juntan hacia el centro y la boca se alarga un poco hacia abajo.
inx2=1-ss(0.8,1.15,np.abs(co[:,0])/0.042)
inz2=(1-ss(top+0.002,top+0.012,co[:,2]))*ss(bottom-0.020,bottom,co[:,2])
w=inx2*inz2*facing
d=np.zeros((N,3)); d[:,0]=-co[:,0]*0.35*w; d[:,2]=-(top-co[:,2])*0.12*w
add('MouthNarrow',d)
# Boca rellena: la boca es un hueco de 3–5 cm con la lengua al fondo. Para la boca cerrada (textura «ω») el hueco
# se rellena hasta la superficie del morro, interpolando entre el labio de arriba y la barbilla en cada columna x.
d=np.zeros((N,3))
zoneF=(np.abs(co[:,0])<0.058)&(co[:,2]>1.200)&(co[:,2]<1.280)&facing

for i in np.where(zoneF)[0]:
    x=co[i,0]
    col=(np.abs(co[:,0]-x)<0.008)&facing
    up=col&(co[:,2]>1.278)&(co[:,2]<1.300); dn=col&(co[:,2]>1.175)&(co[:,2]<1.206)
    if not up.any() or not dn.any(): continue
    iu=np.where(up)[0][np.argmin(co[up,1])]; idn=np.where(dn)[0][np.argmin(co[dn,1])]
    yu,zu=co[iu,1],co[iu,2]; yd,zd=co[idn,1],co[idn,2]
    t=(co[i,2]-zd)/(zu-zd); target=yd+(yu-yd)*t
    edge=1-ss(0.85,1.0,abs(x)/0.058)
    # Solo el interior del hueco: dentro de la abertura de la boca y claramente por detrás de la superficie
    # (la piel de la barbilla y los labios ya están en la superficie y no se tocan).
    inside_mouth=co[i,2]>bottom[i]-0.003 and co[i,2]<top+0.004
    depth=co[i,1]-target
    if inside_mouth and depth>0.004: d[i,1]=-depth*edge*ss(0.004,0.010,depth)
add('MouthFill',d)
# Sonrisa: comisuras arriba y hacia fuera.
d=np.zeros((N,3))
for side in (-1,1):
    r=np.sqrt(((co[:,0]-side*0.036)/0.022)**2+((co[:,2]-1.262)/0.02)**2); w=(1-ss(0.3,1.0,r))*facing
    d[:,2]+=0.007*w; d[:,0]+=side*0.003*w
add('Smile',d)
bpy.ops.wm.save_as_mainfile(filepath=out)
