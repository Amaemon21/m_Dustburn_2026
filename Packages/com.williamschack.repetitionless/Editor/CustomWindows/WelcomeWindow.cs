#if UNITY_EDITOR
using System.IO;
using UnityEngine;
using UnityEditor;
using System;

using Repetitionless.Runtime.Variables;
using Repetitionless.Runtime.Utilities;

namespace Repetitionless.Editor.CustomWindows
{
    using Data;
    using Config;
    using Updating;
    using Processors;
    using Utilities.GUI;

    /// <summary>
    /// The welcome window that is shown when first installing the package
    /// </summary>
    public class WelcomeWindow : EditorWindow
    {
        private const string LOGO_FILE_NAME = "repetitionless_WelcomeLogo";
        private const int LOGO_HEIGHT = 60;
        private const int LOGO_PADDING = 3;
        private const int LOGO_BACKGROUND_PADDING = 4;
        private const int SETTINGS_WIDTH_PADDING = 10;

        private Texture _logoTextureDark;
        private Texture _logoTextureLight;
        private Color _logoBackgroundDarkColour;
        private Color _logoBackgroundLightColour;

        private GUIStyle _headerStyle;
        private GUIStyle _boldLabelStyle;
        private GUIStyle _richBoldLabelStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _largeButtonStyle;

        private bool _stylesSetup = false;

        private bool _updateAvailable = false;
        private string _remoteVersion = "";

        private bool _showWelcomeMessage = false;
        private bool _showUpdateMessage = false;

        private int _toolbarIndex = 0;

        /// <summary>
        /// Opens the window
        /// </summary>
        [MenuItem("Window/Repetitionless/Open Window", priority = 0)]
        public static void Open()
        {
            Open(false, false);
        }

        /// <summary>
        /// Opens the window
        /// </summary>
        /// <param name="showWelcomeMessage">
        /// If the welcome message is shown
        /// </param>
        /// <param name="showUpdateMessage">
        /// If the update message is shown
        /// </param>
        public static void Open(bool showWelcomeMessage = false, bool showUpdateMessage = false)
        {
            WelcomeWindow window = GetWindow<WelcomeWindow>(false, "Repetitionless");
            window._showWelcomeMessage = showWelcomeMessage;
            window._showUpdateMessage = showUpdateMessage;

            window.Show();
        }

        private void CreateGUI()
        {
            _logoTextureDark = Resources.Load<Texture>($"{LOGO_FILE_NAME}_Dark");
            _logoTextureLight = Resources.Load<Texture>($"{LOGO_FILE_NAME}_Light");
            _logoBackgroundDarkColour = new Color(20 / 256f, 20 / 256f, 20 / 256f);
            _logoBackgroundLightColour = new Color(240 / 256f, 240 / 256f, 240 / 256f);

            _updateAvailable = UpdateChecker.UpdateAvailable($"v{RepetitionlessPackageInfo.Info.version}");
            if (_updateAvailable)
                _remoteVersion = UpdateChecker.GetLatestVersion();
        }

        private void SetupStyles()
        {
            _headerStyle = new GUIStyle("label");
            _headerStyle.fontStyle = FontStyle.Bold;
            _headerStyle.fontSize = 18;
            _headerStyle.wordWrap = true;

            _boldLabelStyle = new GUIStyle("label");
            _boldLabelStyle.alignment = TextAnchor.MiddleCenter;
            _boldLabelStyle.fontStyle = FontStyle.Bold;
            _boldLabelStyle.wordWrap = true;

            _richBoldLabelStyle = new GUIStyle("label");
            _richBoldLabelStyle.fontStyle = FontStyle.Bold;
            _richBoldLabelStyle.richText = true;

            _buttonStyle = new GUIStyle("button");
            _buttonStyle.fontStyle = FontStyle.Bold;
            _buttonStyle.fontSize = 14;

            _largeButtonStyle = new GUIStyle(_buttonStyle);
            _largeButtonStyle.fontSize = 18;
        }

