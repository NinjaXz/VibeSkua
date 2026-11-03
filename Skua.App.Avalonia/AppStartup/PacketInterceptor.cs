using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Skua.Core.Interfaces;
using Skua.App.Avalonia.ViewModels.Packets;

namespace Skua.App.Avalonia.AppStartup;

internal static class PacketInterceptor
{
    internal static PacketInterceptorViewModel CreateViewModel(IServiceProvider s)
    {
        return new PacketInterceptorViewModel(CreateFilters(), s.GetRequiredService<ICaptureProxy>(), s.GetRequiredService<IScriptServers>(), s.GetRequiredService<IFileDialogService>());
    }

    // Each filter is given a packet's commands (PacketCommands.Parse), both directions of a request are listed
    internal static List<PacketLogFilterViewModel> CreateFilters()
    {
        List<PacketLogFilterViewModel> filters = new()
        {
            Category("Combat", "ct", "gar", "sar", "sars", "anim", "mtls", "cb", "restRequest", "aggroMon",
                "resPlayerTimed", "resTimed", "respawnMon", "playerDeath"),
            Category("User Data", "retrieveUserData", "retrieveUserDatas", "initUserData", "initUserDatas"),
            Category("Join", "moveToArea", "tfer", "house", "joinOK", "firstJoin", "exitArea"),
            Category("Jump", "moveToCell"),
            Category("Movement", "mv", "mtcid", "mvna", "mvnb"),
            Category("Get Map", "getMapItem"),
            Category("Quest", "getQuest", "getQuests", "getQuests2", "acceptQuest", "tryQuestComplete", "updateQuest",
                "ccqr", "turnIn", "showQuestLink"),
            Category("Shop", "loadShop", "reloadShop", "loadHairShop", "loadEnhShop", "buyItem", "sellItem",
                "enhanceItem", "enhanceItemShop", "enhanceItemLocal"),
            Category("Equip", "equipItem", "forceEquipItem", "unequipItem", "wearItem", "unwearItem"),
            Category("Drop", "getDrop", "dropItem", "getMonsterDrops", "monsterDrops"),
            Category("Chat", "message", "cc", "chatm", "whisper", "em", "emotea"),
            Category("Auras", "aura+", "aura-", "aura++", "aura--", "aura*", "aura+p", "aura-p", "clearAuras", "auSnap",
                "showAuraResult"),
            Category("Skills", "sAct", "gar", "sar", "sars", "geia", "seia"),
            Category("Stats", "uotls", "stu"),
            Category("Inventory", "loadInventoryBig", "retrieveInventory", "initInventory", "addItems", "forceAddItem",
                "removeItem", "removeTempItem", "loadBank", "bankFromInv", "bankToInv", "bankSwapInv"),
            Category("Class", "updateClass", "changeClass"),
            Category("Misc", "crafting", "setHomeTown", "afk", "summonPet")
        };

        List<PacketLogFilterViewModel> specificFilters = new(filters);
        filters.Add(new("Other", p =>
        {
            foreach (PacketLogFilterViewModel f in specificFilters)
            {
                if (f.Filter.Invoke(p))
                    return false;
            }
            return true;
        }));

        return filters;
    }

    private static PacketLogFilterViewModel Category(string name, params string[] commands)
    {
        HashSet<string> known = new(commands);
        return new(name, p => p.Any(known.Contains));
    }
}
