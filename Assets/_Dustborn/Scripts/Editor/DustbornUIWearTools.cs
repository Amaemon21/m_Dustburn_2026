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
            if (assetPath == DustbornUIWearTools.Root + "/Textures/Wear_Grayscale.png")
                DustbornUIWearTools.SetTextureSettings((TextureImporter)assetImporter, false);
            else if (assetPath == DustbornUIWearTools.Root + "/SampleSprites/Frame_Corners.png")
                DustbornUIWearTools.SetTextureSettings((TextureImporter)assetImporter, true);
        }
    }

    public static class DustbornUIWearTools
    {
        public const string Root = "Assets/_Dustborn/UIWear";

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
            Configure(Root + "/Textures/Wear_Grayscale.png", false);
            Configure(Root + "/SampleSprites/Frame_Corners.png", true);
        }

        private static void Configure(string path, bool sprite)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;
            SetTextureSettings(importer, sprite);
            importer.SaveAndReimport();
        }

        private static Material Preset(string name)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/" + name + ".mat");
            if (material == null || material.shader == null)
                throw new InvalidOperationException("Dustborn preset missing: " + name + ". Copy the complete Assets folder first.");
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

        [MenuItem("Tools/Dustborn/UI Wear/Create comparison demo")]
        private static void CreateDemo()
        {
            ConfigureTextures();
            Material line = Preset("Worn_Line");
            Material thin = Preset("Worn_ThinLine");
            Material frame = Preset("Worn_Frame");
            Material panel = Preset("Worn_Panel");
            Sprite frameSprite = AssetDatabase.LoadAssetAtPath<Sprite>(Root + "/SampleSprites/Frame_Corners.png");
            if (frameSprite == null) throw new InvalidOperationException("Sample sprite could not be imported.");

            var root = new GameObject("Dustborn UI Wear — Comparison", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            Undo.RegisterCreatedObjectUndo(root, "Create Dustborn wear comparison");
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;
            canvas.vertexColorAlwaysGammaSpace = true;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var bg = MakeImage(root.transform, "Background", new Rect(0, 0, 1920, 1080), Hex("#141311"));
            var br = bg.rectTransform;
            br.anchorMin = Vector2.zero; br.anchorMax = Vector2.one;
            br.offsetMin = br.offsetMax = Vector2.zero;

            Color gold = Hex("#F9F0AB");
            Color neutral = Hex("#C2C1C1");
            Label(root.transform, "DUSTBORN / UI WEAR", new Rect(80, 54, 1700, 48), 32, gold);
            Label(root.transform, "ИСХОДНЫЙ ЭЛЕМЕНТ", new Rect(80, 140, 750, 35), 24, neutral);
            Label(root.transform, "ФАКТУРА, СКОЛЫ И ОСТРЫЕ КОНЦЫ", new Rect(1020, 140, 800, 35), 24, gold);

            MakeImage(root.transform, "Plain 6px line", new Rect(80, 234, 740, 6), gold);
            Wear(MakeImage(root.transform, "Pointed 6px line", new Rect(1020, 234, 740, 6), gold), line, 13);
            MakeImage(root.transform, "Plain 2px line", new Rect(80, 316, 740, 2), neutral);
            Wear(MakeImage(root.transform, "Worn 2px line", new Rect(1020, 316, 740, 2), neutral), thin, 31);

            Image plainFrame = MakeImage(root.transform, "Plain Frame", new Rect(80, 398, 320, 188), neutral);
            plainFrame.sprite = frameSprite; plainFrame.type = Image.Type.Sliced;
            Image wornFrame = MakeImage(root.transform, "Worn Frame", new Rect(1020, 398, 320, 188), neutral);
            wornFrame.sprite = frameSprite; wornFrame.type = Image.Type.Sliced;
            Wear(wornFrame, frame, 83);
            MakeImage(root.transform, "Plain Panel", new Rect(444, 398, 376, 188), Hex("#75664B"));
            Wear(MakeImage(root.transform, "Worn Panel", new Rect(1384, 398, 376, 188), Hex("#75664B")), panel, 117);

            Label(root.transform, "RectMask2D", new Rect(80, 670, 680, 30), 22, neutral);
            var rectMaskGO = new GameObject("RectMask2D check", typeof(RectTransform), typeof(RectMask2D));
            rectMaskGO.transform.SetParent(root.transform, false);
            Position(rectMaskGO.GetComponent<RectTransform>(), new Rect(80, 726, 600, 100));
            Wear(MakeImage(rectMaskGO.transform, "Clipped line", new Rect(-90, 46, 780, 8), gold), line, 42);
            rectMaskGO.GetComponent<RectMask2D>().softness = new Vector2Int(8, 8);

            Label(root.transform, "Mask / stencil", new Rect(1020, 670, 680, 30), 22, neutral);
            Image stencilImage = MakeImage(root.transform, "Stencil mask check", new Rect(1020, 726, 600, 100), Color.white);
            var stencil = stencilImage.gameObject.AddComponent<Mask>();
            stencil.showMaskGraphic = false;
            Wear(MakeImage(stencilImage.transform, "Clipped line", new Rect(-90, 46, 780, 8), gold), line, 42);

            Label(root.transform, "Выдели правый элемент → открой Material. Настройки материала подписаны по-русски.\nДемо — отдельный Canvas: его можно удалить или отменить через Undo.", new Rect(80, 930, 1740, 84), 22, neutral);
            Selection.activeGameObject = root;
        }

        private static Image MakeImage(Transform parent, string name, Rect rect, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            Position(image.rectTransform, rect);
            return image;
        }

        private static void Wear(Image image, Material material, int seed)
        {
            image.material = material;
            image.gameObject.AddComponent<DustbornUIWear>().PatternSeed = seed;
        }

        private static void Label(Transform parent, string text, Rect rect, int size, Color color)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            Position(go.GetComponent<RectTransform>(), rect);
            var label = go.GetComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = size;
            label.text = text;
            label.color = color;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
        }

        private static void Position(RectTransform rt, Rect r)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(r.x, -r.y);
            rt.sizeDelta = r.size;
        }

        private static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out Color color);
            return color;
        }
    }
}
