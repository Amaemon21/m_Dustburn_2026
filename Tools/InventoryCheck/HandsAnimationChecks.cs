using System;
using System.Collections.Generic;
using UnityEngine;

internal static partial class Harness
{
    private const float FADE_TOLERANCE = 1e-4f;

    private static void VerifyClipFader()
    {
        Expect<ArgumentOutOfRangeException>(() => new ClipFader(1), "a crossfade needs two voices");
        ClipFader fader = new(3);
        Expect<ArgumentOutOfRangeException>(() => fader.Play(-1, 1f, false, 1f, 0f), "a clip id is never negative");

        int idle = fader.Play(0, 2f, true, 1f, 0.25f);
        Check(Close(fader.Voice(idle).Weight, 1f) && !fader.IsFading, "the first clip snaps in without a fade");
        fader.Advance(2.5f);
        Check(Close(fader.Voice(idle).Time, 0.5f) && Close(fader.Elapsed, 2.5f), "a looping clip wraps its time while elapsed keeps counting");

        int attack = fader.Play(1, 1f, false, 2f, 0.2f);
        fader.Advance(0.1f);
        Check(Close(fader.Voice(attack).Weight, 0.5f) && Close(fader.Voice(idle).Weight, 0.5f), "halfway through a fade both clips share the pose");
        Check(Close(fader.Voice(attack).Time, 0.2f), "speed scales clip time");
        int idleGeneration = fader.Voice(idle).Generation;
        fader.Advance(0.1f);
        Check(Close(fader.Voice(attack).Weight, 1f) && fader.Voice(idle).Clip == ClipFader.NO_CLIP && fader.Voice(idle).Generation != idleGeneration,
            "a finished fade releases the old voice");
        fader.Advance(1f);
        Check(Close(fader.Voice(attack).Time, 1f) && Close(fader.Elapsed, 2.4f), "a one-shot clip holds its last frame");

        fader.Play(2, 1f, true, 1f, 0.2f);
        fader.Advance(0.1f);
        fader.Play(3, 1f, true, 1f, 0.2f);
        fader.Advance(0.05f);
        Check(Close(TotalWeight(fader), 1f) && Close(fader.Voice(fader.Current).Weight, 0.25f), "an interrupted fade keeps the weights whole");
        fader.Play(4, 1f, true, 1f, 0.2f);
        Check(Close(TotalWeight(fader), 1f) && Close(fader.Voice(fader.Current).Weight, 0f), "stealing the weakest voice keeps the pose whole");
        fader.Advance(0.05f);
        Check(Close(TotalWeight(fader), 1f) && Close(fader.Voice(fader.Current).Weight, 0.25f), "a fade over a stolen voice stays whole");
        fader.Advance(0.2f);
        Check(Close(fader.Voice(fader.Current).Weight, 1f) && Audible(fader) == 1, "the fade settles on one voice");

        int played = fader.Play(5, 1f, false, 1f, 0f);
        Check(Close(fader.Voice(played).Weight, 1f) && Audible(fader) == 1, "a zero fade cuts at once");
        fader.Stop();
        Check(!fader.IsPlaying && Close(TotalWeight(fader), 0f) && Close(fader.Elapsed, 0f), "stop silences every voice");
        played = fader.Play(0, 1f, true, 1f, 0.5f);
        Check(Close(fader.Voice(played).Weight, 1f), "after a stop the next clip snaps in");

        fader.FadeOut(0.4f);
        fader.Advance(0.1f);
        Check(!fader.IsPlaying && fader.IsFading && Close(TotalWeight(fader), 0.75f), "a fade out lowers the pose toward rest");
        fader.Advance(0.3f);
        Check(!fader.IsFading && Audible(fader) == 0 && Close(TotalWeight(fader), 0f), "a finished fade out releases every voice");
        played = fader.Play(1, 1f, false, 1f, 0.5f);
        Check(Close(fader.Voice(played).Weight, 1f), "after a fade out the next clip snaps in");
        fader.FadeOut(0.4f);
        fader.Advance(0.2f);
        played = fader.Play(2, 1f, false, 1f, 0.2f);
        fader.Advance(0.1f);
        Check(Close(fader.Voice(played).Weight, 0.5f) && Close(TotalWeight(fader), 0.75f), "a clip started mid fade out takes over the fading pose");
        fader.Stop();
        fader.FadeOut(0.4f);
        Check(!fader.IsFading && Audible(fader) == 0, "a silent fader has nothing to fade out");
    }

