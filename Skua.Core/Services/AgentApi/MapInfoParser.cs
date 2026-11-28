using System.Text.RegularExpressions;

namespace Skua.Core.Services.AgentApi;

/// <summary>
/// Turns decompiled map scripts into the list of NPCs, quests, shops and exits
/// in a map.
///
/// AQW keeps this in the map timeline instead of sending it over the wire: the
/// decompiled ActionScript says which NPC linkage is placed over which frame
/// range, and each NPC's APOP class carries its quest ids, its shop id and the
/// map it leads to. So this is the authoritative source, with no wiki scraping
/// and nothing to keep up to date.
///
/// The parser is static and takes a folder of <c>.as</c> text plus the frame
/// labels, so it is testable with a fixture instead of a game client, Java and
/// a downloaded map.
/// </summary>
internal static class MapInfoParser
{
    public static MapInfo Parse(string mapName, string filePath, int roomId, List<string> cells, string scriptsRoot, IReadOnlyList<(int Frame, string Name)> labels)
    {
        Dictionary<string, MapNpc> npcs = new(StringComparer.OrdinalIgnoreCase);

        // 1. placements: which NPC artwork sits over which frame range.
        foreach ((string name, List<string> npcCells) in ParsePlacements(scriptsRoot, labels))
        {
            if (name.Length == 0)
                continue;
            MapNpc npc = GetOrAdd(npcs, name);
            foreach (string cell in npcCells)
            {
                if (!string.IsNullOrWhiteSpace(cell) && !npc.Cells.Contains(cell, StringComparer.OrdinalIgnoreCase))
                    npc.Cells.Add(cell);
            }
        }

        // 2. APOP classes: what each NPC actually does (quests, shop, exit).
        foreach (string file in Directory.EnumerateFiles(scriptsRoot, "*_APOP.as", SearchOption.AllDirectories))
        {
            string npcName = BaseName(Path.GetFileNameWithoutExtension(file), "_APOP");
            if (npcName.Length == 0)
                continue;
            ParseApop(file, GetOrAdd(npcs, npcName));
        }

        foreach (MapNpc npc in npcs.Values)
        {
            npc.Quests = npc.Quests
                .GroupBy(q => q.Id)
                .Select(g => new NpcQuest { Id = g.Key, TurnIn = g.Any(q => q.TurnIn) })
                .OrderBy(q => q.Id)
                .ToList();
            npc.Shops = npc.Shops.GroupBy(s => s.Id).Select(g => g.First()).OrderBy(s => s.Id).ToList();
            npc.Joins = npc.Joins
                .GroupBy(j => j.Map + "|" + j.Cell, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(j => j.Map, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // APOP classes are sometimes shipped as shared assets for other rooms
        // (Battleon carries a dozen unused ones). Prefer the NPCs the timeline
        // places; if the map uses a placement scheme this parser does not know,
        // fall back to every class instead of returning an empty list.
        List<MapNpc> placed = npcs.Values.Where(n => n.Cells.Count > 0).ToList();
        if (placed.Count == 0)
            placed = npcs.Values.ToList();

        Dictionary<string, int> cellOrder = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < cells.Count; i++)
            cellOrder[cells[i]] = i;

        return new MapInfo
        {
            Map = mapName,
            FilePath = filePath,
            RoomId = roomId,
            Cells = cells,
            Npcs = placed
                .OrderBy(n => n.Cells.Count == 0 ? int.MaxValue : cellOrder.GetValueOrDefault(n.Cells[0], int.MaxValue))
                .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
        };
    }

    private static MapNpc GetOrAdd(Dictionary<string, MapNpc> npcs, string name)
    {
        if (!npcs.TryGetValue(name, out MapNpc? npc))
        {
            npc = new MapNpc { Name = name };
            npcs[name] = npc;
        }
        return npc;
    }

    /// <summary>
    /// Reads the placement functions in the map timeline: each one names an NPC
    /// linkage and the frames it covers, and the frame label under its start
    /// frame is the cell the NPC stands in.
    /// </summary>
    internal static Dictionary<string, List<string>> ParsePlacements(string scriptsRoot, IReadOnlyList<(int Frame, string Name)> labels)
    {
        Dictionary<string, List<string>> result = new(StringComparer.OrdinalIgnoreCase);
        string? main = Directory.EnumerateFiles(scriptsRoot, "MainTimeline.as", SearchOption.AllDirectories).FirstOrDefault();
        if (main is null)
            return result;

        string text = File.ReadAllText(main);
        foreach (string block in FunctionBlocks(text))
        {
            if (!block.Contains("param1", StringComparison.Ordinal))
                continue;

            Match npcMatch = Regex.Match(block, @"npcLinkage\s*=\s*""([^""]+)""");
            Match cntMatch = Regex.Match(block, @"cntLinkage\s*=\s*""([^""]+)""");
            Match apopMatch = Regex.Match(block, @"apopLinkage\s*=\s*""([^""]+)""");
            Match frameMatch = Regex.Match(block, @"param1\s*>=\s*(\d+)\s*&&\s*param1\s*<=\s*(\d+)");

            string? linkage = npcMatch.Success ? npcMatch.Groups[1].Value
                : cntMatch.Success ? cntMatch.Groups[1].Value
                : apopMatch.Success ? apopMatch.Groups[1].Value
                : null;

            if (string.IsNullOrWhiteSpace(linkage))
                continue;

            string name = BaseName(linkage, "_NPC", "_APOP");
            if (name.Length == 0)
                continue;

            string cell = frameMatch.Success ? CellForFrame(int.Parse(frameMatch.Groups[1].Value), labels) : string.Empty;
            if (!result.TryGetValue(name, out List<string>? cells))
                result[name] = cells = new List<string>();
            if (cell.Length > 0 && !cells.Contains(cell, StringComparer.OrdinalIgnoreCase))
                cells.Add(cell);
        }

        return result;
    }

    /// <summary>
    /// One NPC class: its quest ids, its shop, and the map its button joins to.
    /// The names are FFDec's decompiler output of AQW's own variables
    /// (strQuests, strTurnIns, strAction, intID, strString, strFrame), so they
    /// are stable for a given game build and worth matching literally.
    /// </summary>
    internal static void ParseApop(string file, MapNpc npc)
    {
        string text = File.ReadAllText(file);

        foreach (Match match in Regex.Matches(text, @"strQuests\s*=\s*""([^""]*)"""))
            AddQuestIds(npc, match.Groups[1].Value, turnIn: false);
        foreach (Match match in Regex.Matches(text, @"strTurnIns\s*=\s*""([^""]*)"""))
            AddQuestIds(npc, match.Groups[1].Value, turnIn: true);

        foreach (string block in FunctionBlocks(text))
        {
            Match action = Regex.Match(block, @"strAction\s*=\s*""([^""]*)""");
            if (!action.Success)
                continue;

            int id = Regex.Match(block, @"intID\s*=\s*(\d+)") is Match idMatch && idMatch.Success
                ? int.Parse(idMatch.Groups[1].Value)
                : 0;
            string value = Regex.Match(block, @"strString\s*=\s*""([^""]*)""") is Match stringMatch && stringMatch.Success
                ? stringMatch.Groups[1].Value.Trim()
                : string.Empty;
            string frame = Regex.Match(block, @"strFrame\s*=\s*""([^""]*)""") is Match frameMatch && frameMatch.Success
                ? frameMatch.Groups[1].Value.Trim()
                : string.Empty;

            switch (action.Groups[1].Value)
            {
                case "Show Quests":
                    AddQuestIds(npc, value, turnIn: false);
                    break;
                case "Item Shop":
                    if (id > 1 && npc.Shops.All(s => s.Id != id))
                        npc.Shops.Add(new NpcShop { Id = id });
                    break;
                case "/Join":
                    if (value.Length > 0 && npc.Joins.All(j => !string.Equals(j.Map, value, StringComparison.OrdinalIgnoreCase)))
                        npc.Joins.Add(new NpcJoin { Map = value, Cell = frame });
                    break;
                case "GotoAndStop":
                case "GotoAndPlay":
                case "Show Front Movie":
                    npc.HasDialogue = true;
                    break;
            }
        }
    }

    private static void AddQuestIds(MapNpc npc, string value, bool turnIn)
    {
        foreach (string part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, out int id) && id > 0 && npc.Quests.All(q => q.Id != id || q.TurnIn != turnIn))
                npc.Quests.Add(new NpcQuest { Id = id, TurnIn = turnIn });
        }
    }

