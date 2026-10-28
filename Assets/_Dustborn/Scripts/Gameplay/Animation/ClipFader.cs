using System;

public sealed class ClipFader
{
    public const int NO_CLIP = -1;
    public const int MIN_VOICES = 2;

    private readonly ClipVoice[] _voices;
    private int _current = -1;
    private float _fade;
    private float _fadeElapsed;

    public ClipFader(int voices)
    {
        if (voices < MIN_VOICES)
            throw new ArgumentOutOfRangeException(nameof(voices), $"A crossfade needs at least {MIN_VOICES} voices");

        _voices = new ClipVoice[voices];
        for (int i = 0; i < voices; i++)
            _voices[i].Clip = NO_CLIP;
    }

    public int VoiceCount => _voices.Length;
    public int Current => _current;
    public bool IsPlaying => _current >= 0;
    public float Elapsed => _current < 0 ? 0f : _voices[_current].Elapsed;
    public bool IsFading => _fadeElapsed < _fade;

    public ref readonly ClipVoice Voice(int index) => ref _voices[index];

    public int Play(int clip, float length, bool loop, float speed, float fade)
    {
        if (clip < 0)
            throw new ArgumentOutOfRangeException(nameof(clip), "A clip id is never negative");

        int voice = TakeVoice();
        bool silent = TotalWeight() <= 0f;
        Snapshot();

        ref ClipVoice target = ref _voices[voice];
        target.Clip = clip;
        target.Generation++;
        target.Length = Math.Max(0f, length);
        target.Loop = loop;
        target.Speed = Math.Max(0f, speed);
        target.Elapsed = 0f;
        target.Time = 0f;
        target.Weight = 0f;
        target.From = 0f;

        _current = voice;
        _fade = silent ? 0f : Math.Max(0f, fade);
        _fadeElapsed = 0f;

        if (_fade <= 0f)
            Blend(1f);

        return voice;
    }

    public void Advance(float deltaTime)
    {
        if (deltaTime <= 0f)
            return;

        for (int i = 0; i < _voices.Length; i++)
            if (_voices[i].Clip != NO_CLIP)
                Step(ref _voices[i], deltaTime);

        if (!IsFading)
            return;

        _fadeElapsed += deltaTime;
        Blend(Math.Min(1f, _fadeElapsed / _fade));
    }

    public void FadeOut(float fade)
    {
        if (fade <= 0f || TotalWeight() <= 0f)
        {
            Stop();
            return;
        }

        Snapshot();
        _current = -1;
        _fade = fade;
        _fadeElapsed = 0f;
    }

    public void Stop()
    {
        for (int i = 0; i < _voices.Length; i++)
            Release(i);

        _current = -1;
        _fade = 0f;
        _fadeElapsed = 0f;
    }

    private static void Step(ref ClipVoice voice, float deltaTime)
    {
        voice.Elapsed += deltaTime * voice.Speed;

        if (voice.Length <= 0f)
            voice.Time = 0f;
        else if (voice.Loop)
            voice.Time = voice.Elapsed % voice.Length;
        else
            voice.Time = Math.Min(voice.Elapsed, voice.Length);
    }

    private int TakeVoice()
    {
        int weakest = -1;

        for (int i = 0; i < _voices.Length; i++)
        {
            if (i == _current)
                continue;
            if (_voices[i].Clip == NO_CLIP)
                return i;
            if (weakest < 0 || _voices[i].Weight < _voices[weakest].Weight)
                weakest = i;
        }

        float lost = _voices[weakest].Weight;
        Release(weakest);

        if (lost > 0f && lost < 1f)
            for (int i = 0; i < _voices.Length; i++)
                _voices[i].Weight /= 1f - lost;

        return weakest;
    }

    private void Snapshot()
    {
        for (int i = 0; i < _voices.Length; i++)
            _voices[i].From = _voices[i].Weight;
    }

    private void Blend(float progress)
    {
        for (int i = 0; i < _voices.Length; i++)
        {
            if (_voices[i].Clip == NO_CLIP)
                continue;

            if (i == _current)
            {
                _voices[i].Weight = _voices[i].From + (1f - _voices[i].From) * progress;
                continue;
            }

            _voices[i].Weight = _voices[i].From * (1f - progress);

            if (progress >= 1f)
                Release(i);
        }
    }

    private float TotalWeight()
    {
        float total = 0f;
        for (int i = 0; i < _voices.Length; i++)
            total += _voices[i].Weight;
        return total;
    }

    private void Release(int index)
    {
        ref ClipVoice voice = ref _voices[index];
        if (voice.Clip == NO_CLIP)
            return;

        voice.Clip = NO_CLIP;
        voice.Generation++;
        voice.Weight = 0f;
        voice.From = 0f;
        voice.Elapsed = 0f;
        voice.Time = 0f;
    }
}
