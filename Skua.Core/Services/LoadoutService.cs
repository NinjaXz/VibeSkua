using Newtonsoft.Json;
using Skua.Core.Interfaces;
using Skua.Core.Models;
using Skua.Core.Models.Items;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Skua.Core.Services;

public class LoadoutService : ILoadoutService
{
    private readonly IScriptInterface _bot;
    private readonly ILogService _logger;
    private readonly IScriptManager _manager;
    private readonly IDispatcherService _dispatcherService;
    private List<LoadoutProfile> _loadouts = new();
    private string _currentUsername = string.Empty;

    public event System.Action? LoadoutsChanged;
    public bool IsLoggedIn => _bot.Player.LoggedIn;

    public List<LoadoutProfile> Loadouts
    {
        get
        {
            EnsureLoaded();
            return _loadouts;
        }
    }

    private string LoadoutsFile
    {
        get
        {
            if (!_bot.Player.LoggedIn || string.IsNullOrWhiteSpace(_bot.Player.Username))
                return Path.Combine(ClientFileSources.SkuaDIR, "Loadouts", "Guest.json");
            
            string dir = Path.Combine(ClientFileSources.SkuaDIR, "Loadouts");
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            
            return Path.Combine(dir, $"{_bot.Player.Username}.json");
        }
    }

    public LoadoutService(IScriptInterface bot, ILogService logger, IScriptManager manager, IDispatcherService dispatcherService)
    {
        _bot = bot;
        _logger = logger;
        _manager = manager;
        _dispatcherService = dispatcherService;
        
        _bot.Events.MapChanged += Events_MapChanged;
        _bot.Events.Logout += Events_PlayerLoggedOut;
        _bot.Events.ExtensionPacketReceived += Events_ExtensionPacketReceived;
        
        LoadFromFile();
    }

    private void Events_ExtensionPacketReceived(dynamic packet)
    {
        if (string.IsNullOrWhiteSpace(_currentUsername))
        {
            _dispatcherService.Invoke(() =>
            {
                if (_bot.Player.LoggedIn)
                {
                    EnsureLoaded();
                }
            });
        }
    }

    private void Events_MapChanged(string map)
    {
        _dispatcherService.Invoke(() =>
        {
            EnsureLoaded();
        });
    }

    private void EnsureLoaded()
    {
        if (!_bot.Player.LoggedIn || string.IsNullOrWhiteSpace(_bot.Player.Username))
        {
            if (_loadouts.Count > 0 || !string.IsNullOrEmpty(_currentUsername))
            {
                _currentUsername = string.Empty;
                _loadouts.Clear();
                LoadoutsChanged?.Invoke();
            }
            return;
        }
            
        if (_currentUsername != _bot.Player.Username)
        {
            _currentUsername = _bot.Player.Username;
            LoadFromFile();
        }
    }

    private void Events_PlayerLoggedOut()
    {
        _dispatcherService.Invoke(() =>
        {
            _currentUsername = string.Empty;
            _loadouts.Clear();
            LoadoutsChanged?.Invoke();
        });
    }

    public void Refresh()
    {
        if (!_bot.Player.LoggedIn || string.IsNullOrWhiteSpace(_bot.Player.Username))
        {
            _dispatcherService.Invoke(() =>
            {
                _currentUsername = string.Empty;
                _loadouts.Clear();
                LoadoutsChanged?.Invoke();
            });
            return;
        }

        _currentUsername = _bot.Player.Username;
        LoadFromFile();
    }

    private void LoadFromFile()
    {
        _loadouts.Clear();
        
        if (File.Exists(LoadoutsFile))
        {
            try
            {
                var content = File.ReadAllText(LoadoutsFile);
                _loadouts = Newtonsoft.Json.JsonConvert.DeserializeObject<List<LoadoutProfile>>(content) ?? new List<LoadoutProfile>();
            }
            catch (System.Exception ex)
            {
                _logger.ScriptLog($"[Loadouts] Failed to load loadouts from file: {ex.Message}");
            }
        }
        
        LoadoutsChanged?.Invoke();
    }

