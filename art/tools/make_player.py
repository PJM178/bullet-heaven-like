"""Generate the low-poly, PS1-style player base: a grey display mannequin.

Run from the project root:

    blender -b --factory-startup --python art/tools/make_player.py
    blender -b --factory-startup --python art/tools/make_player.py -- --preview /some/dir

It writes:
  art/player.blend    - editable source; open it in Blender and keep sculpting
  models/player.glb   - what Godot imports

The character is built from simple "lofted" pieces (a stack of rings joined
into a tube), the way PS1-era characters were built from separate rigid parts.
Every vertex is weighted 100% to a single bone ("rigid skinning"), which is
cheap to animate and gives the authentic segmented look.

Units are metres. Blender is Z-up; the character faces -Y, which the glTF
exporter turns into Godot's model front (+Z).
"""

import math
import os
import sys

import bmesh
import bpy
from mathutils import Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
BLEND_PATH = os.path.join(ROOT, "art", "player.blend")
GLB_PATH = os.path.join(ROOT, "models", "player.glb")

# --- Palette (sRGB 0..1). A neutral display mannequin: light grey shell,
# slightly darker grey hands, feet and neck so the segments read clearly.
# Clothes, hair and faces get built on top of this base later. --------------
BODY = (0.70, 0.71, 0.72)
JOINT = (0.52, 0.53, 0.55)

# --- Skeleton: bone name -> (head, tail, parent). Left side is +X. --------
BONES = {
    "hips":       ((0, 0, 0.92), (0, 0, 1.04), None),
    "spine":      ((0, 0, 1.04), (0, 0, 1.24), "hips"),
    "chest":      ((0, 0, 1.24), (0, 0, 1.46), "spine"),
    "neck":       ((0, 0, 1.46), (0, 0, 1.56), "chest"),
    "head":       ((0, 0, 1.56), (0, 0, 1.80), "neck"),
    "upper_arm.L": ((0.195, 0, 1.43), (0.225, 0.01, 1.14), "chest"),
    "forearm.L":  ((0.225, 0.01, 1.14), (0.240, -0.01, 0.88), "upper_arm.L"),
    "hand.L":     ((0.240, -0.01, 0.88), (0.245, -0.01, 0.76), "forearm.L"),
    "thigh.L":    ((0.09, 0, 0.94), (0.095, 0, 0.51), "hips"),
    "shin.L":     ((0.095, 0, 0.51), (0.095, 0.01, 0.09), "thigh.L"),
    "foot.L":     ((0.095, 0.01, 0.09), (0.095, -0.13, 0.02), "shin.L"),
}


def mirror_name(name):
    """upper_arm.L -> upper_arm.R (names without a side are unchanged)."""
    if name.endswith(".L"):
        return name[:-2] + ".R"
    return name


