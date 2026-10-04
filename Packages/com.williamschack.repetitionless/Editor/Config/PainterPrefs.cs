#if UNITY_EDITOR
using System;

namespace Repetitionless.Editor.Config
{
    /// <summary>
    /// Used to update repetitionless prefs<br />
    /// Prefs are project relative and Stored in "Library/com.williamschack.repetitionless/prefs.json"
    /// </summary>
    internal static class PainterPrefs
    {
        private const string PREFS_FILE_PATH = Constants.LIBRARY_PATH + "/painter_prefs.json";

        internal class Prefs
        {
            public bool SaveSettings = true;

            public int PaintingLayer = 1;

            public string BrushTextureGUID = "";
            public int BrushTextureChannel = 0;

            public float BrushRadiusReal = 15;
            public float BrushRotationDegrees = 0.0f;
            public float BrushOpacity = 1.0f;
            public float BrushSmoothness = 0.5f;
            public float BrushCutoff = 0.01f;

            public int ControlResolution = 512;
            public int HolesResolution = 512;

        }

        private static readonly PrefsStorage<Prefs> _storage = new PrefsStorage<Prefs>(PREFS_FILE_PATH);

        public static Prefs Data => _storage.Data;

        public static void UpdatePrefs(Action<Prefs> updater) => _storage.UpdatePrefs(updater);
    }
}
#endif