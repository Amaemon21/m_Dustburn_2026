using System;
using System.Collections.Generic;

public sealed class LoadingTipRotation
{
    private readonly Random _random = new();
    private readonly List<string> _tips = new();
    private int _nextIndex;
    private string _lastTip;

    public int Count => _tips.Count;

    public LoadingTipRotation(IReadOnlyList<string> tips)
    {
        foreach (string tip in tips)
        {
            if (string.IsNullOrWhiteSpace(tip))
                continue;

            string text = tip.Trim();

            if (!_tips.Contains(text))
                _tips.Add(text);
        }

        _nextIndex = _tips.Count;
    }

    public string Next()
    {
        if (_tips.Count == 0)
            return string.Empty;

        if (_nextIndex == _tips.Count)
            Shuffle();

        _lastTip = _tips[_nextIndex++];
        return _lastTip;
    }

    private void Shuffle()
    {
        for (int i = _tips.Count - 1; i > 0; i--)
        {
            int index = _random.Next(i + 1);
            (_tips[i], _tips[index]) = (_tips[index], _tips[i]);
        }

        if (_tips.Count > 1 && _tips[0] == _lastTip)
        {
            int index = _random.Next(1, _tips.Count);
            (_tips[0], _tips[index]) = (_tips[index], _tips[0]);
        }

        _nextIndex = 0;
    }
}
