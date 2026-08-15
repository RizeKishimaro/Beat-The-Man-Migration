import bpy

mat = bpy.data.materials[0]
print("MAT", mat.name)
if mat.node_tree:
    for n in mat.node_tree.nodes:
        if n.type == "TEX_IMAGE" and n.image:
            img = n.image
            print(
                f"NODE {n.name} img={img.name!r} packed={img.packed_file is not None} "
                f"filepath={img.filepath!r} size={tuple(img.size)} users={img.users}"
            )

print("---IMAGES---")
for im in bpy.data.images:
    print(f"  {im.name!r} packed={im.packed_file is not None} path={im.filepath!r} size={tuple(im.size)}")
