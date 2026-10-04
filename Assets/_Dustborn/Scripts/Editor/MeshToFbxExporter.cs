using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.Formats.Fbx.Exporter;

public static class MeshToFbxExporter
{
    [MenuItem("Tools/Mesh to FBX/Export Selected Mesh")]
    private static void ExportSelectedMesh()
    {
        Object[] selected = Selection.objects;

        if (selected == null || selected.Length == 0)
        {
            EditorUtility.DisplayDialog(
                "Mesh to FBX",
                "Select a Mesh in the Project window.",
                "OK");
            
            return;
        }

        int exported = 0;

        foreach (Object obj in selected)
        {
            Mesh mesh = obj as Mesh;

            if (mesh == null)
                continue;

            ExportMesh(mesh);
            exported++;
        }

        if (exported == 0)
        {
            EditorUtility.DisplayDialog(
                "Mesh to FBX",
                "None of the selected objects is a Mesh.",
                "OK");
            
            return;
        }

        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog(
            "Mesh to FBX",
            $"Meshes exported: {exported}",
            "OK");
    }

    private static void ExportMesh(Mesh mesh)
    {
        string meshPath = AssetDatabase.GetAssetPath(mesh);

        if (string.IsNullOrEmpty(meshPath))
        {
            Debug.LogError($"Could not resolve the asset path of mesh {mesh.name}");
            return;
        }

        string directory = Path.GetDirectoryName(meshPath);

        if (string.IsNullOrEmpty(directory))
            directory = "Assets";

        string fbxPath = Path.Combine(directory, mesh.name + ".fbx");
        
        if (File.Exists(fbxPath))
        {
            bool overwrite = EditorUtility.DisplayDialog(
                "FBX already exists",
                $"The file already exists:\n{fbxPath}\n\nOverwrite?",
                "Yes",
                "No");

            if (!overwrite)
                return;
        }
        
        GameObject temp = new GameObject(mesh.name);

        try
        {
            MeshFilter meshFilter = temp.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = temp.AddComponent<MeshRenderer>();

            meshFilter.sharedMesh = mesh;
            
            Material material = new Material(Shader.Find("Standard"));
            material.name = mesh.name + "_Material";

            meshRenderer.sharedMaterial = material;
            
            string absolutePath = Path.GetFullPath(fbxPath);

            string result = ModelExporter.ExportObject(absolutePath, temp);

            if (!string.IsNullOrEmpty(result))
            {
                Debug.Log($"[Mesh to FBX] Exported:\n{result}");
            }
            else
            {
                Debug.LogError($"[Mesh to FBX] Export failed: {mesh.name}");
            }

            Object.DestroyImmediate(material);
        }
        finally
        {
            Object.DestroyImmediate(temp);
        }
    }

    [MenuItem("Tools/Mesh to FBX/Export Selected Mesh", true)]
    private static bool ValidateExportSelectedMesh()
    {
        foreach (Object obj in Selection.objects)
        {
            if (obj is Mesh)
                return true;
        }

        return false;
    }
}