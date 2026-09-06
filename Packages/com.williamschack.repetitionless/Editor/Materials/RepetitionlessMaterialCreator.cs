#if UNITY_EDITOR
using System.Reflection;

using UnityEditor;
using UnityEngine;

using Repetitionless.Runtime.Variables;
using Repetitionless.Runtime.Utilities;

namespace Repetitionless.Editor.Materials
{
    using Data;

    /// <summary>
    /// Holds all the data related to a repetitionless material
    /// </summary>
    public struct MaterialDataObjects
    {
        /// <summary>
        /// The material
        /// </summary>
        public Material Material;

        /// <summary>
        /// The data manager
        /// </summary>
        public MaterialDataManager DataManager;

        /// <summary>
        /// The texture data scriptable object
        /// </summary>
        public RepetitionlessTextureDataSO TextureDataSO;

        /// <summary>
        /// The material data scriptable object
        /// </summary>
        public RepetitionlessMaterialDataSO MaterialDataSO;
    }

    /// <summary>
    /// Used to create new repetitionless materials
    /// </summary>
    public static class RepetitionlessMaterialCreator
    {
        private const string DEFAULT_MATERIAL_NAME_REGULAR = "RepetitionlessMaterial.mat";
        private const string DEFAULT_MATERIAL_NAME_TERRAIN = "RepetitionlessLayeredMaterial.mat";
        private const string PROGRESS_BAR_TITLE = "Updating Material";

        private static string GetCurrentProjectWindowPath()
        {
            MethodInfo tryGetActiveFolderPath = typeof(ProjectWindowUtil).GetMethod("TryGetActiveFolderPath", BindingFlags.Static | BindingFlags.NonPublic);
    
            object[] args = new object[] { null };
            bool found = (bool)tryGetActiveFolderPath.Invoke(null, args);
            if (found) return (string)args[0];

            // If cannot get folder resort to project root
            return "Assets";
        }

        [MenuItem("Window/Repetitionless/Create Material", priority = 2)]
        [MenuItem("Assets/Create/Repetitionless/Create Material", priority = 102)]
        private static void CreateMaterialToolbar()
        {
            EditorApplication.delayCall += () => { CreateMaterialAtCurrentFolder(); };
        }

        [MenuItem("Window/Repetitionless/Create Layered Material", priority = 3)]
        [MenuItem("Assets/Create/Repetitionless/Create Layered Material", priority = 102)]
        private static void CreateTerrainMaterialToolbar()
        {
            EditorApplication.delayCall += () => { CreateTerrainMaterialAtCurrentFolder(); };
        }

        private static string GetShaderFolder(ERenderPipeline pipeline)
        {
            if (pipeline == ERenderPipeline.Unknown)
                return "";

            string shaderFolder = Constants.SHADER_FOLDER;
            switch (pipeline) {
                case ERenderPipeline.Builtin:
                    shaderFolder += Constants.SHADER_FOLDER_BIRP;
                    break;
                case ERenderPipeline.URP:
                    shaderFolder += Constants.SHADER_FOLDER_URP;
                    break;
                case ERenderPipeline.HDRP:
                    shaderFolder += Constants.SHADER_FOLDER_HDRP;
                    break;
            }

            return shaderFolder;
        }

        private static Shader GetShader(string name)
        {
            Shader shader = Shader.Find(name);
            if (shader == null) {
                Debug.LogError("Could not find shader: " + name);
                return null;
            }

            return shader;
        }

        private static void PingAsset(Object asset)
        {
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        /// <summary>
        /// Creates a repetitionless material
        /// </summary>
        /// <param name="pipeline">
        /// The materials render pipeline
        /// </param>
        /// <param name="folderPath">
        /// The folder to save the material to
        /// </param>
        /// <param name="fileName">
        /// The file name for the material<br />
        /// Must include the extension .mat
        /// </param>
        /// <param name="ping">
        /// If the material will be selected and pinged in the project window after creation
        /// </param>
        /// <returns>
        /// The data objects for the created material
        /// </returns>
        public static MaterialDataObjects CreateMaterial(ERenderPipeline pipeline, string folderPath, string fileName = DEFAULT_MATERIAL_NAME_REGULAR, bool ping = true)
        {
            MaterialDataObjects materialDataObjects = new MaterialDataObjects();

            string shaderName = GetShaderFolder(pipeline);
            if (shaderName == "") return materialDataObjects;

            shaderName += Constants.SHADER_MATERIAL_NAME_REGULAR;
            Shader shader = GetShader(shaderName);
            if (shader == null) return materialDataObjects;

            Material material = new Material(shader);

            string assetPath = folderPath + "/" + fileName;
            assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);

            AssetDatabase.CreateAsset(material, assetPath);
            materialDataObjects = SetupMaterial(material, Constants.MAX_LAYERS_REGULAR);

            if (ping)
                PingAsset(material);

            return materialDataObjects;
        }