        private void OnGUI()
        {
            // Named styles must be created in OnGUI
            if (!_stylesSetup) {
                SetupStyles();
                _stylesSetup = true;
            }

            DrawLogo();

            string[] toolbarOptions = {"Main", "Settings"};
            _toolbarIndex = GUILayout.Toolbar(_toolbarIndex, toolbarOptions, GUILayout.Height(24));
            //GUILayout.Space(2);
            GUIUtilities.BeginBackgroundVertical();
            GUILayout.Space(10);

            switch (_toolbarIndex) {
                case 0:
                    DrawMainSection();
                    break;
                case 1:
                    DrawSettingsSection();
                    break;
            }

            GUILayout.FlexibleSpace();

            GUIUtilities.EndBackgroundVertical();

            if (_showUpdateMessage) {
                DrawUpdateButton();
            }

            EditorGUILayout.HelpBox("Thank for using Repetitionless! Pease consider leaving a review to support the asset and its development. Any feedback is appreciated!", MessageType.Info);

            switch (RepetitionlessPackageInfo.PackageSource) {
                case RepetitionlessPackageInfo.EPackageSource.Unknown:
                    EditorGUILayout.BeginHorizontal();
                    float buttonMinWidth = position.width / 2 - 15;
                    if (GUILayout.Button("Asset Store", GUILayout.MinWidth(buttonMinWidth))) Application.OpenURL(Constants.ASSET_STORE_REVIEW_URL);
                    if (GUILayout.Button("Itch.io",     GUILayout.MinWidth(buttonMinWidth))) Application.OpenURL(Constants.ASSET_ITCH_URL);
                    EditorGUILayout.EndHorizontal();
                    break;
                case RepetitionlessPackageInfo.EPackageSource.AssetStore:
                    if (GUILayout.Button("Leave A Review")) Application.OpenURL(Constants.ASSET_STORE_REVIEW_URL);
                    break;
                case RepetitionlessPackageInfo.EPackageSource.Itch:
                    if (GUILayout.Button("Leave A Review")) Application.OpenURL(Constants.ASSET_ITCH_URL);
                    break;
            }
            
            GUIUtilities.BeginBackgroundHorizontal();
    
            GUILayout.Label($"v{RepetitionlessPackageInfo.Info.version}", _boldLabelStyle);

            GUILayout.FlexibleSpace();
            GUIUtilities.EndBackgroundHorizontal();
        }

        private void DrawMainSection()
        {
            if (_showWelcomeMessage) {
                GUIUtilities.BeginBackgroundVertical();
                GUILayout.Label("Welcome to repetitionless! To get started view the getting started page in the documentation for instructions on how to use the asset, or import the samples for examples. Please also consider leaving a review to support the asset and its development, any feedback is appreciated!", _boldLabelStyle);
                GUIUtilities.EndBackgroundVertical();
                GUILayout.Space(10);
            }

            if (_showUpdateMessage) {
                EditorGUILayout.HelpBox($"An update is available for Repetitionless (v{RepetitionlessPackageInfo.Info.version} > {_remoteVersion}). Click the button at the bottom of the window to update", MessageType.Info);
                GUILayout.Space(10);

            }

            float buttonMinWidth = position.width / 2 - 15;

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Online Documentation",  GUILayout.MinWidth(buttonMinWidth))) OpenDocumentation(false);
            if (GUILayout.Button("Offline Documentation", GUILayout.MinWidth(buttonMinWidth))) OpenDocumentation(true);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Join Discord",  GUILayout.MinWidth(buttonMinWidth))) Application.OpenURL(Constants.DISCORD_INVITE_LINK_ANNOUNCEMENTS);
            if (GUILayout.Button("Unity Forum",   GUILayout.MinWidth(buttonMinWidth))) Application.OpenURL(Constants.UNITY_FORUM_URL);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Contact",      GUILayout.MinWidth(buttonMinWidth))) Application.OpenURL(Constants.SUPPORT_EMAIL_URL);
            if (GUILayout.Button("Report Issue", GUILayout.MinWidth(buttonMinWidth))) {
                int issueResponse = EditorUtility.DisplayDialogComplex(
                    "Report An Issue",
                    "Where would you like to report the issue? You can either create an issue on the github issues page, or create a support post in the discord. Both require an account.",
                    "Discord",
                    "Cancel",
                    "Github"
                );

                switch (issueResponse) {
                    case 0:
                        Application.OpenURL(Constants.DISCORD_INVITE_LINK_SUPPORT);
                        break;
                    case 2:
                        Application.OpenURL(Constants.GITHUB_NEW_ISSUE_URL);
                        break;
                }
            }
            
            GUILayout.EndHorizontal();

            if (GUILayout.Button("Import Samples")) ImportSamples();
        }

        private void DrawSettingsSection()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(SETTINGS_WIDTH_PADDING / 2);
            GUILayout.BeginVertical();

