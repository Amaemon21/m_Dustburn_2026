#if UNITY_EDITOR
using System;

namespace Repetitionless.Editor.Config
{
    /// <summary>
    /// Used to update repetitionless prefs<br />
    /// Prefs are project relative and Stored in "Library/com.williamschack.repetitionless/prefs.json"
    /// </summary>
    internal static class RepetitionlessPrefs
    {
        private const string PREFS_FILE_PATH = Constants.LIBRARY_PATH + "/prefs.json";

        internal class Prefs
        {
            public bool WelcomeWindowShown = false;
            public bool OpenWindowOnUpdate = true;

            public string LastProcessedVersion = "0.0.0";
            public bool LiteMode = false;

            public bool URPActive = false;
            public bool HDRPActive = false;
            public bool HasNewHDRPSupport = true; // If the last processed unity version was 6.3+

            public bool CheckForSales = false; // Only used in the free version

            public long LastSessionId = 0;
        }

        private static readonly PrefsStorage<Prefs> _storage = new PrefsStorage<Prefs>(PREFS_FILE_PATH);

        public static Prefs Data => _storage.Data;

        public static void UpdatePrefs(Action<Prefs> updater) => _storage.UpdatePrefs(updater);
    }
}
#endif