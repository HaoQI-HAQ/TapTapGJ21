"""Derive UI skins from the project's existing watercolor paper, without image generation."""
from pathlib import Path
import math
import random
import uuid
from PIL import Image, ImageDraw, ImageFilter, ImageOps

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Assets/GameMain/Resources/UIHandDrawn'
OUT.mkdir(parents=True, exist_ok=True)
SOURCE = ROOT / 'Assets/GameMain/UI/Sprites/menu_jellyfish_paper.png'
paper = Image.open(SOURCE).convert('RGB').crop((160, 32, 672, 256)).resize((512, 512), Image.Resampling.LANCZOS)
gray = ImageOps.grayscale(paper)
# Use blank paper only. Autocontrast would turn faint illustration strokes into dark scratches.
gray = gray.point(lambda value: max(236, min(250, 245 + round((value - 245) * .35))))
grain = Image.merge('RGB', (gray, gray, gray))
randomizer = random.Random(21013)
noise = Image.frombytes('L', (512, 512), bytes(randomizer.randrange(229, 256) for _ in range(512 * 512)))
grain = Image.blend(grain, Image.merge('RGB', (noise, noise, noise)), .16).convert('RGBA')

def save(name, image, border=0, repeat=False, pixels_per_unit=100):
    path = OUT / (name + '.png')
    image.save(path)
    template = (ROOT / 'Assets/GameMain/UI/Sprites/btn_pearl_round.png.meta').read_text(encoding='utf-8-sig')
    import re
    guid = uuid.uuid5(uuid.NAMESPACE_URL, path.relative_to(ROOT).as_posix()).hex
    template = re.sub(r'^guid: .*', 'guid: ' + guid, template, flags=re.M)
    template = re.sub(r'spriteBorder: \{.*?\}', f'spriteBorder: {{x: {border}, y: {border}, z: {border}, w: {border}}}', template)
    template = re.sub(r'spritePixelsToUnits: .*', f'spritePixelsToUnits: {pixels_per_unit}', template)
    template = template.replace('textureCompression: 1', 'textureCompression: 0')
    if repeat: template = template.replace('wrapU: 1', 'wrapU: 0').replace('wrapV: 1', 'wrapV: 0')
    Path(str(path) + '.meta').write_text(template, encoding='utf-8')

save('paper-grain-v1', grain, repeat=True)
size = 512
mask = Image.new('L', (size, size)); draw = ImageDraw.Draw(mask)
# A continuous, lightly uneven painted contour keeps nine-slice edges quiet.
outline = []
for center, start in [((50, 50), 180), ((462, 50), 270), ((462, 462), 0), ((50, 462), 90)]:
    for degree in range(start, start + 91):
        angle = degree * math.pi / 180
        radius = 36 + math.sin(angle * 7) * .9
        outline.append((center[0] + math.cos(angle) * radius, center[1] + math.sin(angle) * radius))
draw.polygon(outline, fill=255)
label = grain.copy(); label.putalpha(mask.filter(ImageFilter.GaussianBlur(.65)))
# 52 texture pixels become 13 UI units, so keycaps and slider handles retain a usable center.
save('button-paper-v1', label, border=52, pixels_per_unit=400)

circle = Image.new('L', (size, size)); draw = ImageDraw.Draw(circle)
outline = []
for i in range(361):
    angle = i * math.pi / 180
    radius = 237 + math.sin(angle * 7) * 2.1 + math.sin(angle * 13 + .6) * 1.4
    outline.append((256 + math.cos(angle) * radius, 256 + math.sin(angle) * radius))
draw.polygon(outline, fill=255)
bubble = grain.copy(); bubble.putalpha(circle.filter(ImageFilter.GaussianBlur(.6)))
save('bubble-paper-v1', bubble)

ring = Image.new('RGBA', (size, size)); draw = ImageDraw.Draw(ring)
for line in range(2):
    points = []
    for i in range(361):
        angle = i * math.pi / 180
        radius = 235 - line * 8 + math.sin(angle * 7 + line) * 2 + math.cos(angle * 11) * .8
        points.append((256 + math.cos(angle) * radius, 256 + math.sin(angle) * radius))
    draw.line(points, fill=(255, 255, 255, 235 if line == 0 else 65), width=6 if line == 0 else 2)
save('selection-brush-v1', ring)
folder_guid = uuid.uuid5(uuid.NAMESPACE_URL, 'Assets/GameMain/Resources/UIHandDrawn').hex
Path(str(OUT) + '.meta').write_text('fileFormatVersion: 2\nguid: ' + folder_guid + '\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n', encoding='utf-8')
print('Saved 4 paper-derived Unity skins:', OUT)
