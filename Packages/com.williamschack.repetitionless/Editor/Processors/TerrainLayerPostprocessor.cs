#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Repetitionless.Editor.Processors
{
    using Data;

    public class TerrainLayerPostprocessor : AssetModificationProcessor
    {
        static string[] OnWillSaveAssets(string[] paths)
        {
            foreach (string path in paths) {
                TerrainLayer terrainLayer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);

                if (terrainLayer == null )
                    continue;

                UpdateAllRepetitionlessDataUsingLayer(terrainLayer);
            }

            return paths;
        }

        private static void UpdateAllRepetitionlessDataUsingLayer(TerrainLayer layer)
        {
            // Find all the repetitionless terrain layer SOs and update them if they include this layer

            string[] layerSOGuids = AssetDatabase.FindAssets("t:RepetitionlessTerrainDataSO", new string[] { "Assets" });

            foreach (string guid in layerSOGuids) {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path == "") continue;

                RepetitionlessTerrainDataSO so = AssetDatabase.LoadAssetAtPath<RepetitionlessTerrainDataSO>(path);
                if (so == null || so.TerrainLayers == null) continue;

                // Update data
                if (so.TerrainLayers.Contains(layer))
                    so.UpdateLayerMaterialData(layer);
            }
        }
    }
}
#endif