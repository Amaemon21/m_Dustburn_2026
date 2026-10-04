from pathlib import Path
from PIL import Image, ImageDraw
import numpy as np
import os
import re
import tempfile


ROOT = Path(__file__).resolve().parents[2]
TEXTURES = ROOT / "Assets/_Dustborn/Content/Texture/BiomeGrass2D_Textures"
BIOMES = ROOT / "Assets/_Dustborn/Content/World/Biomes"
OUTPUT = Path(os.environ.get("DUSTBORN_VEGETATION_REVIEW", tempfile.gettempdir())) / "dustborn-vegetation-review"


def field(block, name):
    match = re.search(rf"<{name}>k__BackingField: (.+)", block)
    return match.group(1) if match else None


def color(block, name):
    value = field(block, name)
    return np.array([float(part) for part in re.findall(r"[rgb]: ([0-9.]+)", value)], dtype=np.float32)


def preview(card, mask, block):
    base = np.asarray(Image.open(card).convert("RGBA"), dtype=np.float32) / 255
    weights = np.asarray(Image.open(mask).convert("RGB"), dtype=np.float32) / 255
    weights /= np.maximum(weights.sum(axis=2, keepdims=True), .001)
    palette = np.stack([color(block, "LeavesColor"), color(block, "StemsColor"), color(block, "FlowersColor")])
    tint = weights @ palette
    snow = float(field(block, "SnowAmount"))
    y = np.linspace(0, 1, len(base), dtype=np.float32)[:, None, None]
    snow_weight = np.clip((1-y)/.35, 0, 1) * snow
    tint = tint * (1-snow_weight) + np.array([.82, .87, .90], dtype=np.float32) * snow_weight
    shading = float(field(block, "TextureShading"))
    rgb = np.clip(tint * (1-shading+base[:, :, :3]*shading), 0, 1)
    result = np.concatenate([rgb, base[:, :, 3:]], axis=2)
    return Image.fromarray((result*255).astype(np.uint8), "RGBA")


def main():
    OUTPUT.mkdir(exist_ok=True)
    lookup = {}
    for meta in TEXTURES.rglob("*.png.meta"):
        match = re.search(r"guid: ([0-9a-f]+)", meta.read_text(encoding="utf-8"))
        lookup[match.group(1)] = Path(str(meta)[:-5])
    imported = set()
    layer_count = 0
    shore_count = 0
    for asset in BIOMES.glob("*.asset"):
        section = asset.read_text(encoding="utf-8").split("  <Grass>k__BackingField:")[1].split("  <Trees>k__BackingField:")[0]
        blocks = re.findall(r"  - <Card>k__BackingField:.*?(?=  - <Card>k__BackingField:|\Z)", section, re.S)
        sheet = Image.new("RGB", (6*240, ((len(blocks)+5)//6)*270), (78, 78, 78))
        draw = ImageDraw.Draw(sheet)
        total = 0
        for index, block in enumerate(blocks):
            card = lookup[re.search(r"guid: ([0-9a-f]+)", field(block, "Card")).group(1)]
            mask = lookup[re.search(r"guid: ([0-9a-f]+)", field(block, "Mask")).group(1)]
            assert Image.open(card).size == Image.open(mask).size
            if card.parent.name == "ImportedVegetation":
                imported.add(card.stem)
                pixels = np.asarray(Image.open(mask).convert("RGBA"))
                assert not np.any(pixels[:, :, :3][pixels[:, :, 3] == 0])
                assert np.any(pixels[:, :, :3][pixels[:, :, 3] > 128])
            if field(block, "ShoreDistance"):
                shore_count += 1
            density = float(field(block, "Density"))
            assert 0 < density < 1
            layer_count += 1
            total += density
            image = preview(card, mask, block)
            image.thumbnail((230, 225))
            x, y = (index % 6)*240, (index // 6)*270
            sheet.paste(image, (x+(240-image.width)//2, y), image)
            draw.text((x+4, y+229), card.stem.replace("_White", "")[:32], fill="white")
            draw.text((x+4, y+245), f"{density:.4f}/m2 shore={field(block, 'ShoreDistance') or '0'}", fill="white")
            print(asset.stem, card.stem, density, field(block, "ShoreDistance") or "0", color(block, "LeavesColor"), color(block, "FlowersColor"))
        sheet.save(OUTPUT / (asset.stem + ".png"))
        print(asset.stem, "total", round(total, 5))
    assert layer_count == 67
    assert len(imported) == 20
    assert shore_count == 2
    print("Verified", layer_count, "layers,", len(imported), "new species,", shore_count, "shore-only layers")


if __name__ == "__main__":
    main()
