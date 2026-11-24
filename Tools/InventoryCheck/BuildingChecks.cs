using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

internal static partial class Harness
{
    private static async Task VerifyBuildingPersistence()
    {
        SaveRegistry registry = new SaveRegistry().Register<BlocksData>(SaveInstaller.BLOCKS_KEY, SaveScope.Slot, SaveInstaller.WORLD_FILE);
        MemoryStorage storage = new();
        SaveService saves = new(registry, storage, ProjectSerializer());
        await saves.LoadScopeAsync(SaveScope.Slot, CancellationToken.None);
        BlockRepository repository = new(saves);
        Check(repository.Origin.BuildOriginY == null && repository.Origin.Placed.Count == 0,
            "existing saves start without constructed blocks or a grid height");

        Vector3Int cell = new(-3, 2, -7);
        PlacedBlockData placed = new() { Id = "built/test", ItemId = "block_osb", X = cell.x, Y = cell.y, Z = cell.z, Rotation = 3 };
        repository.AddPlaced(placed, 123.25f);
        repository.SetDamage(placed.Id, 40);
        Check(repository.IsOccupied(cell) && !repository.IsOccupied(new Vector3Int(0, 2, -7)),
            "constructed blocks reserve their exact cell including negative coordinates");
        Expect<InvalidOperationException>(() => repository.AddPlaced(new PlacedBlockData
            { Id = "built/duplicate", ItemId = "block_osb", X = cell.x, Y = cell.y, Z = cell.z }, 999f),
            "duplicate occupancy is rejected before changing saved instances");
        Expect<InvalidOperationException>(() => repository.AddPlaced(new PlacedBlockData
            { Id = "built/overlap", ItemId = "block_osb_wall", X = cell.x, Y = cell.y - 2, Z = cell.z, Height = 3 }, 999f),
            "a wall is rejected when any upper cell is already occupied");
        Check(repository.Origin.Placed.Count == 1 && repository.Origin.BuildOriginY == 123.25f,
            "failed placement leaves the saved instance collection and grid unchanged");
        PlacedBlockData doorway = new() { Id = "built/doorway", ItemId = "block_osb_doorway", X = cell.x + 1,
            Y = cell.y, Z = cell.z, Rotation = 1, Height = 3, Grounded = true };
        repository.AddPlaced(doorway, 123.25f);
        Vector3Int top = doorway.GetCell(2);
        Check(repository.TryGetPlaced(top, out PlacedBlockData topOwner) && topOwner == doorway &&
            repository.IsOccupied(doorway.GetCell(1)) && !repository.IsOccupied(top + Vector3Int.up),
            "a doorway reserves exactly three vertical cells under one instance id");
        Check(BlockGrid.AdjacentCell(doorway, top, Vector3.right, 3) == doorway.GetCell() + Vector3Int.right &&
            BlockGrid.AdjacentCell(doorway, top, Vector3.right, 1) == top + Vector3Int.right &&
            BlockGrid.AdjacentCell(doorway, top, Vector3.up, 3) == top + Vector3Int.up,
            "side attachment aligns tall pieces and keeps cubes at the aimed row while top attachment stacks");
        Check(saves.IsDirty(SaveInstaller.WORLD_FILE), "construction dirties the existing per-playthrough world file");
        await saves.SaveDirtyAsync(CancellationToken.None);

        SaveService reloaded = new(registry, storage, ProjectSerializer());
        await reloaded.LoadScopeAsync(SaveScope.Slot, CancellationToken.None);
        BlockRepository restored = new(reloaded);
        PlacedBlockData data = restored.Origin.Placed[placed.Id];
        Check(data.ItemId == "block_osb" && data.Rotation == 3 && restored.IsOccupied(cell),
            "block type, orientation and occupancy survive saving");
        Check(data.Grounded == null && data.CellHeight == 1, "a single-cell block retains its height and unknown ground support");
        PlacedBlockData legacy = Newtonsoft.Json.JsonConvert.DeserializeObject<PlacedBlockData>("{\"Id\":\"built/legacy\"}");
        Check(legacy.CellHeight == 1, "a legacy block without a height reserves one cell");
        Check(restored.TryGetPlaced(top, out PlacedBlockData restoredDoorway) &&
            restoredDoorway.ItemId == doorway.ItemId && restoredDoorway.CellHeight == 3 && restoredDoorway.Rotation == 1,
            "doorway orientation and its upper occupancy survive restoration");
        Check(restored.Origin.BuildOriginY == 123.25f && restored.DamageOf(placed.Id) == 40,
            "grid height and constructed block damage survive saving");
        Vector3 center = BlockGrid.Center(cell, restored.Origin.BuildOriginY.Value);
        Check(center == new Vector3(-2.5f, 125.75f, -6.5f) && BlockGrid.Cell(center, 123.25f) == cell,
            "grid coordinates round trip around a saved height with negative world coordinates");

        restored.MarkDestroyed(placed.Id);
        restored.MarkDestroyed(doorway.Id);
        Check(!restored.IsOccupied(cell) && restored.Origin.Placed.Count == 0 && restored.IsDestroyed(placed.Id),
            "destruction frees the cell and removes the constructed instance");
        Check(!restored.IsOccupied(doorway.GetCell(), 3), "destruction frees every cell of a doorway");
        restored.AddPlaced(new PlacedBlockData { Id = "built/replacement", ItemId = "block_osb", X = cell.x, Y = cell.y, Z = cell.z }, 999f);
        Check(restored.Origin.BuildOriginY == 123.25f && restored.IsOccupied(cell),
            "rebuilding in a freed cell preserves the original grid height");
        await reloaded.SaveDirtyAsync(CancellationToken.None);

        SaveService final = new(registry, storage, ProjectSerializer());
        await final.LoadScopeAsync(SaveScope.Slot, CancellationToken.None);
        BlockRepository finalRepository = new(final);
        Check(!finalRepository.Origin.Placed.ContainsKey(placed.Id) && finalRepository.IsOccupied(cell),
            "destroyed instances stay removed after another save and reload");

        await VerifyBlockSupport();
    }

