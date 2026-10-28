using System.Collections.Generic;

public static partial class PropContentSetup
{
    private const PropContext ROAD = PropContext.Road;
    private const PropContext STREET = PropContext.Street;
    private const PropContext YARD = PropContext.Yard;
    private const PropContext WILD = PropContext.Wild;
    private const PropContext EVERYWHERE = ROAD | STREET | YARD | WILD;

    private const string VEHICLES = "Vehicles";
    private const string ROADSIDE = "Roadside";
    private const string STORAGE = "Storage";
    private const string TRASH = "Trash";
    private const string STREET_FURNITURE = "Street";
    private const string GARDEN = "Yard";
    private const string CAMP = "Camp";

    private static readonly PropRow[] ROWS = Catalog(
        In(VEHICLES,
            Car("SM_Prop_Car_Wrecked_01", ROAD | STREET, 6f),
            Car("SM_Prop_Car_Wrecked_OpenBoot_01", ROAD | STREET, 3f),
            Car("SM_Prop_Car_Wrecked_SUV_01", ROAD | STREET, 4f),
            Car("SM_Prop_Car_Wrecked_Van_01", ROAD | STREET, 2f),
            Car("SM_Prop_Car_Wrecked_Rusted_01", ROAD | YARD | WILD, 3f),
            Car("SM_Prop_Car_Wrecked_Bus_01", ROAD, 0.4f),
            Wreck("SM_Prop_Car_Wrecked_Squished_01", ROAD | STREET, 1f),
            Wreck("SM_Prop_Car_Wrecked_Squished_Rusted_01", ROAD | WILD, 1f),
            Breakable("SM_Prop_Car_Wrecked_Wheel_01", ROAD | STREET, 1f, BLOCK_RUBBER),
            Breakable("SM_Prop_Car_Wrecked_Wheel_02", ROAD | STREET, 1f, BLOCK_RUBBER)),

        In(ROADSIDE,
            Breakable("SM_Prop_Tire_01", EVERYWHERE, 1.5f, BLOCK_RUBBER, 1, 3, 2.5f),
            Breakable("SM_Prop_Tire_Pile_01", EVERYWHERE, 0.6f, BLOCK_RUBBER_PILE),
            Breakable("SM_Prop_Tire_Pile_02", EVERYWHERE, 0.6f, BLOCK_RUBBER_PILE),
            Breakable("SM_Prop_Tire_Pile_03", EVERYWHERE, 0.6f, BLOCK_RUBBER_PILE),
            Breakable("SM_Prop_Tire_Pile_04", EVERYWHERE, 0.6f, BLOCK_RUBBER_PILE),
            OnRoad(Breakable("SM_Prop_Barrier_Plastic_01", ROAD | STREET, 0.4f, BLOCK_PLASTIC), true, 15f),
            OnRoad(Breakable("SM_Prop_Road_Barrier_01", ROAD, 0.4f, BLOCK_METAL_SMALL), true, 10f),
            OnRoad(Breakable("SM_Prop_Barrier_Concrete_01", ROAD | STREET, 0.4f, BLOCK_CONCRETE), true, 8f),
            OnRoad(Breakable("SM_Prop_Barrier_Concrete_Broken_01", ROAD | STREET, 0.3f, BLOCK_CONCRETE), true, 12f),
            OnRoad(Decor("SM_Prop_Cone_01", ROAD | STREET, 1.2f, 2, 5, 4f), false, 180f),
            Decor("SM_Prop_Bike_Broken_01", ROAD | STREET, 0.4f),
            Decor("SM_Prop_GasCan_01", EVERYWHERE, 0.4f)),

        In(STORAGE,
            Searchable("SM_Prop_Luggage_01", EVERYWHERE, 0.8f, LOOT_LUGGAGE, 1, 2, 2f),
            Searchable("SM_Prop_Luggage_Open_01", EVERYWHERE, 0.6f, LOOT_LUGGAGE),
            Searchable("SM_Prop_Luggage_Retro_01", EVERYWHERE, 0.5f, LOOT_LUGGAGE),
            Searchable("SM_Prop_Luggage_Open_02", EVERYWHERE, 0.4f, LOOT_LUGGAGE),
            Searchable("SM_Prop_Luggage_Retro_Open_01", EVERYWHERE, 0.3f, LOOT_LUGGAGE),
            Searchable("SM_Prop_Briefcase_Flaregun_01", EVERYWHERE, 0.25f, LOOT_BRIEFCASE),
            Searchable("SM_Prop_MedicalBox_01", EVERYWHERE, 0.3f, LOOT_MEDICAL),
            Searchable("SM_Prop_CardboardBox_01", EVERYWHERE, 1f, LOOT_CARDBOARD, 1, 2, 1.5f),
            Searchable("SM_Prop_CardboardBox_05", EVERYWHERE, 1f, LOOT_CARDBOARD, 1, 2, 1.5f),
            Searchable("SM_Prop_CardboardBox_02", EVERYWHERE, 0.7f, LOOT_CARDBOARD),
            Searchable("SM_Prop_CardboardBox_03", EVERYWHERE, 0.7f, LOOT_CARDBOARD),
            Searchable("SM_Prop_CardboardBox_04", EVERYWHERE, 0.7f, LOOT_CARDBOARD),
            Searchable("SM_Prop_Crate_02", EVERYWHERE, 1f, LOOT_CRATE),
            Searchable("SM_Prop_Crate_03", EVERYWHERE, 1f, LOOT_CRATE),
            Searchable("SM_Prop_Crate_Large_01", EVERYWHERE, 0.6f, LOOT_CRATE),
            Searchable("SM_Prop_Crate_Large_02", EVERYWHERE, 0.6f, LOOT_CRATE),
            Searchable("SM_Prop_Crate_Open_01", EVERYWHERE, 0.5f, LOOT_CRATE),
            Searchable("SM_Prop_Crate_Open_02", EVERYWHERE, 0.5f, LOOT_CRATE),
            Flat(Breakable("SM_Prop_Crate_Wall_01", EVERYWHERE, 0.2f, BLOCK_WOOD_LARGE)),
            Searchable("SM_Prop_ToolBox_01", EVERYWHERE, 0.5f, LOOT_TOOLBOX),
            Searchable("SM_Prop_Tool_Bucket_01", EVERYWHERE, 0.3f, LOOT_TOOLBOX),
            Searchable("SM_Prop_Generator_01", YARD, 0.15f, LOOT_TOOLBOX),
            Searchable("SM_Prop_Ammo_Box_01", EVERYWHERE, 0.25f, LOOT_MILITARY),
            Searchable("SM_Prop_SupplyPile_01", EVERYWHERE, 0.35f, LOOT_SUPPLY),
            Searchable("SM_Prop_SupplyPile_02", EVERYWHERE, 0.35f, LOOT_SUPPLY),
            Searchable("SM_Prop_SupplyPile_03", EVERYWHERE, 0.35f, LOOT_SUPPLY),
            Searchable("SM_Prop_SupplyPile_04", EVERYWHERE, 0.35f, LOOT_SUPPLY),
            Searchable("SM_Prop_SupplyPile_05", EVERYWHERE, 0.35f, LOOT_SUPPLY)),

        In(TRASH,
            Searchable("SM_Prop_TrashBag_08", EVERYWHERE, 1f, LOOT_TRASH, 1, 3, 1.5f),
            Searchable("SM_Prop_TrashBag_01", EVERYWHERE, 0.8f, LOOT_TRASH, 1, 3, 1.5f),
            Searchable("SM_Prop_TrashBag_02", EVERYWHERE, 0.8f, LOOT_TRASH, 1, 3, 1.5f),
            Searchable("SM_Prop_TrashBag_03", EVERYWHERE, 0.8f, LOOT_TRASH, 1, 3, 1.5f),
            Searchable("SM_Prop_TrashBag_05", EVERYWHERE, 0.8f, LOOT_TRASH, 1, 3, 1.5f),
            Searchable("SM_Prop_RubbishBin_01", STREET, 0.8f, LOOT_TRASH),
            Searchable("SM_Prop_RubbishBin_02", STREET, 0.8f, LOOT_TRASH),
            Searchable("SM_Prop_TrashCan_01", STREET | YARD, 0.7f, LOOT_TRASH),
            Flat(Searchable("SM_Prop_Dumpster_01", STREET | YARD, 0.5f, LOOT_DUMPSTER)),
            Flat(Searchable("SM_Prop_Dumpster_02", STREET | YARD, 0.5f, LOOT_DUMPSTER)),
            Decor("SM_Prop_Papers_01", EVERYWHERE, 1.2f, 2, 5, 3f),
            Decor("SM_Prop_Papers_02", EVERYWHERE, 1.2f, 2, 5, 3f),
            Decor("SM_Prop_Papers_03", EVERYWHERE, 1.2f, 2, 5, 3f),
            Decor("SM_Prop_Glass_Shard_01", ROAD | STREET, 0.6f, 1, 3, 1.5f),
            Decor("SM_Prop_Glass_Shard_03", ROAD | STREET, 0.6f, 1, 3, 1.5f),
            Breakable("SM_Prop_TrashPile_01", EVERYWHERE, 0.6f, BLOCK_JUNK),
            Breakable("SM_Prop_TrashPile_02", EVERYWHERE, 0.6f, BLOCK_JUNK),
            Breakable("SM_Prop_TrashPile_03", EVERYWHERE, 0.6f, BLOCK_JUNK),
            Breakable("SM_Prop_JunkPile_01", EVERYWHERE, 0.3f, BLOCK_JUNK)),

        In(STREET_FURNITURE,
            Decor("SM_Prop_Hydrant_01", STREET, 0.4f),
            Decor("SM_Prop_Mailbox_01", STREET | YARD, 0.4f),
            Decor("SM_Prop_ParkBench_Broken_01", STREET, 0.3f),
            Breakable("SM_Prop_ShoppingCart_01", STREET, 0.3f, BLOCK_METAL_SMALL),
            Breakable("SM_Prop_ShoppingCart_Broken_01", ROAD | STREET, 0.3f, BLOCK_METAL_SMALL),
            Breakable("SM_Prop_Barrel_01", EVERYWHERE, 0.6f, BLOCK_METAL_SMALL, 1, 3, 1.5f),
            Breakable("SM_Prop_Barrel_Old_01", EVERYWHERE, 0.5f, BLOCK_METAL_SMALL, 1, 2, 1.5f),
            Breakable("SM_Prop_Pallet_01", EVERYWHERE, 0.6f, BLOCK_WOOD_SMALL),
            Breakable("SM_Prop_Pallet_02", EVERYWHERE, 0.6f, BLOCK_WOOD_SMALL),
            Decor("SM_Prop_Cinderblock_01", EVERYWHERE, 0.5f, 1, 4, 2f),
            Decor("SM_Prop_Brick_01", EVERYWHERE, 0.4f, 2, 6, 2f)),

        In(GARDEN,
            Decor("SM_Prop_Barbeque_01", YARD, 0.4f),
            Decor("SM_Prop_Grill_01", YARD, 0.3f),
            Flat(Breakable("SM_Prop_Dog_House_01", YARD, 0.25f, BLOCK_WOOD_LARGE)),
            Decor("SM_Prop_Flamingo_01", YARD, 0.3f, 1, 2, 1.5f),
            Decor("SM_Prop_FlowerPot_01", YARD, 0.4f, 1, 3, 1.5f),
            Decor("SM_Prop_FlowerPot_02", YARD, 0.4f, 1, 3, 1.5f),
            Breakable("SM_Prop_Wheelbarrow_01", YARD, 0.3f, BLOCK_METAL_SMALL),
            Breakable("SM_Prop_Wheelbarrow_Damaged_01", YARD | WILD, 0.3f, BLOCK_METAL_SMALL),
            Decor("SM_Prop_Propane_01", YARD, 0.3f),
            Decor("SM_Prop_Outdoor_Table_01", YARD, 0.2f),
            Breakable("SM_Prop_Plank_01", EVERYWHERE, 0.5f, BLOCK_WOOD_SMALL, 1, 3, 1.5f),
            Breakable("SM_Prop_Plank_02", EVERYWHERE, 0.5f, BLOCK_WOOD_SMALL, 1, 3, 1.5f),
            Breakable("SM_Prop_Log_01", YARD | WILD, 0.4f, BLOCK_WOOD_SMALL, 1, 3, 2f),
            Flat(Decor("SM_Prop_WashingLine_01", YARD, 0.15f)),
            Decor("SM_Prop_Couch_02", YARD | STREET, 0.15f)),

        In(CAMP,
            Flat(Searchable("SM_Prop_Tent_Dome_Damaged_01", WILD, 0.5f, LOOT_SUPPLY)),
            Flat(Searchable("SM_Prop_Tent_Long_Damaged_01", WILD, 0.3f, LOOT_SUPPLY)),
            Searchable("SM_Prop_Sleeping_Bag_01", WILD, 0.4f, LOOT_LUGGAGE),
            Decor("SM_Prop_Camp_Chair_01", YARD | WILD, 0.3f),
            Decor("SM_Prop_FirePit_01", WILD, 0.6f),
            Decor("SM_Prop_BurnPile_01", WILD | STREET, 0.4f),
            Breakable("SM_Prop_Log_Big_01", WILD, 0.3f, BLOCK_WOOD_LARGE),
            Decor("SM_Prop_Skull_01", WILD, 0.2f, 1, 3, 2f),
            Decor("SM_Prop_GraveStick_01", WILD, 0.2f, 1, 4, 3f),
            Decor("SM_Prop_Cross_01", WILD, 0.2f, 1, 4, 3f),
            Flat(Decor("SM_Prop_Shipping_Container_Damaged_01", WILD | YARD, 0.08f)),
            Flat(Searchable("SM_Prop_Helicopter_Crashed_01", WILD, 0.03f, LOOT_MILITARY))));

