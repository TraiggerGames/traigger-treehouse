# Textura de ojos y boca cerrados. Se rasterizan en el espacio UV los triángulos de cada zona, guardando su posición
# 3D, y se pinta encima: ojos = piel con una línea de pestañas en arco; boca = morro blanco con una boca de gato «ω».
# Máscara: R = ojo derecho del personaje (izquierda de la pantalla), G = ojo izquierdo, B = boca.
import bpy, sys, numpy as np
args=sys.argv[sys.argv.index('--')+1:]; src,tex,out_closed,out_mask=args
bpy.ops.wm.read_factory_settings(use_empty=True); bpy.ops.import_scene.fbx(filepath=src)
mesh=[o for o in bpy.data.objects if o.type=='MESH'][0]; me=mesh.data; M=mesh.matrix_world
co=np.array([M@v.co for v in me.vertices])
img=bpy.data.images.load(tex); TW,TH=img.size
px=np.array(img.pixels[:],dtype=np.float32).reshape(TH,TW,4)
closed=px.copy(); mask=np.zeros((TH,TW,4),np.float32); mask[...,3]=1
uvl=me.uv_layers.active.data
def ss(e0,e1,x): t=np.clip((x-e0)/(e1-e0),0,1); return t*t*(3-2*t)

EYE_Z=1.347
ZONES=[(-0.073,0),(0.070,1),(0.0,2)]
def ellipse(ch,X,Z):
    if ch==2: return np.sqrt((X/0.058)**2+((Z-1.240)/np.where(Z<1.240,0.040,0.039))**2)
    cx=ZONES[ch][0]
    return np.sqrt(((X-cx)/0.053)**2+((Z-EYE_Z)/np.where(Z<EYE_Z,0.064,0.046))**2)   # más alto por abajo (iris)
def near(ch,p):
    x,z=p[:,0].mean(),p[:,2].mean()
    if ch==2: return abs(x)<0.09 and 1.18<z<1.30
    return abs(x-ZONES[ch][0])<0.075 and abs(z-EYE_Z)<0.09

skin=np.array([0.98,0.86,0.72]); muzzle=np.array([0.95,0.94,0.92])
def run(collect):
    found={'skin':[],'muzzle':[]}
    for poly in me.polygons:
        vs=list(poly.vertices); p=co[vs]
        if p[:,1].mean()>-0.30: continue
        for cx,ch in ZONES:
            if not near(ch,p): continue
            uv=np.array([uvl[l].uv for l in poly.loop_indices])*[TW,TH]
            for k in range(1,len(vs)-1):          # abanico de triángulos
                tri=[0,k,k+1]; a,b,c=uv[tri]; A,B,C=p[tri]
                x0,y0=np.floor(np.minimum(np.minimum(a,b),c)).astype(int); x1,y1=np.ceil(np.maximum(np.maximum(a,b),c)).astype(int)
                x0,y0=max(x0,0),max(y0,0); x1,y1=min(x1,TW-1),min(y1,TH-1)
                if x1<x0 or y1<y0: continue
                gx,gy=np.meshgrid(np.arange(x0,x1+1)+.5,np.arange(y0,y1+1)+.5)
                d=(b[1]-c[1])*(a[0]-c[0])+(c[0]-b[0])*(a[1]-c[1])
                if abs(d)<1e-9: continue
                l1=((b[1]-c[1])*(gx-c[0])+(c[0]-b[0])*(gy-c[1]))/d; l2=((c[1]-a[1])*(gx-c[0])+(a[0]-c[0])*(gy-c[1]))/d; l3=1-l1-l2
                inside=(l1>=-0.02)&(l2>=-0.02)&(l3>=-0.02)
                if not inside.any(): continue
                X=l1*A[0]+l2*B[0]+l3*C[0]; Z=l1*A[2]+l2*B[2]+l3*C[2]
                e=ellipse(ch,X,Z)
                region=px[y0:y1+1,x0:x1+1,:3]; r,g,bb=region[...,0],region[...,1],region[...,2]
                if collect:
                    if ch==2: found['muzzle'].append(region[inside&(e>1.05)&(e<1.4)&(r>0.82)&(g>0.82)&(bb>0.78)])
                    else: found['skin'].append(region[inside&(e>1.0)&(e<1.25)&(r>0.8)&(g>0.65)&(bb>0.5)&(r>bb)])
                    continue
                w=(1-ss(0.80,1.0,e))*inside
                if ch<2:   # el pelo que tapa el ojo no se pinta
                    hair=((r>0.12)&(r<0.55)&(r>bb+0.05)&(g<r)&(Z>1.372))|((bb>0.75)&(r>0.3)&(g>0.65)&(r<0.6)&(Z>1.393))
                    w=w*(~hair)
                if not w.any(): continue
                m=mask[y0:y1+1,x0:x1+1,ch]; mask[y0:y1+1,x0:x1+1,ch]=np.maximum(m,w)
                if ch==2:
                    line=np.zeros_like(X)
                    for ax in (-0.0115,0.0115):   # «ω»: dos arcos bajo la nariz
                        dd=np.sqrt((X-ax)**2+(Z-1.262)**2); line=np.maximum(line,(1-ss(0.0010,0.0022,np.abs(dd-0.0115)))*(Z<1.262))
                    line*=np.abs(X)<0.024
                    paint=muzzle*(1-line[...,None])+np.array([0.30,0.15,0.14])*line[...,None]
                else:
                    u=np.clip((X-cx)/0.040,-1,1)
                    dl=np.abs(Z-(1.326-0.010*(1-u*u)))
                    line=(1-ss(0.0012,0.0026,dl))*(np.abs((X-cx)/0.043)<1)
                    paint=skin*(1-line[...,None])+np.array([0.16,0.09,0.07])*line[...,None]
                cur=closed[y0:y1+1,x0:x1+1,:3]
                closed[y0:y1+1,x0:x1+1,:3]=np.where(w[...,None]>0,region*(1-w[...,None])+paint*w[...,None],cur)
    return found
found=run(True)
def median(lst,default):
    lst=[x for x in lst if len(x)]
    return np.median(np.concatenate(lst),0) if lst else default
skin=median(found['skin'],skin); muzzle=median(found['muzzle'],muzzle)
print('piel',skin,'morro',muzzle)
run(False)
print("texeles: ojos",int((mask[...,0]>0).sum()),int((mask[...,1]>0).sum()),"boca",int((mask[...,2]>0).sum()))
def save(a,path):
    im=bpy.data.images.new('o',TW,TH,alpha=True); im.pixels=a.ravel(); im.filepath_raw=path; im.file_format='PNG'; im.save()
save(closed,out_closed); save(mask,out_mask)
