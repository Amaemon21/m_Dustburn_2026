// Copyright (c) 2025 Synty Studios Limited. All rights reserved.
//
// Use of this software is subject to the terms and conditions of the Synty Studios End User Licence Agreement (EULA)
// available at: https://syntystore.com/pages/end-user-licence-agreement
//
// For additional details, see the LICENSE.MD file bundled with this software.

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Synty.SidekickCharacters
{
    public class ToolDownloader : EditorWindow
    {
        private const string _GIT_URL = "https://github.com/SyntyStudios/SidekicksToolRelease/releases/latest/download/Sidekicks.unitypackage";
        private const string _GIT_DB_URL = "https://github.com/SyntyStudios/SidekicksToolRelease/releases/latest/download/SidekicksDatabase.unitypackage";
        private const string _PACKAGE_CACHE = "Assets/DownloadCache/Sidekicks.unitypackage";
        private const string _DB_PACKAGE_CACHE = "Assets/DownloadCache/SidekicksDatabase.unitypackage";
        private const string _VERSION_FILE = "Assets/Assets/Synty/SidekickCharacters/Scripts/Editor/version.txt";
        private const string _VERSION_TAG = "\"tag_name\":";
        private const string _VERSION_KEY = "sk_current_tool_version";
        private const string _SIDEKICK_TOOL_MENU_ITEM = "Synty/Sidekick Character Tool";

        private string _version = "-";
        private Label _latestVersion;
        private CancellationTokenSource _updateCancellation;

        private void OnEnable()
        {
            _updateCancellation = new CancellationTokenSource();
            _ = BackgroundUpdateCheck(_updateCancellation.Token);
        }

        private void OnDisable()
        {
            _updateCancellation?.Cancel();
            _updateCancellation?.Dispose();
            _updateCancellation = null;
            _latestVersion = null;
        }

        public void CreateGUI()
        {
            VisualElement root = rootVisualElement;
            root.Clear();
            Button downloadButton = new Button(DownloadLatestDBVersion)
            {
                text = "Download and Install",
                style =
                {
                    height = 40
                }
            };

            root.Add(downloadButton);

            Label textPrompt = new Label("This tool will download and install the latest version of the Sidekicks Tool.");
            Label textPrompt2 = new Label("Or you can manually download from this link:\nhttps://github.com/SyntyStudios/SidekicksToolRelease/releases/latest/");

            root.Add(textPrompt);
            root.Add(textPrompt2);

            string version = LoadCurrentInstalledVersion();
            Label currentVersion = new Label("Currently Installed: " + (string.IsNullOrEmpty(version) ? "N/A" : version))
            {
                style =
                {
                    marginTop = 5
                }
            };

            _latestVersion = new Label("Latest Version: " + _version);

            root.Add(currentVersion);
            root.Add(_latestVersion);

            Button changeLogButton = new Button()
            {
                text = "Show Changelog"
            };

            root.Add(changeLogButton);

            changeLogButton.clickable.clicked += delegate
            {
                Application.OpenURL("https://syntystore.com/pages/sidekicks-changelog");
            };
        }

        private async Task BackgroundUpdateCheck(CancellationToken token)
        {
            try
            {
                string version = await CheckAvailableVersion(token);
                if (token.IsCancellationRequested || this == null)
                    return;
                _version = version;
                if (_latestVersion != null)
                    _latestVersion.text = "Latest Version: " + _version;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                if (!token.IsCancellationRequested && this != null)
                    Debug.LogWarning("Sidekick update check failed: " + exception.Message);
            }
        }

        private async Task<string> CheckAvailableVersion(CancellationToken token = default)
        {
            using HttpClient client = new HttpClient();

            string uri = "https://api.github.com/repos/SyntyStudios/SidekicksToolRelease/releases/latest";

            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Add("Accept", "application/vnd.github+json");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            request.Headers.Add("User-Agent", "Sidekicks Download Tool");

            using HttpResponseMessage response = await client.SendAsync(request, token);

            response.EnsureSuccessStatusCode();

            // Read the response content as a string
            string responseBody = await response.Content.ReadAsStringAsync();

            return GetVersionNumber(responseBody);
        }

        private string GetVersionNumber(string data)
        {
            int index = data.IndexOf(_VERSION_TAG, StringComparison.OrdinalIgnoreCase) + _VERSION_TAG.Length;
            string portion = data.Substring(index, data.Length - index);
            string number = portion.Substring(0, portion.IndexOf(',')).Replace("\"", "").Replace(" ", "");
            return number;
        }

        private bool IsNewVersionAvailable(string version)
        {
            string currentVersion = LoadCurrentInstalledVersion();
            if (string.IsNullOrEmpty(currentVersion))
            {
                return true;
            }

            if (!currentVersion.Contains('.') || !version.Contains('.'))
            {
                return false;
            }

            string[] currentSplitVersion = currentVersion.Split('.');
            string[] newSplitVersion = version.Split('.');

            if (currentSplitVersion.Length != newSplitVersion.Length)
            {
                return false;
            }

            for (int i = 0; i < newSplitVersion.Length; i++)
            {
                 if (int.TryParse(currentSplitVersion[i], out int current))
                 {
                     if (int.TryParse(newSplitVersion[i], out int newVersion))
                     {
                         if (newVersion > current)
                         {
                             return true;
                         }
                     }
                 }
            }

            return false;
        }

        private void SaveCurrentInstalledVersion(string version)
        {
            File.WriteAllText(_VERSION_FILE, version);
        }

        private string LoadCurrentInstalledVersion()
        {
            if (File.Exists(_VERSION_FILE))
            {
                return File.ReadAllText(_VERSION_FILE);
            }
            
            return null;
        }

        private void DownloadLatestDBVersion()
        {
            WebClient client = new WebClient();
            client.DownloadFileCompleted += (sender, e) =>
            {
                if (e.Error == null)
                {
                    CloseOpenToolWindow();
                    AssetDatabase.ImportPackage(_DB_PACKAGE_CACHE, false);
                    AssetDatabase.importPackageCompleted += ProceedWithInstall;
                }
                else
                {
                    Debug.LogError("Error downloading file: " + e.Error.Message);
                }
            };

            // Ensure the directory exists
            string directory = Path.GetDirectoryName(_DB_PACKAGE_CACHE);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            client.DownloadFileAsync(new System.Uri(_GIT_DB_URL), _DB_PACKAGE_CACHE);
        }

        private async void DownloadLatestToolVersion()
        {
            WebClient client = new WebClient();
            _version = await CheckAvailableVersion();
            client.DownloadFileCompleted += (sender, e) =>
            {
                if (e.Error == null)
                {
                    SaveCurrentInstalledVersion(_version);
                    AssetDatabase.ImportPackage(_PACKAGE_CACHE, false);
                    AssetDatabase.importPackageCompleted += ProceedWithInstall;
                }
                else
                {
                    Debug.LogError("Error downloading file: " + e.Error.Message);
                }
            };

            // Ensure the directory exists
            string directory = Path.GetDirectoryName(_PACKAGE_CACHE);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            client.DownloadFileAsync(new System.Uri(_GIT_URL), _PACKAGE_CACHE);
        }

        private void ProceedWithInstall(string packageName)
        {
            if (packageName == "SidekicksDatabase")
            {
                AssetDatabase.importPackageCompleted -= ProceedWithInstall;
                DownloadLatestToolVersion();
            }
            else if (packageName == "Sidekicks")
            {
                AssetDatabase.importPackageCompleted -= ProceedWithInstall;
                DownloaderBackgroundService.RefreshWindow();

                if (EditorUtility.DisplayDialog("Installation Finished", "Sidekick Tool installation has completed.", "Re-open Sidekicks Tool",
                        "Close"))
                {
                    ReopenToolWindow();
                }
            }
        }

        private bool CloseOpenToolWindow()
        {
            EditorWindow[] allWindows = Resources.FindObjectsOfTypeAll<EditorWindow>();

            // Filter the windows to find the one with the desired type name
            EditorWindow foundWindow = allWindows.FirstOrDefault(window => window.GetType().Name == "ModularCharacterWindow");

            if (foundWindow != null)
            {
                foundWindow.Close();
                Thread.Sleep(1500);
                return true;
            }

            return false;
        }

        private void ReopenToolWindow()
        {
            try
            {
                EditorApplication.ExecuteMenuItem(_SIDEKICK_TOOL_MENU_ITEM);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Sidekicks Tool menu item not found. Please verify installation.");
                Debug.LogWarning("Exception details: " + ex.Message);
            }
        }
    }

    public static class DownloaderBackgroundService
    {
        public static void RefreshWindow()
        {
            foreach (ToolDownloader window in Resources.FindObjectsOfTypeAll<ToolDownloader>())
            {
                window.CreateGUI();
                window.Repaint();
            }
        }

        [MenuItem("Synty/Sidekick Tool Downloader")]
        public static void ShowToolDownloaderWindow()
        {
            ToolDownloader window = EditorWindow.GetWindow<ToolDownloader>("Sidekick Tool Downloader");
            window.minSize = new Vector2(600, 150);
            window.Show();
        }
    }
}