    private void SaveToFile()
    {
        if (!_bot.Player.LoggedIn) return;
        try
        {
            File.WriteAllText(LoadoutsFile, Newtonsoft.Json.JsonConvert.SerializeObject(_loadouts, Newtonsoft.Json.Formatting.Indented));
        }
        catch (System.Exception ex)
        {
            _logger.ScriptLog($"[Loadouts] Failed to save loadouts to file: {ex.Message}");
        }
    }

    public bool SaveLoadout(LoadoutProfile loadout)
    {
        EnsureLoaded();
        if (!_bot.Player.LoggedIn) return false;

        var existing = _loadouts.FirstOrDefault(l => l.Name == loadout.Name);
        if (existing != null)
        {
            _loadouts.Remove(existing);
        }
        _loadouts.Add(loadout);
        SaveToFile();
        LoadoutsChanged?.Invoke();
        return true;
    }

    public void DeleteLoadout(LoadoutProfile loadout)
    {
        EnsureLoaded();
        _loadouts.Remove(loadout);
        SaveToFile();
        LoadoutsChanged?.Invoke();
    }

    public LoadoutProfile CreateFromCurrentEquipped(string name)
    {
        EnsureLoaded();
        var profile = new LoadoutProfile { Name = name };
        var equipped = _bot.Inventory.Items.Where(i => i.Equipped);
        foreach (var item in equipped)
        {
            string cleanName = item.Name.Replace("&amp;", "&");
            if (item.Category == ItemCategory.Class)
            {
                profile.Class = cleanName;
                profile.ClassEnhancement = GetEnhancementName(item);
                profile.ClassPatternID = item.EnhancementPatternID;
                profile.ClassProcID = item.ProcID;
            }
            else if (item.Category == ItemCategory.Armor)
            {
                profile.Armor = cleanName;
            }
            else if (item.Category == ItemCategory.Sword || item.Category == ItemCategory.Axe || item.Category == ItemCategory.Dagger || item.Category == ItemCategory.Gun || item.Category == ItemCategory.Bow || item.Category == ItemCategory.Mace || item.Category == ItemCategory.Polearm || item.Category == ItemCategory.Staff || item.Category == ItemCategory.Wand || item.Category == ItemCategory.HandGun || item.Category == ItemCategory.Rifle || item.Category == ItemCategory.Gauntlet || item.Category == ItemCategory.Whip)
            {
                profile.Weapon = cleanName;
                profile.WeaponEnhancement = GetEnhancementName(item);
                profile.WeaponPatternID = item.EnhancementPatternID;
                profile.WeaponProcID = item.ProcID;
            }
            else if (item.Category == ItemCategory.Helm)
            {
                profile.Helm = cleanName;
                profile.HelmEnhancement = GetEnhancementName(item);
                profile.HelmPatternID = item.EnhancementPatternID;
                profile.HelmProcID = item.ProcID;
            }
            else if (item.Category == ItemCategory.Cape)
            {
                profile.Cape = cleanName;
                profile.CapeEnhancement = GetEnhancementName(item);
                profile.CapePatternID = item.EnhancementPatternID;
                profile.CapeProcID = item.ProcID;
            }
            else if (item.Category == ItemCategory.Pet)
            {
                profile.Pet = cleanName;
            }
            else if (item.Category == ItemCategory.Amulet || item.Category == ItemCategory.Necklace)
            {
                profile.Amulet = cleanName;
            }
        }
        return profile;
    }