    private static void VerifyHandsMotionPlayer()
    {
        AnimationClip idleClip = FakeAsset<AnimationClip>();
        AnimationClip walkClip = FakeAsset<AnimationClip>();
        AnimationClip equipClip = FakeAsset<AnimationClip>();
        AnimationClip startClip = FakeAsset<AnimationClip>();
        AnimationClip endClip = FakeAsset<AnimationClip>();

        HandsAnimationSet family = AnimationSet(null,
            Motion(HandsMotion.Idle, idleClip, 0.25f, 4f),
            Motion(HandsMotion.Walk, walkClip, 0.2f, 1f),
            Motion(HandsMotion.RunEnd, endClip, 0.15f, 0.3f, HandsMotion.Idle, 0.1f));
        HandsAnimationSet item = AnimationSet(family,
            Motion(HandsMotion.Equip, equipClip, 0f, 0.6f, HandsMotion.Idle, 0.1f),
            Motion(HandsMotion.RunStart, startClip, 0.15f, 0.3f, HandsMotion.Run, 0.1f));

        TestClips clips = new();
        HandsMotionPlayer player = new(clips);
        Check(!player.Play(HandsMotion.Idle) && clips.Played.Count == 0, "no set plays nothing");

        player.Use(item);
        Check(clips.Stops == 1, "a new set stops the old clips");
        Check(player.Play(HandsMotion.Equip) && clips.Played[^1].Clip == equipClip && clips.Played[^1].Fade == 0f,
            "a motion plays its clip with its own fade");
        clips.Elapsed = 0.45f;
        player.Update();
        Check(clips.Played.Count == 1, "a chain waits for its exit time");
        clips.Elapsed = 0.5f;
        player.Update();
        Check(clips.Played[^1].Clip == idleClip && Close(clips.Played[^1].Fade, 0.1f) && player.Playing.Motion == HandsMotion.Idle,
            "a chain starts its next motion at the end minus the chain fade, from the base set");
        clips.Elapsed = 100f;
        player.Update();
        Check(clips.Played.Count == 2, "a loop never chains");

        Check(player.Play(HandsMotion.Walk) && clips.Played[^1].Clip == walkClip && Close(clips.Played[^1].Fade, 0.2f), "a base motion plays through an item set");
        int count = clips.Played.Count;
        Check(!player.Play(HandsMotion.Run) && clips.Played.Count == count, "a missing motion is refused");
        Check(player.Play(HandsMotion.RunStart), "an item motion plays");
        clips.Elapsed = 0.25f;
        player.Update();
        Check(clips.Played.Count == count + 1 && player.Playing == null, "a chain into a missing motion stops chaining");
        player.Update();
        Check(clips.Played.Count == count + 1, "a dead chain stays quiet");

        Check(player.Play(HandsMotion.RunEnd) && clips.Played[^1].Clip == endClip, "a family motion fills a gap in the item");
        player.Stop();
        Check(clips.Stops == 2 && player.Playing == null, "stop drops the playing motion");

        AnimationClip slideClip = FakeAsset<AnimationClip>();
        HandsMotionClip fire = Motion(HandsMotion.Attack, startClip, 0.05f, 0.4f);
        SetBackingField(fire, nameof(HandsMotionClip.ItemClip), slideClip);
        SetBackingField(fire, nameof(HandsMotionClip.Speed), 1.5f);
        HandsAnimationSet pistol = AnimationSet(family, fire);
        TestClips mechanism = new();
        player.Use(pistol);
        player.Attach(mechanism);
        Check(mechanism.Stops == 1 && mechanism.Played.Count == 0, "an attached item starts at rest");
        Check(player.Play(HandsMotion.Attack) && mechanism.Played.Count == 1 && mechanism.Played[^1].Clip == slideClip &&
              Close(mechanism.Played[^1].Speed, 1.5f) && Close(mechanism.Played[^1].Fade, 0.05f), "the item plays its clip with the hands' speed and fade");
        Check(player.Play(HandsMotion.Walk) && mechanism.FadeOuts.Count == 1 && Close(mechanism.FadeOuts[^1], 0.2f) && mechanism.Played.Count == 1,
            "a motion without an item clip fades the item back to rest");
        player.Play(HandsMotion.RunEnd);
        clips.Elapsed = 0.3f;
        player.Update();
        Check(mechanism.FadeOuts.Count == 3, "a chained motion drives the item too");
        player.Stop();
        Check(mechanism.Stops == 2, "stopping the hands stops the item");
        player.Attach(mechanism);
        Check(mechanism.Stops == 2, "attaching the same item twice changes nothing");
        player.Attach(null);
        Check(mechanism.Stops == 3 && player.Play(HandsMotion.Attack) && mechanism.Played.Count == 1, "a detached item is left alone");

        HandsMotionClip swing = Motion(HandsMotion.Attack, idleClip, 0.05f, 0.8f);
        SetBackingField(swing, nameof(HandsMotionClip.Speed), 2f);
        SetBackingField(swing, nameof(HandsMotionClip.Impact), 0.4f);
        Check(Close(swing.PlayTime, 0.4f) && Close(swing.ImpactTime, 0.2f), "speed shortens a motion and moves its impact with it");
    }