            GUILayout.Label("General", _headerStyle);
            GUIUtilities.BeginBackgroundVertical();

            EditorGUI.BeginChangeCheck();
            bool openWindowOnUpdate = GUILayout.Toggle(RepetitionlessPrefs.Data.OpenWindowOnUpdate, "Show window on available update");
            if (EditorGUI.EndChangeCheck()) {
                RepetitionlessPrefs.UpdatePrefs((p) => {
                    p.OpenWindowOnUpdate = openWindowOnUpdate;
                });
            }

            EditorGUI.BeginChangeCheck();
            bool savePaintingSettings = GUILayout.Toggle(PainterPrefs.Data.SaveSettings, "Save painting settings");
            if (EditorGUI.EndChangeCheck()) {
                PainterPrefs.UpdatePrefs((p) => {
                    p.SaveSettings = savePaintingSettings;
                });
            }

            GUIUtilities.EndBackgroundVertical();

            GUILayout.Space(10);
            
            GUILayout.Label("Shaders", _headerStyle);
            GUIUtilities.BeginBackgroundVertical();

            string urpActiveText = RepetitionlessPrefs.Data.URPActive ? "<color=green>Enabled</color>" : "<color=#fc3c3c>Disabled</color>";
            string hdrpActiveText = RepetitionlessPrefs.Data.HDRPActive ? "<color=green>Enabled</color>" : "<color=#fc3c3c>Disabled</color>";
            string shadersActiveText = $"URP {urpActiveText} | HDRP {hdrpActiveText}";
            float size = _richBoldLabelStyle.CalcSize(new GUIContent(shadersActiveText)).x;
            
            GUILayout.Label(shadersActiveText, _richBoldLabelStyle);
            if (GUILayout.Button("Check Shader Folders", GUILayout.Width(size)))
                RenderPipelineChecker.CheckInstalledPackages(true);
            
            GUIUtilities.EndBackgroundVertical();

            GUILayout.Space(10);

            GUILayout.EndVertical();
            GUILayout.Space(SETTINGS_WIDTH_PADDING / 2);
            GUILayout.EndHorizontal();
        }

        private void DrawLogo()
        {
            bool darkMode = EditorGUIUtility.isProSkin;
            Texture texture = darkMode ? _logoTextureDark : _logoTextureLight;
            Color backgroundColour = darkMode ? _logoBackgroundDarkColour : _logoBackgroundLightColour;

            Rect logoBackgroundRect = GUILayoutUtility.GetRect(1, LOGO_HEIGHT);
            logoBackgroundRect.x += LOGO_BACKGROUND_PADDING;
            logoBackgroundRect.width -= LOGO_BACKGROUND_PADDING * 2;
            logoBackgroundRect.y += LOGO_BACKGROUND_PADDING;

            EditorGUI.DrawRect(logoBackgroundRect, backgroundColour);

            Rect logoRect = logoBackgroundRect;
            logoRect.yMin += LOGO_PADDING;
            logoRect.yMax -= LOGO_PADDING;
            
            GUI.DrawTexture(logoRect, texture, ScaleMode.ScaleToFit);

            GUILayout.Space(LOGO_BACKGROUND_PADDING);
        }

        private void DrawUpdateButton()
        {
            if (GUILayout.Button($"Update to {_remoteVersion}", _largeButtonStyle))
                Updater.UpdatePackage();
        }

