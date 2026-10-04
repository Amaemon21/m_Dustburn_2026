#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using Repetitionless.Runtime.Variables;

namespace Repetitionless.Editor.Processors
{
    using Data;
    using CustomWindows;
    using Config;
    using Materials;

    [InitializeOnLoad]
    public static class PostPackageImport
    {
        static PostPackageImport()
        {
            if (Application.isBatchMode)
                return;

            if (RepetitionlessPrefs.Data.LiteMode) {
                ProUpgrade();
            }

            if (NewVersionImported()) {
                HandleVersionUpdate();
                
                RepetitionlessPrefs.UpdatePrefs((p) => {
                    p.LastProcessedVersion = RepetitionlessPackageInfo.Info.version;
                });
            }

            AssetDatabase.importPackageCompleted += PackageImported;
        }

        private static void ProUpgrade()
        {
            // Update old layered materials to new max layer count
            List<Material> repetitionlessMaterials = RepetitionlessMaterialFinder.GetAll();
            foreach (Material mat in repetitionlessMaterials) {
                MaterialDataManager dataManager = new MaterialDataManager(mat);

                if (!dataManager.AssetExists(Constants.LAYERED_DATA_FILE_NAME))
                    continue;

                RepetitionlessMaterialDataSO materialDataSO = dataManager.LoadAsset<RepetitionlessMaterialDataSO>(Constants.PROPERTIES_FILE_NAME);
                RepetitionlessTextureDataSO textureDataSO = dataManager.LoadAsset<RepetitionlessTextureDataSO>(Constants.TEXTURE_DATA_FILE_NAME);
                RepetitionlessLayeredDataSO layeredDataSO = dataManager.LoadAsset<RepetitionlessLayeredDataSO>(Constants.LAYERED_DATA_FILE_NAME);

                materialDataSO.InitNewLayerCount(Constants.MAX_LAYERS_TERRAIN);
                textureDataSO.InitNewLayerCount(Constants.MAX_LAYERS_TERRAIN);
                layeredDataSO.InitNewLayerCount();
            }

            // Remove unused files
            string[] unusedFiles = {
                Constants.PACKAGE_PATH + "/Editor/Updating/SaleChecker.cs"
            };

            foreach (string filePath in unusedFiles) {
                AssetDatabase.DeleteAsset(filePath);
            }

            EditorApplication.delayCall += () => {
                // To update terrains in the current scene
                EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());

                WelcomeWindow.Open();
                ShowReviewLog();

                RepetitionlessPrefs.UpdatePrefs((p) => {
                    p.LiteMode = false;
                });

                EditorUtility.DisplayDialog("Repetitionless Upgrade", "Thanks for purchasing the full version of Repetitionless! Your materials have been automatically updated but any terrain layers past 4 will appear blank. Click the \"Save Textures\" button in each RepetitionlessTerrain component to fix this.\nEnjoy the new features :)", "Ok");
            };
        }

        private static int[] SplitVersion(string version)
        {
            string[] partStrings = version.Split(".");

            int[] numbers = new int[3];
            for (int i = 0; i < numbers.Length; i++) {
                numbers[i] = int.Parse(partStrings[i]);
            }

            return numbers;
        }

        private static bool NewVersionImported()
        {
            return RepetitionlessPrefs.Data.LastProcessedVersion != RepetitionlessPackageInfo.Info.version;
        }

        private static void PackageImported(string packageName)
        {
            AssetDatabase.importPackageCompleted -= PackageImported;

            EditorApplication.delayCall += () => {
                RenderPipelineChecker.CheckInstalledPackages();
            };

            // Show welcome window if first time installing
            if (RepetitionlessPrefs.Data.WelcomeWindowShown)
                return;

            WelcomeWindow.Open(true);
            ShowReviewLog();

            RepetitionlessPrefs.UpdatePrefs((p) => {
                p.WelcomeWindowShown = true;
                p.LastProcessedVersion = RepetitionlessPackageInfo.Info.version;
            });
        }

        private static int[] GetLastVersion()
        {
            return SplitVersion(RepetitionlessPrefs.Data.LastProcessedVersion);
        }

