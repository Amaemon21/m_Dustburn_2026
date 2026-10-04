#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace Repetitionless.Editor.Inspectors
{
    /// <summary>
    /// The editor for the regular repetitionless material
    /// </summary>
    public class RepetitionlessMaterialEditorMaster : RepetitionlessMaterialEditorBase
    {
        /// <summary>
        /// Base OnGUI function
        /// </summary>
        /// <param name="materialEditor">
        /// The material editor being used
        /// </param>
        /// <param name="properties">
        /// The material properties
        /// </param>
        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            base.OnGUI(materialEditor, properties);

            // Base Material
            DrawBaseMaterialGUI(0);

            GUILayout.Space(SETTING_PADDING);

            // Distance Blend Material
            DrawDistanceBlendGUI(0);

            GUILayout.Space(SETTING_PADDING);

            // Material Blend
            DrawMaterialBlendGUI(0);

            GUILayout.Space(SETTING_PADDING);

            // Footer Settings
            DrawDebugGUI();
        }
    }
}
#endif