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
from mathutils import Quaternion, Vector

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


# =========================================================================
# Animation
# =========================================================================
#
# A pose is a dict: bone name -> list of (axis, degrees) rotations, applied
# in order around the *armature's* axes (X = character's left/right axis,
# Y = back/front, Z = up). Thinking in armature axes is much easier than in
# each bone's own local axes, and pose_rotation() converts for us.
#
# Handy signs (the character faces -Y):
#   legs / arms (bones pointing down): X negative = swing forward
#   spine / chest / neck (pointing up): X positive = lean forward
#   left arm:  Y negative = raise out to the side  (right arm: Y positive)
#              (list Y before X: raise the arm out first, then swing it)
#   Z negative = turn the left side forward
#
# "_loc" moves the hips bone (and so the whole body) in metres.

FPS = 24


def mirror_pose(pose):
    """Left/right mirror image of a pose: swap .L/.R bones and flip the
    side-to-side parts (Y and Z rotations, X movement)."""
    out = {}
    for bone, value in pose.items():
        if bone == "_loc":
            out[bone] = (-value[0], value[1], value[2])
            continue
        if bone.endswith(".L"):
            name = bone[:-2] + ".R"
        elif bone.endswith(".R"):
            name = bone[:-2] + ".L"
        else:
            name = bone
        out[name] = [(axis, -deg if axis in "YZ" else deg) for axis, deg in value]
    return out


# ---- Run: a warrior's battle charge. Heavy and driven: a strong forward
# lean with the head held up, looking at the target; knees stay bent and the
# body low; each footfall lands with a drop; the knee drives forward hard;
# arms pump powerfully with fists. Three poses per step (impact, pass,
# push-off) carry the weight; linear motion keeps it stiff and PS1-like. --

RUN_CONTACT = {  # left foot slams down in front, body dips on impact
    "_loc": (0, 0, -0.06),
    "hips": [("Z", -8)],
    "spine": [("X", 14), ("Z", 4)],
    "chest": [("X", 8), ("Z", 8)],
    "neck": [("X", -10)],
    "head": [("X", -10), ("Z", -12)],  # counter-turn: eyes stay on target
    "thigh.L": [("X", -38)],
    "shin.L": [("X", 25)],             # knee bent to absorb the landing
    "foot.L": [("X", -10)],
    "thigh.R": [("X", 30)],
    "shin.R": [("X", 60)],
    "foot.R": [("X", 30)],
    # Arms at their extremes: right fist driven forward, left pulled back.
    "upper_arm.R": [("Y", 8), ("X", -55)],
    "forearm.R": [("X", -85)],
    "upper_arm.L": [("Y", -12), ("X", 55)],
    "forearm.L": [("X", -50)],
}

RUN_PASS = {  # body low over the left leg, right leg drawn through tight
    "_loc": (0, 0, -0.04),
    "spine": [("X", 15)],
    "chest": [("X", 8)],
    "neck": [("X", -10)],
    "head": [("X", -10)],
    "thigh.L": [("X", 5)],
    "shin.L": [("X", 35)],
    "foot.L": [("X", -10)],
    "thigh.R": [("X", -25)],
    "shin.R": [("X", 105)],
    "foot.R": [("X", 25)],
    # Arms passing the body in opposite directions.
    "upper_arm.R": [("Y", 8), ("X", -5)],
    "forearm.R": [("X", -85)],
    "upper_arm.L": [("Y", -10), ("X", 5)],
    "forearm.L": [("X", -70)],
}

