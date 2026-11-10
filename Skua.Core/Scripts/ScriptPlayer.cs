using System.IO;
using Skua.Core.Flash;
using Skua.Core.Interfaces;
using Skua.Core.Models;
using Skua.Core.Models.Items;
using Skua.Core.Models.Monsters;
using Skua.Core.Models.Players;
using Skua.Core.Models.Skills;

namespace Skua.Core.Scripts;

public partial class ScriptPlayer : IScriptPlayer
{
    private readonly Lazy<IFlashUtil> _lazyFlash;
    private readonly Lazy<IScriptOption> _lazyOptions;
    private readonly Lazy<IScriptWait> _lazyWait;
    private readonly Lazy<IScriptInventory> _lazyInventory;
    private IFlashUtil Flash => _lazyFlash.Value;
    private IScriptOption Options => _lazyOptions.Value;
    private IScriptInventory Inventory => _lazyInventory.Value;
    private IScriptWait Wait => _lazyWait.Value;

    public ScriptPlayer(
        Lazy<IFlashUtil> flash,
        Lazy<IScriptOption> options,
        Lazy<IScriptWait> wait,
        Lazy<IScriptInventory> inventory)
    {
        _lazyFlash = flash;
        _lazyOptions = options;
        _lazyWait = wait;
        _lazyInventory = inventory;
    }

    [ObjectBinding("world.myAvatar.uid")]
    private int _ID;

    [ObjectBinding("world.myAvatar.objData.intExp")]
    private int _XP;

    [ObjectBinding("world.myAvatar.objData.intExpToLevel")]
    private int _requiredXP;

    [ObjectBinding("world.strFrame", Default = "string.Empty")]
    private string _cell;

    [ObjectBinding("world.strPad", Default = "string.Empty")]
    private string _pad;

    [ObjectBinding("serverIP", IsStatic = true, Default = "string.Empty")]
    private string _serverIP;

    public bool Playing => LoggedIn && Alive;

