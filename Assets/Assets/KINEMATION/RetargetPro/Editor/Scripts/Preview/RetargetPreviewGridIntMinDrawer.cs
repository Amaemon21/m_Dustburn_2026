// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using UnityEditor;
using UnityEngine;

namespace KINEMATION.RetargetPro.Editor.Scripts.Preview
{
    public sealed class RetargetPreviewGridIntMinDrawer : MaterialPropertyDrawer
    {
        private readonly int _minimum;

        public RetargetPreviewGridIntMinDrawer(float minimum)
        {
            _minimum = Mathf.Max(1, Mathf.RoundToInt(minimum));
        }

        public override void OnGUI(Rect position, MaterialProperty prop, GUIContent label, MaterialEditor editor)
        {
            int currentValue = Mathf.Max(_minimum, Mathf.RoundToInt(prop.floatValue));

            EditorGUI.BeginChangeCheck();
            int nextValue = EditorGUI.IntField(position, label, currentValue);
            if (EditorGUI.EndChangeCheck())
            {
                prop.floatValue = Mathf.Max(_minimum, nextValue);
            }
        }
    }
}