RUN_PUSH = {  # left leg drives off the ground, right knee punches forward
    "_loc": (0, 0, 0.03),
    "hips": [("Z", 6)],
    "spine": [("X", 13), ("Z", -3)],
    "chest": [("X", 8), ("Z", -6)],
    "neck": [("X", -10)],
    "head": [("X", -10), ("Z", 9)],
    "thigh.L": [("X", 30)],
    "shin.L": [("X", 20)],
    "foot.L": [("X", 35)],             # toes push off
    "thigh.R": [("X", -55)],
    "shin.R": [("X", 70)],
    "foot.R": [("X", 5)],
    # Arms still travelling toward their next extremes.
    "upper_arm.L": [("Y", -9), ("X", -45)],
    "forearm.L": [("X", -85)],
    "upper_arm.R": [("Y", 11), ("X", 45)],
    "forearm.R": [("X", -55)],
}

# One full cycle is two steps: the left-foot half, then its mirror image.
# 16 frames at 24 fps = 0.67 s.
RUN_KEYS = [
    (0, RUN_CONTACT),
    (3, RUN_PASS),
    (6, RUN_PUSH),
    (8, mirror_pose(RUN_CONTACT)),
    (11, mirror_pose(RUN_PASS)),
    (14, mirror_pose(RUN_PUSH)),
    (16, RUN_CONTACT),  # same as frame 0, so the loop is seamless
]

# ---- Directional locomotion ---------------------------------------------
#
# While aiming, the character can move in any direction relative to where
# it faces. Godot blends these clips by direction (forward = run, back =
# run_back, sideways = strafe_*), so they must all share the run's timing:
# 16 frames, LEFT foot plants at frame 0, RIGHT foot at frame 8. If the
# feet landed at different moments, a diagonal blend would scramble them.
#
# In all of them the upper body stays squared to the front with the arms in
# a guard: the character is facing what it's fighting.


def shift_keys(keys, offset, length=16):
    """Move a looping cycle's keys later by `offset` frames, wrapping round,
    so the cycle starts from a different point (used to keep foot timing in
    step after mirroring a clip)."""
    shifted = sorted(((f + offset) % length, pose) for f, pose in keys[:-1])
    return shifted + [(length, shifted[0][1])]


# ---- Backpedal: retreating step by step without turning away. Shorter,
# controlled steps than the run, landing on the ball of the foot behind;
# torso upright with a slight forward brace, fists up. --------------------

BACK_CONTACT = {  # left foot reaches back and lands on its toes
    "_loc": (0, 0, -0.05),
    "hips": [("Z", 6)],
    "spine": [("X", 6), ("Z", -3)],
    "chest": [("X", 4), ("Z", -5)],
    "neck": [("X", -4)],
    "head": [("X", -4), ("Z", 4)],
    "thigh.L": [("X", 22)],
    "shin.L": [("X", 20)],
    "foot.L": [("X", 15)],
    "thigh.R": [("X", -22)],
    "shin.R": [("X", 15)],
    "foot.R": [("X", -10)],
    # Opposite arm to leg, as in any gait, but a small guarded swing.
    "upper_arm.L": [("Y", -10), ("X", -30)],
    "forearm.L": [("X", -80)],
    "upper_arm.R": [("Y", 10), ("X", 15)],
    "forearm.R": [("X", -65)],
}

BACK_PASS = {  # weight over the left foot, right leg lifts and swings back
    "_loc": (0, 0, -0.03),
    "spine": [("X", 7)],
    "chest": [("X", 4)],
    "neck": [("X", -4)],
    "head": [("X", -4)],
    "thigh.L": [("X", 2)],
    "shin.L": [("X", 25)],
    "foot.L": [("X", 0)],
    "thigh.R": [("X", 10)],
    "shin.R": [("X", 55)],             # heel lifts, but a short, controlled step
    "foot.R": [("X", 15)],
    "upper_arm.L": [("Y", -9), ("X", -12)],
    "forearm.L": [("X", -80)],
    "upper_arm.R": [("Y", 9), ("X", -2)],
    "forearm.R": [("X", -75)],
}