    public bool LoggedIn
    {
        get
        {
            try
            {
                if (Flash.Call<bool>("isLoggedIn"))
                    return true;
            }
            catch { }

            try
            {
                if (!Flash.IsNull("world.myAvatar"))
                    return true;
            }
            catch { }

            try
            {
                if (Flash.IsWorldLoaded)
                {
                    int hp = Flash.GetGameObject<int>("world.myAvatar.dataLeaf.intHP");
                    if (hp > 0)
                        return true;

                    hp = Flash.GetGameObject<int>("world.myAvatar.objData.intHP");
                    if (hp > 0)
                        return true;

                    string? u = Flash.GetGameObject<string>("world.myAvatar.objData.strUsername");
                    if (!string.IsNullOrWhiteSpace(u) && u != "null")
                        return true;
                }
            }
            catch { }

            try
            {
                string? sfcUser = Flash.GetGameObject<string>("sfc.myUserName");
                if (!string.IsNullOrWhiteSpace(sfcUser) && sfcUser != "null")
                    return true;
            }
            catch { }

            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "VibeSkuaTabs");
                string userFile = Path.Combine(dir, $"{Environment.ProcessId}.txt");
                if (File.Exists(userFile))
                {
                    string u = File.ReadAllText(userFile).Trim();
                    if (!string.IsNullOrWhiteSpace(u) && !u.StartsWith("User ") && !u.StartsWith("Skua "))
                        return true;
                }

                string locFile = Path.Combine(dir, $"{Environment.ProcessId}.location.json");
                if (File.Exists(locFile))
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(locFile));
                    if (doc.RootElement.TryGetProperty("LoggedIn", out var li) && li.GetBoolean())
                        return true;
                }
            }
            catch { }

            return false;
        }
    }

    public string Username
    {
        get
        {
            try
            {
                string? u = Flash.GetGameObjectStatic<string>("loginInfo.strUsername");
                if (!string.IsNullOrWhiteSpace(u) && u != "loginInfo.strUsername" && u != "null")
                    return u.Trim('\"');
            }
            catch { }

            try
            {
                string? u = Flash.GetGameObject<string>("world.myAvatar.objData.strUsername");
                if (!string.IsNullOrWhiteSpace(u) && u != "null")
                    return u.Trim('\"');
            }
            catch { }

            try
            {
                string? u = Flash.GetGameObject<string>("world.strUsername");
                if (!string.IsNullOrWhiteSpace(u) && u != "null")
                    return u.Trim('\"');
            }
            catch { }

            try
            {
                string? u = Flash.GetGameObject<string>("sfc.myUserName");
                if (!string.IsNullOrWhiteSpace(u) && u != "null")
                    return u.Trim('\"');
            }
            catch { }

            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "VibeSkuaTabs");
                string userFile = Path.Combine(dir, $"{Environment.ProcessId}.txt");
                if (File.Exists(userFile))
                {
                    string u = File.ReadAllText(userFile).Trim();
                    if (!string.IsNullOrWhiteSpace(u) && !u.StartsWith("User ") && !u.StartsWith("Skua "))
                        return u;
                }

                string locFile = Path.Combine(dir, $"{Environment.ProcessId}.location.json");
                if (File.Exists(locFile))
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(locFile));
                    if (doc.RootElement.TryGetProperty("Username", out var unProp) && !string.IsNullOrWhiteSpace(unProp.GetString()))
                    {
                        string un = unProp.GetString()!.Trim();
                        if (!un.StartsWith("User ") && !un.StartsWith("Skua ") && un != "Unknown")
                            return un;
                    }
                }
            }
            catch { }

            return string.Empty;
        }
    }

    [ObjectBinding("loginInfo.strPassword", IsStatic = true)]
    private string _password;

    [ObjectBinding("world.myAvatar.objData.guild.Name")]
    private string _guild;

    public bool Kicked
    {
        get
        {
            if (Flash.IsWorldLoaded || Flash.IsNull("mcLogin.warning"))
                return false;

            return Flash.Call<bool>("isKicked");
        }
    }

    [ObjectBinding("world.myAvatar.dataLeaf.intState", RequireNotNull = "world.myAvatar")]
    private int _state;

    public bool InCombat => State == 2;
    public bool IsMember => Flash.GetGameObject<int>("world.myAvatar.objData.iUpgDays") >= 0;
    public bool Alive => State > 0;

    public int Health
    {
        get
        {
            try
            {
                int val = Flash.GetGameObject<int>("world.myAvatar.dataLeaf.intHP");
                if (val > 0) return val;
                val = Flash.GetGameObject<int>("world.myAvatar.objData.intHP");
                if (val > 0) return val;
            }
            catch { }
            return 0;
        }
    }

    public int MaxHealth
    {
        get
        {
            try
            {
                int val = Flash.GetGameObject<int>("world.myAvatar.dataLeaf.intHPMax");
                if (val > 0) return val;
                val = Flash.GetGameObject<int>("world.myAvatar.objData.intHPMax");
                if (val > 0) return val;
            }
            catch { }
            return 0;
        }
    }

    public int Mana
    {
        get
        {
            try
            {
                int val = Flash.GetGameObject<int>("world.myAvatar.dataLeaf.intMP");
                if (val > 0) return val;
                val = Flash.GetGameObject<int>("world.myAvatar.objData.intMP");
                if (val > 0) return val;
            }
            catch { }
            return 0;
        }
    }

    public int MaxMana
    {
        get
        {
            try
            {
                int val = Flash.GetGameObject<int>("world.myAvatar.dataLeaf.intMPMax");
                if (val > 0) return val;
                val = Flash.GetGameObject<int>("world.myAvatar.objData.intMPMax");
                if (val > 0) return val;
            }
            catch { }
            return 100;
        }
    }

    public int Level
    {
        get
        {
            try
            {
                int val = Flash.GetGameObject<int>("world.myAvatar.dataLeaf.intLevel");
                if (val > 0) return val;
                val = Flash.GetGameObject<int>("world.myAvatar.objData.intLevel");
                if (val > 0) return val;
            }
            catch { }
            return 0;
        }
    }

    [ObjectBinding("world.myAvatar.objData.intGold")]
    private int _gold;

    [ObjectBinding("world.myAvatar.objData.intCoins")]
    private int _coins;

    [ObjectBinding("world.myAvatar.objData.iRank")]
    private int _currentClassRank;

    public bool HasTarget
    {
        get
        {
            Monster? m = Target!;
            return m?.Alive ?? false;
        }
    }

    public bool Loaded => Flash.GetGameObject<int>("world.myAvatar.items.length") > 0
                        && !Flash.GetGameObject<bool>("world.mapLoadInProgress")
                        && Flash.CallGameFunction<bool>("world.myAvatar.pMC.artLoaded");

    [ObjectBinding("world.myAvatar.objData.intAccessLevel", HasSetter = true)]
    private int _accessLevel;

    [ObjectBinding("world.actions.active", Default = "Array.Empty<Skua.Core.Models.Skills.SkillInfo>()")]
    private SkillInfo[] _skills;

    [ObjectBinding("world.myAvatar.dataLeaf.afk")]
    private bool _AFK;

    [ObjectBinding("world.myAvatar.pMC.x")]
    private int _X;

    [ObjectBinding("world.myAvatar.pMC.y")]
    private int _Y;

    [ObjectBinding("world.WALKSPEED", HasSetter = true, Default = "8")]
    private int _walkSpeed;

    [ObjectBinding("world.SCALE", HasSetter = true)]
    private int _scale;

    [JsonCallBinding("getTargetMonster", Default = "new()")]
    private Monster? _target;

    [ObjectBinding("world.myAvatar.dataLeaf.sta")]
    private PlayerStats? _stats;

    public InventoryItem? CurrentClass => Playing ? Inventory.Items?.Find(i => i is { Equipped: true, Category: ItemCategory.Class }) : null;

    public void Rest(bool full = false)
    {
        Wait.ForActionCooldown(GameActions.Rest);
        Flash.CallGameFunction("world.rest");
        if (full)
            Wait.ForTrue(() => Health >= MaxHealth && Mana >= MaxMana, 20);
    }

    [MethodCallBinding("walkTo", RunMethodPost = true)]
    private void _walkTo(int x, int y, int speed = 8)
    {
        Wait.ForPlayerPosition(x, y);
    }

    [MethodCallBinding("world.setSpawnPoint", GameFunction = true)]
    private void _setSpawnPoint(string cell, string pad) { }

    [MethodCallBinding("world.goto", GameFunction = true)]
    private void _goto(string name) { }
}