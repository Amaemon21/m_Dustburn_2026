using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

internal static partial class Harness
{
    private static void VerifyMainMenuAssets()
    {
        string root = AppContext.BaseDirectory;
        while (root != null && !Directory.Exists(Path.Combine(root, "Assets", "_Dustborn")))
            root = Directory.GetParent(root)?.FullName;
        Check(root != null, "menu asset checks locate the project root");
        string prefab = File.ReadAllText(Path.Combine(root, "Assets/_Dustborn/Content/Prefabs/UI/Root/MainMenuUI.prefab"));
        MatchCollection objects = Regex.Matches(prefab, @"^--- !u!(\d+) &(-?\d+)\r?\n[\s\S]*?(?=^--- !u!|\z)", RegexOptions.Multiline);
        var blocks = new Dictionary<string, string>();
        foreach (Match item in objects)
            Check(blocks.TryAdd(item.Groups[2].Value, item.Value), "menu object IDs remain unique");
        foreach (string block in blocks.Values)
        {
            foreach (Match reference in Regex.Matches(block, @"\{fileID: (-?\d+)([^}]*)\}"))
            {
                string id = reference.Groups[1].Value;
                if (id != "0" && !reference.Groups[2].Value.Contains("guid:"))
                    Check(blocks.ContainsKey(id), "menu local references resolve");
            }
            if (!block.StartsWith("--- !u!224"))
                continue;
            string idOfParent = Regex.Match(block, @"^--- !u!224 &(\d+)").Groups[1].Value;
            string children = Regex.Match(block, @"m_Children:([\s\S]*?)  m_Father:").Groups[1].Value;
            foreach (Match child in Regex.Matches(children, @"fileID: (\d+)"))
                Check(blocks[child.Groups[1].Value].Contains("m_Father: {fileID: " + idOfParent + "}"),
                    "menu children point back to their parent");
        }
        string main = FindMenuBlock(blocks, "::MainScreenView");
        string binder = FindMenuBlock(blocks, "::UIMainMenuRootBinder");
        string load = FindMenuBlock(blocks, "::LoadGameScreenView");
        foreach (string field in new[] { "_slotPrefab", "_slotContent", "_loadButton", "_deleteButton", "_backButton" })
            Check(Regex.IsMatch(load, field + @": \{fileID: [1-9]\d*"), "load game wires its " + field + " control");

        string newGame = FindMenuBlock(blocks, "::NewGameScreenView");
        foreach (string field in new[] { "_name", "_startButton", "_createWorldButton", "_deleteWorldButton", "_count", "_usage", "_mapName", "_metadata", "_relief", "_stats", "_footer", "_pointer", "_previewAspect", "_zoomInButton", "_zoomOutButton", "_unusedMarker" })
            Check(Regex.IsMatch(newGame, field + @": \{fileID: [1-9]\d*\}"), "new game wires its " + field + " control");
        string rowPrefab = File.ReadAllText(Path.Combine(root, "Assets/_Dustborn/Content/Prefabs/UI/MainMenu/GameSlotView.prefab"));
        Check(Regex.IsMatch(rowPrefab, @"_details: \{fileID: [1-9]\d*\}"), "playthrough rows expose the slot status text");
        Check(rowPrefab.Contains("_thumbnail:") && rowPrefab.Contains("_selectionAccent:"), "shared list rows expose image and selection references");
        Check(rowPrefab.Contains("m_PreferredHeight: 124"), "map list rows retain a nonzero preferred height in layout groups");
        Check(!main.Contains("_myMapsButton:") && !main.Contains("_continueDetails: {fileID: 0}"),
            "the main menu exposes continue details and uses the new game entry for maps");
        string newGameId = Regex.Match(newGame, @"^--- !u!114 &(\d+)").Groups[1].Value;
        Check(binder.Contains("_newGameScreen: {fileID: " + newGameId + "}") && !binder.Contains("_myMapsScreen:"),
            "the root binder uses the combined new game screen");
        string mapContent = Regex.Match(newGame, @"_worldContent: \{fileID: (\d+)\}").Groups[1].Value;
        Check(blocks.TryGetValue(mapContent, out string content), "new game references the map list content");
        string mapViewport = Regex.Match(content, @"m_Father: \{fileID: (\d+)\}").Groups[1].Value;
        int mapScrollRects = 0;

        foreach (string block in blocks.Values)
        {
            if (block.Contains($"m_Content: {{fileID: {mapContent}}}") &&
                block.Contains($"m_Viewport: {{fileID: {mapViewport}}}") &&
                block.Contains("m_Vertical: 1"))
                mapScrollRects++;
        }

        Check(mapScrollRects == 1, "new game has one vertical scroll rect wired to the map list");
    }

    private static string FindMenuBlock(Dictionary<string, string> blocks, string identifier)
    {
        foreach (string block in blocks.Values)
        {
            if (block.Contains(identifier + "\r") || block.Contains(identifier + "\n"))
                return block;
        }
        throw new InvalidOperationException("Missing menu view: " + identifier);
    }
}
