#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace Repetitionless.Editor.Processors
{
    internal static class RepetitionlessMaterialFinder
    {
        public static List<Material> GetAll()
        {
            string[] materialGuids = AssetDatabase.FindAssets("t:Material", new string[] { "Assets" });
            List<Material> repetitionlessMaterials = new List<Material>();

            foreach (string guid in materialGuids) {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path == "") continue;

                Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) continue;

                string shaderName = mat.shader.name;
                if (!shaderName.StartsWith(Constants.SHADER_FOLDER))
                    continue;

                repetitionlessMaterials.Add(mat);
            }

            return repetitionlessMaterials;
        }
    }
}

#endif