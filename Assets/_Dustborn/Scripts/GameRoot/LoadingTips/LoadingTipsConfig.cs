using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "LoadingTips", menuName = "Dustborn/UI/Loading Tips")]
public sealed class LoadingTipsConfig : ScriptableObject
{
    [SerializeField, Min(1f)] private float _displaySeconds = 7f;
    [SerializeField, TextArea(2, 4)] private string[] _tips = Array.Empty<string>();

    public float DisplaySeconds => Mathf.Max(1f, _displaySeconds);
    public IReadOnlyList<string> Tips => _tips;
}
