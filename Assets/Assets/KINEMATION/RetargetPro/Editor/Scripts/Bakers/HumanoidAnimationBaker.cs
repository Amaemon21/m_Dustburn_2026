// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using UnityEngine;

namespace KINEMATION.RetargetPro.Editor.Scripts.Bakers
{
    public class HumanoidAnimationBaker : IRetargetProBaker
    {
        public const string BakerId = "Humanoid";

        public string Id => BakerId;
        public string DisplayName => "Humanoid";

        private readonly GenericAnimationBaker _genericBaker = new GenericAnimationBaker();

        public bool TryGetRootMotionTransformPath(out string path)
        {
            return _genericBaker.TryGetRootMotionTransformPath(out path);
        }

        public void Initialize(KRigComponent rigComponent, KRigElement rootMotionBone)
        {
            _genericBaker.Initialize(rigComponent, rootMotionBone);
        }

        public void BakeAnimationFrame(float time)
        {
            _genericBaker.BakeAnimationFrame(time);
        }

        public void WriteToClip(AnimationClip clip)
        {
            _genericBaker.WriteToClip(clip);
        }

        public void WriteRootMotion(AnimationClip source, AnimationClip target)
        {
            _genericBaker.WriteRootMotion(source, target);
        }
    }
}
