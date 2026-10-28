using System;
using UnityEngine;

[CreateAssetMenu(fileName = "HandsAnimations", menuName = "Dustborn/Hands/Animation Set")]
public sealed class HandsAnimationSet : ScriptableObject
{
    private const int MAX_DEPTH = 8;

    [field: SerializeField] public HandsAnimationSet Base { get; private set; }
    [field: SerializeField] public HandsMotionClip[] Motions { get; private set; } = Array.Empty<HandsMotionClip>();

    public HandsMotionClip Find(HandsMotion motion)
    {
        HandsAnimationSet set = this;

        for (int depth = 0; depth < MAX_DEPTH && set != null; depth++)
        {
            HandsMotionClip found = set.Own(motion);
            if (found != null)
                return found;

            set = set.Base;
        }

        return null;
    }

    private HandsMotionClip Own(HandsMotion motion)
    {
        if (Motions == null)
            return null;

        foreach (HandsMotionClip clip in Motions)
            if (clip != null && clip.Motion == motion && clip.Clip != null)
                return clip;

        return null;
    }

    private void OnValidate()
    {
        Motions ??= Array.Empty<HandsMotionClip>();

        foreach (HandsMotionClip clip in Motions)
            if (clip != null && clip.IsUnset)
                clip.ApplyDefaults();

        if (Base == this)
        {
            Debug.LogWarning($"{name}: an animation set cannot be its own base");
            Base = null;
        }
    }
}
