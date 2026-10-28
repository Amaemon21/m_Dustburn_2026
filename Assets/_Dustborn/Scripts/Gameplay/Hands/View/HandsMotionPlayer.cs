using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class HandsMotionPlayer
{
    private readonly IClipPlayer _clips;
    private readonly HashSet<(HandsAnimationSet, HandsMotion)> _reported = new();
    private HandsMotionClip _playing;
    private IClipPlayer _item;

    public HandsMotionPlayer(IClipPlayer clips)
    {
        _clips = clips ?? throw new ArgumentNullException(nameof(clips));
    }

    public HandsAnimationSet Set { get; private set; }
    public HandsMotionClip Playing => _playing;

    public void Use(HandsAnimationSet set)
    {
        Set = set;
        Stop();
    }

    public void Attach(IClipPlayer item)
    {
        if (_item == item)
            return;

        _item?.Stop();
        _item = item;
        _item?.Stop();
    }

    public void Stop()
    {
        _playing = null;
        _clips.Stop();
        _item?.Stop();
    }

    public bool Play(HandsMotion motion)
    {
        HandsMotionClip clip = Resolve(motion);
        if (clip == null)
            return false;

        Start(clip, clip.FadeIn);
        return true;
    }

    public void Update()
    {
        if (_playing == null || !_playing.Chained || _clips.Elapsed < _playing.ExitTime)
            return;

        HandsMotionClip next = Resolve(_playing.Next);
        if (next == null)
        {
            _playing = null;
            return;
        }

        Start(next, _playing.NextFade);
    }

    private void Start(HandsMotionClip clip, float fade)
    {
        _playing = clip;
        _clips.Play(clip.Clip, clip.Speed, fade);
        PlayItem(clip, fade);
    }

    private void PlayItem(HandsMotionClip clip, float fade)
    {
        if (_item == null)
            return;

        if (clip.ItemClip != null)
            _item.Play(clip.ItemClip, clip.Speed, fade);
        else
            _item.FadeOut(fade);
    }

    private HandsMotionClip Resolve(HandsMotion motion)
    {
        if (Set == null)
            return null;

        HandsMotionClip clip = Set.Find(motion);
        if (clip == null && _reported.Add((Set, motion)))
            Debug.LogError($"{nameof(HandsMotionPlayer)}: the animation set has no '{motion}' clip, the hands skip this motion", Set);

        return clip;
    }
}
