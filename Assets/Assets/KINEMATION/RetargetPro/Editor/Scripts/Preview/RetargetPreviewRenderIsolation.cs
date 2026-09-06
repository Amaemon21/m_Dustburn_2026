// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using System.Reflection;
using UnityEngine;

namespace KINEMATION.RetargetPro.Editor.Scripts.Preview
{
    internal static class RetargetPreviewRenderIsolation
    {
        private const string UniversalAdditionalCameraDataTypeName =
            "UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime";
        private const string HdAdditionalCameraDataTypeName =
            "UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData, Unity.RenderPipelines.HighDefinition.Runtime";
        private const string HdAdditionalLightDataTypeName =
            "UnityEngine.Rendering.HighDefinition.HDAdditionalLightData, Unity.RenderPipelines.HighDefinition.Runtime";

        private static readonly Type UniversalAdditionalCameraDataType =
            Type.GetType(UniversalAdditionalCameraDataTypeName);
        private static readonly Type HdAdditionalCameraDataType = Type.GetType(HdAdditionalCameraDataTypeName);
        private static readonly Type HdAdditionalLightDataType = Type.GetType(HdAdditionalLightDataTypeName);

        private static readonly PropertyInfo RenderPostProcessingProperty =
            UniversalAdditionalCameraDataType?.GetProperty("renderPostProcessing", BindingFlags.Public | BindingFlags.Instance);

        private static readonly PropertyInfo VolumeLayerMaskProperty =
            UniversalAdditionalCameraDataType?.GetProperty("volumeLayerMask", BindingFlags.Public | BindingFlags.Instance);

        private static readonly PropertyInfo VolumeTriggerProperty =
            UniversalAdditionalCameraDataType?.GetProperty("volumeTrigger", BindingFlags.Public | BindingFlags.Instance);

        private static readonly MethodInfo InitDefaultHdAdditionalLightDataMethod =
            HdAdditionalLightDataType?.GetMethod("InitDefaultHDAdditionalLightData", BindingFlags.Public | BindingFlags.Static);

        public static void ConfigureCamera(Camera camera)
        {
            if (camera == null)
            {
                return;
            }

            camera.allowHDR = false;
            camera.allowMSAA = false;

            ConfigureUniversalAdditionalCameraData(camera);
            ConfigureHdAdditionalCameraData(camera);
        }

        public static void ConfigureLight(Light light)
        {
            if (light == null)
            {
                return;
            }

            light.enabled = true;
            ConfigureHdAdditionalLightData(light);
        }

        public static FogOverrideScope SuppressFog()
        {
            return new FogOverrideScope(RenderSettings.fog);
        }

        private static void ConfigureUniversalAdditionalCameraData(Camera camera)
        {
            if (UniversalAdditionalCameraDataType == null)
            {
                return;
            }

            Component additionalCameraData = camera.gameObject.GetComponent(UniversalAdditionalCameraDataType);
            if (additionalCameraData == null)
            {
                additionalCameraData = camera.gameObject.AddComponent(UniversalAdditionalCameraDataType);
            }

            if (additionalCameraData == null)
            {
                return;
            }

            if (RenderPostProcessingProperty != null && RenderPostProcessingProperty.CanWrite)
            {
                RenderPostProcessingProperty.SetValue(additionalCameraData, false);
            }

            if (VolumeLayerMaskProperty != null && VolumeLayerMaskProperty.CanWrite)
            {
                LayerMask volumeLayerMask = 0;
                VolumeLayerMaskProperty.SetValue(additionalCameraData, volumeLayerMask);
            }

            if (VolumeTriggerProperty != null && VolumeTriggerProperty.CanWrite)
            {
                VolumeTriggerProperty.SetValue(additionalCameraData, null);
            }
        }

        private static void ConfigureHdAdditionalCameraData(Camera camera)
        {
            Component additionalCameraData = GetOrAddComponent(camera.gameObject, HdAdditionalCameraDataType);
            if (additionalCameraData == null)
            {
                return;
            }

            SetMemberValue(additionalCameraData, "volumeLayerMask", (LayerMask)0);
            SetMemberValue(additionalCameraData, "volumeAnchorOverride", null);
            SetMemberValue(additionalCameraData, "backgroundColorHDR", camera.backgroundColor.linear);
            SetEnumMemberValue(additionalCameraData, "clearColorMode", "Color");
            SetMemberValue(additionalCameraData, "clearDepth", true);
        }

