"""Templates and a layout mock-up for Astra's modular shop pieces (256 px per unit, tile = 256 x 128 diamond)."""
import shutil, sys
from PIL import Image, ImageDraw, ImageFont

OUT = sys.argv[1]
REPO = sys.argv[2]
RED = (220, 40, 40, 255)
try:
    FONT = ImageFont.truetype("/System/Library/Fonts/Helvetica.ttc", 22)
    BIG = ImageFont.truetype("/System/Library/Fonts/Helvetica.ttc", 30)
except Exception:
    FONT = BIG = ImageFont.load_default()

def dot(d, x, y, r=5):
    d.ellipse([x - r, y - r, x + r, y + r], fill=RED)

def save(img, name):
    img.save(f"{OUT}/{name}")

# 1. Floor tile: same shape as the ground tile (diamond 256 x 128, 24 px lip).
shutil.copyfile(f"{REPO}/Docs/astra-refs/blocks/template_ground_256x152.png", f"{OUT}/template_floor_256x152.png")

H, STUB, T = 358, 77, (13, 6)   # wall height, stub height (0.3 units), thickness offset per side

def wall(name, height, canvas_h, right=False, segments=1):
    w = 48 + 128 * segments
    img = Image.new("RGBA", (w, canvas_h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    base = canvas_h - 16
    ax, ay = 24, base
    bx, by = 24 + 128 * segments, base - 64 * segments
    face = [(ax, ay), (bx, by), (bx, by - height), (ax, ay - height)]
    top = [(ax, ay - height), (bx, by - height), (bx - T[0], by - height - T[1]), (ax - T[0], ay - height - T[1])]
    if right:
        face = [(w - x, y) for x, y in face]
        top = [(w - x, y) for x, y in top]
        ax, bx = w - ax, w - bx
    d.polygon(top, fill=(150, 140, 125, 255))
    d.polygon(face, fill=(205, 195, 175, 255), outline=(120, 110, 95, 255))
    dot(d, (ax + bx) // 2, (ay + by) // 2)
    save(img, name)
    return img

wall("template_wall_left_176x495.png", H, 495)
wall("template_wall_right_176x495.png", H, 495, right=True)
wall("template_stub_left_176x220.png", STUB, 220)
wall("template_stub_right_176x220.png", STUB, 220, right=True)
wall("template_wallpiece_left_2seg_304x560.png", H, 560, segments=2)

# Back corner post and front corner post.
for name, h, ch in (("template_post_back_64x495.png", H + 20, 495), ("template_post_front_64x220.png", STUB + 20, 220)):
    img = Image.new("RGBA", (64, ch), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rectangle([16, ch - 16 - h, 48, ch - 16], fill=(170, 130, 90, 255))
    dot(d, 32, ch - 16)
    save(img, name)

# Floor furniture footprints on a 512 x 512 canvas, ground point (256, 420) = footprint centre.
def footprint(name, a, b):
    img = Image.new("RGBA", (512, 512), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    gx, gy = 256, 420
    A, B = (128, -64), (-128, -64)   # one tile toward the right / toward the left (image px)
    pts = []
    for sa, sb in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
        pts.append((gx + sa * a / 2 * A[0] + sb * b / 2 * B[0], gy + sa * a / 2 * A[1] + sb * b / 2 * B[1]))
    d.polygon(pts, fill=(120, 170, 120, 120), outline=(40, 110, 40, 255))
    dot(d, gx, gy)
    d.text((12, 10), f"{a} x {b} tiles", font=FONT, fill=(40, 40, 40, 255))
    save(img, name)

footprint("template_footprint_1x1.png", 1, 1)
footprint("template_footprint_2x1_facingA.png", 2, 1)
footprint("template_footprint_1x2_facingB.png", 1, 2)
footprint("template_footprint_3x1_facingA.png", 3, 1)
footprint("template_footprint_2x2.png", 2, 2)

# Layout mock-up: a 4 x 4 shop with its expansion plots, furniture placed in both facings.
PX = 64   # px per half tile step in the mock-up (tile = 128 x 64 here)
W, Hh = 1400, 900
img = Image.new("RGBA", (W, Hh), (238, 230, 214, 255))
d = ImageDraw.Draw(img)
fx, fy = 700, 760   # front corner of the built shop

def P(a, b):   # floor point, a toward the right, b toward the left (tiles)
    return (fx + (a - b) * PX, fy - (a + b) * PX / 2)

def tile(a, b, fill, outline=(150, 135, 110, 255)):
    d.polygon([P(a, b), P(a + 1, b), P(a + 1, b + 1), P(a, b + 1)], fill=fill, outline=outline)

N = 4
for a in range(N, N + 2):
    for b in range(0, N + 2):
        tile(a, b, (214, 200, 160, 255), (170, 150, 110, 255))
for b in range(N, N + 2):
    for a in range(0, N):
        tile(a, b, (214, 200, 160, 255), (170, 150, 110, 255))
for a in range(N):
    for b in range(N):
        tile(a, b, (226, 196, 150, 255) if (a + b) % 2 else (216, 184, 138, 255))
WH = 150
def wall_seg(p, q, h, fill):
    d.polygon([p, q, (q[0], q[1] - h), (p[0], p[1] - h)], fill=fill, outline=(120, 105, 85, 255))
for a in range(N):   # back-left wall at b = N
    wall_seg(P(a, N), P(a + 1, N), WH, (232, 214, 182, 255) if a != 1 else (180, 205, 220, 255))
for b in range(N):   # back-right wall at a = N
    wall_seg(P(N, b + 1), P(N, b), WH, (205, 188, 160, 255) if b != 2 else (160, 120, 90, 255))
for a in range(N):   # front stubs
    if a != 1:
        wall_seg(P(a, 0), P(a + 1, 0), 22, (170, 130, 90, 255))
for b in range(N):
    wall_seg(P(0, b + 1), P(0, b), 22, (150, 112, 78, 255))

def block(a, b, fa, fb, h, label, colour):
    p0, p1, p2, p3 = P(a, b), P(a + fa, b), P(a + fa, b + fb), P(a, b + fb)
    up = lambda p: (p[0], p[1] - h)
    d.polygon([p1, p2, up(p2), up(p1)], fill=tuple(int(c * 0.8) for c in colour[:3]) + (255,))
    d.polygon([p0, p1, up(p1), up(p0)], fill=tuple(int(c * 0.9) for c in colour[:3]) + (255,))
    d.polygon([up(p0), up(p1), up(p2), up(p3)], fill=colour)
    c = up(((p0[0] + p2[0]) / 2, (p0[1] + p2[1]) / 2))
    d.text((c[0] - 40, c[1] - 12), label, font=FONT, fill=(30, 30, 30, 255))

def wall_piece(p, q, lo, hi, colour, label):
    d.polygon([(p[0], p[1] - lo), (q[0], q[1] - lo), (q[0], q[1] - hi), (p[0], p[1] - hi)], fill=colour, outline=(40, 40, 40, 255))
    d.text(((p[0] + q[0]) / 2 - 50, (p[1] + q[1]) / 2 - hi - 30), label, font=FONT, fill=(30, 30, 30, 255))

wall_piece(P(2, N), P(4, N), 30, 110, (110, 70, 40, 255), "potion shelf")
wall_piece(P(N, 2), P(N, 0), 30, 100, (120, 120, 130, 255), "weapon rack")
d.text((P(1.5, N)[0] - 40, P(1.5, N)[1] - WH - 30), "window", font=FONT, fill=(30, 30, 30, 255))
d.text((P(N, 3.5)[0] - 20, P(N, 3.5)[1] - WH - 30), "stockroom door", font=FONT, fill=(30, 30, 30, 255))
block(0, 2, 2, 1, 45, "desk A", (170, 120, 70, 255))
block(3, 0, 1, 1, 50, "pedestal", (190, 190, 190, 255))
block(2, 2, 1, 1, 70, "armour", (90, 120, 200, 255))
block(3, 1, 1, 2, 40, "table B", (60, 80, 140, 255))
d.text((P(1.2, -0.6)[0] - 60, P(1.2, -0.6)[1] + 10), "entrance (gap in the stub)", font=FONT, fill=(30, 30, 30, 255))
d.text((P(5, 1)[0] + 30, P(5, 1)[1]), "expansion plot:\nbuy a strip, the\nback wall moves out", font=FONT, fill=(110, 90, 50, 255))
d.text((P(0, 5)[0] - 190, P(0, 5)[1] - 40), "expansion plot", font=FONT, fill=(110, 90, 50, 255))
d.text((30, 20), "Modular shop: 4 x 4 start, grows a strip at a time; walls and stubs follow the edge", font=BIG, fill=(60, 45, 35, 255))
img = img.crop((0, 0, W, Hh))
bbox = (0, 0, W, Hh)
save(img, "shop_modular_layout.png")
print("ok")
