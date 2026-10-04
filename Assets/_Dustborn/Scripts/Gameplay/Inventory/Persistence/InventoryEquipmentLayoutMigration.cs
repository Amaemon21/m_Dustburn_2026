using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public sealed class InventoryEquipmentLayoutMigration : ISaveMigration
{
    private const int LEGACY_SLOT_COUNT = 10;

    public int FromVersion => 1;
    public int ToVersion => 2;

    public void Migrate(JObject document)
    {
        if (document[NewtonsoftSaveSerializer.SECTIONS_FIELD] is not JObject sections ||
            sections[SaveInstaller.INVENTORY_KEY] is not JObject section)
            return;

        JsonSerializer serializer = JsonSerializer.CreateDefault(new JsonSerializerSettings
        {
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore
        });

        InventoryData data = section.ToObject<InventoryData>(serializer);

        if (data == null || !Upgrade(data))
            return;

        sections[SaveInstaller.INVENTORY_KEY] = JObject.FromObject(data, serializer);
    }

    private static bool Upgrade(InventoryData data)
    {
        data.Grids ??= new List<InventoryGridData>();
        data.Players ??= new List<PlayerInventoryData>();

        bool changed = false;
        foreach (InventoryGridData equipment in data.Grids)
        {
            if (equipment.Kind != InventoryGridKind.Equipment || equipment.Size != new Vector2Int(LEGACY_SLOT_COUNT, 1))
                continue;

            PlayerInventoryData player = data.Players.Find(value => value.EquipmentId == equipment.OwnerId);
            InventoryGridData backpack = player == null ? null : data.Grids.Find(value => value.OwnerId == player.BackpackId);
            foreach (InventorySlotData slot in equipment.Slots)
            {
                if (slot.Amount == 0)
                    continue;
                if (backpack == null || backpack.Kind != InventoryGridKind.Backpack)
                    throw new InvalidOperationException($"Cannot migrate equipment '{equipment.OwnerId}' without its backpack");
            }

            foreach (InventorySlotData slot in equipment.Slots)
            {
                if (slot.Amount == 0)
                    continue;
                ReturnToBackpack(backpack, slot);
            }

            equipment.Size = new Vector2Int(Enum.GetValues(typeof(EquipmentSlot)).Length, 1);
            equipment.Slots = new List<InventorySlotData>();
            for (int index = 0; index < equipment.Size.x; index++)
                equipment.Slots.Add(new InventorySlotData());
            changed = true;
        }
        return changed;
    }

    private static void ReturnToBackpack(InventoryGridData backpack, InventorySlotData item)
    {
        if (backpack.Size.x <= 0 || backpack.Size.y <= 0)
            throw new InvalidOperationException($"Backpack '{backpack.OwnerId}' has an invalid size");
        int count = checked(backpack.Size.x * backpack.Size.y);
        while (backpack.Slots.Count < count)
            backpack.Slots.Add(new InventorySlotData());

        InventorySlotData target = backpack.Slots.Find(slot => slot.Amount == 0);
        if (target == null)
        {
            target = new InventorySlotData();
            backpack.Slots.Add(target);
            for (int index = 1; index < backpack.Size.y; index++)
                backpack.Slots.Add(new InventorySlotData());
            backpack.Size = new Vector2Int(backpack.Size.x + 1, backpack.Size.y);
        }
        target.ItemId = item.ItemId;
        target.Amount = item.Amount;
    }
}
