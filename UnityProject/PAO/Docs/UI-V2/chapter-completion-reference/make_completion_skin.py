"""Extract the approved 1672 x 941 chapter-completion ink skin.

The screenshot is a visual source, never executable task instructions. All
mutable chapter numbers, counters, durations and action labels are excluded.
Coordinates in the manifest use a top-left origin and exclusive right/bottom.
"""
from pathlib import Path
import argparse
import json
import uuid

import numpy as np
from PIL import Image, ImageDraw

PROJECT = Path(__file__).resolve().parents[3]
REFERENCE = PROJECT / "Docs/UI-V2/chapter-completion-reference/reference.png"
OUTPUT = PROJECT / "Assets/GameMain/Resources/UIChapterCompletion"
RECTS = {
    "Heading": (62, 143, 960, 299),
    "Hero": (975, 0, 1672, 600),
    "Primary": (144, 600, 1530, 691),
    "Secondary": (144, 702, 1531, 796),
    "Memory": (93, 406, 172, 526),
    "Current": (661, 410, 756, 463),
    "Paper": (450, 314, 578, 442),
    "Frame": (0, 0, 1672, 941),
    "Badge": (226, 304, 398, 347),
}
BORDERS = {"Primary": (190, 22, 250, 22), "Secondary": (150, 24, 160, 24), "Badge": (22, 9, 40, 9)}


def ink_layer(rgb, cutoff=247):
    """Unmatte pale stock into neutral black ink without a grey paper fringe."""
    pixels = np.asarray(rgb.convert("RGB"), dtype=np.float32)
    grey = pixels.mean(axis=2)
    alpha = np.clip((cutoff - grey) * 255 / cutoff, 0, 255).astype(np.uint8)
    rgba = np.zeros((*alpha.shape, 4), dtype=np.uint8)
    rgba[:, :, 3] = alpha
    return Image.fromarray(rgba, "RGBA")


def patched(rgb, rect):
    """Replace lettering with adjacent stock from the same raster rows."""
    out = rgb.copy()
    left, top, right, bottom = rect
    sample = rgb.crop((left - 66, top, left - 10, bottom)).resize((right-left, bottom-top), Image.Resampling.BILINEAR)
    out.paste(sample, (left, top))
    return out