    private static PropRow[] Catalog(params PropRow[][] sections)
    {
        var rows = new List<PropRow>();
        foreach (PropRow[] section in sections)
            rows.AddRange(section);
        return rows.ToArray();
    }

    private static PropRow[] In(string folder, params PropRow[] rows)
    {
        var placed = new PropRow[rows.Length];
        for (int i = 0; i < rows.Length; i++)
            placed[i] = rows[i].With(folder: folder);
        return placed;
    }

    private static PropRow Car(string prefab, PropContext contexts, float weight)
        => new(prefab, contexts, weight, PropRole.Searchable, LOOT_CAR, true, true, 20f, 14f, 1, 1, 0f);

    private static PropRow Wreck(string prefab, PropContext contexts, float weight)
        => new(prefab, contexts, weight, PropRole.Breakable, BLOCK_METAL_LARGE, true, true, 30f, 14f, 1, 1, 0f);

    private static PropRow Searchable(string prefab, PropContext contexts, float weight, string loot,
        int clusterMin = 1, int clusterMax = 1, float clusterRadius = 0f)
        => new(prefab, contexts, weight, PropRole.Searchable, loot, false, false, 180f, 20f, clusterMin, clusterMax, clusterRadius);

    private static PropRow Breakable(string prefab, PropContext contexts, float weight, string block,
        int clusterMin = 1, int clusterMax = 1, float clusterRadius = 0f)
        => new(prefab, contexts, weight, PropRole.Breakable, block, false, false, 180f, 20f, clusterMin, clusterMax, clusterRadius);

    private static PropRow Decor(string prefab, PropContext contexts, float weight,
        int clusterMin = 1, int clusterMax = 1, float clusterRadius = 0f)
        => new(prefab, contexts, weight, PropRole.Decor, null, false, false, 180f, 30f, clusterMin, clusterMax, clusterRadius);

    private static PropRow OnRoad(PropRow row, bool alongRoad, float yawJitter) => row.With(onRoad: true, alongRoad: alongRoad, yawJitter: yawJitter);

    private static PropRow Flat(PropRow row) => row.With(maxSlope: FLAT_SLOPE);
}