class Builder:
    """Collects geometry into one bmesh, with per-face colours and
    per-vertex bone assignment."""

    def __init__(self, bone_names):
        self.bm = bmesh.new()
        self.deform = self.bm.verts.layers.deform.verify()
        self.group_index = {name: i for i, name in enumerate(bone_names)}
        self.face_colors = []  # parallel to faces, in creation order

    def _vert(self, co, bone):
        v = self.bm.verts.new(co)
        v[self.deform][self.group_index[bone]] = 1.0
        return v

    def _face(self, verts, color, smooth, flip):
        if flip:
            verts = list(reversed(verts))
        f = self.bm.faces.new(verts)
        f.smooth = smooth
        self.face_colors.append(color)
        return f

    def loft(self, rings, colors, sides=6, cap_bottom=None, cap_top=None,
             mirror=False, twist=0.0):
        """Build a tube through a list of horizontal elliptical rings.

        rings:  [(x, y, z, rx, ry, bone), ...] from bottom to top
        colors: one colour per segment between consecutive rings
        sides:  ring resolution; a flat face always points to the front (-Y)
        cap_*:  colour for an end cap, or None to leave that end open
        mirror: build the right-side copy of a left-side part
        """
        sign = -1.0 if mirror else 1.0
        offset = math.pi / sides + twist
        ring_verts = []
        for (x, y, z, rx, ry, bone) in rings:
            bone = mirror_name(bone) if mirror else bone
            ring = []
            for i in range(sides):
                a = offset + 2.0 * math.pi * i / sides
                ring.append(self._vert(
                    (sign * (x + rx * math.cos(a)), y + ry * math.sin(a), z), bone))
            ring_verts.append(ring)

        # Mirroring across X flips the winding, so faces get reversed to keep
        # their normals pointing outward.
        for r in range(len(ring_verts) - 1):
            lo, hi = ring_verts[r], ring_verts[r + 1]
            for i in range(sides):
                j = (i + 1) % sides
                self._face([lo[i], lo[j], hi[j], hi[i]], colors[r], True, mirror)
        if cap_bottom is not None:
            self._face(list(reversed(ring_verts[0])), cap_bottom, False, mirror)
        if cap_top is not None:
            self._face(ring_verts[-1], cap_top, False, mirror)

    def box(self, corners, color, bone, mirror=False):
        """A six-sided block from 8 corners: bottom 4 then top 4, each in
        counter-clockwise order seen from above."""
        sign = -1.0 if mirror else 1.0
        bone = mirror_name(bone) if mirror else bone
        v = [self._vert((sign * c[0], c[1], c[2]), bone) for c in corners]
        quads = [
            [v[3], v[2], v[1], v[0]],  # bottom
            [v[4], v[5], v[6], v[7]],  # top
            [v[0], v[1], v[5], v[4]],
            [v[1], v[2], v[6], v[5]],
            [v[2], v[3], v[7], v[6]],
            [v[3], v[0], v[4], v[7]],
        ]
        for q in quads:
            self._face(q, color, False, mirror)

    def panel(self, points, color, bone):
        """A single quad from 4 (x, y, z) points, counter-clockwise as seen
        from the side it should face."""
        v = [self._vert(p, bone) for p in points]
        self._face(v, color, False, False)

    def quad_front(self, x0, x1, z0, z1, y, color, bone, mirror=False):
        """A single forward-facing (-Y) quad, used for small face details."""
        sign = -1.0 if mirror else 1.0
        bone = mirror_name(bone) if mirror else bone
        v = [self._vert((sign * x, y, z), bone)
             for (x, z) in ((x0, z0), (x1, z0), (x1, z1), (x0, z1))]
        self._face(v, color, False, mirror)


