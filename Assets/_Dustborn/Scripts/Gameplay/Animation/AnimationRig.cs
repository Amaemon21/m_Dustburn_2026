using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

public sealed class AnimationRig : IDisposable
{
    private readonly PlayableGraph _graph;
    private readonly AnimationLayerMixerPlayable _layers;
    private readonly List<ClipLayer> _clipLayers = new();

    public AnimationRig(string name, Animator animator)
    {
        if (animator == null)
            throw new ArgumentNullException(nameof(animator));

        animator.runtimeAnimatorController = null;
        _graph = PlayableGraph.Create(name);
        _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        _layers = AnimationLayerMixerPlayable.Create(_graph, 0);
        AnimationPlayableOutput output = AnimationPlayableOutput.Create(_graph, name, animator);
        output.SetSourcePlayable(_layers);
        _graph.Play();
    }

    public ClipLayer AddLayer(int voices, AvatarMask mask = null, bool additive = false)
    {
        int index = _layers.GetInputCount();
        ClipLayer layer = new(_graph, voices);

        _layers.SetInputCount(index + 1);
        _graph.Connect(layer.Mixer, 0, _layers, index);
        _layers.SetInputWeight(index, 1f);
        _layers.SetLayerAdditive((uint)index, additive);

        if (mask != null)
            _layers.SetLayerMaskFromAvatarMask((uint)index, mask);

        _clipLayers.Add(layer);
        return layer;
    }

    public void SetWeight(ClipLayer layer, float weight)
    {
        int index = _clipLayers.IndexOf(layer);
        if (index < 0)
            throw new ArgumentException("The layer belongs to another rig", nameof(layer));

        _layers.SetInputWeight(index, Mathf.Clamp01(weight));
    }

    public void Evaluate()
    {
        foreach (ClipLayer layer in _clipLayers)
            layer.Apply();

        _graph.Evaluate();
    }

    public void Dispose()
    {
        if (!_graph.IsValid())
            return;

        foreach (ClipLayer layer in _clipLayers)
            layer.Dispose();

        _clipLayers.Clear();
        _graph.Destroy();
    }
}
