#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace Repetitionless.Editor.Updating
{
    using Config;

    internal static class Updater
    {
        public static void UpdatePackage()
        {
            switch (RepetitionlessPackageInfo.PackageSource) {
                case RepetitionlessPackageInfo.EPackageSource.AssetStore:
                    UpdateAssetStore();
                    break;
                case RepetitionlessPackageInfo.EPackageSource.Itch:
                    UpdateItch();
                    break;
                case RepetitionlessPackageInfo.EPackageSource.Unknown:
                    UpdateUnknown();
                    break;
            }
        }

        private static void UpdateAssetStore(bool displayDialog = true)
        {
            if (displayDialog) {
                EditorUtility.DisplayDialog(
                    "Updating Repetitionless",
                    "You will need to manually update and reimport Repetitionless from\nPackage Manager > My Assets > Repetitionless",
                    "Ok", ""
                );
            }

#if UNITY_6000_0_OR_NEWER
            EditorApplication.ExecuteMenuItem("Window/Package Management/Package Manager");
#else
            EditorApplication.ExecuteMenuItem("Window/Package Manager");
#endif
        }

        private static void UpdateItch(bool displayDialog = true)
        {
            bool openItch = !displayDialog;
            if (displayDialog) {
                openItch = EditorUtility.DisplayDialog(
                    "Updating Repetitionless",
                    "You will need to manually download the updated package and reimport it from the itch page which will automatically open now",
                    "Ok", "Cancel"
                );
            }

            if (openItch) Application.OpenURL(Constants.ASSET_ITCH_URL);
        }

        private static void UpdateUnknown()
        {
            int selected = EditorUtility.DisplayDialogComplex(
                "Updating Repetitionless",
                "You will need to manually download the updated package and reimport it\nAsset Store: Package Manager > My Assets > Repetitionless\nItch: Download the updated package and reimport it",
                "Itch.io",
                "Asset Store",
                "Cancel"
            );

            switch (selected) {
                case 0: // Itch.io
                    UpdateItch(false);
                    break;
                case 1: // Asset Store
                    UpdateAssetStore(false);
                    break;
            }
        }
    }
}
#endif