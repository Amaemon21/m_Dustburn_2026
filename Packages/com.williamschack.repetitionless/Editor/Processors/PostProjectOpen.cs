#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

namespace Repetitionless.Editor.Processors
{
    using CustomWindows;
    using Config;
    using Updating;

    [InitializeOnLoad]
    public static class PostProjectOpen
    {
        static PostProjectOpen()
        {
            if (Application.isBatchMode)
                return;

            // Wait a frame for the editor id to initialize
            EditorApplication.delayCall += OnInitializeOnLoad;
        }

        private static void OnInitializeOnLoad()
        {
            if (!IsEditorStartup())
                return;

            // Setup colour space checker
            RepetitionlessColourSpaceUpdater.Initialize();

            // Check if urp/hdrp was updated while project was closed
            EditorApplication.delayCall += () => {
                RenderPipelineChecker.CheckInstalledPackages();
            };

            // Update hdrp terrain shader if required
            CheckAndUpdateHDRPTerrainShader();

            // Open window if update available
            if (UpdateChecker.UpdateAvailable($"v{RepetitionlessPackageInfo.Info.version}") && RepetitionlessPrefs.Data.OpenWindowOnUpdate)
                WelcomeWindow.Open(showUpdateMessage: true);
        }

        // InitializeOnLoad is called every domain reload, this makes sure its only on startup
        private static bool IsEditorStartup()
        {
            long sessionId = EditorAnalyticsSessionInfo.id;
            long lastSessionId = RepetitionlessPrefs.Data.LastSessionId;

            if (sessionId == lastSessionId)
                return false;

            RepetitionlessPrefs.UpdatePrefs((p) => {
                p.LastSessionId = sessionId;
            });

            return true;
        }

        internal static void CheckAndUpdateHDRPTerrainShader(bool forceUpdate = false)
        {
            bool newTerrainActive = RepetitionlessPrefs.Data.HasNewHDRPSupport;

            string hdrpFolderName = RepetitionlessPrefs.Data.HDRPActive ? "HDRP" : "HDRP~";
            string hdrpShaderFolderPath = Constants.PACKAGE_PATH + "/Shaders/" + hdrpFolderName + "/";
            string oldFolderName = "TerrainOld";
            string newFolderName = "TerrainNew";

#if UNITY_6000_3_OR_NEWER
            if (!forceUpdate && newTerrainActive) return;
            
            string oldFolderPath = hdrpShaderFolderPath + oldFolderName;
            string newFolderPath = hdrpShaderFolderPath + newFolderName;
            bool hasNewHDRPSupport = true;
#else
            if (!forceUpdate && !newTerrainActive) return;

            string oldFolderPath = hdrpShaderFolderPath + newFolderName;
            string newFolderPath = hdrpShaderFolderPath + oldFolderName;
            bool hasNewHDRPSupport = false;
#endif

            // Get materials that need to be updated
            string[] materialGuids = AssetDatabase.FindAssets("t:Material", new string[] { "Assets" });
            List<Material> updatingMaterials = new List<Material>();

            foreach (string guid in materialGuids) {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path == "") continue;

                Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) continue;

                string shaderName = mat.shader.name;

                if (!shaderName.StartsWith(Constants.SHADER_FOLDER) ||                    // Must be repetitionless shader
                    !shaderName.Contains(Constants.SHADER_FOLDER_HDRP) ||                 // Must be hdrp
                    !shaderName.Contains(Constants.SHADER_MATERIAL_NAME_LAYERED_TERRAIN)) // Must use terrain shader
                    continue;

                updatingMaterials.Add(mat);
            }

            // Change paths to absolute paths
            string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../"));
            oldFolderPath = projectPath + "/" + oldFolderPath;
            newFolderPath = projectPath + "/" + newFolderPath;

            // Hide old folder, unhide new folder
            if (Directory.Exists(oldFolderPath)) {
                Directory.Move(oldFolderPath, oldFolderPath + "~");

                if (File.Exists(oldFolderPath + ".meta"))
                    File.Delete(oldFolderPath + ".meta");
            }

            if (Directory.Exists(newFolderPath + "~"))
                Directory.Move(newFolderPath + "~", newFolderPath);

            AssetDatabase.Refresh();

            // Update materials to the new shader
            foreach (Material mat in updatingMaterials) {
                mat.shader = Shader.Find(Constants.SHADER_FOLDER + Constants.SHADER_FOLDER_HDRP + Constants.SHADER_MATERIAL_NAME_LAYERED_TERRAIN);
            }

            // Update pref
            RepetitionlessPrefs.UpdatePrefs((p) => {
                p.HasNewHDRPSupport = hasNewHDRPSupport;
            });
        }
    }
}
#endif