    public async Task<List<string>> EquipLoadoutAsync(LoadoutProfile loadout)
    {
        EnsureLoaded();
        _logger.ScriptLog($"[Loadouts] Equipping Loadout: {loadout.Name}");
        
        return await Task.Run(async () => 
        {
            var missingItems = new List<string>();
            try
            {
                var loadoutItems = new List<string> { loadout.Class, loadout.Armor, loadout.Weapon, loadout.Helm, loadout.Cape, loadout.Pet, loadout.Amulet }
                                    .Where(x => !string.IsNullOrWhiteSpace(x))
                                    .Select(x => x.Replace("&amp;", "&").ToLowerInvariant())
                                    .ToList();

                if (!string.IsNullOrWhiteSpace(loadout.Class) && !EquipItemSafe(loadout.Class, loadoutItems)) missingItems.Add(loadout.Class);
                if (!string.IsNullOrWhiteSpace(loadout.Armor) && !EquipItemSafe(loadout.Armor, loadoutItems)) missingItems.Add(loadout.Armor);
                if (!string.IsNullOrWhiteSpace(loadout.Weapon) && !EquipItemSafe(loadout.Weapon, loadoutItems)) missingItems.Add(loadout.Weapon);
                if (!string.IsNullOrWhiteSpace(loadout.Helm) && !EquipItemSafe(loadout.Helm, loadoutItems)) missingItems.Add(loadout.Helm);
                if (!string.IsNullOrWhiteSpace(loadout.Cape) && !EquipItemSafe(loadout.Cape, loadoutItems)) missingItems.Add(loadout.Cape);
                if (!string.IsNullOrWhiteSpace(loadout.Pet) && !EquipItemSafe(loadout.Pet, loadoutItems)) missingItems.Add(loadout.Pet);
                if (!string.IsNullOrWhiteSpace(loadout.Amulet) && !EquipItemSafe(loadout.Amulet, loadoutItems)) missingItems.Add(loadout.Amulet);
                
                _logger.ScriptLog($"[Loadouts] Successfully processed items for {loadout.Name}. Checking enhancements...");

                if (_manager.ScriptRunning)
                {
                    _logger.ScriptLog("[Loadouts] Cannot apply dynamic enhancements because a script is currently running. Please stop the script first.");
                    return missingItems;
                }

                int cPattern = loadout.ClassPatternID > 0 ? loadout.ClassPatternID : ParsePatternID(loadout.ClassEnhancement);
                int cProc = loadout.ClassPatternID > 0 ? loadout.ClassProcID : ParseProcID(loadout.ClassEnhancement);
                int wPattern = loadout.WeaponPatternID > 0 ? loadout.WeaponPatternID : ParsePatternID(loadout.WeaponEnhancement);
                int wProc = loadout.WeaponPatternID > 0 ? loadout.WeaponProcID : ParseProcID(loadout.WeaponEnhancement);
                int hPattern = loadout.HelmPatternID > 0 ? loadout.HelmPatternID : ParsePatternID(loadout.HelmEnhancement);
                int hProc = loadout.HelmPatternID > 0 ? loadout.HelmProcID : ParseProcID(loadout.HelmEnhancement);
                int capePattern = loadout.CapePatternID > 0 ? loadout.CapePatternID : ParsePatternID(loadout.CapeEnhancement);
                int capeProc = loadout.CapePatternID > 0 ? loadout.CapeProcID : ParseProcID(loadout.CapeEnhancement);

                bool needsEnhancement = false;
                var scriptBody = new System.Text.StringBuilder();

                var classItem = _bot.Inventory.Items.FirstOrDefault(i => i.Equipped && i.Name.Replace("&amp;", "&").Equals(loadout.Class, StringComparison.OrdinalIgnoreCase));
                if (classItem != null && (classItem.EnhancementPatternID != cPattern || classItem.ProcID != cProc))
                {
                    scriptBody.AppendLine($@"        adv.EnhanceItem(""{loadout.Class}"", (EnhancementType){cPattern}, CapeSpecial.None, HelmSpecial.None, WeaponSpecial.None);");
                    needsEnhancement = true;
                }

                var weaponItem = _bot.Inventory.Items.FirstOrDefault(i => i.Equipped && i.Name.Replace("&amp;", "&").Equals(loadout.Weapon, StringComparison.OrdinalIgnoreCase));
                if (weaponItem != null && (weaponItem.EnhancementPatternID != wPattern || weaponItem.ProcID != wProc))
                {
                    int wBaseEnh = wPattern > 9 ? 9 : wPattern;
                    scriptBody.AppendLine($@"        adv.EnhanceItem(""{loadout.Weapon}"", (EnhancementType){wBaseEnh}, CapeSpecial.None, HelmSpecial.None, {MapWeaponSpecial(wPattern, wProc)});");
                    needsEnhancement = true;
                }

                var helmItem = _bot.Inventory.Items.FirstOrDefault(i => i.Equipped && i.Name.Replace("&amp;", "&").Equals(loadout.Helm, StringComparison.OrdinalIgnoreCase));
                if (helmItem != null && (helmItem.EnhancementPatternID != hPattern || helmItem.ProcID != hProc))
                {
                    int hBaseEnh = hPattern > 9 ? 9 : hPattern;
                    scriptBody.AppendLine($@"        adv.EnhanceItem(""{loadout.Helm}"", (EnhancementType){hBaseEnh}, CapeSpecial.None, {MapHelmSpecial(hPattern)}, WeaponSpecial.None);");
                    needsEnhancement = true;
                }

                var capeItem = _bot.Inventory.Items.FirstOrDefault(i => i.Equipped && i.Name.Replace("&amp;", "&").Equals(loadout.Cape, StringComparison.OrdinalIgnoreCase));
                if (capeItem != null && (capeItem.EnhancementPatternID != capePattern || capeItem.ProcID != capeProc))
                {
                    int capeBaseEnh = capePattern > 9 ? 9 : capePattern;
                    scriptBody.AppendLine($@"        adv.EnhanceItem(""{loadout.Cape}"", (EnhancementType){capeBaseEnh}, {MapCapeSpecial(capePattern)}, HelmSpecial.None, WeaponSpecial.None);");
                    needsEnhancement = true;
                }

                if (!needsEnhancement)
                {
                    _logger.ScriptLog($"[Loadouts] All items are already properly enhanced for {loadout.Name}.");
                    return missingItems;
                }

                string currentMapName = _bot.Map.Name?.ToLowerInvariant() ?? "home";
                string currentMapFullName = _bot.Map.FullName ?? "home";
                
                string returnJoin;
                if (currentMapName == "house")
                {
                    returnJoin = @"bot.Send.Packet($""%xt%zm%house%1%{bot.Player.Username}%"");
        bot.Wait.ForMapLoad(""house"");";
                }
                else if (currentMapName == "home")
                {
                    returnJoin = "core.Join(\"home\");";
                }
                else
                {
                    returnJoin = $"core.Join(\"{currentMapFullName}\");";
                }

                string scriptPath = Path.Combine(ClientFileSources.SkuaScriptsDIR, "LoadoutEnhancerTemp.cs");
                string scriptContent = $@"
//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreFarms.cs
//cs_include Scripts/CoreAdvanced.cs
using Skua.Core.Interfaces;

public class LoadoutEnhancerTemp
{{
    public void ScriptMain(IScriptInterface bot)
    {{
        CoreBots core = CoreBots.Instance;
        CoreAdvanced adv = new CoreAdvanced();

{scriptBody.ToString()}
        {returnJoin}
    }}
}}";
                File.WriteAllText(scriptPath, scriptContent);
                _manager.SetLoadedScript(scriptPath);
                await _manager.StartScript();
                
                // Wait for the script to completely finish running before letting the task resolve
                while (_manager.ScriptRunning)
                {
                    await Task.Delay(500);
                }
                
                _logger.ScriptLog($"[Loadouts] Enhancements successfully applied for {loadout.Name}!");
                return missingItems;
            }
            catch (System.Exception ex)
            {
                _logger.ScriptLog($"[Loadouts] Failed to equip loadout {loadout.Name}: {ex.Message}");
                return missingItems;
            }
        });
    }

