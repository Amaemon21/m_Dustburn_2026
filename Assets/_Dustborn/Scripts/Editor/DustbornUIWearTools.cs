using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Dustborn.UI.Editor
{
    internal sealed class DustbornUIWearTextureImport : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (assetPath == DustbornUIWearTools.WearTexturePath)
                DustbornUIWearTools.SetTextureSettings((TextureImporter)assetImporter, false);
            else if (assetPath == DustbornUIWearTools.FrameSpritePath)
                DustbornUIWearTools.SetTextureSettings((TextureImporter)assetImporter, true);
        }
    }

    public static class DustbornUIWearTools
    {
        public const string MaterialsRoot = "Assets/_Dustborn/Content/Material/UIWear";
        public const string WearTexturePath = "Assets/_Dustborn/Content/Texture/UIWear/Wear_Grayscale.png";
        public const string FrameSpritePath = "Assets/_Dustborn/Content/Sprites/DustbornUI/Sprites/slot_frame_idle.png";

        internal static void SetTextureSettings(TextureImporter importer, bool sprite)
        {
            importer.textureType = sprite ? TextureImporterType.Sprite : TextureImporterType.Default;
            importer.sRGBTexture = sprite;
            importer.mipmapEnabled = !sprite;
            importer.isReadable = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.crunchedCompression = false;
            importer.maxTextureSize = 2048;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = sprite ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = sprite;
            if (sprite)
            {
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 100;
                importer.spriteBorder = new Vector4(22, 22, 22, 22);
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(settings);
            }
        }

        [MenuItem("Tools/Dustborn/UI Wear/Configure bundled textures")]
        public static void ConfigureTextures()
        {
            Configure(WearTexturePath, false);
            Configure(FrameSpritePath, true);
        }

        private static void Configure(string path, bool sprite)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                Debug.LogWarning("Не найдена текстура UI Wear: " + path);
                return;
            }
            SetTextureSettings(importer, sprite);
            importer.SaveAndReimport();
        }

        private static Material Preset(string name)
        {
            string path = MaterialsRoot + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null || material.shader == null)
                throw new InvalidOperationException("Не найден материал UI Wear или его шейдер: " + path);
            if (ShaderUtil.ShaderHasError(material.shader))
                throw new InvalidOperationException("Dustborn shader has compilation errors. Open Console before applying it.");
            return material;
        }

        [MenuItem("Tools/Dustborn/UI Wear/Apply to selection/Pointed line")]
        private static void ApplyLine() => Apply("Worn_Line", true);
        [MenuItem("Tools/Dustborn/UI Wear/Apply to selection/Thin line (2 px)")]
        private static void ApplyThinLine() => Apply("Worn_ThinLine", true);
        [MenuItem("Tools/Dustborn/UI Wear/Apply to selection/Sprite or frame")]
        private static void ApplyFrame() => Apply("Worn_Frame", false);
        [MenuItem("Tools/Dustborn/UI Wear/Apply to selection/Panel")]
        private static void ApplyPanel() => Apply("Worn_Panel", false);

        private static void Apply(string name, bool line)
        {
            ConfigureTextures();
            Material material = Preset(name);
            int count = 0;
            foreach (GameObject go in Selection.gameObjects)
            {
                var image = go.GetComponent<Image>();
                if (image == null) continue;
                Undo.RecordObject(image, "Apply Dustborn UI Wear");
                image.material = material;
                if (line)
                {
                    image.sprite = null;
                    image.overrideSprite = null;
                    image.type = Image.Type.Simple;
                    image.preserveAspect = false;
                    image.useSpriteMesh = false;
                }
                var effect = go.GetComponent<DustbornUIWear>();
                if (effect == null) effect = Undo.AddComponent<DustbornUIWear>(go);
                Undo.RecordObject(effect, "Set wear variation");
                effect.PatternSeed = go.GetInstanceID() % 32749;
                effect.enabled = true;
                EditorUtility.SetDirty(image);
                EditorUtility.SetDirty(effect);
                count++;
            }
            if (count == 0) Debug.LogWarning("Выбери объекты с компонентом UI Image в Hierarchy.");
        }
    }
}
