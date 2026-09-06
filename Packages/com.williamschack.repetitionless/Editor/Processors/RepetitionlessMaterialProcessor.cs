#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Repetitionless.Editor.Processors
{
    using Data;

    public class RepetitionlessMaterialProcessor : AssetModificationProcessor
    {
	    private static Material GetRepetitionlessMaterial(string assetPath)
        {
            if (!assetPath.EndsWith(".mat"))
                return null;

            Material material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (material == null)
                return null;

            string shaderName = material.shader.name;
            if (!shaderName.StartsWith("Repetitionless/"))
                return null;

            return material;
        }

        private static string GetDataPath(string assetPath)
        {
            // Material Name
            int substringStartIndex = assetPath.LastIndexOf("/") + 1;
            int substringLength = assetPath.Length - substringStartIndex - 4; // Remove end .mat
            string materialName = assetPath.Substring(substringStartIndex, substringLength);

            // Data path
            int lastDirIndex = assetPath.LastIndexOf("/");
            string dataPath = assetPath.Substring(0, lastDirIndex);
            dataPath += $"/{MaterialDataManager.GenerateFolderName(materialName)}";

            return dataPath;
        }

        // Move data folder when moving a repetitionless material
        private static AssetMoveResult OnWillMoveAsset(string sourcePath, string destinationPath)
        {
            Material material = GetRepetitionlessMaterial(sourcePath);
            if (material == null) return AssetMoveResult.DidNotMove;

            // Data paths
            string sourceDataPath = GetDataPath(sourcePath);
            string destinationDataPath = GetDataPath(destinationPath);

            if (!AssetDatabase.IsValidFolder(sourceDataPath))
                return AssetMoveResult.DidNotMove;

            // Move data folder
            AssetDatabase.MoveAsset(sourceDataPath, destinationDataPath);

            return AssetMoveResult.DidNotMove;
        }

        // Cleanup data when deleting a repetitionless material
        private static AssetDeleteResult OnWillDeleteAsset(string assetPath, RemoveAssetOptions options)
        {
            Material material = GetRepetitionlessMaterial(assetPath);
            if (material == null) return AssetDeleteResult.DidNotDelete;

            // Data Path
            string materialDataPath = GetDataPath(assetPath);

            // Delete data folder if it exists
            if (AssetDatabase.IsValidFolder(materialDataPath))
                AssetDatabase.DeleteAsset(materialDataPath);

            return AssetDeleteResult.DidNotDelete;
        }
    }
}
#endif
