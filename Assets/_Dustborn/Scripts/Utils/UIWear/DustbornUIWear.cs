using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

namespace Dustborn.UI
{
    /// <summary>
    /// Supplies local UI coordinates, sprite atlas bounds and a stable seed.
    /// Does not instantiate materials or perform per-frame work.
    /// Reserves UV1, UV2 and UV3 on the Image mesh.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Image))]
    [AddComponentMenu("UI/Dustborn UI Wear")]
    public sealed class DustbornUIWear : BaseMeshEffect
    {
        [SerializeField, Tooltip("Постоянный рисунок потёртостей. Разные числа дают разные варианты.")]
        private int patternSeed = 1;

        public int PatternSeed
        {
            get => patternSeed;
            set { if (patternSeed == value) return; patternSeed = value; MarkDirty(); }
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            EnableChannels();
            MarkDirty();
        }

        protected override void OnCanvasHierarchyChanged()
        {
            base.OnCanvasHierarchyChanged();
            EnableChannels();
            MarkDirty();
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            MarkDirty();
        }

        private void MarkDirty()
        {
            if (graphic != null) graphic.SetVerticesDirty();
        }

        private void EnableChannels()
        {
            if (graphic == null || graphic.canvas == null) return;
            const AdditionalCanvasShaderChannels channels = AdditionalCanvasShaderChannels.TexCoord1
                | AdditionalCanvasShaderChannels.TexCoord2 | AdditionalCanvasShaderChannels.TexCoord3;
            // Do not clear channels when this component is disabled: siblings may need them.
            graphic.canvas.additionalShaderChannels |= channels;
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0) return;
            Image image = graphic as Image;
            if (image == null) return;
            Rect rect = image.GetPixelAdjustedRect();
            Sprite sprite = image.overrideSprite;
            Vector4 bounds = sprite != null ? DataUtility.GetOuterUV(sprite) : new Vector4(0, 0, 1, 1);

            if (sprite != null && image.preserveAspect && image.type == Image.Type.Simple
                && sprite.rect.width > 0 && sprite.rect.height > 0 && rect.width > 0 && rect.height > 0)
            {
                float ratio = sprite.rect.width / sprite.rect.height;
                Vector2 pivot = image.rectTransform.pivot;
                if (rect.width / rect.height > ratio)
                {
                    float width = rect.height * ratio;
                    rect.x += (rect.width - width) * pivot.x;
                    rect.width = width;
                }
                else
                {
                    float height = rect.width / ratio;
                    rect.y += (rect.height - height) * pivot.y;
                    rect.height = height;
                }
            }

            UIVertex vertex = default;
            float seed = patternSeed % 32749;
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref vertex, i);
                vertex.uv1 = new Vector4(vertex.position.x - rect.xMin,
                    vertex.position.y - rect.yMin, rect.width, rect.height);
                vertex.uv2 = bounds;
                vertex.uv3 = new Vector4(seed, 0, 0, 0);
                vh.SetUIVertex(vertex, i);
            }
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            EnableChannels();
        }
#endif
    }
}