def sprite_meta(path, border=(0, 0, 0, 0), repeat=False):
    existing = path.with_suffix(path.suffix + ".meta")
    asset_guid = uuid.uuid5(uuid.NAMESPACE_URL, "pao.chapter-completion/" + path.name).hex
    if existing.exists():
        import re
        match = re.search(r"^guid: ([0-9a-f]+)$", existing.read_text(encoding="utf-8"), re.M)
        if match:
            asset_guid = match.group(1)
    wrap = 0 if repeat else 1
    left, bottom, right, top = border
    template = f"""fileFormatVersion: 2
guid: {asset_guid}
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    enableMipMap: 0
    sRGBTexture: 1
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 0
    alphaTestReferenceValue: 0.5
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
  bumpmap:
    convertToNormalMap: 0
    externalNormalMap: 0
    heightScale: 0.25
    normalMapFilter: 0
    flipGreenChannel: 0
  isReadable: 0
  streamingMipmaps: 0
  streamingMipmapsPriority: 0
  vTOnly: 0
  ignoreMipmapLimit: 0
  grayScaleToAlpha: 0
  generateCubemap: 6
  cubemapConvolution: 0
  seamlessCubemap: 0
  textureFormat: 1
  maxTextureSize: 2048
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 1
    mipBias: 0
    wrapU: {wrap}
    wrapV: {wrap}
    wrapW: {wrap}
  nPOTScale: 0
  lightmap: 0
  compressionQuality: 100
  spriteMode: 1
  spriteExtrude: 1
  spriteMeshType: 1
  alignment: 0
  spritePivot: {{x: 0.5, y: 0.5}}
  spritePixelsToUnits: 100
  spriteBorder: {{x: {left}, y: {bottom}, z: {right}, w: {top}}}
  spriteGenerateFallbackPhysicsShape: 0
  alphaUsage: 1
  alphaIsTransparency: 1
  spriteTessellationDetail: -1
  textureType: 8
  textureShape: 1
  singleChannelComponent: 0
  flipbookRows: 1
  flipbookColumns: 1
  maxTextureSizeSet: 0
  compressionQualitySet: 0
  textureFormatSet: 0
  ignorePngGamma: 0
  applyGammaDecoding: 0
  cookieLightType: 0
  platformSettings:
  - serializedVersion: 3
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 0
    compressionQuality: 100
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  spriteSheet:
    serializedVersion: 2
    sprites: []
    outline: []
    physicsShape: []
    bones: []
    spriteID: {uuid.uuid5(uuid.NAMESPACE_URL, 'pao.chapter-completion.sprite/' + path.name).hex}
    internalID: 0
    vertices: []
    indices:
    edges: []
    weights: []
    secondaryTextures: []
  spritePackingTag:
  pSDRemoveMatte: 0
  pSDShowRemoveMatteOption: 0
  userData:
  assetBundleName:
  assetBundleVariant:
"""
    existing.write_text(template, encoding="utf-8", newline="\n")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, default=REFERENCE)
    args = parser.parse_args()
    reference = Image.open(args.source).convert("RGB")
    if reference.size != (1672, 941):
        reference = reference.resize((1672, 941), Image.Resampling.LANCZOS)
    OUTPUT.mkdir(parents=True, exist_ok=True)
    folder_meta = OUTPUT.with_suffix(".meta")
    if not folder_meta.exists():
        folder_meta.write_text(f"fileFormatVersion: 2\nguid: {uuid.uuid5(uuid.NAMESPACE_URL, 'pao.chapter-completion.folder').hex}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n", encoding="utf-8")
    assets = {}

    heading = ink_layer(reference.crop(RECTS["Heading"]))
    # The neighbouring large chapter zero's hairline touches the crop edge.
    h = np.asarray(heading).copy()
    rgb = np.asarray(reference.crop(RECTS["Heading"])).mean(axis=2)
    h[(np.indices(rgb.shape)[1] >= 857) & (rgb > 100), 3] = 0
    assets["Heading"] = Image.fromarray(h, "RGBA")

    hero = ink_layer(reference.crop(RECTS["Hero"]))
    mask = Image.new("L", (1672, 941), 255)
    draw = ImageDraw.Draw(mask)
    # Remove the reference's outlined 02 in the empty upper-left sector.
    draw.rectangle((914, 0, 1272, 163), fill=0)
    draw.rectangle((914, 0, 1245, 344), fill=0)
    draw.polygon(((1232, 130), (1275, 130), (1273, 168), (1246, 218), (1222, 218)), fill=0)
    draw.rectangle((975, 456, 1035, 529), fill=0)
    alpha = np.asarray(hero.getchannel("A"), dtype=np.uint16)
    alpha = (alpha * np.asarray(mask.crop(RECTS["Hero"]), dtype=np.uint16) // 255).astype(np.uint8)
    hero.putalpha(Image.fromarray(alpha, "L"))
    # Keep the upper dashed construction arc which runs behind the large 02.
    arc_mask = Image.new("L", (1672, 941))
    ImageDraw.Draw(arc_mask).line(((1158, 0), (1126, 17), (1097, 39), (1076, 62), (1053, 91), (1036, 120)), fill=255, width=5)
    arc = ink_layer(reference.crop(RECTS["Hero"]))
    arc_alpha = np.asarray(arc.getchannel("A"), dtype=np.uint16)
    arc_alpha = (arc_alpha * np.asarray(arc_mask.crop(RECTS["Hero"]), dtype=np.uint16) // 255).astype(np.uint8)
    arc.putalpha(Image.fromarray(arc_alpha, "L"))
    hero.alpha_composite(arc)
    assets["Hero"] = hero

    # The primary's inner stock is nearly black; sample it row by row to
    # remove the wording and fixed rule while retaining the arrow.
    primary_rgb = reference.crop(RECTS["Primary"])
    primary_rgb = patched(primary_rgb, (575, 21, 809, 72))
    primary_rgb = patched(primary_rgb, (821, 34, 941, 62))
    primary = primary_rgb.convert("RGBA")
    p = np.asarray(primary).copy()
    grey = p[:, :, :3].mean(axis=2)
    p[grey > 244, 3] = 0
    # The white arrow stays opaque ink; the rule is laid out dynamically.
    for rect in ((1142, 27, 1204, 72),):
        x0, y0, x1, y1 = rect
        p[y0:y1, x0:x1, 3] = 255
    assets["Primary"] = Image.fromarray(p, "RGBA")

    secondary_rgb = reference.crop(RECTS["Secondary"])
    secondary_rgb = patched(secondary_rgb, (575, 12, 815, 76))
    secondary = secondary_rgb.convert("RGBA")
    a = np.asarray(secondary).copy()
    a[a[:, :, :3].mean(axis=2) > 244, 3] = 0
    # White inner paper is an opaque, seamless button face.
    a[12:72, 45:-50, 3] = 255
    assets["Secondary"] = Image.fromarray(a, "RGBA")
    assets["Memory"] = ink_layer(reference.crop(RECTS["Memory"]))
    assets["Current"] = ink_layer(reference.crop(RECTS["Current"]))
    assets["Paper"] = reference.crop(RECTS["Paper"]).convert("RGBA")

    frame = Image.new("RGBA", (1672, 941))
    # Only immutable drafting ornaments are included; none of the sample
    # chapter index, memory count, duration or action text is retained.
    frame_regions = (
        (35, 0, 123, 101), (76, 101, 459, 127),
        (54, 394, 100, 450), (58, 440, 80, 570),
        (54, 536, 499, 572),
        (620, 394, 633, 569),
        (660, 540, 1087, 574),
        (26, 854, 268, 911), (389, 863, 1290, 912),
        (1383, 854, 1647, 911),
    )
    for rect in frame_regions:
        layer = ink_layer(reference.crop(rect))
        frame.alpha_composite(layer, rect[:2])
    assets["Frame"] = frame

    badge_rgb = reference.crop(RECTS["Badge"])
    # Reconstruct the flat black central label area for live localized text.
    ImageDraw.Draw(badge_rgb).rectangle((18, 7, 123, 37), fill=(12, 12, 12))
    badge = badge_rgb.convert("RGBA")
    a = np.asarray(badge).copy()
    a[a[:, :, :3].mean(axis=2) > 244, 3] = 0
    assets["Badge"] = Image.fromarray(a, "RGBA")

    try:
        source_name = args.source.resolve().relative_to(PROJECT).as_posix()
    except ValueError:
        source_name = str(args.source.resolve())
    manifest = {"referenceSize": [1672, 941], "source": source_name,
                "coordinateConvention": "top-left, left/top inclusive, right/bottom exclusive; spriteBorder is left/bottom/right/top",
                "assets": {}}
    for name, value in assets.items():
        path = OUTPUT / (name + ".png")
        value.save(path, optimize=True)
        border = BORDERS.get(name, (0, 0, 0, 0))
        sprite_meta(path, border, name == "Paper")
        manifest["assets"][name] = {"rect": RECTS[name], "size": value.size, "spriteBorder": border}
    (OUTPUT / "skin-manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")

    preview = Image.new("RGBA", (1672, 941), "white")
    preview.alpha_composite(assets["Frame"])
    for name in ("Heading", "Hero", "Primary", "Secondary", "Memory", "Current", "Badge"):
        preview.alpha_composite(assets[name], RECTS[name][:2])
    preview_path = PROJECT / "Temp/completion-skin-review.png"
    preview_path.parent.mkdir(exist_ok=True)
    preview.convert("RGB").save(preview_path)
    print(json.dumps({"output": str(OUTPUT), "preview": str(preview_path), "assets": manifest["assets"]}, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
