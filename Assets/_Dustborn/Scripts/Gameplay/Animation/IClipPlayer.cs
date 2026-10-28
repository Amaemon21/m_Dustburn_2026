using UnityEngine;

public interface IClipPlayer
{
    float Elapsed { get; }
    void Play(AnimationClip clip, float speed, float fade);
    void FadeOut(float fade);
    void Stop();
}
