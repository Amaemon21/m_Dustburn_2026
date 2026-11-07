using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using R3;

internal static partial class Harness
{
    private static void VerifyListItemSelection()
    {
        using var selected = new ReactiveProperty<string>();
        using var first = new ListItemViewModel("First", "", selected.Select(id => id == "first"), () => selected.Value = "first");
        using var second = new ListItemViewModel("Second", "", selected.Select(id => id == "second"), () => selected.Value = "second");
        selected.Value = "first";
        Check(first.IsSelected.CurrentValue && !second.IsSelected.CurrentValue,
            "the initial map selection is available before its row is bound");
        bool initialSelection = false;
        using var binding = first.IsSelected.Subscribe(value => initialSelection = value);
        Check(initialSelection, "binding a selected row receives its selected state immediately");
        second.Select.Execute(Unit.Default);
        Check(!first.IsSelected.CurrentValue && second.IsSelected.CurrentValue,
            "selecting another row moves persistent selection to exactly that row");

        string root = AppContext.BaseDirectory;
        while (root != null && !Directory.Exists(Path.Combine(root, "Assets", "_Dustborn")))
            root = Directory.GetParent(root)?.FullName;
        string prefab = File.ReadAllText(Path.Combine(root, "Assets/_Dustborn/Content/Prefabs/UI/MainMenu/GameSlotView.prefab"));
        var blocks = new Dictionary<string, string>();
        foreach (Match match in Regex.Matches(prefab, @"^--- !u!(\d+) &(-?\d+)\r?\n[\s\S]*?(?=^--- !u!|\z)", RegexOptions.Multiline))
            blocks.Add(match.Groups[2].Value, match.Value);
        string row = FindMenuBlock(blocks, "::ListItemView");
        string owner = RowReference(row, "m_GameObject");
        string button = blocks[RowReference(row, "_button")];
        Check(RowReference(button, "m_GameObject") == owner,
            "the row receives selection interfaces on the same object as its Button");
        string fill = blocks[RowReference(row, "_hoverFill")];
        string fillOwner = RowReference(fill, "m_GameObject");
        string fillRect = null;
        foreach (string block in blocks.Values)
        {
            if (block.StartsWith("--- !u!224") && RowReference(block, "m_GameObject") == fillOwner)
                fillRect = block;
        }
        Check(fillRect != null && fillRect.Contains("m_AnchorMin: {x: 0, y: 0}") && fillRect.Contains("m_AnchorMax: {x: 1, y: 1}"),
            "the highlight stretches across the entire row");
        Check(fillRect.Contains("m_AnchoredPosition: {x: 0, y: 0}") && fillRect.Contains("m_SizeDelta: {x: 0, y: 0}"),
            "the highlight is inside the row instead of displaced outside it");
        Check(fill.Contains("m_RaycastTarget: 0"), "the highlight does not intercept pointer input");
    }

    private static string RowReference(string block, string name)
        => Regex.Match(block, name + @": \{fileID: (-?\d+)").Groups[1].Value;
}
