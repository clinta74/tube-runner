"""The installer's two bitmaps, made from the game's icon: banner.bmp across the top of the inner
pages (493 x 58) and dialog.bmp down the left of the first and last (493 x 312). WiX's own are red
placeholders that look like a warning. Needs Pillow; run from anywhere:

    python installer/make_bitmaps.py
"""
from pathlib import Path

from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
icon = Image.open(HERE.parent / 'game' / 'icon.ico')
icon.size = max(icon.ico.sizes())  # the largest frame in the .ico
icon = icon.convert('RGBA')

DARK = (14, 18, 24)
ACCENT = (127, 240, 255)  # the see-through style's line colour


def stripe(draw, h, x0, thickness, colour=ACCENT):
    """A pale slanted stroke, fading across its width: a hint of the tube's lines."""
    for i in range(thickness):
        a = int(90 * (1 - i / thickness))
        draw.line([(x0 + i, 0), (x0 + i + h * 0.35, h)], fill=colour + (a,), width=1)


def banner():
    # Light, because WiX draws each page's title over it in black.
    w, h = 493, 58
    im = Image.new('RGBA', (w, h), (244, 247, 250, 255))
    over = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(over)
    for x in (300, 330, 360):
        stripe(d, h, x, 6, colour=(20, 120, 140))
    im = Image.alpha_composite(im, over)
    im.alpha_composite(icon.resize((44, 44), Image.LANCZOS), (w - 44 - 7, 7))
    im.convert('RGB').save(HERE / 'banner.bmp')


def dialog():
    w, h, panel = 493, 312, 164
    im = Image.new('RGB', (w, h), (255, 255, 255))
    left = Image.new('RGBA', (panel, h), DARK + (255,))
    over = Image.new('RGBA', (panel, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(over)
    for x in (-40, 0, 40):
        stripe(d, h, x, 10)
    left = Image.alpha_composite(left, over)
    left.alpha_composite(icon.resize((104, 104), Image.LANCZOS), ((panel - 104) // 2, 40))
    im.paste(left.convert('RGB'), (0, 0))
    im.save(HERE / 'dialog.bmp')


banner()
dialog()
print('wrote banner.bmp and dialog.bmp')
