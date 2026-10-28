using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

public static class ListDuplicates
{
    public static int Clear<T, TKey>(IList<T> list, Func<T, TKey> key, Action<int> clear, Object owner, string listName)
    {
        List<int> repeats = Find(list, key);
        foreach (int index in repeats)
            clear(index);

        if (repeats.Count > 0)
            Debug.LogWarning($"{Name(owner)}: {listName} already holds this entry, the repeat in row {Rows(repeats)} was cleared. Each entry may appear once", owner);

        return repeats.Count;
    }

    public static int Warn<T, TKey>(IList<T> list, Func<T, TKey> key, Object owner, string listName)
    {
        List<int> repeats = Find(list, key);
        if (repeats.Count > 0)
            Debug.LogWarning($"{Name(owner)}: {listName} repeats an entry in row {Rows(repeats)}, only the first one is used. Each entry may appear once", owner);

        return repeats.Count;
    }

    public static List<int> Find<T, TKey>(IList<T> list, Func<T, TKey> key)
    {
        var repeats = new List<int>();
        if (list == null)
            return repeats;

        var seen = new HashSet<TKey>();
        for (int i = 0; i < list.Count; i++)
        {
            if (IsMissing(list[i]))
                continue;

            TKey value = key(list[i]);
            if (IsMissing(value))
                continue;

            if (!seen.Add(value))
                repeats.Add(i);
        }

        return repeats;
    }

    private static bool IsMissing(object value) => value == null || (value is Object unityObject && unityObject == null);

    private static string Name(Object owner) => owner == null ? "config" : owner.name;

    private static string Rows(List<int> repeats) => string.Join(", ", repeats.ConvertAll(index => (index + 1).ToString()));
}
