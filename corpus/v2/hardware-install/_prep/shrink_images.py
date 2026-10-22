#!/usr/bin/env python3
"""Downscales corpus images in place (max 720 px wide, JPEG q70, optimised PNG) so the corpus stays small.
Usage: shrink_images.py IMAGE_DIR. Dev tool; needs Pillow (uv run --with pillow)."""
import os
import sys
from PIL import Image

MAXW = 720
d = sys.argv[1]
before = after = 0
for name in sorted(os.listdir(d)):
    p = os.path.join(d, name)
    ext = os.path.splitext(name)[1].lower()
    if ext not in (".jpg", ".jpeg", ".png"):
        continue
    before += os.path.getsize(p)
    im = Image.open(p)
    if im.width > MAXW:
        h = round(im.height * MAXW / im.width)
        im = im.resize((MAXW, h), Image.LANCZOS)
    if ext in (".jpg", ".jpeg"):
        im.convert("RGB").save(p, "JPEG", quality=70, optimize=True)
    else:
        if im.mode in ("RGBA", "LA"):
            im.save(p, "PNG", optimize=True)
        else:
            im = im.convert("RGB").quantize(colors=128, method=Image.MEDIANCUT) if im.width * im.height > 20000 else im
            im.save(p, "PNG", optimize=True)
    after += os.path.getsize(p)
print("images: %.1f MB -> %.1f MB" % (before / 1048576, after / 1048576))