        /// <summary>
        /// Creates a repetitionless material<br />
        /// Uses the currently selected render pipeline
        /// </summary>
        /// <param name="folderPath">
        /// The folder to save the material to
        /// </param>
        /// <param name="fileName">
        /// The file name for the material<br />
        /// Must include the extension .mat
        /// </param>
        /// <param name="ping">
        /// If the material will be selected and pinged in the project window after creation
        /// </param>
        /// <returns>
        /// The data objects for the created material
        /// </returns>
        public static MaterialDataObjects CreateMaterial(string folderPath, string fileName = DEFAULT_MATERIAL_NAME_REGULAR, bool ping = true)
        {
            ERenderPipeline currentPipeline = RenderPipelineUtilities.GetActiveRenderPipeline();
            return CreateMaterial(currentPipeline, folderPath, fileName, ping);
        }

        /// <summary>
        /// Creates a repetitionless material<br />
        /// Uses the currently selected render pipeline<br />
        /// Saves to the currently opened folder in the project window
        /// </summary>
        /// <param name="ping">
        /// If the material will be selected and pinged in the project window after creation
        /// </param>
        /// <returns>
        /// The data objects for the created material
        /// </returns>
        public static MaterialDataObjects CreateMaterialAtCurrentFolder(bool ping = true)
        {
            string path = GetCurrentProjectWindowPath();
            return CreateMaterial(path, ping: ping);
        }

        /// <summary>
        /// Creates a repetitionless terrain material
        /// </summary>
        /// <param name="pipeline">
        /// The materials render pipeline
        /// </param>
        /// <param name="folderPath">
        /// The folder to save the material to
        /// </param>
        /// <param name="fileName">
        /// The file name for the material<br />
        /// Must include the extension .mat
        /// </param>
        /// <param name="ping">
        /// If the material will be selected and pinged in the project window after creation
        /// </param>
        /// <returns>
        /// The data objects for the created material
        /// </returns>
        public static MaterialDataObjects CreateTerrainMaterial(ERenderPipeline pipeline, string folderPath, string fileName = DEFAULT_MATERIAL_NAME_TERRAIN, bool ping = true)
        {
            MaterialDataObjects materialDataObjects = new MaterialDataObjects();

            string shaderName = GetShaderFolder(pipeline);
            if (shaderName == "") return materialDataObjects;

            shaderName += Constants.SHADER_MATERIAL_NAME_LAYERED_TERRAIN;
            Shader shader = GetShader(shaderName);
            if (shader == null) return materialDataObjects;

            Material material = new Material(shader);

            string assetPath = folderPath + "/" + fileName;
            assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);

            AssetDatabase.CreateAsset(material, assetPath);
            materialDataObjects = SetupMaterial(material, Constants.MAX_LAYERS_TERRAIN, (RepetitionlessMaterialDataSO data) => { RepetitionlessLayeredMaterialUtilities.SetupProperties(material, data); });
            RepetitionlessLayeredMaterialUtilities.SetupLayeredData(materialDataObjects.DataManager);
            RepetitionlessLayeredMaterialUtilities.SetupTerrainData(materialDataObjects.DataManager);

            if (ping)
                PingAsset(material);

            return materialDataObjects;
        }

        /// <summary>
        /// Creates a repetitionless terrain material<br />
        /// Uses the currently selected render pipeline
        /// </summary>
        /// <param name="folderPath">
        /// The folder to save the material to
        /// </param>
        /// <param name="fileName">
        /// The file name for the material<br />
        /// Must include the extension .mat
        /// </param>
        /// <param name="ping">
        /// If the material will be selected and pinged in the project window after creation
        /// </param>
        /// <returns>
        /// The data objects for the created material
        /// </returns>
        public static MaterialDataObjects CreateTerrainMaterial(string folderPath, string fileName = DEFAULT_MATERIAL_NAME_TERRAIN, bool ping = true)
        {
            ERenderPipeline currentPipeline = RenderPipelineUtilities.GetActiveRenderPipeline();
            return CreateTerrainMaterial(currentPipeline, folderPath, fileName, ping);
        }

