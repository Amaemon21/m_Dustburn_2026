using System;
using System.Collections.Generic;
using R3;

public sealed class ShareGroup : IDisposable
{
    public const int STEP = WorldVariation.SHARE_STEP;
    public const int TOTAL_STEPS = 100 / STEP;

    private static readonly string[] PERCENTS = Percents();

    private readonly OptionFieldViewModel[] _fields;
    private readonly int[] _values;
    private readonly CompositeDisposable _subscriptions = new();
    private bool _balancing;

    public IReadOnlyList<OptionFieldViewModel> Fields => _fields;

    public ShareGroup(int[] percents, int[] maxima = null)
    {
        _fields = new OptionFieldViewModel[percents.Length];
        _values = new int[percents.Length];

        for (int i = 0; i < percents.Length; i++)
        {
            int maximum = maxima == null ? TOTAL_STEPS : maxima[i] / STEP;
            _fields[i] = new OptionFieldViewModel(PERCENTS, percents[i] / STEP, maximum);
            _values[i] = _fields[i].Index.Value;
        }

        for (int i = 0; i < _fields.Length; i++)
        {
            int field = i;
            _subscriptions.Add(_fields[i].Index.Subscribe(index => Changed(field, index)));
        }
    }

    public int Percent(int field) => _values[field] * STEP;

    public void Set(int[] percents)
    {
        _balancing = true;

        for (int i = 0; i < _fields.Length; i++)
        {
            _values[i] = _fields[i].Clamp(percents[i] / STEP);
            _fields[i].Index.Value = _values[i];
        }

        _balancing = false;
    }

    public void SetInteractable(bool interactable)
    {
        foreach (OptionFieldViewModel field in _fields)
            field.SetInteractable(interactable);
    }

    private void Changed(int field, int index)
    {
        if (_balancing || index == _values[field])
            return;

        int delta = index - _values[field];
        _values[field] = index;
        int left = Balance(field, -delta);

        if (left != 0)
            _values[field] -= left;

        Publish();
    }

    private int Balance(int changed, int delta)
    {
        while (delta != 0)
        {
            int target = delta < 0 ? Largest(changed) : NextOpen(changed);
            if (target < 0)
                return -delta;

            int step = Math.Sign(delta);
            _values[target] += step;
            delta -= step;
        }

        return 0;
    }

    private int Largest(int changed)
    {
        int best = -1;

        for (int offset = 1; offset < _values.Length; offset++)
        {
            int i = (changed + offset) % _values.Length;

            if (_values[i] > 0 && (best < 0 || _values[i] > _values[best]))
                best = i;
        }

        return best;
    }

    private int NextOpen(int changed)
    {
        for (int offset = 1; offset < _values.Length; offset++)
        {
            int i = (changed + offset) % _values.Length;

            if (_values[i] < _fields[i].Maximum)
                return i;
        }

        return -1;
    }

    private void Publish()
    {
        _balancing = true;

        for (int i = 0; i < _fields.Length; i++)
            _fields[i].Index.Value = _values[i];

        _balancing = false;
    }

    private static string[] Percents()
    {
        var labels = new string[TOTAL_STEPS + 1];

        for (int i = 0; i < labels.Length; i++)
            labels[i] = $"{i * STEP}%";

        return labels;
    }

    public void Dispose()
    {
        _subscriptions.Dispose();

        foreach (OptionFieldViewModel field in _fields)
            field.Dispose();
    }
}