BACK_PUSH = {  # left foot, now in front, pushes the body back
    "_loc": (0, 0, 0.01),
    "hips": [("Z", -4)],
    "spine": [("X", 6), ("Z", 2)],
    "chest": [("X", 4), ("Z", 4)],
    "neck": [("X", -4)],
    "head": [("X", -4), ("Z", -4)],
    "thigh.L": [("X", -18)],
    "shin.L": [("X", 15)],
    "foot.L": [("X", 12)],
    "thigh.R": [("X", 28)],
    "shin.R": [("X", 45)],
    "foot.R": [("X", 25)],
    "upper_arm.L": [("Y", -9), ("X", 8)],
    "forearm.L": [("X", -70)],
    "upper_arm.R": [("Y", 9), ("X", -20)],
    "forearm.R": [("X", -80)],
}

BACK_KEYS = [
    (0, BACK_CONTACT),
    (3, BACK_PASS),
    (6, BACK_PUSH),
    (8, mirror_pose(BACK_CONTACT)),
    (11, mirror_pose(BACK_PASS)),
    (14, mirror_pose(BACK_PUSH)),
    (16, BACK_CONTACT),
]

# ---- Strafe right: a fighter's side shuffle toward the character's right
# (-X). The right (lead) leg steps out wide, the left (trail) leg closes in
# under the body; the feet never cross, so the stance stays planted and
# ready. A side shuffle isn't symmetrical, so all six poses are written
# out instead of mirroring the first half. --------------------------------

# Shared by every strafe pose: braced forward lean, a slight lean into
# the move, head level, fists up in a guard.
STRAFE_UPPER = {
    "spine": [("X", 8), ("Y", -4)],
    "chest": [("X", 4)],
    "neck": [("X", -4)],
    "head": [("X", -4), ("Y", 4)],
    "upper_arm.L": [("Y", -8), ("X", -25)],
    "forearm.L": [("X", -80)],
    "upper_arm.R": [("Y", 8), ("X", -25)],
    "forearm.R": [("X", -80)],
}


def strafe_pose(**legs):
    """STRAFE_UPPER plus this pose's legs and hips. Python's ** collects
    the keyword arguments into a dict; bone names with dots can't be
    keywords, so legs use underscores here and are renamed."""
    pose = dict(STRAFE_UPPER)
    for key, value in legs.items():
        name = key if key == "_loc" else key.replace("_L", ".L").replace("_R", ".R")
        pose[name] = value
    return pose


STRAFE_KEYS = [
    # Left foot closes in and lands; both feet under the body, weight drops.
    (0, strafe_pose(
        _loc=(0, 0, -0.06),
        thigh_L=[("X", -6), ("Y", 2)], shin_L=[("X", 22)], foot_L=[("X", -8)],
        thigh_R=[("X", -6), ("Y", 4)], shin_R=[("X", 22)], foot_R=[("X", -8)])),
    # Right knee lifts and the leg starts reaching out to the right.
    (3, strafe_pose(
        _loc=(0, 0, -0.03),
        thigh_L=[("X", -4), ("Y", -4)], shin_L=[("X", 25)], foot_L=[("X", -4)],
        thigh_R=[("X", -30), ("Y", 12)], shin_R=[("X", 55)], foot_R=[("X", 10)])),
    # Left leg drives off its toes; right leg extends out to land.
    (6, strafe_pose(
        _loc=(0, 0, 0.02),
        thigh_L=[("X", 0), ("Y", -14)], shin_L=[("X", 10)], foot_L=[("X", 20)],
        thigh_R=[("X", -12), ("Y", 22)], shin_R=[("X", 20)], foot_R=[("X", -5)])),
    # Right foot plants wide; widest stance, body dips.
    (8, strafe_pose(
        _loc=(0, 0, -0.06),
        thigh_L=[("X", 0), ("Y", -14)], shin_L=[("X", 15)], foot_L=[("X", 10)],
        thigh_R=[("X", -6), ("Y", 18)], shin_R=[("X", 25)], foot_R=[("X", -6)])),
    # Left leg lifts and draws in toward the right foot.
    (11, strafe_pose(
        _loc=(0, 0, -0.03),
        thigh_L=[("X", -25), ("Y", -2)], shin_L=[("X", 60)], foot_L=[("X", 10)],
        thigh_R=[("X", -4), ("Y", 8)], shin_R=[("X", 25)], foot_R=[("X", 0)])),
    # Body passes over the right foot; left foot reaches in to land.
    (14, strafe_pose(
        _loc=(0, 0, 0.01),
        thigh_L=[("X", -12), ("Y", 6)], shin_L=[("X", 30)], foot_L=[("X", -5)],
        thigh_R=[("X", 0), ("Y", -2)], shin_R=[("X", 15)], foot_R=[("X", 15)])),
]
STRAFE_KEYS.append((16, STRAFE_KEYS[0][1]))