        private static void HandleVersionUpdate()
        {
            int[] splitLastVersion = GetLastVersion();
            if (splitLastVersion[0] == 0) return;

            EditorApplication.delayCall += () => {
                RenderPipelineChecker.CheckInstalledPackages(true);
                RenderPipelineChecker.MergeNewShaderFolders();
            };

            // Upgrading to 1.0.3
            if (splitLastVersion[0] == 1 && splitLastVersion[1] == 0 && splitLastVersion[2] <= 3) {
                ShowReviewLog();
            }

            // Upgrading to 1.2.0
            if (splitLastVersion[0] == 1 && splitLastVersion[1] <= 2) {
                AssetDatabase.importPackageCompleted += ConvertTerrainMaterials;
            }

            // Upgrading to 1.4.0
            if (splitLastVersion[0] == 1 && splitLastVersion[1] < 4) {
                AssetDatabase.importPackageCompleted += UpdateTo140;
            }
        }

#region Updating to 1.0.3
        private static void ShowReviewLog()
        {
            Debug.Log("<b>Thanks for purchasing Repetitionless! <color=#3FFFFF>Please consider leaving a review to support the asset and its development, any feedback is appreciated!</color></b>");
        }
#endregion

#region Updating to 1.2.0
        private static void ConvertTerrainMaterials(string packageName = "")
        {
            AssetDatabase.importPackageCompleted -= ConvertTerrainMaterials;

            EditorUtility.DisplayDialog("Repetitionless Update", "RepetitionlessTerrain materials have been changed to RepetitionlessLayered materials. Any existing terrain materials will be converted.", "Ok");

            // Convert terrain materials to layered materials
            List<Material> repetitionlessMaterials = RepetitionlessMaterialFinder.GetAll();
            
            bool convertedTerrains = false;
            foreach (Material mat in repetitionlessMaterials) {
                string shaderName = mat.shader.name;
                if (!shaderName.EndsWith("RepetitionlessTerrain"))
                    continue;

                string rp = shaderName.Split("/")[1];
                string newShaderName = $"{Constants.SHADER_FOLDER}{rp}/{Constants.SHADER_MATERIAL_NAME_LAYERED_TERRAIN}";

                mat.shader = Shader.Find(newShaderName);

                MaterialDataManager dataManager = new MaterialDataManager(mat);
                RepetitionlessMaterialDataSO materialProperties = dataManager.LoadAsset<RepetitionlessMaterialDataSO>(Constants.PROPERTIES_FILE_NAME);
                materialProperties.CallOnExternalDataChanged();

                EditorUtility.SetDirty(mat);
                AssetDatabase.SaveAssetIfDirty(mat);

                convertedTerrains = true;
            }

            // Delete the old shaders
            string projectFolder = Path.GetFullPath(Path.Combine(Application.dataPath, "../"));
            string shaderGraphsFolder = $"{projectFolder}/Packages/com.williamschack.repetitionless/Shaders/ShaderGraphs";
            string shaderFileName = "RepetitionlessTerrain.shadergraph";
            string[] pipelineFolderNames = {
                "BIRP",
                "URP",
                "HDRP"
            };

            foreach (string pipelineFolderName in pipelineFolderNames) {
                string filePath = $"{shaderGraphsFolder}/{pipelineFolderName}/{shaderFileName}";
                string metaPath = $"{filePath}.meta";
                if (File.Exists(filePath)) File.Delete(filePath);
                if (File.Exists(metaPath)) File.Delete(metaPath);
            }

            AssetDatabase.Refresh();

            if (convertedTerrains)
                EditorUtility.DisplayDialog("Repetitionless Update", "Some terrains using Repetitionless may have pink materials.\nTo fix this, click the Save Textures button on the RepetitionlessTerrain component and make sure those materials are set to the RepetitionlessLayered shader", "Ok");
        }
#endregion

#region Updating to 1.4.0
        private static void UpdateTo140(string packageName = "")
        {
            AssetDatabase.importPackageCompleted -= UpdateTo140;

            RemoveOldShaderFiles();
            UpdateOldMaterials();
            PostProjectOpen.CheckAndUpdateHDRPTerrainShader(true);

            EditorUtility.DisplayDialog("Repetitionless Update", "v1.4.0 includes major performance increases but also comes with many breaking changes. Many of these have been automatically updated but if you are using the sub graphs or shader code, view the github or discord for any required changes. Enjoy the extra frames :)", "Ok");
        }

        private static void RemoveOldShaderFiles()
        {
            string projectFolder = Path.GetFullPath(Path.Combine(Application.dataPath, "../"));
            string shadersFolder = projectFolder + Constants.PACKAGE_PATH + "/Shaders/";
            string hlslFolder = shadersFolder + "HLSL";
            string graphsFolder = shadersFolder + "ShaderGraphs";

            if (Directory.Exists(hlslFolder)) {
                Directory.Delete(hlslFolder, true);
                File.Delete(hlslFolder + ".meta");
            }

            if (Directory.Exists(graphsFolder)) {
                Directory.Delete(graphsFolder, true);
                File.Delete(graphsFolder + ".meta");
            }

            AssetDatabase.Refresh();
        }

        private static bool MaterialIsTerrain(Material mat)
        {
            MaterialDataManager dataManager = new MaterialDataManager(mat);
            if (!dataManager.AssetExists(Constants.LAYERED_DATA_FILE_NAME))
                return true; // Shouldnt happen but here just incase

            RepetitionlessLayeredDataSO layeredData = dataManager.LoadAsset<RepetitionlessLayeredDataSO>(Constants.LAYERED_DATA_FILE_NAME);
            return layeredData.LayerMode == ELayerMode.TerrainLayers;
        }