def build_body(b):
    # ---- Legs (left side; mirrored for the right) -----------------------
    for m in (False, True):
        # Foot: a simple wedge, wider and lower at the toes.
        b.box([
            (0.058, 0.050, 0.0), (0.058, -0.150, 0.0),
            (0.138, -0.150, 0.0), (0.132, 0.050, 0.0),
            (0.065, 0.040, 0.085), (0.060, -0.140, 0.030),
            (0.132, -0.140, 0.030), (0.125, 0.040, 0.085),
        ], JOINT, "foot.L", m)

        # Shin: slim ankle, a calf bulge at the back, narrowing to the knee.
        b.loft([
            (0.095, 0.005, 0.06, 0.036, 0.038, "shin.L"),
            (0.095, 0.010, 0.22, 0.044, 0.048, "shin.L"),
            (0.095, 0.016, 0.37, 0.054, 0.060, "shin.L"),
            (0.095, 0.002, 0.52, 0.049, 0.051, "shin.L"),
        ], [BODY, BODY, BODY], sides=6, cap_bottom=BODY, mirror=m)

        # Thigh: from the knee up to the hip, fuller at the top.
        b.loft([
            (0.095, 0.000, 0.49, 0.050, 0.053, "thigh.L"),
            (0.092, -0.006, 0.70, 0.068, 0.074, "thigh.L"),
            (0.086, 0.000, 0.90, 0.072, 0.080, "thigh.L"),
        ], [BODY, BODY], sides=6, mirror=m)

    # ---- Pelvis -----------------------------------------------------------
    b.loft([
        # The bottom sits inside the thighs, so it needs no cap.
        (0, 0.005, 0.80, 0.085, 0.060, "hips"),     # crotch, between thighs
        (0, 0.000, 0.88, 0.150, 0.100, "hips"),
        (0, 0.000, 0.95, 0.160, 0.104, "hips"),
        (0, 0.000, 1.03, 0.128, 0.088, "hips"),
    ], [BODY, BODY, BODY], sides=8)

    # ---- Torso: waist -> ribs -> chest -> shoulders ---------------------
    b.loft([
        (0, 0.000, 1.01, 0.116, 0.082, "spine"),
        (0, -0.004, 1.16, 0.132, 0.090, "spine"),
        (0, -0.010, 1.30, 0.162, 0.104, "chest"),
        (0, 0.000, 1.40, 0.175, 0.094, "chest"),
        (0, 0.005, 1.46, 0.110, 0.070, "chest"),
    ], [BODY, BODY, BODY, BODY], sides=8, cap_top=BODY)

    # ---- Neck -----------------------------------------------------------
    b.loft([
        (0, 0.010, 1.44, 0.052, 0.052, "neck"),
        (0, 0.010, 1.58, 0.046, 0.046, "neck"),
    ], [JOINT], sides=6)

    # ---- Head: a featureless egg, narrower at the chin ------------------
    b.loft([
        (0, -0.012, 1.540, 0.048, 0.052, "head"),   # chin
        (0, 0.000, 1.600, 0.078, 0.092, "head"),    # jaw
        (0, 0.002, 1.690, 0.088, 0.102, "head"),    # brow
        (0, 0.006, 1.765, 0.074, 0.090, "head"),    # crown
        (0, 0.008, 1.800, 0.036, 0.046, "head"),    # top
    ], [BODY, BODY, BODY, BODY], sides=8, cap_bottom=BODY, cap_top=BODY)

    # ---- Arms (left side; mirrored for the right) ------------------------
    for m in (False, True):
        # Upper arm: rounded into the shoulder at the top.
        b.loft([
            (0.226, 0.010, 1.13, 0.038, 0.040, "upper_arm.L"),
            (0.210, 0.003, 1.33, 0.045, 0.048, "upper_arm.L"),
            (0.196, 0.000, 1.42, 0.044, 0.048, "upper_arm.L"),
            (0.170, 0.000, 1.465, 0.024, 0.032, "upper_arm.L"),
        ], [BODY, BODY, BODY], sides=6, cap_top=BODY, mirror=m)
        # Forearm: thicker just below the elbow, slim at the wrist.
        b.loft([
            (0.241, -0.010, 0.88, 0.028, 0.030, "forearm.L"),
            (0.234, 0.000, 1.06, 0.038, 0.040, "forearm.L"),
            (0.226, 0.010, 1.15, 0.036, 0.038, "forearm.L"),
        ], [BODY, BODY], sides=6, cap_bottom=BODY, mirror=m)
        # Hand: a flattened mitt.
        b.loft([
            (0.246, -0.012, 0.750, 0.016, 0.028, "hand.L"),
            (0.246, -0.012, 0.810, 0.022, 0.040, "hand.L"),
            (0.243, -0.010, 0.890, 0.022, 0.030, "hand.L"),
        ], [JOINT, JOINT], sides=6, cap_bottom=JOINT, mirror=m)


