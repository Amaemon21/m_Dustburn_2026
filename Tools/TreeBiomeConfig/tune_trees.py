from pathlib import Path
import re


ROOT = Path(__file__).resolve().parents[2]
TREE_PREFABS = ROOT / "Assets/Assets/Synty/PolygonNature/Prefabs/Trees"
BIOMES = ROOT / "Assets/_Dustborn/Content/World/Biomes"


SPECS = {
    "PineForest": [
        ("SM_Tree_Pine_01", 32, 7, .80, 1.42, 2.24075, .36, 4),
        ("SM_Tree_Pine_02", 26, 7, .85, 1.50, 2.13442, .38, 4),
        ("SM_Tree_Pine_Large_01", 8, 13, 1.05, 1.90, 1.71064, .44, 6),
        ("SM_Tree_Pine_Large_02", 5, 16, 1.10, 1.95, 2.30, .48, 6),
        ("SM_Tree_Pine_Small_01", 19, 6, .70, 1.20, 1.43592, .32, 3),
        ("SM_Tree_Birch_01", 10, 9, .80, 1.55, 1.70073, .52, 4),
        ("SM_Tree_Round_02", 7, 10, .90, 1.50, 2.56609, .55, 5),
        ("SM_Tree_Generic_Giant_01", .25, 35, .90, 1.25, 5.50, .58, 9),
        ("SM_Tree_Stump_01", 3.5, 16, .80, 1.25, .59702, .50, 3),
    ],
    "BurntForest": [
        ("SM_Tree_Dead_01", 15, 8, .85, 1.65, 1.05606, .38, 4),
        ("SM_Tree_Dead_02", 12, 9, .80, 1.75, .88647, .40, 4),
        ("SM_Tree_Dead_03", 9, 10, .80, 1.60, .66857, .45, 3),
        ("SM_Tree_Pine_Dead_01", 10, 8, .85, 1.60, .79088, .40, 4),
        ("SM_Tree_Birch_Dead_01", 6, 12, .90, 1.50, 1.58566, .48, 4),
        ("SM_Tree_Generic_Dead_01", .5, 20, 1.10, 2.00, 3.00, .58, 6),
        ("SM_Tree_Stump_02", 5, 15, .75, 1.35, 1.67159, .50, 3),
        ("SM_Tree_Log_01", 2, 23, .80, 1.25, 1.56515, .50, 3),
    ],
    "Desert": [
        ("SM_Tree_Dead_01", 4, 14, .75, 1.85, 1.05606, .55, 5),
        ("SM_Tree_Dead_02", 2.5, 18, .70, 1.65, .88647, .58, 4),
        ("SM_Tree_Birch_Dead_01", 1.2, 25, .80, 1.50, 1.58566, .62, 4),
        ("SM_Tree_Stump_02", 1.5, 24, .70, 1.30, 1.67159, .55, 3),
    ],
    "Snow": [
        ("SM_Tree_Pine_01", 13, 8, .80, 1.45, 2.24075, .35, 5),
        ("SM_Tree_Pine_02", 9, 9, .80, 1.50, 2.13442, .38, 5),
        ("SM_Tree_Pine_Large_01", 6, 15, 1.00, 1.85, 1.71064, .42, 7),
        ("SM_Tree_Pine_Large_02", 4, 18, 1.10, 1.90, 2.30, .48, 7),
        ("SM_Tree_Pine_Small_01", 7, 10, .70, 1.15, 1.43592, .40, 4),
        ("SM_Tree_Birch_Dead_01", 3, 20, .80, 1.40, 1.58566, .55, 4),
        ("SM_Tree_Stump_01", 2, 25, .70, 1.20, .59702, .55, 3),
    ],
    "Wasteland": [
        ("SM_Tree_Dead_01", 6, 11, .90, 1.50, 1.05606, .48, 4),
        ("SM_Tree_Dead_02", 4, 13, .85, 1.60, .88647, .50, 4),
        ("SM_Tree_Dead_03", 2.5, 16, .85, 1.50, .66857, .55, 3),
        ("SM_Tree_Pine_Dead_01", 2.5, 17, .90, 1.60, .79088, .56, 4),
        ("SM_Tree_Generic_Dead_01", .3, 28, 1.10, 1.90, 3.00, .60, 6),
        ("SM_Tree_Stump_02", 2, 22, .80, 1.35, 1.67159, .58, 3),
        ("SM_Tree_Log_01", 1.2, 25, .80, 1.30, 1.56515, .58, 3),
    ],
}


def root_file_id(prefab):
    contents = prefab.read_text(encoding="utf-8")
    match = re.search(r"m_RootGameObject: \{fileID: (\d+)\}", contents)
    if match:
        return match.group(1)
    for section in re.split(r"(?=^--- !u!)", contents, flags=re.M):
        if not section.startswith("--- !u!4 ") or "m_Father: {fileID: 0}" not in section:
            continue
        match = re.search(r"m_GameObject: \{fileID: (\d+)\}", section)
        if match:
            return match.group(1)
    raise ValueError(prefab)


def prefab_reference(name):
    prefab = TREE_PREFABS / (name + ".prefab")
    meta = Path(str(prefab) + ".meta")
    guid = re.search(r"guid: ([0-9a-f]+)", meta.read_text(encoding="utf-8")).group(1)
    return "{fileID: %s, guid: %s, type: 3}" % (root_file_id(prefab), guid)


def set_field(block, name, value):
    return re.sub(rf"(<{name}>k__BackingField: ).*", lambda match: match.group(1) + str(value), block)


def make_layer(template, biome, spec):
    name, per_hectare, spacing, min_scale, max_scale, footprint, patch_threshold, road_clearance = spec
    block = template
    values = {
        "Prefab": prefab_reference(name),
        "PerHectare": per_hectare,
        "Spacing": spacing,
        "PatchFrequency": 240 if "Giant" in name or "Large" in name else 180,
        "PatchThreshold": patch_threshold,
        "MaxSlope": 30 if biome == "Desert" else 34,
        "MinHeight": 0,
        "MaxHeight": 1,
        "MinScale": min_scale,
        "MaxScale": max_scale,
        "Squash": .10,
        "Tint": "{r: 1, g: 1, b: 1, a: 1}",
        "TintVariance": .06,
        "BendFactor": 0,
        "Footprint": footprint,
        "RoadClearance": road_clearance,
    }
    for field, value in values.items():
        block = set_field(block, field, value)
    return block


def main():
    for biome, specs in SPECS.items():
        asset = BIOMES / ("Biome_" + biome + ".asset")
        contents = asset.read_text(encoding="utf-8")
        start = contents.index("  <Trees>k__BackingField:\n")
        end = contents.index("  <Rocks>k__BackingField:", start)
        blocks = re.findall(r"  - <Prefab>k__BackingField:.*?(?=  - <Prefab>k__BackingField:|\Z)", contents[start:end], re.S)
        if not blocks:
            raise ValueError(asset)
        trees = "  <Trees>k__BackingField:\n" + "".join(make_layer(blocks[0], biome, spec) for spec in specs)
        asset.write_text(contents[:start] + trees + contents[end:], encoding="utf-8")
        print(biome, len(specs), "layers", round(sum(spec[1] for spec in specs), 1), "trees per hectare before filtering")


if __name__ == "__main__":
    main()