    private static async Task VerifyBlockSupport()
    {
        SaveRegistry registry = new SaveRegistry().Register<BlocksData>(SaveInstaller.BLOCKS_KEY, SaveScope.Slot, SaveInstaller.WORLD_FILE);
        MemoryStorage storage = new();
        using SaveService saves = new(registry, storage, ProjectSerializer());
        BlockRepository repository = new(saves);
        BlockSupport support = new(repository);
        using BlockService blocks = new(repository);
        List<PlacedBlockData> unsupported = new();

        PlacedBlockData leftBase = Place("left-base", 0, 0, true);
        Place("left", 0, 1, false);
        Place("middle", 1, 1, false);
        Place("right", 2, 1, false);
        PlacedBlockData rightBase = Place("right-base", 2, 0, true);
        Place("diagonal-base", 3, 0, true);

        repository.MarkDestroyed("left-base");
        support.FindUnsupported(leftBase, data => data.Grounded == true, unsupported);
        Check(unsupported.Count == 0, "a bridge remains connected to its second footing");

        repository.MarkDestroyed("right-base");
        support.FindUnsupported(rightBase, data => data.Grounded == true, unsupported);
        Check(unsupported.Select(data => data.Id).ToHashSet().SetEquals(new[] { "left", "middle", "right" }),
            "losing the final footing collapses the whole segment, with no diagonal support");

        foreach (PlacedBlockData block in unsupported)
        {
            blocks.Collapse(block.Id);
            blocks.Collapse(block.Id);
            Check(!repository.IsOccupied(block.GetCell()) && repository.IsDestroyed(block.Id),
                "collapse releases the cell and repeated collapse is harmless");
        }

        await saves.SaveDirtyAsync(CancellationToken.None);
        using SaveService reloaded = new(registry, storage, ProjectSerializer());
        await reloaded.LoadScopeAsync(SaveScope.Slot, CancellationToken.None);
        BlockRepository restored = new(reloaded);
        Check(restored.Origin.Placed.Count == 1 && restored.Origin.Placed["diagonal-base"].Grounded == true,
            "the saved footing survives while the collapsed segment stays removed");

        PlacedBlockData columnBase = Place("column-base", -5, 0, true);
        Place("column-middle", -5, 1, false);
        Place("column-top", -5, 2, false);
        repository.MarkDestroyed("column-base");
        support.FindUnsupported(columnBase, data => data.Grounded == true, unsupported);
        Check(unsupported.Select(data => data.Id).ToHashSet().SetEquals(new[] { "column-middle", "column-top" }),
            "a column falls when its footing is destroyed");

        PlacedBlockData tallBase = Place("tall-base", 10, 0, true);
        Check(support.HasNeighbor(new Vector3Int(10, 1, 0), 3), "a wall can be supported by a block below");
        Place("wall", 10, 1, false, 3);
        Check(support.HasNeighbor(new Vector3Int(11, 3, 0), 1), "the upper cell of a wall can support an adjacent cube");
        Place("doorway", 11, 2, false, 3);
        Place("roof", 10, 4, false);
        repository.MarkDestroyed(tallBase.Id);
        support.FindUnsupported(tallBase, data => data.Grounded == true, unsupported);
        Check(unsupported.Count == 3 && unsupported.Select(data => data.Id).ToHashSet().SetEquals(new[] { "wall", "doorway", "roof" }),
            "collapse traverses upper wall and doorway cells and returns each piece once");
        repository.MarkDestroyed("wall");
        support.FindUnsupported(new PlacedBlockData { Id = "wall", X = 10, Y = 1, Height = 3 },
            data => data.Grounded == true, unsupported);
        Check(unsupported.Count == 2 && unsupported.All(data => data.Id is "doorway" or "roof"),
            "direct destruction of a tall piece discovers neighbors at all its former heights");

        PlacedBlockData Place(string id, int x, int y, bool grounded, int height = 1)
        {
            PlacedBlockData data = new() { Id = id, ItemId = "block_osb", X = x, Y = y, Height = height, Grounded = grounded };
            repository.AddPlaced(data, 0f);
            return data;
        }
    }
}
