// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using UnityEditor;
using UnityEngine;

namespace KINEMATION.RetargetPro.Editor.Scripts.Window
{
    internal sealed class RetargetPreviewTransformIndicator
    {
        internal readonly struct SizeOptions
        {
            public readonly float AxisLengthFactor;
            public readonly float ArrowCapSizeFactor;
            public readonly float CenterCapSizeFactor;
            public readonly float RotationDiscSizeFactor;
            public readonly float AxisLineThickness;

            public SizeOptions(float axisLengthFactor, float arrowCapSizeFactor, float centerCapSizeFactor,
                float rotationDiscSizeFactor, float axisLineThickness = 3f)
            {
                AxisLengthFactor = axisLengthFactor;
                ArrowCapSizeFactor = arrowCapSizeFactor;
                CenterCapSizeFactor = centerCapSizeFactor;
                RotationDiscSizeFactor = rotationDiscSizeFactor;
                AxisLineThickness = axisLineThickness;
            }
        }

        internal readonly struct ColorOptions
        {
            public readonly Color XAxisColor;
            public readonly Color YAxisColor;
            public readonly Color ZAxisColor;
            public readonly Color CenterColor;

            public ColorOptions(Color xAxisColor, Color yAxisColor, Color zAxisColor, Color centerColor)
            {
                XAxisColor = xAxisColor;
                YAxisColor = yAxisColor;
                ZAxisColor = zAxisColor;
                CenterColor = centerColor;
            }
        }

        private readonly SizeOptions _sizeOptions;
        private readonly ColorOptions _colorOptions;

        internal RetargetPreviewTransformIndicator(SizeOptions sizeOptions, ColorOptions colorOptions)
        {
            _sizeOptions = sizeOptions;
            _colorOptions = colorOptions;
        }

        internal void Draw(Transform modelTransform, Camera previewCamera)
        {
            if (modelTransform == null || previewCamera == null || Event.current.type != EventType.Repaint)
            {
                return;
            }

            Vector3 position = modelTransform.position;
            if (previewCamera.WorldToViewportPoint(position).z <= 0f)
            {
                return;
            }

            float handleSize = Mathf.Max(0.001f, HandleUtility.GetHandleSize(position));
            DrawPositionIndicator(position, modelTransform.rotation, handleSize);
        }

        private void DrawPositionIndicator(Vector3 position, Quaternion rotation, float handleSize)
        {
            float axisLength = handleSize * Mathf.Max(0.001f, _sizeOptions.AxisLengthFactor);
            float arrowCapSize = handleSize * Mathf.Max(0.001f, _sizeOptions.ArrowCapSizeFactor);
            float centerCapSize = handleSize * Mathf.Max(0.001f, _sizeOptions.CenterCapSizeFactor);

            DrawAxis(position, rotation * Vector3.right, axisLength, arrowCapSize, _colorOptions.XAxisColor);
            DrawAxis(position, rotation * Vector3.up, axisLength, arrowCapSize, _colorOptions.YAxisColor);
            DrawAxis(position, rotation * Vector3.forward, axisLength, arrowCapSize, _colorOptions.ZAxisColor);

            Handles.color = _colorOptions.CenterColor;
            Handles.SphereHandleCap(0, position, Quaternion.identity, centerCapSize, EventType.Repaint);
        }

        private void DrawRotationIndicator(Vector3 position, Quaternion rotation, float handleSize)
        {
            float discRadius = handleSize * Mathf.Max(0.001f, _sizeOptions.RotationDiscSizeFactor);
            float centerCapSize = handleSize * Mathf.Max(0.001f, _sizeOptions.CenterCapSizeFactor);

            Handles.color = _colorOptions.XAxisColor;
            Handles.DrawWireDisc(position, rotation * Vector3.right, discRadius);

            Handles.color = _colorOptions.YAxisColor;
            Handles.DrawWireDisc(position, rotation * Vector3.up, discRadius);

            Handles.color = _colorOptions.ZAxisColor;
            Handles.DrawWireDisc(position, rotation * Vector3.forward, discRadius);

            Handles.color = _colorOptions.CenterColor;
            Handles.SphereHandleCap(0, position, Quaternion.identity, centerCapSize, EventType.Repaint);
        }

        private void DrawAxis(Vector3 origin, Vector3 axisDirection, float axisLength, float arrowCapSize, Color color)
        {
            Vector3 endPosition = origin + axisDirection.normalized * axisLength;

            Handles.color = color;
            Handles.DrawAAPolyLine(Mathf.Max(1f, _sizeOptions.AxisLineThickness), origin, endPosition);
            Handles.ConeHandleCap(0, endPosition, Quaternion.LookRotation(axisDirection), arrowCapSize,
                EventType.Repaint);
        }
    }
}