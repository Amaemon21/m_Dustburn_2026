#if UNITY_EDITOR
using UnityEngine;
using System;
using System.IO;

namespace Repetitionless.Editor.Config
{
    /// <summary>
    /// Handles storing prefs at a given file path
    /// </summary>
    /// <typeparam name="T">
    /// The class that defines what data is stored
    /// </typeparam>
    internal class PrefsStorage<T> where T : class, new()
    {
        private readonly string _filePath;
        private T _cache;

        public T Data => _cache ??= LoadPrefs();

        public PrefsStorage(string filePath)
        {
            _filePath = filePath;
        }

        private FileInfo GetPrefsFileInfo()
        {
            FileInfo prefsFileInfo = new FileInfo(_filePath);
            if (!prefsFileInfo.Exists)
                CreatePrefs();

            return prefsFileInfo;
        }

        private void CreatePrefs()
        {
            FileInfo prefsFileInfo = new FileInfo(_filePath);
            if (prefsFileInfo.Exists) return;

            string parentDir = prefsFileInfo.DirectoryName;
            if (!Directory.Exists(parentDir))
                Directory.CreateDirectory(parentDir);

            T prefs = new T();
            string prefsJson = JsonUtility.ToJson(prefs);

            File.WriteAllText(prefsFileInfo.FullName, prefsJson);
        }

        private T LoadPrefs()
        {
            FileInfo prefsFileInfo = GetPrefsFileInfo();

            string prefsJson = File.ReadAllText(prefsFileInfo.FullName);
            return JsonUtility.FromJson<T>(prefsJson);
        }

        private void WritePrefs(T prefs)
        {
            FileInfo prefsFileInfo = GetPrefsFileInfo();
            
            string prefsJson = JsonUtility.ToJson(prefs);
            File.WriteAllText(prefsFileInfo.FullName, prefsJson);
        }

        /// <summary>
        /// Writes the prefs after calling the updater action
        /// </summary>
        /// <param name="updater">
        /// The action used to modify the prefs before writing them
        /// </param>
        public void UpdatePrefs(Action<T> updater)
        {
            updater(Data);
            WritePrefs(Data);
        }
    }
}
#endif