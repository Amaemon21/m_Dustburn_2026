// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using UnityEngine;

namespace KINEMATION.RetargetPro.Editor.Scripts.Bakers
{
    public interface IRetargetProBaker
    {
        string Id { get; }
        string DisplayName { get; }
        bool TryGetRootMotionTransformPath(out string path);
        void Initialize(KRigComponent rigComponent, KRigElement rootMotionBone);
        void BakeAnimationFrame(float time);
        void WriteToClip(AnimationClip clip);
        void WriteRootMotion(AnimationClip source, AnimationClip target);
    }
}