using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public sealed class StoryBattleOption
{
    public StoryBattleReferenceDocument reference;
    public string mapName;
    public string npcName;
    public string battleName;
    public bool isMod;

    public string displayName => (isMod ? "[当前 Mod] " : "[本体] ")
        + mapName + " / " + npcName + " / " + battleName;
}

public static class StoryBattleCatalog
{
    public static List<StoryBattleOption> GetOptions(string filter)
    {
        Dictionary<string, StoryBattleOption> options = new Dictionary<string, StoryBattleOption>(StringComparer.OrdinalIgnoreCase);
        foreach (TextAsset asset in Resources.LoadAll<TextAsset>("Data/Maps"))
        {
            try { AddMap(options, ResourceManager.GetXML<Map>(asset.text)); }
            catch { }
        }

        string modDirectory = Path.Combine(Application.persistentDataPath, "Mod", "Maps");
        try
        {
            if (Directory.Exists(modDirectory))
            {
                foreach (string path in Directory.GetFiles(modDirectory, "*.xml", SearchOption.TopDirectoryOnly))
                {
                    try { AddMap(options, ResourceManager.GetXML<Map>(File.ReadAllText(path))); }
                    catch { }
                }
            }
        }
        catch { }

        string query = (filter ?? string.Empty).Trim();
        return options.Values.Where(option => string.IsNullOrEmpty(query)
                || option.displayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                || option.reference.mapId.ToString().Contains(query)
                || option.reference.npcId.ToString().Contains(query)
                || (option.reference.battleId ?? string.Empty).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderBy(option => option.isMod).ThenBy(option => option.reference.mapId)
            .ThenBy(option => option.reference.npcId).ThenBy(option => option.reference.battleId)
            .ToList();
    }

    public static bool TryResolve(StoryBattleReferenceDocument reference, out BattleInfo battleInfo, out string error)
    {
        battleInfo = null;
        error = string.Empty;
        if (reference == null || !StoryResourceValidator.TryLoadMapDefinition(reference.mapId, out Map map, out error))
            return false;
        BattleInfo source = Map.GetBattleInfo(map, reference.npcId, reference.battleId);
        if (source == null)
        {
            error = "指定的 XML 战斗配置不存在。";
            return false;
        }
        battleInfo = new BattleInfo(source)
        {
            winHandler = null,
            loseHandler = null,
        };
        return true;
    }

    private static void AddMap(Dictionary<string, StoryBattleOption> options, Map map)
    {
        if (map?.entities?.npcs == null)
            return;
        foreach (NpcInfo npc in map.entities.npcs.Where(value => value?.battleHandler != null))
        {
            foreach (BattleInfo battle in npc.battleHandler.Where(value => value != null && !string.IsNullOrWhiteSpace(value.id)))
            {
                string key = map.id + ":" + npc.id + ":" + battle.id;
                options[key] = new StoryBattleOption
                {
                    reference = new StoryBattleReferenceDocument { mapId = map.id, npcId = npc.id, battleId = battle.id },
                    mapName = string.IsNullOrWhiteSpace(map.name) ? "地图 " + map.id : map.name,
                    npcName = string.IsNullOrWhiteSpace(npc.name) ? "NPC " + npc.id : npc.name,
                    battleName = string.IsNullOrWhiteSpace(battle.content) ? "战斗 " + battle.id : battle.content,
                    isMod = Map.IsMod(map.id),
                };
            }
        }
    }
}
