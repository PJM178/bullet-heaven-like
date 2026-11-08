"""Generate the hammer smash's pixel-art effect sprite sheets.

Run from the project root (needs Pillow: `pip install pillow`):

    python3 art/tools/make_hammer_fall_sprites.py

It writes, into weapons/hammer/sprites/:
  hammer.png     - a ghostly phantom hammer swinging down  (6 frames, 96x96)
  hit.png        - the bright burst where it lands         (5 frames, 32x32)
  ripple.png     - tight rings rippling out from the hit   (6 frames, 64x64)
  shockwave.png  - the big soft-rimmed ring that follows   (8 frames, 96x96)
  chunks.png     - pale chunks of ground thrown up         (3 variants, 8x8)

Each sheet is one row of frames, left to right. The game plays them as
flipbooks (core/sprite_flipbook.gdshader) at a low, stepped frame rate.
All effects except the chunks are drawn with additive blending in the game,
so on screen a pixel's brightness is (colour x alpha): low alpha = faint.

Everything is drawn with hard pixel edges (no anti-aliasing) so it stays
crisp when scaled up with nearest-neighbour filtering. A tribute to the
sprite effects of 2D/3D-era action RPGs, drawn from scratch.
"""

import math
import os

from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "weapons", "hammer", "sprites")

CREAM = (255, 238, 222)
PINK = (240, 196, 196)
WHITE = (255, 255, 255)


def sheet(frame_size, frames):
    return Image.new("RGBA", (frame_size * frames, frame_size), (0, 0, 0, 0))


def save(image, name):
    os.makedirs(OUT, exist_ok=True)
    path = os.path.join(OUT, name)
    image.save(path)
    print("wrote", os.path.relpath(path, ROOT))


# ---- Hammer ---------------------------------------------------------------
# A phantom hammer seen from the side, swinging overhead from upper right,
# over the top, down onto the landing point at the bottom centre. In the game
# the sheet is mirrored when the target is to the right of the player, so
# the grip is always on the player's side.
#
# It's drawn as a ghost: a nearly see-through body, a brighter pale rim, a
# faint glow just outside the rim, and fading afterimages of where it just
# was while it swings.

HAMMER_SIZE = 96
PIVOT = (66, 50)        # where the hands grip it
# Where the head's centre ends up on impact. Its striking face is then about
# 10 px lower, at y = 90: the point that touches the ground (6 px up from the
# bottom of the frame, which HammerWeapon.cs relies on).
LANDING = (48, 80)
REACH = math.dist(PIVOT, LANDING)

# Head angle per frame, in degrees: 0 = straight up, negative = turning
# left (counter-clockwise). The steps grow as it falls, so it accelerates.
# Frame 3 is the impact; frames 4 and 5 are the hammer fading on the ground.
LANDING_ANGLE = math.degrees(math.atan2(LANDING[0] - PIVOT[0], -(LANDING[1] - PIVOT[1])))
HAMMER_FRAMES = [
    # (angle, strength 0..1, afterimage angles, oldest first)
    (5.0, 0.7, []),
    (-35.0, 0.85, [-10.0]),
    (-95.0, 1.0, [-55.0, -75.0]),
    (LANDING_ANGLE, 1.0, [-115.0, -135.0]),
    (LANDING_ANGLE, 0.5, []),
    (LANDING_ANGLE, 0.22, []),
]

# Alpha of each part of the ghost at full strength.
GHOST_BODY = 65
GHOST_RIM = 160
GHOST_GLOW = 45
# Afterimages are drawn at these strengths (oldest first).
AFTERIMAGE_STRENGTHS = [0.2, 0.4]


def direction(angle_degrees):
    a = math.radians(angle_degrees)
    return (math.sin(a), -math.cos(a))


