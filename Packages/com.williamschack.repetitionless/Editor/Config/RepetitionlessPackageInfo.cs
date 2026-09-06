#if UNITY_EDITOR
using System.Diagnostics;
using System.IO;
using UnityEditor.PackageManager;

namespace Repetitionless.Editor.Config
{
    /// <summary>
    /// Used to retrieve the package info for repetitionless
    /// </summary>
    internal static class RepetitionlessPackageInfo
    {
        public enum EPackageSource
        {
            Unchecked,
            Unknown,
            AssetStore,
            Itch
        }

        private const string PACKAGE_INFO_PATH = Constants.PACKAGE_PATH + "/package.json";

        private static PackageInfo _packageInfoCache;

        /// <summary>
        /// The repetitionless package info
        /// </summary>
        public static PackageInfo Info => _packageInfoCache ??= LoadPackageInfo();

        private static EPackageSource _packageSource = EPackageSource.Unchecked;

        /// <summary>
        /// Where the package was downloaded from<br />
        /// Cannot check before Unity 2023, in that case returns Unknown
        /// </summary>
        public static EPackageSource PackageSource {
            get {
                if (_packageSource == EPackageSource.Unchecked)
                    _packageSource = GetPackageSource();

                return _packageSource;
            }
        }

        private static PackageInfo LoadPackageInfo()
        {
            return PackageInfo.FindForAssetPath(PACKAGE_INFO_PATH);
        }

        // Loads a meta file and checks if it has the productId
        // There is no way to check before Unity 2023, returns unknown in that case
        private static EPackageSource GetPackageSource()
        {
#if UNITY_2023_1_OR_NEWER
            string metaFilePath = UnityEditor.AssetDatabase.GetTextMetaFilePathFromAssetPath(PACKAGE_INFO_PATH);
            string metaText = File.ReadAllText(metaFilePath);

            if (metaText.Contains("AssetOrigin:") && 
                metaText.Contains($"productId: {Constants.PACKAGE_ID}")) {
                return EPackageSource.AssetStore;
            }

            return EPackageSource.Itch;
#else
            return EPackageSource.Unknown;
#endif
        }
    }
}
#endif