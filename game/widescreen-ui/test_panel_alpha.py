"""Regression for the invisible lower-panel rectangle. No game is launched.

Checks real generated assets, including transparent world/button locations and
opaque panel locations. This verifies the stock decal input-mask prerequisites;
live engine picking and button activation still require manual acceptance.
"""
import argparse
import hashlib
import json
import struct
from pathlib import Path

p = argparse.ArgumentParser()
p.add_argument("assets", type=Path)
a = p.parse_args()
checks = 0

def check(ok, why):
    global checks
    assert ok, why
    checks += 1

def texture(path):
    b = path.read_bytes()
    w, h = struct.unpack_from("<HH", b, 12)
    check(b[2] == 2 and b[16] == 32 and len(b) == 18 + w*h*4, str(path))
    def alpha(x, y, logical_width):
        px = min(w-1, int(x*w/logical_width))
        py = min(h-1, int(y*h/256))
        if not b[17] & 32:
            py = h-1-py
        if b[17] & 16:
            px = w-1-px
        return b[18+(py*w+px)*4+3]
    return alpha

for scope in ("pure", "arcane"):
    root = a.assets / scope
    manifest = json.loads((root / "assets.json").read_text())
    icon_layouts = {'data/ui/game/'+n+'.tgi' for n in
                    ('floaters','template_game','favorites_display','organization_editor')}
    check(len(manifest) == 67 or (len(manifest) == 71 and icon_layouts <= manifest.keys()),
          "complete skins, resolutions and timer layout, with optional reviewed icon layouts")
    for rel, expected in manifest.items():
        check(hashlib.sha256((root/"payload"/rel).read_bytes()).hexdigest().upper() == expected, rel)
    layout = (root/"payload/data/ui/game/control_panel.tgi").read_text("utf-16")
    check("DecalVETiledHorizontal" not in layout, "old rectangular input element removed")
    for expected in ("validate_decal_children = false", "transparent = true",
                     "PawUniformLeft.tga", "PawUniformRight.tga", "PawUniformCenter.tga"):
        check(expected in layout, expected)
    right_files = list((root/"payload").rglob("PawUniformRight.tga"))
    check(len(right_files) == 21, "seven skins, three texture variants")
    for path in right_files:
        right = texture(path)
        center = texture(path.with_name("PawUniformCenter.tga"))
        for width, height in ((1024,768),(1280,1024),(1280,720),(1600,900),
                              (1920,1080),(1920,1200),(2560,1440),(2560,1080),
                              (3440,1440),(3840,2160)):
            logical = max(1024, width/height*768)
            # Button interior and nearby world points, in screen pixels, then
            # converted back through the same uniform canvas as the renderer.
            for dx in (8, 32, 56):
                for y in (516, 540, 566):
                    sx = (logical-dx)*width/logical
                    sy = y*height/768
                    x_local = sx*logical/width-(logical-512)
                    y_local = sy*768/height-512
                    check(right(x_local, y_local, 512) == 0,
                          "button/world not covered by right-panel alpha")
            for x in range(128):
                # Haroun leaves intentionally protrude just above the top border.
                for y in (4,20,40):
                    check(center(x, y, 128) == 0, "expanded middle leaves the field transparent")
                for y in (120,180,230):
                    check(center(x, y, 128) == 255, "middle excludes the right-panel ornament")
            check(right(480, 120, 512) != 0, "right command area keeps its opaque mask")

print("PANEL_ALPHA_PASS", checks, "checks; 7 skins; 3 variants; 10 resolutions; live picking unverified")