def hammer_mask(angle):
    """The hammer's silhouette at this angle, as a one-frame 0/255 mask."""
    mask = Image.new("L", (HAMMER_SIZE, HAMMER_SIZE), 0)
    draw = ImageDraw.Draw(mask)
    dx, dy = direction(angle)
    px, py = PIVOT
    # The haft, from a little behind the grip to the head.
    draw.line([(px - dx * 8, py - dy * 8), (px + dx * (REACH - 8), py + dy * (REACH - 8))],
              fill=255, width=6)
    # The head: a big block across the end of the haft, its long side
    # perpendicular to the haft so a striking face leads the swing.
    cx, cy = px + dx * REACH, py + dy * REACH
    perp = (-dy, dx)
    half_long, half_short = 16.0, 10.0
    corners = []
    for s_long, s_short in ((1, 1), (1, -1), (-1, -1), (-1, 1)):
        corners.append((cx + perp[0] * half_long * s_long + dx * half_short * s_short,
                        cy + perp[1] * half_long * s_long + dy * half_short * s_short))
    draw.polygon(corners, fill=255)
    return mask


def ghost(angle, strength):
    """One ghostly hammer as an RGBA frame: body, rim and outer glow."""
    mask = hammer_mask(angle)
    # MinFilter shrinks the shape by a pixel, MaxFilter grows it by a
    # pixel. The differences give the 1-2 px rim inside the edge and the
    # glow outside it.
    inner = mask.filter(ImageFilter.MinFilter(5))
    outer = mask.filter(ImageFilter.MaxFilter(3))
    frame = Image.new("RGBA", mask.size, (0, 0, 0, 0))
    pixels = frame.load()
    m, i, o = mask.load(), inner.load(), outer.load()
    for y in range(HAMMER_SIZE):
        for x in range(HAMMER_SIZE):
            if i[x, y]:
                pixels[x, y] = PINK + (int(GHOST_BODY * strength),)
            elif m[x, y]:
                pixels[x, y] = WHITE + (int(GHOST_RIM * strength),)
            elif o[x, y]:
                pixels[x, y] = CREAM + (int(GHOST_GLOW * strength),)
    return frame


def make_hammer():
    image = sheet(HAMMER_SIZE, len(HAMMER_FRAMES))
    for k, (angle, strength, afterimages) in enumerate(HAMMER_FRAMES):
        frame = Image.new("RGBA", (HAMMER_SIZE, HAMMER_SIZE), (0, 0, 0, 0))
        # Afterimages first (underneath), then the hammer itself on top.
        for after_angle, after_strength in zip(afterimages, AFTERIMAGE_STRENGTHS[-len(afterimages):] if afterimages else []):
            frame = Image.alpha_composite(frame, ghost(after_angle, strength * after_strength))
        frame = Image.alpha_composite(frame, ghost(angle, strength))
        image.paste(frame, (k * HAMMER_SIZE, 0))
    save(image, "hammer.png")


# ---- Hit burst ------------------------------------------------------------
# A star-shaped flash: a bright core with rays that shoot out, thin, break
# up and scatter into sparkles.

HIT_SIZE = 32


def make_hit():
    frames = 5
    image = sheet(HIT_SIZE, frames)
    draw = ImageDraw.Draw(image)
    c = HIT_SIZE / 2 - 0.5
    for k in range(frames):
        ox = k * HIT_SIZE
        cx = ox + c

        def ray(angle_degrees, r0, r1, color, width):
            a = math.radians(angle_degrees)
            draw.line([(cx + math.cos(a) * r0, c + math.sin(a) * r0),
                       (cx + math.cos(a) * r1, c + math.sin(a) * r1)],
                      fill=color, width=width)

        if k == 0:
            draw.ellipse([cx - 4, c - 4, cx + 4, c + 4], fill=WHITE + (255,))
            for a in range(0, 360, 90):
                ray(a, 3, 8, WHITE + (255,), 2)
        elif k == 1:
            draw.ellipse([cx - 6, c - 6, cx + 6, c + 6], fill=WHITE + (255,))
            for i, a in enumerate(range(0, 360, 45)):
                ray(a + 22, 5, 15 if i % 2 == 0 else 10, CREAM + (240,), 2)
        elif k == 2:
            draw.ellipse([cx - 8, c - 8, cx + 8, c + 8], outline=PINK + (220,), width=2)
            draw.ellipse([cx - 3, c - 3, cx + 3, c + 3], fill=WHITE + (200,))
            for i, a in enumerate(range(0, 360, 45)):
                ray(a + 22, 9, 15 if i % 2 == 0 else 12, CREAM + (200,), 1)
        elif k == 3:
            draw.ellipse([cx - 10, c - 10, cx + 10, c + 10], outline=PINK + (120,), width=1)
            for i, a in enumerate(range(0, 360, 45)):
                ray(a + 22, 11, 15, CREAM + (140,), 1)
        else:
            for i, a in enumerate(range(0, 360, 45)):
                r = 13 + (i % 2) * 2
                rad = math.radians(a + 22)
                draw.point((round(cx + math.cos(rad) * r), round(c + math.sin(rad) * r)),
                           fill=WHITE + (110,))
    save(image, "hit.png")


