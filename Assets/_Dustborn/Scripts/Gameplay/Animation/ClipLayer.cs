using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

public sealed class ClipLayer : IClipPlayer, IDisposable
{
    private readonly PlayableGraph _graph;
    private readonly ClipFader _fader;
    private readonly AnimationClipPlayable[] _playables;
    private readonly int[] _bound;
    private readonly List<AnimationClip> _clips = new();
    private readonly Dictionary<AnimationClip, int> _ids = new();

    internal ClipLayer(PlayableGraph graph, int voices)
    {
        _graph = graph;
        _fader = new ClipFader(voices);
        _playables = new AnimationClipPlayable[voices];
        _bound = new int[voices];
        Mixer = AnimationMixerPlayable.Create(graph, voices);
    }

    internal AnimationMixerPlayable Mixer { get; }

    public float Elapsed => _fader.Elapsed;
    public bool IsPlaying => _fader.IsPlaying;

    public void Play(AnimationClip clip, float speed, float fade)
    {
        if (clip == null)
            throw new ArgumentNullException(nameof(clip));

        _fader.Play(IdOf(clip), clip.length, clip.isLooping, speed, fade);
    }

    public void FadeOut(float fade) => _fader.FadeOut(fade);

    public void Stop() => _fader.Stop();

    public void Advance(float deltaTime) => _fader.Advance(deltaTime);

    internal void Apply()
    {
        for (int i = 0; i < _fader.VoiceCount; i++)
        {
            ref readonly ClipVoice voice = ref _fader.Voice(i);

            if (_bound[i] != voice.Generation)
                Bind(i, voice);

            if (voice.Clip == ClipFader.NO_CLIP)
                continue;

            Mixer.SetInputWeight(i, voice.Weight);
            _playables[i].SetTime(voice.Time);
        }
    }

    private void Bind(int input, in ClipVoice voice)
    {
        Unbind(input);
        _bound[input] = voice.Generation;

        if (voice.Clip == ClipFader.NO_CLIP)
            return;

        AnimationClipPlayable playable = AnimationClipPlayable.Create(_graph, _clips[voice.Clip]);
        playable.SetApplyFootIK(false);
        playable.SetApplyPlayableIK(false);
        playable.Pause();
        _graph.Connect(playable, 0, Mixer, input);
        _playables[input] = playable;
    }

    private void Unbind(int input)
    {
        Mixer.SetInputWeight(input, 0f);

        if (!_playables[input].IsValid())
            return;

        _graph.Disconnect(Mixer, input);
        _playables[input].Destroy();
        _playables[input] = default;
    }

    private int IdOf(AnimationClip clip)
    {
        if (_ids.TryGetValue(clip, out int id))
            return id;

        id = _clips.Count;
        _clips.Add(clip);
        _ids.Add(clip, id);
        return id;
    }

    public void Dispose()
    {
        if (!_graph.IsValid())
            return;

        for (int i = 0; i < _playables.Length; i++)
            Unbind(i);
    }
}