    private static bool Close(float value, float expected) => Math.Abs(value - expected) <= FADE_TOLERANCE;

    private static float TotalWeight(ClipFader fader)
    {
        float total = 0f;
        for (int i = 0; i < fader.VoiceCount; i++)
            total += fader.Voice(i).Weight;
        return total;
    }

    private static int Audible(ClipFader fader)
    {
        int audible = 0;
        for (int i = 0; i < fader.VoiceCount; i++)
            if (fader.Voice(i).Clip != ClipFader.NO_CLIP)
                audible++;
        return audible;
    }

    private static HandsAnimationSet AnimationSet(HandsAnimationSet baseSet, params HandsMotionClip[] motions)
    {
        HandsAnimationSet set = FakeAsset<HandsAnimationSet>();
        SetBackingField(set, nameof(HandsAnimationSet.Base), baseSet);
        SetBackingField(set, nameof(HandsAnimationSet.Motions), motions);
        return set;
    }

    private static HandsMotionClip Motion(HandsMotion motion, AnimationClip clip, float fade, float end)
    {
        HandsMotionClip entry = new();
        SetBackingField(entry, nameof(HandsMotionClip.Motion), motion);
        SetBackingField(entry, nameof(HandsMotionClip.Clip), clip);
        SetBackingField(entry, nameof(HandsMotionClip.FadeIn), fade);
        SetBackingField(entry, nameof(HandsMotionClip.End), end);
        return entry;
    }

    private static HandsMotionClip Motion(HandsMotion motion, AnimationClip clip, float fade, float end, HandsMotion next, float nextFade)
    {
        HandsMotionClip entry = Motion(motion, clip, fade, end);
        SetBackingField(entry, nameof(HandsMotionClip.Chained), true);
        SetBackingField(entry, nameof(HandsMotionClip.Next), next);
        SetBackingField(entry, nameof(HandsMotionClip.NextFade), nextFade);
        return entry;
    }

    private sealed class TestClips : IClipPlayer
    {
        public readonly List<(AnimationClip Clip, float Speed, float Fade)> Played = new();
        public readonly List<float> FadeOuts = new();
        public int Stops;

        public float Elapsed { get; set; }

        public void Play(AnimationClip clip, float speed, float fade)
        {
            Played.Add((clip, speed, fade));
            Elapsed = 0f;
        }

        public void FadeOut(float fade) => FadeOuts.Add(fade);

        public void Stop() => Stops++;
    }
}
