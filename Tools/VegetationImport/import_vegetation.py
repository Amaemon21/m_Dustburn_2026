from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter
import numpy as np
import re
import shutil
import uuid


ROOT = Path(__file__).resolve().parents[2]
TEXTURES = ROOT / "Assets/_Dustborn/Content/Texture/BiomeGrass2D_Textures"
BIOMES = ROOT / "Assets/_Dustborn/Content/World/Biomes"
SOURCES = {
    "Vegetation": Path("D:/vegetation_large_10_1024"),
    "Bush": Path("D:/normal_bushes_pack"),
}
PALETTE = {
    "PineForest": ((0.38, 0.48, 0.25), (0.30, 0.36, 0.22), (0.82, 0.72, 0.42)),
    "BurntForest": ((0.31, 0.28, 0.23), (0.21, 0.19, 0.18), (0.57, 0.48, 0.35)),
    "Desert": ((0.66, 0.58, 0.41), (0.47, 0.39, 0.28), (0.88, 0.72, 0.43)),
    "Snow": ((0.48, 0.53, 0.51), (0.36, 0.39, 0.38), (0.79, 0.79, 0.75)),
    "Wasteland": ((0.42, 0.36, 0.29), (0.29, 0.25, 0.22), (0.65, 0.55, 0.39)),
}
LAYERS = {
    "PineForest": [("Vegetation_01_Broadleaf", .085), ("Vegetation_03_Flowering", .055), ("Vegetation_04_Clover", .090), ("Vegetation_05_Cattail", .035), ("Vegetation_06_Fern", .075), ("Vegetation_07_Wildflowers", .045), ("Vegetation_08_Berries", .035), ("Vegetation_09_SeedSpikes", .055), ("Vegetation_10_Lilies", .025), ("Bush_01_DenseRound", .045), ("Bush_02_LeafyCompact", .045), ("Bush_03_TallWild", .035), ("Bush_04_SparseBranch", .025), ("Bush_05_Flowering", .030), ("Bush_06_BerryBush", .025), ("Bush_10_WideBush", .040)],
    "BurntForest": [("Vegetation_02_Thorny", .035), ("Vegetation_09_SeedSpikes", .025), ("Bush_04_SparseBranch", .025), ("Bush_07_ThornBush", .030), ("Bush_09_DryBush", .035)],
    "Desert": [("Vegetation_02_Thorny", .014), ("Vegetation_09_SeedSpikes", .012), ("Bush_07_ThornBush", .012), ("Bush_08_DesertBush", .018), ("Bush_09_DryBush", .016)],
    "Snow": [("Vegetation_01_Broadleaf", .028), ("Vegetation_06_Fern", .025), ("Vegetation_09_SeedSpikes", .022), ("Bush_02_LeafyCompact", .020), ("Bush_03_TallWild", .015), ("Bush_04_SparseBranch", .015)],
    "Wasteland": [("Vegetation_02_Thorny", .012), ("Vegetation_09_SeedSpikes", .010), ("Bush_04_SparseBranch", .010), ("Bush_07_ThornBush", .012), ("Bush_09_DryBush", .016)],
}
OLD_SCALE = {"PineForest": .75, "BurntForest": 1.3, "Desert": 1.5, "Snow": 2.5, "Wasteland": .75}
FLOWERS = {
    "Vegetation_03_Flowering": [(.51, .30, .27, .23), (.31, .47, .26, .22), (.78, .49, .26, .23), (.56, .61, .25, .21)],
    "Vegetation_05_Cattail": [(0.24, .33, .09, .30), (.48, .22, .09, .30), (.72, .32, .09, .30)],
    "Vegetation_07_Wildflowers": [(.54, .20, .29, .25), (.22, .41, .27, .24), (.82, .52, .27, .24), (.59, .67, .24, .20)],
    "Vegetation_08_Berries": [(.32, .29, .16, .17), (.86, .47, .15, .18), (.15, .72, .15, .16), (.62, .74, .16, .17)],
    "Vegetation_09_SeedSpikes": [(0.23, .37, .12, .30), (.50, .24, .12, .35), (.74, .37, .12, .30)],
    "Vegetation_10_Lilies": [(.57, .23, .33, .30), (.25, .50, .34, .25), (.82, .53, .32, .28)],
    "Bush_05_Flowering": [(.37, .35, .14, .12), (.75, .47, .14, .12), (.30, .58, .14, .13), (.59, .77, .14, .13)],
    "Bush_06_BerryBush": [(.25, .55, .14, .13), (.58, .46, .14, .14), (.77, .61, .14, .14), (.43, .75, .13, .13)],
}
SHORE = {"Vegetation_05_Cattail": 7, "Vegetation_10_Lilies": 5}
FLOWER_COLORS = {
    "Vegetation_07_Wildflowers": (.68, .52, .70),
    "Vegetation_08_Berries": (.72, .19, .21),
    "Vegetation_10_Lilies": (.87, .82, .71),
    "Bush_05_Flowering": (.79, .55, .62),
    "Bush_06_BerryBush": (.72, .19, .21),
}
DRY_SEED_COLORS = {
    "BurntForest": (.38, .34, .28),
    "Desert": (.70, .61, .47),
    "Wasteland": (.49, .43, .35),
}
OLD_FLOWER_COLORS = {
    "BurntForest": ((.46, .41, .34), (.48, .43, .36)),
    "Wasteland": ((.57, .50, .40), (.57, .50, .40)),
}


