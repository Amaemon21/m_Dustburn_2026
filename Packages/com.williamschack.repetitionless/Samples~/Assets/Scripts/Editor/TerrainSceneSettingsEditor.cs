#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

using Repetitionless.Runtime.Samples;

namespace Repetitionless.Editor.Samples
{
    [CustomEditor(typeof(TerrainSceneSettings))]
    public class TerrainSceneSettingsEditor : UnityEditor.Editor
    {
        private TerrainSceneSettings _main;
        private bool _setup = false;

        private GUIStyle _labelStyleOn;
        private GUIStyle _labelStyleOff;

        private void OnSetup()
        {
            _main = (TerrainSceneSettings)target;

            _labelStyleOn = new GUIStyle();
            _labelStyleOn.fontSize = 16;
            _labelStyleOn.alignment = TextAnchor.MiddleCenter;
            _labelStyleOn.normal.textColor = Color.green;

            _labelStyleOff = new GUIStyle(_labelStyleOn);
            _labelStyleOff.normal.textColor = Color.red;
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            if (!_setup) {
                _setup = true;
                OnSetup();
            }

            GUILayout.Space(20);

            bool usingRepetitionless = _main.TerrainUsingRepetitionless;
            string currentLabelText = "Repetitionless " + (usingRepetitionless ? "ON" : "OFF");
            GUIStyle currentStyle = usingRepetitionless ? _labelStyleOn : _labelStyleOff;
            if (currentStyle != null)
                GUILayout.Label(currentLabelText, currentStyle);

            GUILayout.Space(5);

            if (GUILayout.Button("Toggle Repetitionless Material", GUILayout.Height(30)))
                _main.ToggleTerrainRepetitionless();

            if (GUILayout.Button("Modify Repetitionless Material", GUILayout.Height(30)))
                _main.SelectRepetitionlessMaterial();
        }
    }
}
#endif