        /// <summary>
        /// Creates a repetitionless terrain material<br />
        /// Uses the currently selected render pipeline<br />
        /// Saves to the currently opened folder in the project window
        /// </summary>
        /// <param name="ping">
        /// If the material will be selected and pinged in the project window after creation
        /// </param>
        /// <returns>
        /// The data objects for the created material
        /// </returns>
        public static MaterialDataObjects CreateTerrainMaterialAtCurrentFolder(bool ping = true)
        {
            string path = GetCurrentProjectWindowPath();
            return CreateTerrainMaterial(path, ping: ping);
        }

        /// <summary>
        /// Loads or Creates the data objects for a repetitionless material
        /// </summary>
        /// <param name="mat">
        /// The material to setup
        /// </param>
        /// <param name="maxLayers">
        /// The max amount of layers that can be used
        /// </param>
        /// <param name="onPropertiesCreatedCallback">
        /// Callback for when the material properties are created
        /// </param>
        /// <returns>
        /// The data objects for the material
        /// </returns>
        public static MaterialDataObjects SetupMaterial(Material mat, int maxLayers, System.Action<RepetitionlessMaterialDataSO> onPropertiesCreatedCallback = null)
        {
            MaterialDataManager dataManager = new MaterialDataManager(mat);
            RepetitionlessTextureDataSO textureData = null;
            RepetitionlessMaterialDataSO materialProperties = null;

            try {
                if (dataManager.AssetExists(Constants.TEXTURE_DATA_FILE_NAME)) {
                    textureData = dataManager.LoadAsset<RepetitionlessTextureDataSO>(Constants.TEXTURE_DATA_FILE_NAME);

                    if (textureData.LayersTextureData.Length == 0) {
                        textureData.Init(maxLayers);
                        textureData.Save();
                    }
                } else {
                    EditorUtility.DisplayProgressBar(PROGRESS_BAR_TITLE, "Creating Texture Data", 0.2f);

                    AssetDatabase.StartAssetEditing();
                    try {
                        textureData = ScriptableObject.CreateInstance<RepetitionlessTextureDataSO>();
                        dataManager.CreateAsset(textureData, Constants.TEXTURE_DATA_FILE_NAME);
                        textureData.Init(maxLayers);

                        textureData.Save();
                        AssetDatabase.SaveAssetIfDirty(textureData);
                    } finally {
                        AssetDatabase.StopAssetEditing();
                    }
                }

                if (dataManager.AssetExists(Constants.PROPERTIES_FILE_NAME)) {
                    materialProperties = dataManager.LoadAsset<RepetitionlessMaterialDataSO>(Constants.PROPERTIES_FILE_NAME);

                    if (materialProperties.Data.Length == 0) {
                        materialProperties.Init(maxLayers);
                        materialProperties.Save();
                    }
                } else {
                    EditorUtility.DisplayProgressBar(PROGRESS_BAR_TITLE, "Creating Properties", 0.5f);

                    AssetDatabase.StartAssetEditing();
                    try {
                        materialProperties = ScriptableObject.CreateInstance<RepetitionlessMaterialDataSO>();
                        dataManager.CreateAsset(materialProperties, Constants.PROPERTIES_FILE_NAME);
                        materialProperties.Init(maxLayers);
                        
                        RepetitionlessMaterialUtilities.SetNoiseQuality(mat, materialProperties.NoiseQuality);

                        materialProperties.Save();
                        AssetDatabase.SaveAssetIfDirty(materialProperties);
                    } finally {
                        AssetDatabase.StopAssetEditing();
                    }

                    EditorUtility.DisplayProgressBar(PROGRESS_BAR_TITLE, "Writing Properties", 0.8f);
                    materialProperties.UpdateMaterialTexture(mat, 0);

                    if (onPropertiesCreatedCallback != null)
                        onPropertiesCreatedCallback(materialProperties);
                }
            } finally {
                // Clear progress in case of error to prevent infinite progress bar
                EditorUtility.ClearProgressBar();
            }


            return new MaterialDataObjects {
                Material = mat,
                DataManager = dataManager,
                TextureDataSO = textureData,
                MaterialDataSO = materialProperties
            };
        }
    }
}
#endif