"""Generate the low-poly, PS1-style slime enemy.

Run from the project root:

    blender -b --factory-startup --python art/tools/make_slime.py
    blender -b --factory-startup --python art/tools/make_slime.py -- --preview /some/dir

It writes:
  art/slime.blend    - editable source
  enemies/slime.glb  - what Godot imports

A simple slime: a round drop of goo that rises into a pointed tip swept
back, with two dark eyes. Smooth (Gouraud) shading keeps it soft and
rounded even with few triangles; colours stay flat per face.

Unlike the player's rigid skinning, the slime is soft: vertices blend
between three stacked bones by height. "root" holds the base on the
ground, "body" does the squash and stretch (bone scale), and "top" sways
the tip, lagging behind the body.

Units are metres. Blender is Z-up; the slime faces -Y, which the glTF
exporter turns into Godot's model front (+Z).
"""

import math
import os
import sys

import bmesh
import bpy
from mathutils import Quaternion, Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
BLEND_PATH = os.path.join(ROOT, "art", "slime.blend")
GLB_PATH = os.path.join(ROOT, "enemies", "slime.glb")

# --- Palette (sRGB 0..1). A muted teal, darker where it touches the
# ground. Kept dusty rather than bright to sit in the game's PS1 mood. -----
GOO = (0.33, 0.52, 0.52)
GOO_DARK = (0.19, 0.31, 0.32)
EYE = (0.06, 0.07, 0.08)

# --- Skeleton: bone name -> (head, tail, parent). All point straight up. --
BONES = {
    "root": ((0, 0, 0.00), (0, 0, 0.06), None),
    "body": ((0, 0, 0.06), (0, 0, 0.24), "root"),
    "top":  ((0, 0, 0.24), (0, 0, 0.40), "body"),
}


def smoothstep(edge0, edge1, x):
    t = max(0.0, min(1.0, (x - edge0) / (edge1 - edge0)))
    return t * t * (3.0 - 2.0 * t)


def weights_for_height(z):
    """Blend root -> body -> top by height, so the goo bends softly."""
    to_body = smoothstep(0.01, 0.10, z)
    to_top = smoothstep(0.22, 0.34, z)
    return {
        "root": 1.0 - to_body,
        "body": to_body * (1.0 - to_top),
        "top": to_body * to_top,
    }


class Builder:
    """Collects geometry into one bmesh, with per-face colours and
    height-blended bone weights."""

    def __init__(self, bone_names):
        self.bm = bmesh.new()
        self.deform = self.bm.verts.layers.deform.verify()
        self.group_index = {name: i for i, name in enumerate(bone_names)}
        self.face_colors = []

    def _vert(self, co, weight_z=None):
        v = self.bm.verts.new(co)
        z = co[2] if weight_z is None else weight_z
        for bone, w in weights_for_height(z).items():
            if w > 1e-3:
                v[self.deform][self.group_index[bone]] = w
        return v

    def _face(self, verts, color, smooth):
        f = self.bm.faces.new(verts)
        f.smooth = smooth
        self.face_colors.append(color)
        return f

    def drop(self, rings, colors, sides, tip):
        """A closed, smooth-shaded drop through horizontal rings.

        rings:  [(y, z, radius), ...] bottom to top
        colors: one colour per band between consecutive rings
        tip:    (y, z) of the single point the top closes into

        Returns the ring vertices, so details (eyes) can be placed on the
        actual faces.
        """
        offset = math.pi / sides
        grid = []
        for (y, z, radius) in rings:
            ring = []
            for i in range(sides):
                a = offset + 2.0 * math.pi * i / sides
                ring.append(self._vert((radius * math.cos(a), y + radius * math.sin(a), z)))
            grid.append(ring)

        for r in range(len(grid) - 1):
            lo, hi = grid[r], grid[r + 1]
            for i in range(sides):
                j = (i + 1) % sides
                self._face([lo[i], lo[j], hi[j], hi[i]], colors[r], True)
        # Flat underside (nobody sees it), and the top closes into one
        # point so the tip is a cone of triangles.
        self._face(list(reversed(grid[0])), GOO_DARK, False)
        peak = self._vert((0.0, tip[0], tip[1]))
        top = grid[-1]
        for i in range(sides):
            j = (i + 1) % sides
            self._face([top[i], top[j], peak], colors[-1], True)
        return grid

    def disc(self, center, normal, width, height, sides, color):
        """A small flat oval lying on a surface (eyes). It is weighted as a
        whole to its centre's height, so it moves with the goo but never
        stretches out of shape."""
        n = normal.normalized()
        # Two directions across the disc: "up" along the surface, and
        # "right" perpendicular to it. Their order makes the face point
        # outward along the normal.
        up = (Vector((0, 0, 1)) - n * n.z).normalized()
        right = up.cross(n)
        verts = []
        for i in range(sides):
            a = 2.0 * math.pi * i / sides
            p = center + right * (width * math.cos(a)) + up * (height * math.sin(a))
            verts.append(self._vert(p, weight_z=center.z))
        self._face(verts, color, False)