# Strafe left is the mirror image. Mirroring swaps the legs, so in the
# mirrored clip the RIGHT foot would land at frame 0; shifting it by half a
# cycle puts the left foot back at frame 0, in step with the other clips.
STRAFE_LEFT_KEYS = shift_keys([(f, mirror_pose(p)) for f, p in STRAFE_KEYS], 8)

# ---- Idle: standing relaxed and breathing. ------------------------------

IDLE_REST = {
    "upper_arm.L": [("Y", -7)], "upper_arm.R": [("Y", 7)],
    "forearm.L": [("X", -12)], "forearm.R": [("X", -12)],
    "chest": [],
    "head": [],
}
IDLE_BREATHE = {
    "_loc": (0, 0, -0.006),
    "upper_arm.L": [("Y", -9)], "upper_arm.R": [("Y", 9)],
    "forearm.L": [("X", -15)], "forearm.R": [("X", -15)],
    "chest": [("X", -2.5)],
    "head": [("X", 2)],
}
IDLE_KEYS = [(0, IDLE_REST), (24, IDLE_BREATHE), (48, IDLE_REST)]

# name -> (keys, how to move between keys). "LINEAR" gives the even,
# mechanical motion of the run; "AUTO" is smooth Bezier for the idle.
ANIMATIONS = {
    "run": (RUN_KEYS, "LINEAR"),
    "run_back": (BACK_KEYS, "LINEAR"),
    "strafe_right": (STRAFE_KEYS, "LINEAR"),
    "strafe_left": (STRAFE_LEFT_KEYS, "LINEAR"),
    "idle": (IDLE_KEYS, "AUTO"),
}

# The clips Godot blends by direction; previews render a strip of each.
LOCOMOTION = ["run", "run_back", "strafe_right", "strafe_left"]


def pose_rotation(rest_quat, rotations):
    """Turn [(axis, degrees), ...] in armature space into the bone-local
    quaternion that Blender stores for a pose bone."""
    world = Quaternion()  # identity
    for axis, deg in rotations:
        vec = {"X": (1, 0, 0), "Y": (0, 1, 0), "Z": (0, 0, 1)}[axis]
        world = Quaternion(vec, math.radians(deg)) @ world
    # A pose bone's rotation is relative to its own rest orientation, so
    # express the armature-space rotation in the bone's frame.
    local = rest_quat.inverted() @ world @ rest_quat
    if local.w < 0:  # keep neighbouring keys in the same "hemisphere"
        local.negate()
    return local


def set_interpolation(action, mode):
    """Set how every key moves to the next one.

    "LINEAR": straight, even motion between keys.
    "AUTO": smooth Bezier that flows *through* keys. Blender's default,
    "Auto Clamped", eases into and out of every key instead, which makes
    limbs pause at each one."""
    # Blender 4.4+ stores keys in layers -> strips -> channel bags.
    for layer in action.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                for fcurve in bag.fcurves:
                    for key in fcurve.keyframe_points:
                        if mode == "LINEAR":
                            key.interpolation = "LINEAR"
                        else:
                            key.interpolation = "BEZIER"
                            key.handle_left_type = "AUTO"
                            key.handle_right_type = "AUTO"
                    fcurve.update()