    private bool EquipItemSafe(string itemName, List<string> loadoutItems)
    {
        if (string.IsNullOrWhiteSpace(itemName)) return true;

        var decodedName = itemName.Replace("&amp;", "&");
        var lowerName = decodedName.ToLowerInvariant();
        
        try
        {
            var equippedItem = _bot.Inventory.Items.FirstOrDefault(i => i.Equipped && i.Name.Replace("&amp;", "&").Equals(lowerName, StringComparison.OrdinalIgnoreCase));
            if (equippedItem != null) return true;

            var invItem = _bot.Inventory.Items.FirstOrDefault(i => i.Name.Replace("&amp;", "&").Equals(lowerName, StringComparison.OrdinalIgnoreCase));
            if (invItem != null)
            {
                _bot.Wait.ForActionCooldown(Skua.Core.Models.GameActions.EquipItem);
                _bot.Sleep(500); // Server-sync breathing room
                _bot.Inventory.EquipItem(invItem.Name);
                if (!_bot.Wait.ForItemEquip(invItem.Name, 10))
                {
                    _logger.ScriptLog($"[Loadouts] Timeout: Server refused to equip {itemName}.");
                    return false;
                }
                else
                {
                    _bot.Sleep(500); // Let server settle before the next item
                    return true;
                }
            }

            var bankItem = _bot.Bank.Items.FirstOrDefault(i => i.Name.Replace("&amp;", "&").Equals(lowerName, StringComparison.OrdinalIgnoreCase));
            if (bankItem != null)
            {
                if (_bot.Inventory.FreeSlots == 0)
                {
                    var toBank = _bot.Inventory.Items.FirstOrDefault(i => !i.Equipped && !loadoutItems.Contains(i.Name.Replace("&amp;", "&").ToLowerInvariant()));
                    if (toBank != null)
                    {
                        _bot.Wait.ForActionCooldown(Skua.Core.Models.GameActions.Transfer);
                        _bot.Inventory.ToBank(toBank.Name);
                        _bot.Sleep(1000); 
                    }
                    else
                    {
                        _logger.ScriptLog($"[Loadouts] Inventory full, cannot safely unbank for {itemName}.");
                        return false;
                    }
                }
                
                _bot.Wait.ForActionCooldown(Skua.Core.Models.GameActions.Transfer);
                _bot.Bank.ToInventory(bankItem.Name);
                _bot.Sleep(1500); 
                
                var newlyInvItem = _bot.Inventory.Items.FirstOrDefault(i => i.Name.Replace("&amp;", "&").Equals(lowerName, StringComparison.OrdinalIgnoreCase));
                if (newlyInvItem != null)
                {
                    _bot.Wait.ForActionCooldown(Skua.Core.Models.GameActions.EquipItem);
                    _bot.Sleep(500);
                    _bot.Inventory.EquipItem(newlyInvItem.Name);
                    if (!_bot.Wait.ForItemEquip(newlyInvItem.Name, 10))
                    {
                        _logger.ScriptLog($"[Loadouts] Timeout: Server refused to equip {itemName} after banking.");
                        return false;
                    }
                    else
                    {
                        _bot.Sleep(500);
                        return true;
                    }
                }
                else
                {
                    _logger.ScriptLog($"[Loadouts] Failed to transfer {itemName} from Bank to Inventory.");
                    return false;
                }
            }

            _logger.ScriptLog($"[Loadouts] Warning: {itemName} not found in Inventory or Bank.");
            return false;
        }
        catch (System.Exception ex)
        {
            _logger.ScriptLog($"[Loadouts] Error equipping {itemName}: {ex.Message}");
            return false;
        }
    }