def build_body(b):
    # Bottom to top: (y, z, radius). A round, bottom-heavy drop that rises
    # into a tip leaning slightly back (+Y), so it reads as heading forward.
    rings = [
        (0.000, 0.000, 0.20),  # where it meets the ground
        (0.000, 0.030, 0.25),
        (0.000, 0.090, 0.27),  # widest point, low down: it's heavy
        (0.005, 0.170, 0.24),
        (0.015, 0.240, 0.17),
        (0.030, 0.300, 0.09),
        (0.050, 0.340, 0.035),
    ]
    colors = [GOO_DARK, GOO, GOO, GOO, GOO, GOO]
    sides = 10
    grid = b.drop(rings, colors, sides, tip=(0.085, 0.385))

    # Eyes: one on each of the two faces either side of the front-centre
    # vertex column, in the band between rings 3 and 4. Placed from the
    # real face corners, so they sit exactly on the low-poly surface.
    front = 7  # with this ring layout, vertex 7 points straight forward (-Y)
    for i, j in ((front - 1, front), (front, front + 1)):
        corners = [grid[3][i].co, grid[3][j].co, grid[4][j].co, grid[4][i].co]
        center = sum(corners, Vector()) / 4
        normal = (corners[1] - corners[0]).cross(corners[3] - corners[0]).normalized()
        b.disc(center + normal * 0.003, normal, 0.024, 0.038, 6, EYE)


def make_armature():
    arm_data = bpy.data.armatures.new("SlimeRig")
    arm_obj = bpy.data.objects.new("SlimeRig", arm_data)
    bpy.context.scene.collection.objects.link(arm_obj)
    bpy.context.view_layer.objects.active = arm_obj
    bpy.ops.object.mode_set(mode="EDIT")
    for name, (head, tail, parent) in BONES.items():
        eb = arm_data.edit_bones.new(name)
        eb.head = Vector(head)
        eb.tail = Vector(tail)
        if parent:
            eb.parent = arm_data.edit_bones[parent]
            eb.use_connect = True
    bpy.ops.object.mode_set(mode="OBJECT")
    return arm_obj, list(BONES.keys())


# =========================================================================
# Animation
# =========================================================================
#
# A pose is a dict: bone name -> dict with any of
#   "rot":   [(axis, degrees), ...] in armature axes (X positive = lean
#            forward, Y = tip sideways, Z = twist)
#   "loc":   (x, y, z) move in metres, armature axes (-Y is forward)
#   "scale": (width, height) - squash/stretch. Children inherit it.
#
# The hop should feel heavy, not springy: a clear gather before take-off,
# a low arc, and a hard landing where the tip keeps going a moment later.

FPS = 24