        private static void ConfigureHdAdditionalLightData(Light light)
        {
            Component additionalLightData = GetOrAddComponent(light.gameObject, HdAdditionalLightDataType);
            if (additionalLightData == null)
            {
                return;
            }

            if (InitDefaultHdAdditionalLightDataMethod != null)
            {
                ParameterInfo[] parameters = InitDefaultHdAdditionalLightDataMethod.GetParameters();
                if (parameters.Length == 1)
                {
                    try
                    {
                        InitDefaultHdAdditionalLightDataMethod.Invoke(null, new object[] { additionalLightData });
                    }
                    catch
                    {
                        // Ignore API differences between HDRP package versions.
                    }
                }
            }

            SetMemberValue(additionalLightData, "lightDimmer", 1f);
            SetMemberValue(additionalLightData, "volumetricDimmer", 0f);
        }

        private static Component GetOrAddComponent(GameObject gameObject, Type componentType)
        {
            if (gameObject == null || componentType == null)
            {
                return null;
            }

            Component component = gameObject.GetComponent(componentType);
            if (component == null)
            {
                component = gameObject.AddComponent(componentType);
            }

            return component;
        }

        private static void SetMemberValue(object target, string memberName, object value)
        {
            if (target == null || string.IsNullOrEmpty(memberName))
            {
                return;
            }

            Type targetType = target.GetType();
            PropertyInfo property = targetType.GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance);
            if (property != null && property.CanWrite)
            {
                TrySetPropertyValue(target, property, value);
                return;
            }

            FieldInfo field = targetType.GetField(memberName, BindingFlags.Public | BindingFlags.Instance);
            if (field != null)
            {
                TrySetFieldValue(target, field, value);
            }
        }

        private static void SetEnumMemberValue(object target, string memberName, string enumValueName)
        {
            if (target == null || string.IsNullOrEmpty(memberName) || string.IsNullOrEmpty(enumValueName))
            {
                return;
            }

            Type targetType = target.GetType();
            PropertyInfo property = targetType.GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance);
            if (property != null && property.CanWrite && property.PropertyType.IsEnum)
            {
                if (TryParseEnum(property.PropertyType, enumValueName, out object enumValue))
                {
                    TrySetPropertyValue(target, property, enumValue);
                }
                return;
            }

            FieldInfo field = targetType.GetField(memberName, BindingFlags.Public | BindingFlags.Instance);
            if (field != null && field.FieldType.IsEnum)
            {
                if (TryParseEnum(field.FieldType, enumValueName, out object enumValue))
                {
                    TrySetFieldValue(target, field, enumValue);
                }
            }
        }

        private static void TrySetPropertyValue(object target, PropertyInfo property, object value)
        {
            try
            {
                object convertedValue = ConvertValue(property.PropertyType, value);
                property.SetValue(target, convertedValue);
            }
            catch
            {
                // Ignore package-version differences and leave Unity defaults in place.
            }
        }

        private static void TrySetFieldValue(object target, FieldInfo field, object value)
        {
            try
            {
                object convertedValue = ConvertValue(field.FieldType, value);
                field.SetValue(target, convertedValue);
            }
            catch
            {
                // Ignore package-version differences and leave Unity defaults in place.
            }
        }

        private static object ConvertValue(Type targetMemberType, object value)
        {
            if (value == null)
            {
                return null;
            }

            Type valueType = value.GetType();
            if (targetMemberType.IsAssignableFrom(valueType))
            {
                return value;
            }

            if (targetMemberType == typeof(int) && value is LayerMask layerMask)
            {
                return layerMask.value;
            }

            if (targetMemberType == typeof(LayerMask) && value is int intValue)
            {
                return (LayerMask)intValue;
            }

            return value;
        }

        private static bool TryParseEnum(Type enumType, string enumValueName, out object enumValue)
        {
            try
            {
                enumValue = Enum.Parse(enumType, enumValueName, true);
                return true;
            }
            catch
            {
                enumValue = null;
                return false;
            }
        }

        internal readonly struct FogOverrideScope : IDisposable
        {
            private readonly bool _fogEnabled;

            public FogOverrideScope(bool fogEnabled)
            {
                _fogEnabled = fogEnabled;

                if (fogEnabled)
                {
                    RenderSettings.fog = false;
                }
            }

            public void Dispose()
            {
                if (RenderSettings.fog != _fogEnabled)
                {
                    RenderSettings.fog = _fogEnabled;
                }
            }
        }
    }
}