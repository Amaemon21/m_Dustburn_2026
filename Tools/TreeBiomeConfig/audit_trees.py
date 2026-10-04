from pathlib import Path
import re
from tune_trees import BIOMES, SPECS, TREE_PREFABS, prefab_reference


def field(block, name):
    return re.search(rf"<{name}>k__BackingField: (.+)", block).group(1)


def main():
    for biome, specs in SPECS.items():
        asset = BIOMES / ("Biome_" + biome + ".asset")
        contents = asset.read_text(encoding="utf-8")
        trees = contents.split("  <Trees>k__BackingField:")[1].split("  <Rocks>k__BackingField:")[0]
        blocks = re.findall(r"  - <Prefab>k__BackingField:.*?(?=  - <Prefab>k__BackingField:|\Z)", trees, re.S)
        assert len(blocks) == len(specs)
        assert max(float(field(block, "MaxScale")) for block in blocks) >= 1.75
        for block, spec in zip(blocks, specs):
            name, density, spacing, min_scale, max_scale, footprint, _, _ = spec
            assert (TREE_PREFABS / (name + ".prefab")).is_file()
            assert field(block, "Prefab") == prefab_reference(name)
            assert float(field(block, "PerHectare")) == density
            assert float(field(block, "Spacing")) == spacing
            assert float(field(block, "MinScale")) == min_scale
            assert float(field(block, "MaxScale")) == max_scale
            assert float(field(block, "Footprint")) == footprint
            assert 0 < min_scale <= max_scale
        print(biome, len(blocks), "PolygonNature trees", sum(spec[1] for spec in specs), "per hectare", "largest scale", max(spec[4] for spec in specs))


if __name__ == "__main__":
    main()