        private static string GetNewRepetitionlessShaderName(Material mat, string oldGuid)
        {
            switch(oldGuid) {
                // BIRP - Base
                case "2e442388a9557679a8eaf0a9230f4c74":
                    return Constants.SHADER_FOLDER + Constants.SHADER_FOLDER_BIRP + Constants.SHADER_MATERIAL_NAME_REGULAR;
                // BIRP - Layered
                case "d9f3c26619b9fff3fba543d61ffaa00f":
                    return MaterialIsTerrain(mat) ?
                            Constants.SHADER_FOLDER + Constants.SHADER_FOLDER_BIRP + Constants.SHADER_MATERIAL_NAME_LAYERED_TERRAIN :
                            Constants.SHADER_FOLDER + Constants.SHADER_FOLDER_BIRP + Constants.SHADER_MATERIAL_NAME_LAYERED_LIT;
                // URP - Base
                case "2668dc74239987d2abd177adfc8716b8":
                    return Constants.SHADER_FOLDER + Constants.SHADER_FOLDER_URP + Constants.SHADER_MATERIAL_NAME_REGULAR;
                // URP - Layered
                case "cb3ba3cb005025b548d1daf1d3c2b48f":
                    return MaterialIsTerrain(mat) ?
                            Constants.SHADER_FOLDER + Constants.SHADER_FOLDER_URP + Constants.SHADER_MATERIAL_NAME_LAYERED_TERRAIN :
                            Constants.SHADER_FOLDER + Constants.SHADER_FOLDER_URP + Constants.SHADER_MATERIAL_NAME_LAYERED_LIT;
                // HDRP - Base
                case "76352105cf8ad4a27979f0a922d49682":
                    return Constants.SHADER_FOLDER + Constants.SHADER_FOLDER_HDRP + Constants.SHADER_MATERIAL_NAME_REGULAR;
                // HDRP - Layered
                case "e182ab88072826c43bb34ab1197f5041":
                    return MaterialIsTerrain(mat) ?
                            Constants.SHADER_FOLDER + Constants.SHADER_FOLDER_HDRP + Constants.SHADER_MATERIAL_NAME_LAYERED_TERRAIN :
                            Constants.SHADER_FOLDER + Constants.SHADER_FOLDER_HDRP + Constants.SHADER_MATERIAL_NAME_LAYERED_LIT;
            }

            return "";
        }

        // Upgrade all materials pre 1.4.0 to the new shaders
        private static void UpdateOldMaterials()
        {
            // Get all materials in project
            string[] materialGuids = AssetDatabase.FindAssets("t:Material", new string[] { "Assets" });
            List<Material> repetitionlessMaterials = new List<Material>();

            foreach (string guid in materialGuids) {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path == "") continue;

                // Read the material folder to find the shader guid
                string projectFolder = Path.GetFullPath(Path.Combine(Application.dataPath, "../"));
                string fileText = File.ReadAllText(projectFolder + path);
                if (fileText == "") continue;

                string oldShaderGuid = Regex.Match(fileText, @"m_Shader.+guid:\s([0-9,a-f]{32})").Groups[1].Value;
                if (oldShaderGuid == "") continue;

                // If its an old repetitionless shader, convert it
                Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) continue;

                string newShader = GetNewRepetitionlessShaderName(mat, oldShaderGuid);
                if (newShader == "") continue;

                mat.shader = Shader.Find(newShader);

                // Add new keywords if applicable
                MaterialDataManager dataManager = new MaterialDataManager(mat);
                if (!dataManager.AssetExists(Constants.PROPERTIES_FILE_NAME))
                    continue;

                RepetitionlessMaterialDataSO data = dataManager.LoadAsset<RepetitionlessMaterialDataSO>(Constants.PROPERTIES_FILE_NAME);

                bool isLayered = newShader.Contains(Constants.SHADER_MATERIAL_NAME_LAYERED);

                RepetitionlessMaterialUtilities.UpdateDistanceBlendKeyword(mat, data);
                RepetitionlessMaterialUtilities.UpdateMaterialBlendKeyword(mat, data);
                RepetitionlessMaterialUtilities.UpdateVariationKeyword(mat, isLayered ? Constants.MAX_LAYERS_TERRAIN : Constants.MAX_LAYERS_REGULAR, data);

                // Get and apply max layers
                if (isLayered) {
                    // Get the layer count
                    // For control texture materials, it will have to be manually increased
                    int layerCount = 4;
                    if (dataManager.AssetExists(Constants.TERRAIN_DATA_FILE_NAME)) {
                        RepetitionlessTerrainDataSO terrainData = dataManager.LoadAsset<RepetitionlessTerrainDataSO>(Constants.TERRAIN_DATA_FILE_NAME);
                        layerCount = terrainData.TerrainLayers.Count;
                    }

                    // Update the layer count and keyword
                    RepetitionlessLayeredDataSO layeredData = dataManager.LoadAsset<RepetitionlessLayeredDataSO>(Constants.LAYERED_DATA_FILE_NAME);
                    layeredData.UpdateMaxLayers(layerCount);
                }

                // Save the scene to reload terrains in the active scene
                EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            }
        }

#endregion
    }
}
#endif