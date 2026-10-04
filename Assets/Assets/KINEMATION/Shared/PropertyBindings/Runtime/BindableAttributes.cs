// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace KINEMATION.Shared.PropertyBindings.Runtime
{
    [MovedFrom("KINEMATION.PropertyBindings.Runtime")]
    [AttributeUsage(AttributeTargets.Field)]
    public class BindAnimatorAttribute : PropertyAttribute
    {
        public BindAnimatorAttribute() { }
    }

    [MovedFrom("KINEMATION.PropertyBindings.Runtime")]
    public class CustomBindableDrawerAttribute : PropertyAttribute
    {
        public bool showLabel;
        
        public CustomBindableDrawerAttribute(bool showLabel)
        {
            this.showLabel = showLabel;
        }
    }
}