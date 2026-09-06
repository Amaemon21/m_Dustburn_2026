using UnityEngine;

public static class VoxelGroundMaterial
{
    public class Request
    {
        public WorldGenerationConfig Config;
        public BiomeDatabase Biomes;
        public Texture2D BiomeMap;
        public Texture2D RoadMask;
        public HeightMap Map;
        public int ControlResolution = 1024;
        public bool UseRepetitionless = true;
        public string MaterialPath;
        public Material Material;
    }

    public static string Bake(Request request, Object context)
    {
        if (request.Biomes == null || !request.Biomes.IsValid() || request.BiomeMap == null)
        {
            Debug.LogWarning("Ground material skipped: assign the BiomeDatabase and the generated BiomeMap texture", context);
            return "no splatmap";
        }

        BiomeMap biomeMap = BiomeMapTexture.Read(request.BiomeMap, request.Biomes, request.Config.WorldSize);

        if (biomeMap == null)
            return "no splatmap";

        var weightField = new BiomeWeightField(biomeMap, request.Biomes.Count, request.Config.BiomeBlendPasses);

        float[] roadMask = ReadRoadMask(request, context, out int roadMaskResolution);

        var painter = new GroundSplatPainter(request.Config, request.Biomes, weightField, roadMask, roadMaskResolution);

        try
        {
            if (!painter.HasLayers)
            {
                Debug.LogWarning("Ground material skipped: no biome has a TerrainLayer and the config has no Cliff Layer", context);
                return "no splatmap";
            }

            if (!Resolve(request, painter.Layers, context))
                return "no splatmap: the material could not be prepared";

            Texture2D[] controls = VoxelSplatBaker.Bake(request.Config, painter, request.Map, request.ControlResolution);

            for (int i = 0; i < controls.Length; i++)
            {
                Release(request.Material, i);

                request.Material.SetTexture($"_Control{i}", Persist(request, controls[i], i));
            }

            request.Material.SetFloat("_LayersCount", painter.Layers.Length);
            request.Material.SetFloat("_UVSpace", 1f);

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.EditorUtility.SetDirty(request.Material);
                UnityEditor.AssetDatabase.SaveAssets();
            }
#endif

            return $"splatmap: {painter.Layers.Length} layers in {controls.Length} control textures of {request.ControlResolution}";
        }
        finally
        {
            painter.Dispose();
        }
    }

    private static void Release(Material material, int index)
    {
        var existing = material.GetTexture($"_Control{index}") as Texture2D;

        if (existing == null)
            return;

#if UNITY_EDITOR
        if (UnityEditor.AssetDatabase.Contains(existing))
            return;
#endif

        if (Application.isPlaying)
            Object.Destroy(existing);
        else
            Object.DestroyImmediate(existing);
    }

    private static Texture2D Persist(Request request, Texture2D texture, int index)
    {
#if UNITY_EDITOR
        if (Application.isPlaying || string.IsNullOrEmpty(request.MaterialPath))
            return texture;

        string path = $"{System.IO.Path.ChangeExtension(request.MaterialPath, null)}_Control{index}.asset";

        UnityEditor.AssetDatabase.CreateAsset(texture, path);
#endif
        return texture;
    }

    private static bool Resolve(Request request, TerrainLayer[] layers, Object context)
    {
        if (!request.UseRepetitionless)
        {
            if (request.Material == null)
                Debug.LogError("Ground material: Repetitionless is switched off and no material is assigned", context);

            return request.Material != null;
        }

#if UNITY_EDITOR
        if (Application.isPlaying)
        {
            if (request.Material == null)
                Debug.LogError("Ground material: Repetitionless bakes its texture arrays through editor API only. Bake the material once outside play mode and assign it", context);

            return request.Material != null;
        }

        request.Material = RepetitionlessVoxelSetup.Apply(layers, request.MaterialPath);

        return request.Material != null;
#else
        return request.Material != null;
#endif
    }

    private static float[] ReadRoadMask(Request request, Object context, out int resolution)
    {
        resolution = 0;

        if (request.RoadMask == null)
        {
            Debug.LogWarning("Roads will not be painted: the Road Mask texture is not assigned (Assets/_Dustborn/Generated/RoadMask.png)", context);
            return null;
        }

        if (request.RoadMask.width != request.Config.HeightMapResolution)
        {
            Debug.LogWarning($"Road mask is {request.RoadMask.width} px but the height map is {request.Config.HeightMapResolution}, regenerate it. Roads will not be painted", context);
            return null;
        }

        float[] mask = MaskTexture.Read(request.RoadMask);

        if (mask == null)
            return null;

        resolution = request.RoadMask.width;

        return mask;
    }
}