    /// <summary>The text of each function in a decompiled class, in order.</summary>
    internal static IEnumerable<string> FunctionBlocks(string text)
    {
        MatchCollection matches = Regex.Matches(text, @"\b(?:internal|public|private)\s+function\s+\w+\s*\(");
        for (int i = 0; i < matches.Count; i++)
        {
            int start = matches[i].Index;
            int end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
            yield return text[start..end];
        }
    }

    /// <summary>
    /// The plain NPC name behind a library linkage: AQW names its artwork
    /// "Wulthys_NPC", its interactive wrapper "Wulthys_APOP", and sometimes
    /// appends a copy index. Repeatedly strip those to get one name per NPC,
    /// so the placement entry and the APOP class describe the same character.
    /// </summary>
    internal static string BaseName(string linkage, params string[] suffixes)
    {
        string name = linkage.Trim();
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (string suffix in suffixes)
            {
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    name = name[..^suffix.Length];
                    changed = true;
                }
            }
        }

        name = Regex.Replace(name, @"_NPC\d+$", string.Empty, RegexOptions.IgnoreCase);
        return name.Trim();
    }

    private static string CellForFrame(int frame, IReadOnlyList<(int Frame, string Name)> labels)
    {
        string cell = string.Empty;
        foreach ((int start, string name) in labels)
        {
            if (start > frame)
                break;
            cell = name;
        }
        return cell;
    }
}
