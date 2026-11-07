using UnityEditor;
using UnityEngine;
using UnityEngine.Assertions;

namespace LP.AutoAnchoring.Editor
{
    public class AutoAnchorTool : EditorWindow
    {
        [MenuItem("CONTEXT/RectTransform/Adjust Anchors")]
        private static void AdjustAnchorsForSelection(MenuCommand command)
        {
            AdjustAnchors((RectTransform)command.context);
        }

        public static void AdjustAnchors(RectTransform rectTransform)
        {
           
            RectTransform parentRectTransform = rectTransform.parent as RectTransform;
            if (parentRectTransform == null)
            {
                Debug.LogWarning($"The selected RectTransform '{rectTransform.name}' has no parent. Anchors cannot be adjusted.");
                return;
            }
            Vector3[] targetCorners = new Vector3[4];
            Vector3[] parentCorners = new Vector3[4];

            rectTransform.GetWorldCorners(targetCorners);
            parentRectTransform.GetWorldCorners(parentCorners);
            Rect parentRect = parentRectTransform.rect;
            float anchorMinX = (targetCorners[0].x - parentCorners[0].x) / parentRect.width;
            float anchorMaxX = (targetCorners[2].x - parentCorners[0].x) / parentRect.width;

            float anchorMinY = (targetCorners[0].y - parentCorners[0].y) / parentRect.height;
            float anchorMaxY = (targetCorners[1].y - parentCorners[0].y) / parentRect.height;
            rectTransform.anchorMin = new Vector2(anchorMinX, anchorMinY);
            rectTransform.anchorMax = new Vector2(anchorMaxX, anchorMaxY);
            rectTransform.anchoredPosition = Vector2.zero;
            rectTransform.sizeDelta = Vector2.zero;
        }
    }
}
