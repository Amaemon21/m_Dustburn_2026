public enum WorldGenStage
{
    MapBiomeSeeds,
    MapBiomeClassify,
    MapBiomeSmooth,
    MapBiomeRegions,
    MapBiomes,
    MapWeights,
    MapHeightNoise,
    MapHeightHydraulic,
    MapHeightThermal,
    MapHeights,
    MapHubs,
    MapRoadPlan,
    MapCarveTrunk,
    MapCities,
    MapCarveStreets,
    MapProximity,
    MapPoiPlace,
    MapCarvePads,
    MapApplyHeights,
    MapTotal,

    BakeMapsSave,
    BakeHeightRaw,
    BakeTextures,
    BakeImport,
    BakeRoadAsset,
    BakePoiAsset,
    BakeSplat,
    BakeMaterial,
    BakeSaveAssets,
    BakeTotal,

    PreviewGeometry,
    PreviewDecor,
    PreviewPoi,
    PreviewClear,

    RuntimeConstruct,
    RuntimeDecodeHeights,
    RuntimeLowest,
    RuntimeField,
    RuntimeDecorSetup,
    RuntimePlan,
    RuntimeTick,
    RuntimeRush,
    RuntimeDispatch,
    RuntimeCollect,
    RuntimeSow,
    RuntimeDecorate,
    RuntimeSettle,
    RuntimeDemolish,

    VoxelMeshSchedule,
    VoxelMeshDrain,
    VoxelMeshBlocking,
    VoxelMeshUpload,
    VoxelSpawn,

    ColliderDispatch,
    ColliderBlocking,
    ColliderCollect,
    ColliderAttach,

    DecorStep,
    DecorPlace,
    DecorCombine,
    DecorInstance,
    DecorInstantiate,

    PoiBatch,

    CleanupUnload,
    CleanupDispose,

    Count
}

public enum WorldGenMilestone
{
    RunStart,
    RuntimeConstructed,
    Planned,
    FirstTerrain,
    FirstCollider,
    TerrainQueuesEmpty,
    CollidersIdle,
    InitialDecor,
    TerrainReady,
    PoiStart,
    PoiReady,
    WorldReady,
    DecorSettled,

    Count
}

public enum WorldGenQueueKind
{
    TerrainChunk,
    Collider,
    Decor,
    Demolish,
    Poi
}

public enum WorldGenQueuePhase
{
    Requested,
    Scheduled,
    Observed,
    Applied,
    Cancelled
}