def make_armature():
    arm_data = bpy.data.armatures.new("PlayerRig")
    arm_obj = bpy.data.objects.new("PlayerRig", arm_data)
    bpy.context.scene.collection.objects.link(arm_obj)
    bpy.context.view_layer.objects.active = arm_obj
    bpy.ops.object.mode_set(mode="EDIT")

    names = []
    for name, (head, tail, parent) in BONES.items():
        pairs = [(name, head, tail, parent)]
        if name.endswith(".L"):
            mh = (-head[0], head[1], head[2])
            mt = (-tail[0], tail[1], tail[2])
            mp = mirror_name(parent) if parent else None
            pairs.append((mirror_name(name), mh, mt, mp))
        for (n, h, t, p) in pairs:
            eb = arm_data.edit_bones.new(n)
            eb.head = Vector(h)
            eb.tail = Vector(t)
            if p:
                eb.parent = arm_data.edit_bones[p]
                # Connect the bone to its parent when they touch, so the
                # chain behaves like a real limb when posed.
                eb.use_connect = (eb.parent.tail - eb.head).length < 1e-4
            names.append(n)

    bpy.ops.object.mode_set(mode="OBJECT")
    return arm_obj, names


def make_material():
    """A matte material whose colour comes from the vertex colours. glTF
    exports this as COLOR_0, which Godot multiplies into the albedo."""
    mat = bpy.data.materials.new("PlayerMat")
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    bsdf = nodes.get("Principled BSDF")
    attr = nodes.new("ShaderNodeVertexColor")
    attr.layer_name = "Color"
    mat.node_tree.links.new(attr.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 1.0
    return mat


def build():
    bpy.ops.wm.read_factory_settings(use_empty=True)

    arm_obj, bone_names = make_armature()

    b = Builder(bone_names)
    build_body(b)

    mesh = bpy.data.meshes.new("Player")
    obj = bpy.data.objects.new("Player", mesh)
    bpy.context.scene.collection.objects.link(obj)
    # Vertex groups must exist on the object before the bmesh's deform layer
    # (which refers to groups by index) is written into the mesh.
    for name in bone_names:
        obj.vertex_groups.new(name=name)
    b.bm.to_mesh(mesh)
    b.bm.free()

    # Per-corner colour attribute: every face gets one flat colour, so seams
    # between coat and scarf etc. stay crisp.
    col = mesh.color_attributes.new("Color", "BYTE_COLOR", "CORNER")
    for poly, c in zip(mesh.polygons, b.face_colors):
        for li in poly.loop_indices:
            col.data[li].color_srgb = (c[0], c[1], c[2], 1.0)
    mesh.color_attributes.active_color = col

    mesh.materials.append(make_material())

    obj.parent = arm_obj
    mod = obj.modifiers.new("Armature", "ARMATURE")
    mod.object = arm_obj

    tris = sum(len(p.vertices) - 2 for p in mesh.polygons)
    print(f"[make_player] vertices={len(mesh.vertices)} triangles={tris}")
    return arm_obj, obj


def export():
    os.makedirs(os.path.dirname(GLB_PATH), exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)
    bpy.ops.export_scene.gltf(
        filepath=GLB_PATH,
        export_format="GLB",
        export_yup=True,
        export_apply=False,          # keep the armature modifier as skinning
        export_skins=True,
        export_animations=False,
    )
    print(f"[make_player] wrote {BLEND_PATH} and {GLB_PATH}")


def render_previews(out_dir):
    """Workbench renders from a few angles, for checking the shape."""
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "VERTEX"
    scene.render.resolution_x = 480
    scene.render.resolution_y = 720
    scene.render.film_transparent = False
    world = bpy.data.worlds.new("W")
    scene.world = world

    cam_data = bpy.data.cameras.new("Cam")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = 2.15
    cam = bpy.data.objects.new("Cam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam

    target = Vector((0, 0, 0.90))
    # name -> (turn around the character, camera elevation), in degrees.
    # "game" roughly matches the in-game camera: high and at 45 degrees.
    views = {"front": (0, 5), "three_quarter": (35, 10), "side": (90, 5),
             "back": (180, 5), "game": (45, 30)}
    for name, (deg, elev) in views.items():
        a, e = math.radians(deg), math.radians(elev)
        cam.location = target + 6 * Vector((
            math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))
        direction = target - cam.location
        cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(out_dir, f"player_{name}.png")
        bpy.ops.render.render(write_still=True)


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    build()
    export()
    if "--preview" in argv:
        render_previews(argv[argv.index("--preview") + 1])
