import bpy, sys, math
args=sys.argv[sys.argv.index('--')+1:]; src,tex,out=args
bpy.ops.wm.read_factory_settings(use_empty=True); bpy.ops.import_scene.fbx(filepath=src)
arm=[o for o in bpy.data.objects if o.type=='ARMATURE'][0]; mesh=[o for o in bpy.data.objects if o.type=='MESH'][0]
arm.data.pose_position='POSE'
if arm.animation_data: arm.animation_data.action=None
for pb in arm.pose.bones: pb.rotation_mode='XYZ'; pb.rotation_euler=(0,0,0); pb.location=(0,0,0)
for i in range(1,6): arm.pose.bones[f"Tail{i}"].rotation_euler=(0,0,math.radians(18))
arm.pose.bones["LeftEar"].rotation_euler=(math.radians(25),0,0); arm.pose.bones["RightEar"].rotation_euler=(0,0,math.radians(-25))
for sk in mesh.data.shape_keys.key_blocks: print("forma",sk.name)
mesh.data.shape_keys.key_blocks['MouthClosed'].value=1
img=bpy.data.images.load(tex); mat=bpy.data.materials.new('m'); mat.use_nodes=True; nt=mat.node_tree
t=nt.nodes.new('ShaderNodeTexImage'); t.image=img; b=next((n for n in nt.nodes if n.type=='BSDF_PRINCIPLED'),None) or nt.nodes.new('ShaderNodeBsdfPrincipled')
o=next((n for n in nt.nodes if n.type=='OUTPUT_MATERIAL'),None) or nt.nodes.new('ShaderNodeOutputMaterial'); nt.links.new(b.outputs[0],o.inputs[0]); nt.links.new(t.outputs[0],b.inputs['Base Color'])
mesh.data.materials.clear(); mesh.data.materials.append(mat)
s=bpy.context.scene; s.render.engine='BLENDER_WORKBENCH'; s.display.shading.color_type='TEXTURE'; s.display.shading.light='FLAT'; s.render.resolution_x=s.render.resolution_y=700
for name,loc,rot,scale in (('front',(0,-3,0.9),(90,0,0),1.9),('back',(0,3,0.9),(90,0,180),1.9)):
    cam=bpy.data.cameras.new(name); cam.type='ORTHO'; cam.ortho_scale=scale; c=bpy.data.objects.new(name,cam); s.collection.objects.link(c)
    c.location=loc; c.rotation_euler=tuple(math.radians(a) for a in rot); s.camera=c; s.render.filepath=f"{out}/pose_{name}.png"; bpy.ops.render.render(write_still=True)