# ---- Ripples --------------------------------------------------------------
# Three thin rings close together, spreading out quickly and fading. Drawn
# flat on the ground in the game, so the camera sees them as ellipses.

RIPPLE_SIZE = 64


def make_ripple():
    frames = 6
    image = sheet(RIPPLE_SIZE, frames)
    draw = ImageDraw.Draw(image)
    c = RIPPLE_SIZE / 2 - 0.5
    for k in range(frames):
        ox = k * RIPPLE_SIZE
        fade = 1.0 - k / frames
        for i in range(3):
            r = 6 + k * 4.5 + i * 5
            if r > c:
                continue
            alpha = int(235 * fade * (1.0 - i * 0.25))
            draw.ellipse([ox + c - r, c - r, ox + c + r, c + r],
                         outline=WHITE + (alpha,), width=2 if i == 0 else 1)
    save(image, "ripple.png")


# ---- Shockwave ------------------------------------------------------------
# A big ring that bursts out and slows down: a bright rim, a dimmer band
# inside it and a faint fill, all in hard steps (no smooth gradients), so the
# "soft" edge is drawn the way a pixel artist would.

SHOCK_SIZE = 96


def make_shockwave():
    frames = 8
    image = sheet(SHOCK_SIZE, frames)
    pixels = image.load()
    c = SHOCK_SIZE / 2 - 0.5
    for k in range(frames):
        ox = k * SHOCK_SIZE
        t = k / (frames - 1)
        grow = 1.0 - (1.0 - t) ** 3          # ease out: bursts, then slows
        radius = 10 + (SHOCK_SIZE / 2 - 12) * grow
        rim = 5 - 3 * t                       # bright rim width
        band = 11 - 6 * t                     # dimmer band width (incl. rim)
        rim_alpha = int(235 - 170 * t)
        band_alpha = int(120 - 90 * t)
        fill_alpha = int(45 * (1.0 - t) ** 2)
        halo_alpha = int(70 * (1.0 - t))
        for y in range(SHOCK_SIZE):
            for x in range(SHOCK_SIZE):
                d = math.hypot(x - c, y - c)
                if d > radius + 2:
                    continue
                if d > radius:
                    color = CREAM + (halo_alpha,)
                elif d > radius - rim:
                    color = WHITE + (rim_alpha,)
                elif d > radius - band:
                    color = CREAM + (band_alpha,)
                else:
                    color = PINK + (fill_alpha,)
                if color[3] > 0:
                    pixels[ox + x, y] = color
    save(image, "shockwave.png")


# ---- Chunks ---------------------------------------------------------------
# Small pale lumps of ground, lit from the top left. Normal (not additive)
# blending, three variants picked at random per particle.

CHUNK_SIZE = 8
CHUNK_SHAPES = [
    [(1, 2), (4, 0), (7, 2), (6, 6), (2, 7), (0, 5)],
    [(0, 1), (5, 0), (7, 4), (4, 7), (1, 6)],
    [(2, 0), (6, 1), (7, 5), (3, 7), (0, 4)],
]


def make_chunks():
    image = sheet(CHUNK_SIZE, len(CHUNK_SHAPES))
    draw = ImageDraw.Draw(image)
    for k, shape in enumerate(CHUNK_SHAPES):
        ox = k * CHUNK_SIZE
        points = [(ox + x, y) for x, y in shape]
        draw.polygon(points, fill=(214, 196, 180, 255), outline=(170, 150, 140, 255))
        # Highlight on the top-left edges.
        draw.line(points[:2], fill=(250, 240, 228, 255))
    save(image, "chunks.png")


if __name__ == "__main__":
    make_hammer()
    make_hit()
    make_ripple()
    make_shockwave()
    make_chunks()
