"""Generates src/GdAntiVirus/Assets/app.ico and logo.png (requires Pillow)."""
from pathlib import Path
from PIL import Image, ImageDraw

S = 1024
OUT = Path(__file__).resolve().parent.parent / "src" / "GdAntiVirus" / "Assets"


def shield(size: int) -> Image.Image:
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    # vertical gradient fill clipped by the shield mask
    grad = Image.new("RGBA", (S, S))
    gd = ImageDraw.Draw(grad)
    top, bottom = (92, 205, 255), (0, 103, 192)
    for y in range(S):
        t = y / (S - 1)
        gd.line([(0, y), (S, y)], fill=tuple(int(a + (b - a) * t) for a, b in zip(top, bottom)) + (255,))

    mask = Image.new("L", (S, S), 0)
    md = ImageDraw.Draw(mask)
    pts = [(512, 60), (880, 190), (880, 500)]
    # right curve down to the tip
    for i in range(1, 41):
        t = i / 40
        x = 880 + (512 - 880) * t
        y = 500 + (980 - 500) * (t ** 0.8)
        x += 120 * t * (1 - t)
        pts.append((x, y))
    left = [(1024 - x, y) for x, y in reversed(pts[3:-1])]
    pts += left + [(144, 500), (144, 190)]
    md.polygon(pts, fill=255)
    img.paste(grad, (0, 0), mask)

    d = ImageDraw.Draw(img)
    d.line([(330, 520), (465, 660), (710, 380)], fill=(255, 255, 255, 255), width=96, joint="curve")
    for cx, cy in [(330, 520), (710, 380)]:
        d.ellipse([cx - 48, cy - 48, cx + 48, cy + 48], fill=(255, 255, 255, 255))
    return img.resize((size, size), Image.LANCZOS)


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    big = shield(256)
    big.save(OUT / "app.ico", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    shield(128).save(OUT / "logo.png")
    print("ok")
