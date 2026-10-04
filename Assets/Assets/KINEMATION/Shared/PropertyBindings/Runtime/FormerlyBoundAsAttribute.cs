// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using UnityEngine.Scripting.APIUpdating;

namespace KINEMATION.Shared.PropertyBindings.Runtime
{
    [MovedFrom("KINEMATION.PropertyBindings.Runtime")]
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class FormerlyBoundAsAttribute : Attribute
    {
        public string oldName { get; }
        
        public FormerlyBoundAsAttribute(string oldName)
        {
            this.oldName = oldName;
        }
    }
}