def make_animations(arm_obj):
    arm_obj.animation_data_create()
    for pb in arm_obj.pose.bones:
        pb.rotation_mode = "QUATERNION"
    hips_rest = arm_obj.data.bones["hips"].matrix_local.to_quaternion()

    for anim_name, (keys, interpolation) in ANIMATIONS.items():
        action = bpy.data.actions.new(anim_name)
        action.use_fake_user = True  # keep it saved even when not assigned
        arm_obj.animation_data.action = action
        for frame, pose in keys:
            # Key every bone on every key frame (identity if the pose doesn't
            # mention it), so no bone keeps a leftover value.
            for pb in arm_obj.pose.bones:
                rest = pb.bone.matrix_local.to_quaternion()
                pb.rotation_quaternion = pose_rotation(rest, pose.get(pb.name, []))
                pb.keyframe_insert("rotation_quaternion", frame=frame)
            hips = arm_obj.pose.bones["hips"]
            hips.location = hips_rest.inverted() @ Vector(pose.get("_loc", (0, 0, 0)))
            hips.keyframe_insert("location", frame=frame)
        set_interpolation(action, interpolation)
        print(f"[make_player] animation '{anim_name}': {keys[-1][0]} frames")

    scene = bpy.context.scene
    scene.render.fps = FPS
    scene.frame_start = 0
    scene.frame_end = RUN_KEYS[-1][0]
    # Leave "run" active so pressing Play in Blender shows it straight away.
    arm_obj.animation_data.action = bpy.data.actions["run"]


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

    make_animations(arm_obj)

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
        export_animations=True,
        export_animation_mode="ACTIONS",  # every action becomes a clip
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
    def aim(deg, elev):
        a, e = math.radians(deg), math.radians(elev)
        cam.location = target + 6 * Vector((
            math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))
        direction = target - cam.location
        cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()

    # Static views in the rest pose (no action playing).
    rig = bpy.data.objects["PlayerRig"]
    run_action = rig.animation_data.action
    rig.animation_data.action = None
    for pb in rig.pose.bones:
        pb.rotation_quaternion = Quaternion()
        pb.location = (0, 0, 0)
    for name, (deg, elev) in views.items():
        aim(deg, elev)
        scene.render.filepath = os.path.join(out_dir, f"player_{name}.png")
        bpy.ops.render.render(write_still=True)

    # Each locomotion cycle's key frames, side by side in one strip per view.
    # Side shows the leg swing of run/backpedal, front the strafe stance.
    scene.render.resolution_x = 300
    scene.render.resolution_y = 480
    cam_data.ortho_scale = 2.3
    strip_views = {"side": (90, 5), "front": (0, 5), "game": (45, 30)}
    for anim_name in LOCOMOTION:
        rig.animation_data.action = bpy.data.actions[anim_name]
        keys = ANIMATIONS[anim_name][0]
        frames = [f for f, _ in keys[:-1]]
        for name, (deg, elev) in strip_views.items():
            aim(deg, elev)
            render_strip(scene, frames,
                         os.path.join(out_dir, f"{anim_name}_{name}.png"))
    rig.animation_data.action = run_action


def render_strip(scene, frames, out_path):
    """Render the given frames and paste them side by side into one PNG."""
    out_dir = os.path.dirname(out_path)
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
            row = tile[y * w * 4:(y + 1) * w * 4]
            start = (y * w * len(tiles) + t * w) * 4
            px[start:start + w * 4] = row
    strip.pixels = px
    strip.filepath_raw = out_path
    strip.file_format = "PNG"
    strip.save()
    bpy.data.images.remove(strip)


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    build()
    export()
    if "--preview" in argv:
        render_previews(argv[argv.index("--preview") + 1])
