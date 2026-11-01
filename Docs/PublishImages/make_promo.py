"""Generates the Asset Store graphics: Icon-512.png, Icon-160.png, CardImage.png, Background*.png.

Run with Pillow installed:  python make_promo.py
Everything is drawn at 4x and scaled down, for smooth edges.
"""
import math
import os
from PIL import Image, ImageDraw, ImageFont

SS = 4  # supersampling
OUT = os.path.dirname(os.path.abspath(__file__))
FONTS = "C:/Windows/Fonts"

BG_TOP = (46, 56, 72)
BG_BOTTOM = (24, 28, 36)
RING = (232, 238, 245)
PIN_GREEN = (123, 224, 123)
SCENE_BLUE = (143, 199, 255)
CURRENT_YELLOW = (255, 216, 74)
PILL_GREY = (88, 96, 110)
INK = (30, 34, 42)


def font(name, size):
    return ImageFont.truetype(os.path.join(FONTS, name), int(size * SS))


def gradient(w, h, top, bottom):
    img = Image.new("RGB", (w, h))
    d = ImageDraw.Draw(img)
    for y in range(h):
        t = y / max(h - 1, 1)
        d.line([(0, y), (w, y)], fill=tuple(round(a + (b - a) * t) for a, b in zip(top, bottom)))
    return img


def history_arrow(d, cx, cy, r, thickness, color=RING, grow=0):
    """Counter-clockwise ring arrow ("back in history"), open at the upper right."""
    start, end = -20, 290  # PIL angles: 0 = 3 o'clock, clockwise
    # grow > 0 draws the same shape that much bigger all around, for an outline drawn underneath.
    box = [cx - r - grow, cy - r - grow, cx + r + grow, cy + r + grow]
    d.arc(box, start, end, fill=color, width=round(thickness + grow * 2))
    # Round cap at the tail.
    tail = math.radians(end)
    tx, ty = cx + (r - thickness / 2) * math.cos(tail), cy + (r - thickness / 2) * math.sin(tail)
    cap = thickness / 2 + grow
    d.ellipse([tx - cap, ty - cap, tx + cap, ty + cap], fill=color)

    # Arrow head at the start, pointing counter-clockwise.
    a = math.radians(start)
    mid = r - thickness / 2
    px, py = cx + mid * math.cos(a), cy + mid * math.sin(a)
    dx, dy = math.sin(a), -math.cos(a)          # tangent, decreasing angle
    nx, ny = math.cos(a), math.sin(a)           # radial
    head = thickness * 1.25
    tip = (px + dx * head * 1.15, py + dy * head * 1.15)
    base = (px - dx * thickness * 0.15, py - dy * thickness * 0.15)
    points = [
        tip,
        (base[0] + nx * head, base[1] + ny * head),
        (base[0] - nx * head, base[1] - ny * head),
    ]
    d.polygon(points, fill=color)
    if grow > 0:
        d.line(points + points[:1], fill=color, width=round(grow * 2), joint="curve")
        # The line's joints skip the point where it closes (the tip), so round every corner explicitly.
        for x, y in points:
            d.ellipse([x - grow, y - grow, x + grow, y + grow], fill=color)


def entry_pill(d, x, y, w, h, color, text=None, text_font=None):
    """A history button: tinted pill with an icon square and a name (text, or a bar standing in for it)."""
    d.rounded_rectangle([x, y, x + w, y + h], radius=h * 0.28, fill=color)
    pad = h * 0.22
    icon = h - pad * 2
    d.rounded_rectangle([x + pad, y + pad, x + pad + icon, y + pad + icon], radius=icon * 0.2, fill=INK)
    tx = x + pad * 2 + icon
    if text:
        d.text((tx, y + h / 2), text, font=text_font, fill=INK, anchor="lm")
    else:
        bar = h * 0.2
        d.rounded_rectangle([tx, y + h / 2 - bar / 2, x + w - pad * 1.6, y + h / 2 + bar / 2], radius=bar / 2, fill=INK)


def draw_emblem(d, cx, cy, scale, arrow_outline=0):
    """Ring arrow around three history entries: pinned (green), scene object (blue), current (yellow)."""
    s = scale * SS
    if arrow_outline:
        history_arrow(d, cx, cy, 168 * s, 34 * s, INK, arrow_outline * s)
    history_arrow(d, cx, cy, 168 * s, 34 * s)
    w, h, gap = 176 * s, 46 * s, 14 * s
    top = cy - (h * 3 + gap * 2) / 2
    for i, color in enumerate((PIN_GREEN, SCENE_BLUE, CURRENT_YELLOW)):
        entry_pill(d, cx - w / 2, top + i * (h + gap), w, h, color)


