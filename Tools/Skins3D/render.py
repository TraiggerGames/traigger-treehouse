import bpy, sys, math
args=sys.argv[sys.argv.index('--')+1:]; src,tex,out,cz,scale=args[0],args[1],args[2],float(args[3]),float(args[4])
shape=args[5] if len(args)>5 else ''
value=float(args[6]) if len(args)>6 else 1.0
SIDE=len(args)>7 and args[7]=='side'
ANGLE=len(args)>7 and args[7]=='angle'
if src.endswith('.blend'): bpy.ops.wm.open_mainfile(filepath=src)
else:
    bpy.ops.wm.read_factory_settings(use_empty=True); bpy.ops.import_scene.fbx(filepath=src)
mesh=[o for o in bpy.data.objects if o.type=='MESH'][0]; me=mesh.data
for o in bpy.data.objects:
    if o.type=='ARMATURE': o.data.pose_position='REST'
if me.shape_keys:
    for k in me.shape_keys.key_blocks: k.value=0
    for sh in shape.split('+'):
        if sh: me.shape_keys.key_blocks[sh].value=value
img=bpy.data.images.load(tex)
mat=bpy.data.materials.new('m'); mat.use_nodes=True
t=mat.node_tree.nodes.new('ShaderNodeTexImage'); t.image=img
nt=mat.node_tree; bsdf=next((n for n in nt.nodes if n.type=='BSDF_PRINCIPLED'),None) or nt.nodes.new('ShaderNodeBsdfPrincipled')
outn=next((n for n in nt.nodes if n.type=='OUTPUT_MATERIAL'),None) or nt.nodes.new('ShaderNodeOutputMaterial')
nt.links.new(bsdf.outputs[0],outn.inputs[0]); nt.links.new(t.outputs['Color'],bsdf.inputs['Base Color'])
me.materials.clear(); me.materials.append(mat)
cam=bpy.data.cameras.new('c'); cam.type='ORTHO'; cam.ortho_scale=scale
c=bpy.data.objects.new('cam',cam); bpy.context.scene.collection.objects.link(c)
c.location=(2,-0.35,cz) if SIDE else ((1.2,-1.6,cz) if ANGLE else (0,-2,cz)); c.rotation_euler=(math.radians(90),0,math.radians(90)) if SIDE else ((math.radians(90),0,math.radians(44)) if ANGLE else (math.radians(90),0,0)); bpy.context.scene.camera=c
s=bpy.context.scene; s.render.engine='BLENDER_WORKBENCH'; s.display.shading.color_type='TEXTURE'; s.display.shading.light='FLAT'
s.render.resolution_x=s.render.resolution_y=800; s.render.filepath=out; bpy.ops.render.render(write_still=True)
