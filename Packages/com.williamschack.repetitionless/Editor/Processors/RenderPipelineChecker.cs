#if UNITY_EDITOR
using UnityEngine;
using UnityEditor.PackageManager;
using System.IO;

namespace Repetitionless.Editor.Processors
{
    using Config;

    [UnityEditor.InitializeOnLoad]
    public static class RenderPipelineChecker
    {
        private const string URP_PACKAGE_NAME = "com.unity.render-pipelines.universal";
        private const string HDRP_PACKAGE_NAME = "com.unity.render-pipelines.high-definition";

        static RenderPipelineChecker()
        {
            if (Application.isBatchMode)
                return;

            Events.registeredPackages += OnPackagesRegistered;
        }

        private static void OnPackagesRegistered(PackageRegistrationEventArgs args)
        {
            foreach (PackageInfo added in args.added) {
                if (added.name == URP_PACKAGE_NAME)
                    UnhideURP();

                if (added.name == HDRP_PACKAGE_NAME)
                    UnhideHDRP();
            }

            foreach (PackageInfo removed in args.removed) {
                if (removed.name == URP_PACKAGE_NAME)
                    HideURP();

                if (removed.name == HDRP_PACKAGE_NAME)
                    HideHDRP();
            }
        }

        /// <param name="forceCheck">
        /// Overrides the RPActive pref
        /// </param>
        internal static void CheckInstalledPackages(bool forceCheck = false)
        {
            bool urpFound = false;
            bool hdrpFound = false;
            foreach (PackageInfo package in PackageInfo.GetAllRegisteredPackages()) {
                if (package.name == URP_PACKAGE_NAME)  urpFound = true;
                if (package.name == HDRP_PACKAGE_NAME) hdrpFound = true;
            }

            if (urpFound)  UnhideURP(forceCheck);
            else           HideURP(forceCheck);
            if (hdrpFound) UnhideHDRP(forceCheck);
            else           HideHDRP(forceCheck);
        }

        private static void UnhideFolder(string folderPath)
        {
            if (!Directory.Exists(folderPath + "~") || Directory.Exists(folderPath))
                return;

            Directory.Move(folderPath + "~", folderPath);

            UnityEditor.AssetDatabase.Refresh();
        }

        private static void HideFolder(string folderPath)
        {
            if (!Directory.Exists(folderPath) || Directory.Exists(folderPath + "~"))
                return;

            Directory.Move(folderPath, folderPath + "~");
            File.Delete(folderPath + ".meta");

            UnityEditor.AssetDatabase.Refresh();
        }

        private static string GetPathURP()
        {
            string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../"));
            return projectPath + "/" + Constants.PACKAGE_PATH + "/Shaders/URP";
        }

        private static string GetPathHDRP()
        {
            string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../"));
            return projectPath + "/" + Constants.PACKAGE_PATH + "/Shaders/HDRP";
        }

        private static void UnhideURP(bool forceCheck = false)
        {
            if (RepetitionlessPrefs.Data.URPActive && !forceCheck)
                return;

            string folderPath = GetPathURP();
            UnhideFolder(folderPath);

            RepetitionlessPrefs.UpdatePrefs((p) => {
                p.URPActive = true; 
            });
        }

        private static void HideURP(bool forceCheck = false)
        {
            if (!RepetitionlessPrefs.Data.URPActive && !forceCheck)
                return;

            string folderPath = GetPathURP();

            if (Directory.Exists(folderPath) && Directory.Exists(folderPath + "~")) {
                // We can assume that changes have been made due to an update and have not been properly merged
                MergeURP();
            }

            HideFolder(folderPath);

            RepetitionlessPrefs.UpdatePrefs((p) => {
                p.URPActive = false; 
            });
        }

        private static void UnhideHDRP(bool forceCheck = false)
        {
            if (RepetitionlessPrefs.Data.HDRPActive && !forceCheck)
                return;

            string folderPath = GetPathHDRP();
            UnhideFolder(folderPath);

            RepetitionlessPrefs.UpdatePrefs((p) => {
                p.HDRPActive = true; 
            });

            PostProjectOpen.CheckAndUpdateHDRPTerrainShader(true);
        }

        private static void HideHDRP(bool forceCheck = false)
        {
            if (!RepetitionlessPrefs.Data.HDRPActive && !forceCheck)
                return;

            string folderPath = GetPathHDRP();

            if (Directory.Exists(folderPath) && Directory.Exists(folderPath + "~")) {   
                // We can assume that changes have been made due to an update and have not been properly merged
                MergeHDRP();
            }

            HideFolder(folderPath);

            RepetitionlessPrefs.UpdatePrefs((p) => {
                p.HDRPActive = false; 
            });
        }

        /// <summary>
        /// All files from source will overwrite files in new path
        /// </summary>
        /// <param name="sourceFolderPath">
        /// The folder with the new files to use
        /// </param>
        /// <param name="newFolderPath">
        /// The folder with the old files that will be overwritten for the new files
        /// </param>
        private static void MergeFolders(string sourceFolderPath, string newFolderPath)
        {
            if (!Directory.Exists(sourceFolderPath))
                return;

            // Move the directory if no folder exists to merge
            if (!Directory.Exists(newFolderPath)) {
                Directory.Move(sourceFolderPath, newFolderPath);
                return;
            }

            // Merge subdirectories
            foreach (string dir in Directory.GetDirectories(sourceFolderPath)) {
                string dirName = Path.GetFileName(dir);
                string destDir = Path.Combine(newFolderPath, dirName);
                MergeFolders(dir, destDir);
            }

            // Overwrite files in this directory
            foreach (string file in Directory.GetFiles(sourceFolderPath)) {
                string fileName = Path.GetFileName(file);
                string destFile = Path.Combine(newFolderPath, fileName);

                if (File.Exists(destFile))
                    File.Delete(destFile);

                File.Move(file, destFile);
            }

            // Delete this folder
            Directory.Delete(sourceFolderPath, true);
        }

        internal static void MergeNewShaderFolders()
        {
            // These files will exist if they have had changes
            // No need to check respective folders if they are still hidden

            if (RepetitionlessPrefs.Data.URPActive) {
                MergeURP();
            }

            if (RepetitionlessPrefs.Data.HDRPActive) {
                MergeHDRP();
            }

            UnityEditor.AssetDatabase.Refresh();
        }

        internal static void MergeURP()
        {
            string folderPath = GetPathURP();
            MergeFolders(folderPath + "~", folderPath);
        }

        internal static void MergeHDRP()
        {
            string folderPath = GetPathHDRP();
            MergeFolders(folderPath + "~", folderPath);

            // Check TerrainNew~, TerrainOld~, they should have been moved to the main HDRP folder
            string oldTerrainPath = folderPath + "/TerrainOld";
            string newTerrainPath = folderPath + "/TerrainNew";
            if (Directory.Exists(oldTerrainPath))
                MergeFolders(oldTerrainPath + "~", oldTerrainPath);
            if (Directory.Exists(newTerrainPath))
                MergeFolders(newTerrainPath + "~", newTerrainPath);
        }
    }
}
#endif
