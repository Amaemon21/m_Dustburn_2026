#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;

namespace Repetitionless.Editor.Data
{
    /// <summary>
    /// Manages the data folder for a repetitionless material<br />
    /// Stores the data in a folder accompanying the material
    /// </summary>
    public class MaterialDataManager
    {
        private const string DATA_FOLDER_SUFFIX = "_RepetitionlessData";

        private Material _material;

        /// <summary>
        /// The material this is handling data for
        /// </summary>
        public Material Material => _material;

        /// <summary>
        /// MaterialDataManager Constructor
        /// </summary>
        /// <param name="material">
        /// The material to manage
        /// </param>
        public MaterialDataManager(Material material)
        {
            _material = material;
        }

        /// <summary>
        /// Gets a data manager from the path of an asset inside the data folder<br />
        /// If the main material cannot be found, Material will be null
        /// </summary>
        /// <param name="path">
        /// The path of the asset from inside the data folder
        /// </param>
        public MaterialDataManager(Object dataAsset)
        {
            string assetPath = AssetDatabase.GetAssetPath(dataAsset);

            // Get the folder paths that material is contained in
            DirectoryInfo dataFolder = new DirectoryInfo(Path.GetDirectoryName(assetPath));
            DirectoryInfo parentFolder = dataFolder.Parent;

            // Convert parent path to project path taking into account assets or packages
            DirectoryInfo projectPath = new DirectoryInfo(Application.dataPath);

            string projectRelativeParentPath = parentFolder.FullName.Replace(projectPath.Parent.FullName, "");
            projectRelativeParentPath = projectRelativeParentPath.Substring(1, projectRelativeParentPath.Length - 1); // Remove first /

            // Get Material
            string materialFileName = dataFolder.Name;
            materialFileName = materialFileName.Substring(0, materialFileName.Length - DATA_FOLDER_SUFFIX.Length); // Remove Data folder suffix
            materialFileName += ".mat";

            string materialPath = $"{projectRelativeParentPath}/{materialFileName}";

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);

            if (mat == null) {
                Debug.LogError("Unable to find material from data asset...");
                return;
            }

            _material = mat;
        }

#region Path
        /// <summary>
        /// Adds the folder suffix to the input name
        /// </summary>
        /// <param name="prefix">
        /// The input folder name
        /// </param>
        /// <returns>
        /// The new folder name
        /// </returns>
        public static string GenerateFolderName(string prefix)
        {
            return prefix + DATA_FOLDER_SUFFIX;
        }

        /// <summary>
        /// Gets the path of the data folder parent
        /// </summary>
        /// <returns>
        /// The parent folder path
        /// </returns>
        public string DataFolderParentPath()
        {
            string materialPath = AssetDatabase.GetAssetPath(_material);   
            if (materialPath == "") return "";

            int lastDirIndex = materialPath.LastIndexOf("/");
            return materialPath.Substring(0, lastDirIndex);
        }

        /// <summary>
        /// Gets the data folder name
        /// </summary>
        /// <returns>
        /// The data folder name
        /// </returns>
        public string DataFolderName()
        {
            return GenerateFolderName(_material.name);
        }

        /// <summary>
        /// Gets the data folder path
        /// </summary>
        /// <returns>
        /// The data folder path
        /// </returns>
        public string DataFolderPath()
        {
            string parentPath = DataFolderParentPath();
            if (parentPath == "") return "";

            return $"{parentPath}/{DataFolderName()}";
        }

        private bool DataFolderCreated()
        {
            return AssetDatabase.IsValidFolder(DataFolderPath());
        }

        private bool DataFolderEmpty()
        {
            return !Directory.EnumerateFiles(DataFolderPath()).Any();
        }

        private bool CreateDataFolder()
        {
            if (!DataFolderCreated()) {
                string folderGUID = AssetDatabase.CreateFolder(DataFolderParentPath(), DataFolderName());
                return folderGUID != "";
            }

            return true;
        }

        private bool DeleteDataFolder()
        {
            if (DataFolderCreated()) {
                string folderPath = DataFolderPath();
                bool empty = DataFolderEmpty();
                if (empty) AssetDatabase.DeleteAsset(folderPath);

                return empty;
            }

            return false;
        }
#endregion

#region Asset Management
        /// <summary>
        /// Creates an asset in the data folder
        /// </summary>
        /// <param name="asset">
        /// The asset to create
        /// </param>
        /// <param name="fileName">
        /// The file name of the asset
        /// </param>
        /// <param name="overwrite">
        /// If it will overwrite an asset with the same name if it exists
        /// </param>
        public void CreateAsset(Object asset, string fileName, bool overwrite = false)
        {
            bool folderExists = CreateDataFolder();
            if (!folderExists) return;

            string assetPath = $"{DataFolderPath()}/{fileName}";

            if (overwrite) AssetDatabase.DeleteAsset(assetPath);
            AssetDatabase.CreateAsset(asset, assetPath);
        }

        /// <summary>
        /// Loads an asset from the data folder
        /// </summary>
        /// <typeparam name="T">
        /// The asset type
        /// </typeparam>
        /// <param name="fileName">
        /// The filename of the asset
        /// </param>
        /// <returns>
        /// The asset
        /// </returns>
        public T LoadAsset<T>(string fileName) where T : Object
        {
            string dataFolderPath = DataFolderPath();
            if (dataFolderPath == "") return null;

            string assetPath = $"{dataFolderPath}/{fileName}";
            return (T)AssetDatabase.LoadAssetAtPath(assetPath, typeof(T));
        }

        /// <summary>
        /// Checks if an asset exists in the data folder with the given name
        /// </summary>
        /// <param name="fileName">
        /// The file name to check
        /// </param>
        /// <returns>
        /// If the asset exists
        /// </returns>
        public bool AssetExists(string fileName)
        {
            string assetPath = $"{DataFolderPath()}/{fileName}";
            string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../"));

            string fullPath = $"{projectPath}{assetPath}";
            return File.Exists(fullPath);
        }

        /// <summary>
        /// Deletes an asset in the data folder with the given name if it exists
        /// </summary>
        /// <param name="fileName">
        /// The asset file name
        /// </param>
        public void DeleteAsset(string fileName)
        {
            string assetPath = $"{DataFolderPath()}/{fileName}";

            if (AssetExists(fileName)) {
                AssetDatabase.DeleteAsset(assetPath);
                AssetDatabase.Refresh();
            }

            if (DataFolderEmpty())
                DeleteDataFolder();
        }
#endregion
    }
}
#endif