MOVE_KEYS = [
    # A short, heavy hop, played in place (the game moves the node).
    # Settled.
    (0, {}),
    # Gather: squash down and spread out, the tip pulls back.
    (4, {"body": {"scale": (1.12, 0.78)},
         "top": {"rot": [("X", -6)]}}),
    # Launch: stretch up as it leaves the ground; the tip lags behind.
    (8, {"root": {"loc": (0, 0, 0.07)},
         "body": {"scale": (0.90, 1.16)},
         "top": {"rot": [("X", -8)]}}),
    # Top of the hop: shape relaxes, the tip starts to swing forward.
    (11, {"root": {"loc": (0, 0, 0.09)},
          "body": {"scale": (0.97, 1.05)},
          "top": {"rot": [("X", 4)]}}),
    # Land hard: squash wide, the tip keeps going (follow-through).
    (14, {"body": {"scale": (1.16, 0.76)},
          "top": {"rot": [("X", 10)]}}),
    # Recover with a small rebound.
    (17, {"body": {"scale": (0.97, 1.05)},
          "top": {"rot": [("X", -2)]}}),
    (20, {}),
]

IDLE_KEYS = [
    # Slow, uneven breathing with a slight sway of the crown.
    (0, {"body": {"scale": (1.00, 1.00)}, "top": {"rot": [("Y", 0)]}}),
    (12, {"body": {"scale": (0.97, 1.06)}, "top": {"rot": [("Y", 4), ("X", 2)]}}),
    (24, {"body": {"scale": (1.03, 0.96)}, "top": {"rot": [("Y", -3)]}}),
    (36, {"body": {"scale": (1.00, 1.00)}, "top": {"rot": [("Y", 0)]}}),
]

ANIMATIONS = {
    "move": MOVE_KEYS,
    "idle": IDLE_KEYS,
}


def to_bone_space(rest_quat, rotations):
    """Armature-space rotations -> the bone-local quaternion Blender stores."""
    world = Quaternion()
    for axis, deg in rotations:
        vec = {"X": (1, 0, 0), "Y": (0, 1, 0), "Z": (0, 0, 1)}[axis]
        world = Quaternion(vec, math.radians(deg)) @ world
    local = rest_quat.inverted() @ world @ rest_quat
    if local.w < 0:
        local.negate()
    return local


def make_animations(arm_obj):
    arm_obj.animation_data_create()
    for pb in arm_obj.pose.bones:
        pb.rotation_mode = "QUATERNION"

    for anim_name, keys in ANIMATIONS.items():
        action = bpy.data.actions.new(anim_name)
        action.use_fake_user = True
        arm_obj.animation_data.action = action
        for frame, pose in keys:
            # Key every channel of every bone on every key, so nothing keeps a
            # leftover value from another key or animation.
            for pb in arm_obj.pose.bones:
                spec = pose.get(pb.name, {})
                rest = pb.bone.matrix_local.to_quaternion()
                pb.rotation_quaternion = to_bone_space(rest, spec.get("rot", []))
                pb.location = rest.inverted() @ Vector(spec.get("loc", (0, 0, 0)))
                # A bone's own Y axis runs along it (here: up), so height is
                # the Y scale and width is X and Z.
                w, h = spec.get("scale", (1.0, 1.0))
                pb.scale = (w, h, w)
                pb.keyframe_insert("rotation_quaternion", frame=frame)
                pb.keyframe_insert("location", frame=frame)
                pb.keyframe_insert("scale", frame=frame)
        # Smooth Bezier through the keys (see make_player.set_interpolation).
        for layer in action.layers:
            for strip in layer.strips:
                for bag in strip.channelbags:
                    for fcurve in bag.fcurves:
                        for key in fcurve.keyframe_points:
                            key.interpolation = "BEZIER"
                            key.handle_left_type = "AUTO"
                            key.handle_right_type = "AUTO"
                        fcurve.update()
        print(f"[make_slime] animation '{anim_name}': {keys[-1][0]} frames")

    scene = bpy.context.scene
    scene.render.fps = FPS
    scene.frame_start = 0
    scene.frame_end = MOVE_KEYS[-1][0]
    arm_obj.animation_data.action = bpy.data.actions["move"]