def make_icon():
    n = 512 * SS
    img = gradient(n, n, BG_TOP, BG_BOTTOM)
    d = ImageDraw.Draw(img)
    draw_emblem(d, n / 2, n / 2, 1.0)
    icon = img.resize((512, 512), Image.LANCZOS)
    icon.save(os.path.join(OUT, "Icon-512.png"))
    icon.resize((160, 160), Image.LANCZOS).save(os.path.join(OUT, "Icon-160.png"))

    # Transparent, with a dark outline around the arrow so it shows on light backgrounds too.
    img = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    draw_emblem(ImageDraw.Draw(img), n / 2, n / 2, 1.0, arrow_outline=8)
    icon = img.resize((512, 512), Image.LANCZOS)
    icon.save(os.path.join(OUT, "Icon-512_Transparent.png"))
    icon.resize((160, 160), Image.LANCZOS).save(os.path.join(OUT, "Icon-160_Transparent.png"))


def draw_mouse_icon(d, x, y, h, color):
    """Small mouse glyph (outline, button split, wheel), h tall, top-left at (x, y)."""
    w = h * 0.68
    t = max(h * 0.11, SS)
    d.rounded_rectangle([x, y, x + w, y + h], radius=w / 2, outline=color, width=round(t))
    split = y + h * 0.42
    d.line([(x + w / 2, y + t), (x + w / 2, split)], fill=color, width=round(t * 0.8))
    d.line([(x + t, split), (x + w - t, split)], fill=color, width=round(t * 0.8))


def make_card():
    """Same as the card, with an extra line selling the mouse Back / Forward buttons."""
    w, h = 420 * SS, 280 * SS
    img = gradient(w, h, BG_TOP, BG_BOTTOM)
    d = ImageDraw.Draw(img)

    draw_emblem(d, 104 * SS, 108 * SS, 0.4)

    # Texts moved up to make room for the extra line.
    title = font("segoeuib.ttf", 27)
    d.text((196 * SS, 34 * SS), "Wise Selection", font=title, fill=RING)
    d.text((196 * SS, 68 * SS), "History", font=title, fill=RING)
    d.text((197 * SS, 112 * SS), "Back & forward through\nyour selections", font=font("segoeui.ttf", 15),
           fill=(175, 192, 212), spacing=3 * SS)

    draw_mouse_icon(d, 198 * SS, 163 * SS, 17 * SS, CURRENT_YELLOW)
    d.text((216 * SS, 171.5 * SS), "Mouse buttons supported!", font=font("segoeuib.ttf", 15),
           fill=CURRENT_YELLOW, anchor="lm")

    draw_strip(d, w)
    img.resize((420, 280), Image.LANCZOS).save(os.path.join(OUT, "CardImage.png"))


def draw_strip(d, w):
    """A strip like the window itself, along the bottom of the card."""
    y, ph = 218 * SS, 30 * SS
    x = 16 * SS
    d.rounded_rectangle([10 * SS, y - 8 * SS, w - 10 * SS, y + ph + 8 * SS], radius=8 * SS, fill=(20, 23, 30))
    for arrow in ("back", "forward"):
        bw = 30 * SS
        d.rounded_rectangle([x, y, x + bw, y + ph], radius=ph * 0.28, fill=PILL_GREY)
        cx, cy, a = x + bw / 2, y + ph / 2, 7 * SS
        sign = -1 if arrow == "back" else 1
        d.polygon([(cx + sign * a, cy), (cx - sign * a * 0.6, cy - a), (cx - sign * a * 0.6, cy + a)], fill=RING)
        x += bw + 5 * SS
    x += 6 * SS

    label = font("segoeuib.ttf", 12)
    for text, color in (("Player", PIN_GREEN), ("Main Camera", SCENE_BLUE), ("Cube [3]", CURRENT_YELLOW), ("Materials", PILL_GREY)):
        pw = d.textlength(text, font=label) + ph * 1.25
        if x + pw > w - 16 * SS:
            break
        entry_pill(d, x, y, pw, ph, color, text, label)
        x += pw + 5 * SS


def make_backgrounds():
    """Just the gradient, for composing other promo images."""
    for w, h in ((1950, 1300), (1200, 700)):
        gradient(w, h, BG_TOP, BG_BOTTOM).save(os.path.join(OUT, f"Background{w}x{h}.png"))


if __name__ == "__main__":
    make_icon()
    make_card()
    make_backgrounds()
