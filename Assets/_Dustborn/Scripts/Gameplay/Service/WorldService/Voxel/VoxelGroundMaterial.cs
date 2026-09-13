using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

public static class VoxelGroundMaterial
{
    public class Request
    {
        public WorldGenerationConfig Config;
        public BiomeDatabase Biomes;
        public Texture2D BiomeMap;
        public IReadOnlyList<Road> Roads;
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

        var weightField = new BiomeWeightField(biomeMap, request.Biomes.Count, request.Config.BiomeBlendRadius);

        if (!RoadPaintIndex.HasRoads(request.Roads))
            Debug.LogWarning("Roads will not be painted: no road network was passed in (Assets/_Dustborn/Generated/RoadNetwork.asset)", context);

        var painter = new GroundSplatPainter(request.Config, request.Biomes, weightField, request.Roads);

        try
        {
            if (!painter.HasLayers)
            {
                Debug.LogWarning("Ground material skipped: no biome has a TerrainLayer and the config has no Cliff Layer", context);
                return "no splatmap";
            }

            bool resolved;

            using (WorldGenProbe.Measure(WorldGenStage.BakeLayers))
                resolved = Resolve(request, painter.Layers, context);

            if (!resolved)
                return "no splatmap: the material could not be prepared";

            Texture2D[] controls;

            using (WorldGenProbe.Measure(WorldGenStage.BakeSplat))
                controls = VoxelSplatBaker.Bake(request.Config, painter, request.Map, request.ControlResolution);

            using WorldGenProbe.Span persist = WorldGenProbe.Measure(WorldGenStage.BakePersist);

            Texture2D[] stored = Persist(request, controls);

            for (int i = 0; i < stored.Length; i++)
            {
                Release(request.Material, i);

                request.Material.SetTexture($"_Control{i}", stored[i]);
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

    private static Texture2D[] Persist(Request request, Texture2D[] controls)
    {
#if UNITY_EDITOR
        if (Application.isPlaying || string.IsNullOrEmpty(request.MaterialPath))
            return controls;

        var encoded = new Task<byte[]>[controls.Length];

        for (int i = 0; i < controls.Length; i++)
        {
            byte[] raw = controls[i].GetRawTextureData();
            var size = (uint)controls[i].width;

            encoded[i] = Task.Run(() => ImageConversion.EncodeArrayToPNG(raw, GraphicsFormat.R8G8B8A8_UNorm, size, size));
        }

        string stem = System.IO.Path.ChangeExtension(request.MaterialPath, null);
        var stored = new Texture2D[controls.Length];

        for (int i = 0; i < controls.Length; i++)
        {
            string legacy = $"{stem}_Control{i}.asset";

            if (UnityEditor.AssetDatabase.LoadMainAssetAtPath(legacy) != null)
                UnityEditor.AssetDatabase.DeleteAsset(legacy);

            string path = $"{stem}_Control{i}.png";

            System.IO.File.WriteAllBytes(path, encoded[i].GetAwaiter().GetResult());
            stored[i] = ImportControl(path);

            if (stored[i] == null)
                throw new System.InvalidOperationException($"The control texture was written but could not be imported: {path}");

            Object.DestroyImmediate(controls[i]);
        }

        return stored;
#else
        return controls;
#endif
    }

#if UNITY_EDITOR
    private static Texture2D ImportControl(string path)
    {
        UnityEditor.AssetDatabase.ImportAsset(path, UnityEditor.ImportAssetOptions.ForceUpdate);

        if (UnityEditor.AssetImporter.GetAtPath(path) is UnityEditor.TextureImporter importer && !ControlImportReady(importer))
        {
            importer.textureType = UnityEditor.TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = false;
            importer.alphaSource = UnityEditor.TextureImporterAlphaSource.FromInput;
            importer.textureCompression = UnityEditor.TextureImporterCompression.Uncompressed;
            importer.npotScale = UnityEditor.TextureImporterNPOTScale.None;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.isReadable = false;
            importer.maxTextureSize = 16384;
            importer.SaveAndReimport();
        }

        return UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static bool ControlImportReady(UnityEditor.TextureImporter importer)
    {
        return importer.textureType == UnityEditor.TextureImporterType.Default
            && !importer.sRGBTexture
            && !importer.mipmapEnabled
            && !importer.alphaIsTransparency
            && importer.alphaSource == UnityEditor.TextureImporterAlphaSource.FromInput
            && importer.textureCompression == UnityEditor.TextureImporterCompression.Uncompressed
            && importer.npotScale == UnityEditor.TextureImporterNPOTScale.None
            && importer.filterMode == FilterMode.Bilinear
            && importer.wrapMode == TextureWrapMode.Clamp
            && !importer.isReadable
            && importer.maxTextureSize == 16384;
    }
#endif

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
}