    private string MapWeaponSpecial(int patternId, int procId)
    {
        if (patternId == 10 && procId == 0) return "WeaponSpecial.Forge";
        return procId switch
        {
            2 => "WeaponSpecial.Spiral_Carve",
            3 => "WeaponSpecial.Awe_Blast",
            4 => "WeaponSpecial.Health_Vamp",
            5 => "WeaponSpecial.Mana_Vamp",
            6 => "WeaponSpecial.Powerword_Die",
            7 => "WeaponSpecial.Lacerate",
            8 => "WeaponSpecial.Smite",
            9 => "WeaponSpecial.Valiance",
            10 => "WeaponSpecial.Arcanas_Concerto",
            11 => "WeaponSpecial.Acheron",
            12 => "WeaponSpecial.Elysium",
            13 => "WeaponSpecial.Praxis",
            14 => "WeaponSpecial.Dauntless",
            15 => "WeaponSpecial.Ravenous",
            _ => "WeaponSpecial.None"
        };
    }

    private string MapHelmSpecial(int patternId)
    {
        return patternId switch
        {
            10 => "HelmSpecial.Forge",
            25 => "HelmSpecial.Vim",
            26 => "HelmSpecial.Examen",
            27 => "HelmSpecial.Pneuma",
            28 => "HelmSpecial.Anima",
            32 => "HelmSpecial.Hearty",
            _ => "HelmSpecial.None"
        };
    }

