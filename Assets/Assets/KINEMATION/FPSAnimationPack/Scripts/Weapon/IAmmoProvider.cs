// Copyright (c) 2026 KINEMATION.
// All rights reserved.

namespace KINEMATION.FPSAnimationPack.Scripts.Weapon
{
    public interface IAmmoProvider
    {
        public int GetActiveAmmo();
        public int GetMaxAmmo();
    }
}