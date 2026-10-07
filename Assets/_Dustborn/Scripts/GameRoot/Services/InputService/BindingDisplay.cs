using System.Collections.Generic;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;

public static class BindingDisplay
{
    private const string SEPARATOR = " | ";
    private const string PART_SEPARATOR = "/";
    private const InputControlPath.HumanReadableStringOptions OPTIONS =
        InputControlPath.HumanReadableStringOptions.OmitDevice | InputControlPath.HumanReadableStringOptions.UseShortNames;

    public static string Of(InputAction action)
    {
        if (action == null)
            return string.Empty;

        List<string> names = new();
        ReadOnlyArray<InputBinding> bindings = action.bindings;
        for (int i = 0; i < bindings.Count; i++)
        {
            if (bindings[i].isPartOfComposite)
                continue;

            string name = bindings[i].isComposite ? CompositeName(bindings, i) : NameOf(bindings[i]);
            if (name.Length > 0)
                names.Add(name);
        }

        return string.Join(SEPARATOR, names);
    }

    private static string CompositeName(ReadOnlyArray<InputBinding> bindings, int composite)
    {
        List<string> parts = new();
        for (int i = composite + 1; i < bindings.Count && bindings[i].isPartOfComposite; i++)
        {
            string name = NameOf(bindings[i]);
            if (name.Length > 0)
                parts.Add(name);
        }

        return string.Join(PART_SEPARATOR, parts);
    }

    private static string NameOf(InputBinding binding)
        => string.IsNullOrEmpty(binding.effectivePath)
            ? string.Empty
            : InputControlPath.ToHumanReadableString(binding.effectivePath, OPTIONS);
}