def guid(name):
    return uuid.uuid5(uuid.NAMESPACE_URL, "dustborn-vegetation/" + name).hex


def mask(source, name):
    image = Image.open(source).convert("RGBA")
    width, height = image.size
    alpha = image.getchannel("A")
    stem = Image.new("L", image.size)
    draw = ImageDraw.Draw(stem)
    if name.startswith("Vegetation_05") or name.startswith("Vegetation_09"):
        paths = [(.24, .32), (.50, .23), (.75, .32)]
    elif name.startswith(("Bush_04", "Bush_07", "Bush_08", "Bush_09")):
        paths = [(.30, .48), (.50, .30), (.70, .48)]
    elif name.startswith("Bush"):
        paths = [(.32, .65), (.50, .56), (.68, .65)]
    else:
        paths = [(.33, .63), (.50, .48), (.67, .63)]
    for x, y in paths:
        draw.line([(width * .50, height * .91), (width * x, height * y)], fill=255, width=max(6, width // 75))
    stem = stem.filter(ImageFilter.GaussianBlur(max(2, width // 260)))
    bloom = Image.new("L", image.size)
    draw = ImageDraw.Draw(bloom)
    for x, y, rx, ry in FLOWERS.get(name, []):
        draw.ellipse((width * (x-rx/2), height * (y-ry/2), width * (x+rx/2), height * (y+ry/2)), fill=255)
    bloom = bloom.filter(ImageFilter.GaussianBlur(max(2, width // 250)))
    pixels = np.asarray(image, dtype=np.float32)
    coverage = pixels[:, :, 3] / 255
    luminance = pixels[:, :, :3].mean(axis=2)
    height_weight = np.linspace(.4, 1, height, dtype=np.float32)[:, None]
    dark_stems = np.clip((180-luminance)/65, 0, .8) * height_weight
    stem_weight = np.maximum(np.asarray(stem, dtype=np.float32)/300, dark_stems)
    bloom_weight = np.asarray(bloom, dtype=np.float32)/255
    stem_weight *= 1-bloom_weight
    channels = [
        coverage * (1-stem_weight-bloom_weight),
        coverage * stem_weight,
        coverage * bloom_weight,
    ]
    rgb = [Image.fromarray(np.uint8(np.clip(channel*255, 0, 255))) for channel in channels]
    return Image.merge("RGBA", (*rgb, alpha))


def replace_field(block, field, value):
    pattern = rf"(<{field}>k__BackingField: ).*"
    return re.sub(pattern, lambda m: m.group(1) + str(value), block)


def color(values):
    return "{r: %.6f, g: %.6f, b: %.6f, a: 1}" % values


def make_block(template, name, density, biome):
    block = template
    for field, suffix in (("Card", "White"), ("Mask", "Mask")):
        block = replace_field(block, field, "{fileID: 2800000, guid: %s, type: 3}" % guid(name + "_" + suffix))
    block = replace_field(block, "Density", density)
    block = replace_field(block, "PatchFrequency", 35 if density >= .04 else 55)
    block = replace_field(block, "PatchThreshold", .24 if density >= .04 else .32)
    block = replace_field(block, "MaxSlope", 28 if name in SHORE else 32)
    block = replace_field(block, "MinWidth", .7 if name.startswith("Vegetation") else 1.0)
    block = replace_field(block, "MaxWidth", 1.15 if name.startswith("Vegetation") else 1.65)
    block = replace_field(block, "MinHeight", .55 if name.startswith("Vegetation") else .75)
    block = replace_field(block, "MaxHeight", 1.15 if name.startswith("Vegetation") else 1.45)
    for field, value in zip(("LeavesColor", "StemsColor", "FlowersColor"), PALETTE[biome]):
        if field == "FlowersColor" and biome == "PineForest":
            value = FLOWER_COLORS.get(name, value)
        if field == "FlowersColor" and name == "Vegetation_09_SeedSpikes":
            value = DRY_SEED_COLORS.get(biome, value)
        block = replace_field(block, field, color(value))
    block = replace_field(block, "SnowAmount", .55 if biome == "Snow" else 0)
    block = replace_field(block, "RootDarkening", .32 if biome in ("BurntForest", "Wasteland") else .18)
    block = replace_field(block, "Translucency", .06 if biome in ("BurntForest", "Wasteland") else .18)
    block = replace_field(block, "ColorVariation", .1)
    if name in SHORE:
        block = block.replace("    <MaxSlope>k__BackingField:", "    <ShoreDistance>k__BackingField: %s\n    <MaxSlope>k__BackingField:" % SHORE[name])
    return block


def main():
    target = TEXTURES / "ImportedVegetation"
    target.mkdir(exist_ok=True)
    (target.with_name(target.name + ".meta")).write_text("fileFormatVersion: 2\nguid: %s\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n" % guid("ImportedVegetation"), encoding="utf-8")
    white_meta = (TEXTURES / "PineForest/T_PineForest_Bush_01_White.png.meta").read_text(encoding="utf-8")
    mask_meta = (TEXTURES / "PineForest/T_PineForest_Bush_01_Mask.png.meta").read_text(encoding="utf-8")
    for folder in SOURCES.values():
        for source in sorted(folder.glob("*.png")):
            name = source.stem
            white = target / (name + "_White.png")
            colored_mask = target / (name + "_Mask.png")
            shutil.copyfile(source, white)
            mask(source, name).save(colored_mask)
            for path, template in ((white, white_meta), (colored_mask, mask_meta)):
                meta = re.sub(r"guid: [0-9a-f]+", "guid: " + guid(path.stem), template, count=1)
                Path(str(path) + ".meta").write_text(meta, encoding="utf-8")
    for biome, layers in LAYERS.items():
        asset = BIOMES / ("Biome_" + biome + ".asset")
        text = asset.read_text(encoding="utf-8")
        start = text.index("  <Grass>k__BackingField:\n")
        end = text.index("  <Trees>k__BackingField:", start)
        grass = text[start:end]
        blocks = re.findall(r"  - <Card>k__BackingField:.*?(?=  - <Card>k__BackingField:|\Z)", grass, re.S)
        adjusted = []
        imported_guids = {guid(name + "_White") for name, _ in layers}
        already_imported = any(any(item in block for item in imported_guids) for block in blocks)
        for index, block in enumerate(blocks):
            if any(item in block for item in imported_guids):
                continue
            match = re.search(r"<Density>k__BackingField: ([0-9.]+)", block)
            old = block if already_imported else replace_field(block, "Density", round(float(match.group(1)) * OLD_SCALE[biome], 6))
            if biome in OLD_FLOWER_COLORS and index >= 4:
                old = replace_field(old, "FlowersColor", color(OLD_FLOWER_COLORS[biome][index-4]))
            adjusted.append(old)
        template = blocks[0]
        extra = [make_block(template, name, density, biome) for name, density in layers]
        asset.write_text(text[:start] + "  <Grass>k__BackingField:\n" + "".join(adjusted + extra) + text[end:], encoding="utf-8")


if __name__ == "__main__":
    main()
