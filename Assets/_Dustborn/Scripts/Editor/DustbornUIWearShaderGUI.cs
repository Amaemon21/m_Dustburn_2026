using UnityEditor;
using UnityEngine;

namespace Dustborn.UI.Editor
{
    public sealed class DustbornUIWearShaderGUI : ShaderGUI
    {
        public override void OnGUI(MaterialEditor editor, MaterialProperty[] properties)
        {
            EditorGUILayout.HelpBox(LocalizationService.Text("editor.uiWear.guide"), MessageType.Info);
            MaterialProperty shape = FindProperty("_Shape", properties);
            EditorGUI.showMixedValue = shape.hasMixedValue;
            EditorGUI.BeginChangeCheck();
            int selected = EditorGUILayout.Popup(LocalizationService.Text("editor.uiWear.shape"), Mathf.RoundToInt(shape.floatValue),
                new[] { LocalizationService.Text("editor.uiWear.shapeSprite"), LocalizationService.Text("editor.uiWear.shapeHorizontal"), LocalizationService.Text("editor.uiWear.shapeVertical") });
            if (EditorGUI.EndChangeCheck())
            {
                editor.RegisterPropertyChangeUndo("Dustborn shape");
                shape.floatValue = selected;
            }
            EditorGUI.showMixedValue = false;
            editor.ShaderProperty(FindProperty("_Color", properties), LocalizationService.Text("editor.uiWear.tint"));

            Section(LocalizationService.Text("editor.uiWear.effects"));
            MaterialProperty useWear = FindProperty("_UseWear", properties);
            MaterialProperty useGlow = FindProperty("_UseGlow", properties);
            Show("_UseWear", LocalizationService.Text("editor.uiWear.wear"), LocalizationService.Text("editor.uiWear.wearHelp"));
            Show("_UseGlow", LocalizationService.Text("editor.uiWear.glow"), LocalizationService.Text("editor.uiWear.glowHelp"));

            if (useWear.hasMixedValue || useWear.floatValue > 0.5f)
            {
                Section(LocalizationService.Text("editor.uiWear.surface"));
                Show("_SurfaceStrength", LocalizationService.Text("editor.uiWear.surfaceStrength"), LocalizationService.Text("editor.uiWear.surfaceStrengthHelp"));
                Show("_GrainSize", LocalizationService.Text("editor.uiWear.grainSize"), LocalizationService.Text("editor.uiWear.grainSizeHelp"));
                editor.TexturePropertySingleLine(new GUIContent(LocalizationService.Text("editor.uiWear.wearTexture")), FindProperty("_WearTex", properties));
                Show("_TextureInfluence", LocalizationService.Text("editor.uiWear.textureInfluence"), LocalizationService.Text("editor.uiWear.textureInfluenceHelp"));
                Show("_TextureTileSize", LocalizationService.Text("editor.uiWear.textureTileSize"), LocalizationService.Text("editor.uiWear.textureTileSizeHelp"));
                Show("_TextureContrast", LocalizationService.Text("editor.uiWear.textureContrast"), LocalizationService.Text("editor.uiWear.textureContrastHelp"));

                Section(LocalizationService.Text("editor.uiWear.edges"));
                Show("_EdgeRoughness", LocalizationService.Text("editor.uiWear.edgeRoughness"), LocalizationService.Text("editor.uiWear.edgeRoughnessHelp"));
                Show("_EdgeChipSize", LocalizationService.Text("editor.uiWear.edgeChipSize"), LocalizationService.Text("editor.uiWear.edgeChipSizeHelp"));

                Section(LocalizationService.Text("editor.uiWear.cracks"));
                Show("_CrackAmount", LocalizationService.Text("editor.uiWear.crackAmount"), LocalizationService.Text("editor.uiWear.crackAmountHelp"));
                Show("_CrackWidth", LocalizationService.Text("editor.uiWear.crackWidth"), LocalizationService.Text("editor.uiWear.crackWidthHelp"));
                Show("_CrackDepth", LocalizationService.Text("editor.uiWear.crackDepth"), LocalizationService.Text("editor.uiWear.crackDepthHelp"));
                Show("_CrackSpacing", LocalizationService.Text("editor.uiWear.crackSpacing"), LocalizationService.Text("editor.uiWear.crackSpacingHelp"));
            }

            if (useGlow.hasMixedValue || useGlow.floatValue > 0.5f)
            {
                Section(LocalizationService.Text("editor.uiWear.bloom"));
                Show("_GlowColor", LocalizationService.Text("editor.uiWear.glowColor"), LocalizationService.Text("editor.uiWear.glowColorHelp"));
                Show("_GlowIntensity", LocalizationService.Text("editor.uiWear.glowIntensity"), LocalizationService.Text("editor.uiWear.glowIntensityHelp"));
                Show("_GlowPulseSpeed", LocalizationService.Text("editor.uiWear.glowPulseSpeed"), LocalizationService.Text("editor.uiWear.glowPulseSpeedHelp"));
                Show("_GlowPulseAmount", LocalizationService.Text("editor.uiWear.glowPulseAmount"), LocalizationService.Text("editor.uiWear.glowPulseAmountHelp"));
            }

            if (shape.hasMixedValue || selected != 0)
            {
                Section(LocalizationService.Text("editor.uiWear.taper"));
                Show("_TaperStart", LocalizationService.Text("editor.uiWear.taperStart"), LocalizationService.Text("editor.uiWear.taperStartHelp"));
                Show("_TaperEnd", LocalizationService.Text("editor.uiWear.taperEnd"), LocalizationService.Text("editor.uiWear.taperEndHelp"));
                Show("_TipPower", LocalizationService.Text("editor.uiWear.tipPower"), LocalizationService.Text("editor.uiWear.tipPowerHelp"));
                Show("_LineFeather", LocalizationService.Text("editor.uiWear.lineFeather"), LocalizationService.Text("editor.uiWear.lineFeatherHelp"));
                EditorGUILayout.HelpBox(LocalizationService.Text("editor.uiWear.lineGuide"), MessageType.None);
            }
            Section(LocalizationService.Text("editor.uiWear.variation"));
            Show("_PatternSeed", LocalizationService.Text("editor.uiWear.patternSeed"), LocalizationService.Text("editor.uiWear.patternSeedHelp"));

            void Show(string property, string label, string tooltip)
            {
                editor.ShaderProperty(FindProperty(property, properties), new GUIContent(label, tooltip));
            }
        }

        private static void Section(string label)
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
        }
    }
}