def make_material():
    """Matte material coloured by the vertex colours (glTF COLOR_0)."""
    mat = bpy.data.materials.new("SlimeMat")
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

    mesh = bpy.data.meshes.new("Slime")
    obj = bpy.data.objects.new("Slime", mesh)
    bpy.context.scene.collection.objects.link(obj)
    for name in bone_names:
        obj.vertex_groups.new(name=name)
    b.bm.to_mesh(mesh)
    b.bm.free()

    col = mesh.color_attributes.new("Color", "BYTE_COLOR", "CORNER")
    for poly, c in zip(mesh.polygons, b.face_colors):
        for li in poly.loop_indices:
            col.data[li].color_srgb = (c[0], c[1], c[2], 1.0)
    mesh.color_attributes.active_color = col
    mesh.materials.append(make_material())

    obj.parent = arm_obj
    mod = obj.modifiers.new("Armature", "ARMATURE")
    mod.object = arm_obj

    make_animations(arm_obj)

    tris = sum(len(p.vertices) - 2 for p in mesh.polygons)
    print(f"[make_slime] vertices={len(mesh.vertices)} triangles={tris}")


def export():
    os.makedirs(os.path.dirname(GLB_PATH), exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)
    bpy.ops.export_scene.gltf(
        filepath=GLB_PATH,
        export_format="GLB",
        export_yup=True,
        export_apply=False,
        export_skins=True,
        export_animations=True,
        export_animation_mode="ACTIONS",
    )
    print(f"[make_slime] wrote {BLEND_PATH} and {GLB_PATH}")


def render_previews(out_dir):
    """Workbench renders: rest views, plus a strip of the move cycle."""
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "VERTEX"
    scene.world = bpy.data.worlds.new("W")

    cam_data = bpy.data.cameras.new("Cam")
    cam_data.type = "ORTHO"
    cam = bpy.data.objects.new("Cam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam

    target = Vector((0, 0, 0.20))

    def aim(deg, elev):
        a, e = math.radians(deg), math.radians(elev)
        cam.location = target + 6 * Vector((
            math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))
        cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()

    rig = bpy.data.objects["SlimeRig"]
    move = rig.animation_data.action
    views = {"front": (0, 5), "three_quarter": (35, 15), "side": (90, 5),
             "back": (180, 5), "game": (45, 30)}

    scene.render.resolution_x = 480
    scene.render.resolution_y = 360
    cam_data.ortho_scale = 0.85
    scene.frame_set(0)
    for name, (deg, elev) in views.items():
        aim(deg, elev)
        scene.render.filepath = os.path.join(out_dir, f"slime_{name}.png")
        bpy.ops.render.render(write_still=True)

    # Move-cycle key frames side by side.
    rig.animation_data.action = move
    scene.render.resolution_x = 240
    scene.render.resolution_y = 240
    aim(90, 5)
    frames = [f for f, _ in MOVE_KEYS[:-1]]
    tiles = []
    for f in frames:
        scene.frame_set(f)
        path = os.path.join(out_dir, f"_tile_{f}.png")
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        img = bpy.data.images.load(path)
        tiles.append(list(img.pixels))
        bpy.data.images.remove(img)
        os.remove(path)
    w, h = scene.render.resolution_x, scene.render.resolution_y
    strip = bpy.data.images.new("strip", w * len(tiles), h)
    px = [0.0] * (w * len(tiles) * h * 4)
    for t, tile in enumerate(tiles):
        for y in range(h):
            start = (y * w * len(tiles) + t * w) * 4
            px[start:start + w * 4] = tile[y * w * 4:(y + 1) * w * 4]
    strip.pixels = px
    strip.filepath_raw = os.path.join(out_dir, "move_side.png")
    strip.file_format = "PNG"
    strip.save()


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    build()
    export()
    if "--preview" in argv:
        render_previews(argv[argv.index("--preview") + 1])