        private string ProjectPath()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "../"));
        }

        private void OpenDocumentation(bool offline)
        {
            string url = "";
            if (offline) {
                url = $"{ProjectPath()}{Constants.LOCAL_DOCUMENTATION_PATH}";
            } else {
                url = Constants.DOCUMENTATION_URL;
            }

            Application.OpenURL(url);
        }

        private void ImportSamples()
        {
            // Get paths
            string projectPath = ProjectPath();

            string samplesCorePathFull = $"{projectPath}{Constants.SAMPLES_PATH_ASSETS}";
            string samplesPipelinePathFull = projectPath;

            string targetBasePath = $"Assets/Samples/Repetitionless/{RepetitionlessPackageInfo.Info.version}";
            string targetBasePathFull = $"{projectPath}{targetBasePath}";

            string targetCorePathFull = $"{targetBasePath}/Core Sample Assets";
            string targetPipelinePath = $"{targetBasePath}/";

            string renderPipelineName = "";

            ERenderPipeline currentPipeline = RenderPipelineUtilities.GetActiveRenderPipeline();
            switch (currentPipeline) {
                case ERenderPipeline.Builtin:
                    samplesPipelinePathFull += Constants.SAMPLES_PATH_BIRP;
                    renderPipelineName = "BIRP";
                    targetPipelinePath += "BIRP Examples";
                    break;
                case ERenderPipeline.URP:
                    samplesPipelinePathFull += Constants.SAMPLES_PATH_URP;
                    renderPipelineName = "URP";
                    targetPipelinePath += "URP Examples";
                    break;
                case ERenderPipeline.HDRP:
                    samplesPipelinePathFull += Constants.SAMPLES_PATH_HDRP;
                    renderPipelineName = "HDRP";
                    targetPipelinePath += "HDRP Examples";
                    break;
                default:
                    return;
            }

            string targetPipelinePathFull = $"{projectPath}{targetPipelinePath}";

            // Create samples folder
            bool anyFilesCreated = false;

            if (!Directory.Exists(targetBasePathFull)) {
                Directory.CreateDirectory(targetBasePathFull);
                anyFilesCreated = true;
            }

            // Copy samples
            if (!Directory.Exists(targetCorePathFull)) {
                FileUtil.CopyFileOrDirectory(samplesCorePathFull, targetCorePathFull);
                anyFilesCreated = true;
            }

            if (!Directory.Exists(targetPipelinePathFull)) {
                FileUtil.CopyFileOrDirectory(samplesPipelinePathFull, targetPipelinePathFull);
                anyFilesCreated = true;
            }

            // Copy material data files to their respective folders
            // (These are removed from the Samples~ folder when building the package)
            string materialDataFolderCommon = targetCorePathFull + "/MaterialData/";

            // Sample, Material Folder
            Tuple<string, string>[] materialFolders = {
                new Tuple<string, string>("Comparison", "Repetitionless"),
                new Tuple<string, string>("Comparison", "Repetitionless 1"),
                new Tuple<string, string>("Comparison", "Repetitionless 2"),
                new Tuple<string, string>("Comparison", "Repetitionless 3"),
                new Tuple<string, string>("Flat", "Repetitionless"),
                new Tuple<string, string>("Forest", "Terrain"),
            };

            foreach (Tuple<string, string> folders in materialFolders) {
                string sampleName = folders.Item1;
                string materialName = folders.Item2;

                string materialFolder = "_" + sampleName + "_" + materialName + "_RepetitionlessData";
                string coreFolderPath = materialDataFolderCommon + sampleName + "/Repetitionless" + materialFolder;
                string targetFolderPath = targetPipelinePathFull + "/" + sampleName + "/Materials/Repetitionless_" + renderPipelineName + materialFolder;

                // Dont do if target folder exists
                if (Directory.Exists(targetFolderPath))
                    continue;

                FileUtil.MoveFileOrDirectory(coreFolderPath, targetFolderPath);

                // Remove all meta files
                foreach (string metaFilePath in Directory.GetFiles(targetFolderPath, "*.meta", SearchOption.TopDirectoryOnly))
                    File.Delete(metaFilePath);

                anyFilesCreated = true;
            }

            if (anyFilesCreated) {
                AssetDatabase.Refresh();

                // Fix materials, textures will not be assigned and they will appear broken otherwise
                foreach (Tuple<string, string> folders in materialFolders) {
                    string sampleName = folders.Item1;
                    string materialName = folders.Item2;
                    string materialPath = targetPipelinePath + "/" + sampleName + "/Materials/Repetitionless_" + renderPipelineName + "_" + sampleName + "_" + materialName + ".mat";
                    Material mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);

                    MaterialDataManager dataManager = new MaterialDataManager(mat);
                    RepetitionlessTextureDataSO textureData = dataManager.LoadAsset<RepetitionlessTextureDataSO>(Constants.TEXTURE_DATA_FILE_NAME);
                    RepetitionlessMaterialDataSO materialProperties = dataManager.LoadAsset<RepetitionlessMaterialDataSO>(Constants.PROPERTIES_FILE_NAME);
                    textureData.SetupTextureDrawers();
                    textureData.UpdateTextureProperties();
                    textureData.UpdateAssignedTexturesTexture();
                    materialProperties.UpdateMaterialTexture(mat, 0);
                }
            } else {
                // Ping object in project window if already created
                UnityEngine.Object samplesFolderObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(targetPipelinePath);
                EditorGUIUtility.PingObject(samplesFolderObject);
            }
        }
    }
}
#endif