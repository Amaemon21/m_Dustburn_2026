// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace KINEMATION.Shared.PropertyBindings.Runtime
{
    [MovedFrom("KINEMATION.PropertyBindings.Runtime")]
    [AttributeUsage(AttributeTargets.Field)]
    public class BoundRangeAttribute : PropertyAttribute
    {
        public readonly float min;
        public readonly float max;

        public BoundRangeAttribute(float min, float max)
        {
            this.min = min;
            this.max = max;
        }
    }
}