    private string MapCapeSpecial(int patternId)
    {
        return patternId switch
        {
            10 => "CapeSpecial.Forge",
            11 => "CapeSpecial.Absolution",
            12 => "CapeSpecial.Avarice",
            24 => "CapeSpecial.Vainglory",
            29 => "CapeSpecial.Penitence",
            30 => "CapeSpecial.Lament",
            _ => "CapeSpecial.None"
        };
    }

    private int ParsePatternID(string enhancementName)
    {
        if (string.IsNullOrWhiteSpace(enhancementName)) return 9; // Fallback to Lucky
        return enhancementName switch
        {
            "Adventurer" => 1,
            "Fighter" => 2,
            "Thief" => 3,
            "Armsman" => 4,
            "Hybrid" => 5,
            "Wizard" => 6,
            "Healer" => 7,
            "Spellbreaker" => 8,
            "Lucky" => 9,
            "Forge" => 10,
            "Absolution" => 11,
            "Avarice" => 12,
            "Depths" => 23,
            "Vainglory" => 24,
            "Vim" => 25,
            "Examen" => 26,
            "Pneuma" => 27,
            "Anima" => 28,
            "Penitence" => 29,
            "Lament" => 30,
            "Hearty" => 32,
            _ => 9 
        };
    }

    private int ParseProcID(string enhancementName)
    {
        if (string.IsNullOrWhiteSpace(enhancementName)) return 0;
        return enhancementName switch
        {
            "Spiral Carve" => 2,
            "Awe Blast" => 3,
            "Health Vamp" => 4,
            "Mana Vamp" => 5,
            "Powerword DIE" => 6,
            "Lacerate" => 7,
            "Smite" => 8,
            "Valiance" => 9,
            "Arcana's Concerto" => 10,
            "Acheron" => 11,
            "Elysium" => 12,
            "Praxis" => 13,
            "Dauntless" => 14,
            "Ravenous" => 15,
            _ => 0
        };
    }

    private string GetEnhancementName(InventoryItem item)
    {
        if (item.ProcID > 0)
        {
            return item.ProcID switch
            {
                2 => "Spiral Carve",
                3 => "Awe Blast",
                4 => "Health Vamp",
                5 => "Mana Vamp",
                6 => "Powerword DIE",
                7 => "Lacerate",
                8 => "Smite",
                9 => "Valiance",
                10 => "Arcana's Concerto",
                11 => "Acheron",
                12 => "Elysium",
                13 => "Praxis",
                14 => "Dauntless",
                15 => "Ravenous",
                _ => "Unknown"
            };
        }

        return item.EnhancementPatternID switch
        {
            1 => "Adventurer",
            2 => "Fighter",
            3 => "Thief",
            4 => "Armsman",
            5 => "Hybrid",
            6 => "Wizard",
            7 => "Healer",
            8 => "Spellbreaker",
            9 => "Lucky",
            10 => "Forge",
            11 => "Absolution",
            12 => "Avarice",
            23 => "Depths",
            24 => "Vainglory",
            25 => "Vim",
            26 => "Examen",
            27 => "Pneuma",
            28 => "Anima",
            29 => "Penitence",
            30 => "Lament",
            32 => "Hearty",
            _ => "Unknown"
        };
    }
}
