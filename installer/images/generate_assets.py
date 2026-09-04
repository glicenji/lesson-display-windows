#!/usr/bin/env python3
"""
Regenerates every branded installer asset (the app icon, the two static
wizard banner bitmaps, and the slideshow crossfade frames) from the four
source photos in installer/images/source-photos/.

To use different photos: replace the files in source-photos/ (keep the same
names -- icon.png, slide-1.png, slide-2.png, slide-3.png; any common image
format works, not just .png) and re-run this script from anywhere:

    python3 installer/images/generate_assets.py

Requires Pillow (`pip install pillow`). Nothing else in the project needs
Pillow or Python at build/run time -- this script is a one-off asset step,
not part of the installer or either app.
"""
import os
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))          # installer/images
REPO_ROOT = os.path.dirname(os.path.dirname(HERE))          # repo root
SRC = os.path.join(HERE, "source-photos")
SLIDES_OUT = os.path.join(HERE, "slideshow")
ICON_OUT_DIR = os.path.join(REPO_ROOT, "assets")

os.makedirs(SLIDES_OUT, exist_ok=True)
os.makedirs(ICON_OUT_DIR, exist_ok=True)

ICON_SRC = os.path.join(SRC, "icon.png")
SLIDE_SRCS = [
    os.path.join(SRC, "slide-1.png"),
    os.path.join(SRC, "slide-2.png"),
    os.path.join(SRC, "slide-3.png"),
]

# ---------------------------------------------------------------------------
# 1. Windows .ico (multi-resolution) -- used by both .csproj files
#    (<ApplicationIcon>) and by the installer itself (SetupIconFile).
# ---------------------------------------------------------------------------
icon = Image.open(ICON_SRC).convert("RGBA")
ico_path = os.path.join(ICON_OUT_DIR, "icon.ico")
icon.save(ico_path, sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
print("wrote", ico_path)

# ---------------------------------------------------------------------------
# 2. Wizard banner bitmaps, built by compositing the icon artwork onto a
#    matching blue gradient (so its transparent rounded corners blend in
#    cleanly instead of showing hard edges or a mismatched background).
# ---------------------------------------------------------------------------
TOP_BLUE = (13, 42, 99)
BOTTOM_BLUE = (22, 82, 168)


def gradient_canvas(w, h, top, bottom):
    canvas = Image.new("RGB", (w, h))
    for y in range(h):
        t = y / max(h - 1, 1)
        row = tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(3))
        for x in range(w):
            canvas.putpixel((x, y), row)
    return canvas


def load_font(size):
    for candidate in [
        "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf",
        "C:\\Windows\\Fonts\\segoeuib.ttf",
        "C:\\Windows\\Fonts\\arialbd.ttf",
    ]:
        if os.path.exists(candidate):
            return ImageFont.truetype(candidate, size)
    return ImageFont.load_default()


def make_wizard_image(w, h, icon_frac, with_text):
    canvas = gradient_canvas(w, h, TOP_BLUE, BOTTOM_BLUE).convert("RGBA")
    icon_w = int(w * icon_frac)
    icon_resized = icon.resize((icon_w, icon_w), Image.LANCZOS)
    ix = (w - icon_w) // 2
    iy = int(h * 0.06)
    canvas.alpha_composite(icon_resized, (ix, iy))
    if with_text:
        draw = ImageDraw.Draw(canvas)
        font = load_font(max(14, w // 7))  # one short word now, size up to fill the space
        text = "ClassSync"
        bbox = draw.multiline_textbbox((0, 0), text, font=font, align="center", spacing=4)
        tw, th = bbox[2] - bbox[0], bbox[3] - bbox[1]
        tx = (w - tw) // 2
        ty = iy + icon_w + int(h * 0.05)
        draw.multiline_text((tx, ty), text, font=font, fill=(255, 255, 255, 255), align="center", spacing=4)
    return canvas.convert("RGB")


# 164x314 -- shown large on the Welcome/Finished pages (see WizardImageFile
# in LessonDisplaySetup.iss). 55x58 -- small corner logo on every other page
# (WizardSmallImageFile). Both dimensions are fixed by Inno Setup.
wizard_image = make_wizard_image(164, 314, icon_frac=0.86, with_text=True)
wizard_image_path = os.path.join(HERE, "wizard-image.bmp")
wizard_image.save(wizard_image_path)
print("wrote", wizard_image_path, wizard_image.size)

wizard_small = make_wizard_image(55, 58, icon_frac=0.92, with_text=False)
wizard_small_path = os.path.join(HERE, "wizard-small.bmp")
wizard_small.save(wizard_small_path)
print("wrote", wizard_small_path, wizard_small.size)

# ---------------------------------------------------------------------------
# 3. Slideshow: the 3 classroom photos, crossfading into each other. All the
#    *timing* (7s hold, ~600ms crossfade) is done on the Inno Setup side --
#    see the [Code] section of LessonDisplaySetup.iss -- this just renders
#    the individual crossfade frames themselves, so Setup only ever has to
#    load a plain .bmp file, never blend anything itself.
#
#    IMPORTANT: if you change FRAMES_PER_TRANSITION, update the matching
#    FramesPerTransition constant in LessonDisplaySetup.iss's [Code] section
#    too -- they have to agree.
# ---------------------------------------------------------------------------
SLIDE_W, SLIDE_H = 380, 214   # kept modest so it comfortably fits inside a
                               # standard (non-resizable) Inno Setup wizard
                               # page.
FRAMES_PER_TRANSITION = 12    # ~600ms crossfade at the 50ms tick used in .iss

# Clear out any previous slideshow frames so a resolution/count change here
# doesn't leave stale files behind for ExtractSlideshowFrames to trip over.
for name in os.listdir(SLIDES_OUT):
    if name.endswith(".bmp"):
        os.remove(os.path.join(SLIDES_OUT, name))

slides = [Image.open(p).convert("RGB").resize((SLIDE_W, SLIDE_H), Image.LANCZOS) for p in SLIDE_SRCS]

pairs = [(0, 1, "t12"), (1, 2, "t23"), (2, 0, "t31")]
manifest = []
for a, b, prefix in pairs:
    for i in range(FRAMES_PER_TRANSITION):
        t = i / (FRAMES_PER_TRANSITION - 1)
        frame = Image.blend(slides[a], slides[b], t)
        name = f"{prefix}_{i:02d}.bmp"
        frame.save(os.path.join(SLIDES_OUT, name))
        manifest.append(name)

print(f"wrote {len(manifest)} slideshow frames to {SLIDES_OUT}")

total_bytes = sum(os.path.getsize(os.path.join(SLIDES_OUT, n)) for n in manifest)
print(f"slideshow total size: {total_bytes / 1024 / 1024:.1f} MB")

with open(os.path.join(SLIDES_OUT, "_manifest.txt"), "w") as f:
    f.write("\n".join(manifest) + "\n")
