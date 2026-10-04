// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using UnityEngine;

namespace KINEMATION.CharacterAnimationSystem.Examples.Scripts
{
    [AddComponentMenu(CasNames.Path_ComponentMenu + "Examples/Cas Prop")]
    public class CasProp : MonoBehaviour
    {
        public CharacterAnimationSettings animationSettings;
        public Transform rightHandTarget;
        public Transform leftHandTarget;

        public virtual void UseItem()
        {
        }

        public virtual void StopUsingItem()
        {
        }

        public virtual void OnEquipped()
        {
        }

        public virtual float OnUnEquipped()
        {
            return 0f;
        }

        public virtual void OnAim(bool isAiming)
        {
        }

        public virtual void SetVisibility(bool isVisible)
        {
            gameObject.SetActive(isVisible);
        }
    }
}