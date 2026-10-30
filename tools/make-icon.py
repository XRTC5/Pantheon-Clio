"""Draws clio.ico: a cyan stylus nib inside HUD corner brackets, Argus palette.

Run once; the .ico is committed. python tools/make-icon.py
"""
from pathlib import Path

from PIL import Image, ImageDraw

BG = (7, 11, 16, 255)
ACCENT = (34, 211, 238, 255)
DIM = (14, 74, 86, 255)


def draw(size: int) -> Image.Image:
    s = size * 4  # supersample, then downscale for clean edges
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    r = s // 8
    d.rounded_rectangle([0, 0, s - 1, s - 1], radius=r, fill=BG)

    w = max(4, s // 22)
    m = s // 7
    arm = s // 4
    for x, y, dx, dy in ((m, m, 1, 1), (s - m, m, -1, 1), (m, s - m, 1, -1), (s - m, s - m, -1, -1)):
        d.line([(x, y), (x + dx * arm, y)], fill=DIM, width=w)
        d.line([(x, y), (x, y + dy * arm)], fill=DIM, width=w)

    # The nib: a diamond with a slit, pointing down-left like a pen at rest.
    c = s / 2
    k = s * 0.24
    nib = [(c, c - k), (c + k * 0.62, c), (c, c + k), (c - k * 0.62, c)]
    d.polygon(nib, fill=ACCENT)
    d.line([(c, c - k * 0.15), (c, c + k)], fill=BG, width=max(3, w))
    d.ellipse([c - w * 1.2, c - k * 0.15 - w * 1.2, c + w * 1.2, c - k * 0.15 + w * 1.2], fill=BG)
    return img.resize((size, size), Image.Resampling.LANCZOS)


def main() -> None:
    out = Path(__file__).resolve().parent.parent / "src" / "Clio" / "clio.ico"
    sizes = [16, 20, 24, 32, 40, 48, 64, 256]
    big = draw(256)
    big.save(out, sizes=[(n, n) for n in sizes], append_images=[draw(n) for n in sizes[:-1]])
    print(out)


if __name__ == "__main__":
    main()
