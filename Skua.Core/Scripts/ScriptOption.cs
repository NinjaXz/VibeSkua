using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Flash;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using Skua.Core.Models;
using System.Collections.Immutable;
using System.Collections.Specialized;
using System.Linq.Expressions;
using System.Reflection;

namespace Skua.Core.Scripts;

public partial class ScriptOption : ObservableRecipient, IScriptOption, IOptionDictionary
{
    private ScriptOption(Lazy<IFlashUtil> flash)
    {
        _lazyFlash = flash;
    }

    public ScriptOption(
        Lazy<IFlashUtil> flash,
        ISettingsService settingsService)
    {
        _lazyFlash = flash;
        _settingsService = settingsService;
        GetOptions();
        OptionDictionary = GenerateDictionary().ToImmutableDictionary();
        StrongReferenceMessenger.Default.Register<ScriptOption, ScriptStoppedMessage, int>(this, (int)MessageChannels.ScriptStatus, ScriptStopped);
        StrongReferenceMessenger.Default.Register<ScriptOption, MapChangedMessage, int>(this, (int)MessageChannels.GameEvents, MapChanged);
    }

    private void ScriptStopped(ScriptOption recipient, ScriptStoppedMessage message)
    {
        recipient.AutoRelogin = false;
        recipient.LagKiller = false;
        recipient.LagKiller = true;
        recipient.LagKiller = false;
        recipient.AggroAllMonsters = false;
        recipient.AggroMonsters = false;
        recipient.SkipCutscenes = false;
    }

    private void MapChanged(ScriptOption recipient, MapChangedMessage message)
    {
        if (recipient.StreamerMode)
        {
            Task.Run(async () => 
            {
                await Task.Delay(1000); // Give the Flash map UI time to load the string
                var rawMapName = recipient._lazyFlash.Value.GetGameObject("world.strMapName") ?? "";
                if (!string.IsNullOrEmpty(rawMapName))
                {
                    var cleanMapName = rawMapName.Length > 1 
                        ? char.ToUpper(rawMapName[0]) + rawMapName.Substring(1).ToLower() 
                        : rawMapName.ToUpper();
                    
                    recipient._lazyFlash.Value.Call("setGameObject", "ui.mcInterface.areaList.title.t1.text", cleanMapName);
                }
            });
        }
    }

    private readonly Lazy<IFlashUtil> _lazyFlash;
    private readonly ISettingsService _settingsService;
    private Dictionary<string, string>? _userOptions;

    private IFlashUtil Flash => _lazyFlash.Value;

    public ImmutableDictionary<string, Func<object>> OptionDictionary { get; }

    [ObservableProperty]
    private bool _attackWithoutTarget;

    private bool _acceptACDrops;

    public bool AcceptACDrops
    {
        get => _acceptACDrops;
        set => SetProperty(ref _acceptACDrops, value, true);
    }

    private bool _acceptAllDrops;

    public bool AcceptAllDrops
    {
        get => _acceptAllDrops;
        set
        {
            if (SetProperty(ref _acceptAllDrops, value, true) && value)
                RejectAllDrops = false;
        }
    }

    private bool _rejectAllDrops;

    public bool RejectAllDrops
    {
        get => _rejectAllDrops;
        set
        {
            if (SetProperty(ref _rejectAllDrops, value, true) && value)
                AcceptAllDrops = false;
        }
    }

    [ObservableProperty]
    private bool _restPackets;

    [ObservableProperty]
    private bool _safeTimings = true;

    [CallBinding("skipCutscenes", UseValue = false, Get = false, HasSetter = true)]
    private bool _skipCutscenes;

    [ObservableProperty]
    private bool _privateRooms;

    [CallBinding("magnetize", UseValue = false, Get = false, HasSetter = true)]
    private bool _magnetise;

    [CallBinding("killLag", Get = false, HasSetter = true)]
    private bool _lagKiller;

    [ObservableProperty]
    private bool _headlessMode;

    [ObjectBinding("stage.frameRate", Get = false, HasSetter = true)]
    private int _setFPS = 30;

    [ObjectBinding("ui.mcFPS.visible", HasSetter = true)]
    private bool _showFPS = false;

    [ObservableProperty]
    private bool _aggroMonsters;

    [ObservableProperty]
    private bool _aggroAllMonsters;

    [CallBinding("infiniteRange", UseValue = false, Get = false, HasSetter = true)]
    private bool _infiniteRange;

    [ModuleBinding("DisableFX")]
    private bool _disableFX;

    [ObservableProperty]
    private bool _autoRelogin;

    [ObservableProperty]
    private bool _autoReloginAny;

    [ObservableProperty]
    private bool _retryRelogin = true;

    [ObservableProperty]
    private bool _safeRelogin;

    [ModuleBinding("DisableCollisions")]
    private bool _disableCollisions;

    [CallBinding("disableDeathAd", Get = false, HasSetter = true)]
    private bool _disableDeathAds;

    [ModuleBinding("HidePlayers")]
    private bool _hidePlayers;

    private string? _reloginServer = "Twilly";

    public string? ReloginServer
    {
        get => _reloginServer;
        set => SetProperty(ref _reloginServer, value, true);
    }

    [ObjectBinding("world.myAvatar.objData.strUsername", "world.rootClass.ui.mcPortrait.strName.text", "world.myAvatar.pMC.pname.ti.text", Get = false, HasSetter = true, Default = "string.Empty")]
    private string _customName = string.Empty;

    [ObjectBinding("world.myAvatar.pMC.pname.ti.textColor", Get = false, HasSetter = true, Default = "0xFFFFFF")]
    private int _nameColor = 0xFFFFFF;

    [ObjectBinding("world.myAvatar.pMC.pname.tg.text", Get = false, HasSetter = true, Default = "string.Empty")]
    private string _customGuild = string.Empty;

    [ObjectBinding("world.myAvatar.pMC.pname.tg.textColor", Get = false, HasSetter = true)]
    public int _guildColor;

    private bool _streamerMode;
    public bool StreamerMode
    {
        get => _streamerMode;
        set
        {
            if (SetProperty(ref _streamerMode, value, true))
            {
                ApplyStreamerMode(value);
            }
        }
    }
    
    private string _originalName = string.Empty;
    private string _originalGuild = string.Empty;
    private bool _originalHidePlayers = false;
    private System.Threading.CancellationTokenSource? _streamerModeCts;

    private void ApplyStreamerMode(bool enabled)
    {
        if (enabled)
        {
            if (CustomName != "Hidden" && CustomName != "VibeSkuaUser")
                _originalName = CustomName;
            if (CustomGuild != "Hidden")
                _originalGuild = CustomGuild;
                
            _originalHidePlayers = HidePlayers;
            HidePlayers = true;
            CustomName = "VibeSkuaUser";
            CustomGuild = "Hidden";
            
            _streamerModeCts = new System.Threading.CancellationTokenSource();
            System.Threading.Tasks.Task.Run(async () => 
            {
                while (_streamerModeCts != null && !_streamerModeCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var flash = _lazyFlash.Value;
                        if (flash == null) return;
                        
                        // Force avatar floating name and portrait UI
                        flash.Call("setGameObject", "world.myAvatar.objData.strUsername", "VibeSkuaUser");
                        flash.Call("setGameObject", "world.myAvatar.pMC.pname.ti.text", "VibeSkuaUser");
                        flash.Call("setGameObject", "world.myAvatar.pMC.pname.tg.text", "Hidden");
                        
                        flash.Call("setGameObject", "world.rootClass.ui.mcPortrait.strName.text", "VibeSkuaUser");
                        flash.Call("setGameObject", "world.rootClass.ui.mcPortraitTarget.strName.text", "Hidden");

                        // Hide Gold and Coins visually by setting the client-side cache to 0
                        flash.Call("setGameObject", "world.myAvatar.objData.intGold", 0);
                        flash.Call("setGameObject", "world.myAvatar.objData.intCoins", 0);
                        
                        // Hide main HUD gold and coins (middle bottom UI)
                        flash.Call("setGameObject", "world.rootClass.ui.mcInterface.teGold.text", "0");
                        flash.Call("setGameObject", "world.rootClass.ui.mcInterface.teCoins.text", "0");
                        flash.Call("setGameObject", "world.rootClass.ui.mcInterface.strGold.text", "0");
                        flash.Call("setGameObject", "world.rootClass.ui.mcInterface.strCoins.text", "0");
                        flash.Call("setGameObject", "world.rootClass.ui.mcInterface.txtGold.text", "0");
                        flash.Call("setGameObject", "world.rootClass.ui.mcInterface.txtCoins.text", "0");
                        
                        // Hide chat
                        flash.Call("setGameObject", "world.rootClass.ui.mcInterface.t1.visible", false);
                        flash.Call("setGameObject", "world.rootClass.ui.mcInterface.te.visible", false);
                        
                        // Force map UI text to strictly be the base map name
                        var rawMapName = flash.GetGameObject("world.strMapName") ?? "";
                        if (!string.IsNullOrEmpty(rawMapName))
                        {
                            // Capitalize first letter for visual polish
                            var cleanMapName = rawMapName.Length > 1 
                                ? char.ToUpper(rawMapName[0]) + rawMapName.Substring(1).ToLower() 
                                : rawMapName.ToUpper();
                            
                            flash.Call("setGameObject", "ui.mcInterface.areaList.title.t1.text", cleanMapName);
                        }
                    }
                    catch { }
                    await System.Threading.Tasks.Task.Delay(500, _streamerModeCts.Token);
                }
            });
        }
        else
        {
            _streamerModeCts?.Cancel();
            _streamerModeCts?.Dispose();
            _streamerModeCts = null;
            
            CustomName = _originalName;
            CustomGuild = _originalGuild;
            HidePlayers = _originalHidePlayers;
            
            try
            {
                var flash = _lazyFlash.Value;
                if (flash != null)
                {
                    flash.Call("setGameObject", "world.rootClass.ui.mcInterface.t1.visible", true);
                    flash.Call("setGameObject", "world.rootClass.ui.mcInterface.te.visible", true);
                }
            }
            catch { }
        }
    }

    [ObjectBinding("world.WALKSPEED", Get = false, HasSetter = true, Default = "8")]
    private int _walkSpeed = 8;

    [ObservableProperty]
    private int _loadTimeout = 30000;

    [ObservableProperty]
    private int _huntDelay = 1000;

    [ObservableProperty]
    private int _huntBuffer = 1;

    [ObservableProperty]
    private int _maximumTries = 10;

    [ObservableProperty]
    private int _actionDelay = 800;

    [ObservableProperty]
    private int _privateNumber = 0;

    [ObservableProperty]
    private int _joinMapTries = 3;

    [ObservableProperty]
    private int _questAcceptAndCompleteTries = 30;

    [ObservableProperty]
    private int _reloginTries = 5;

    [ObservableProperty]
    private int _reloginTryDelay = 800;

    [ObservableProperty]
    private int _loginTimeout = 30000;

    [ObservableProperty]
    private HuntPriorities _HuntPriority = HuntPriorities.None;

    private bool _useFunctionBasedSkills = false;

    public bool UseFunctionBasedSkills
    {
        get => _useFunctionBasedSkills;
        set
        {
            if (SetProperty(ref _useFunctionBasedSkills, value, true))
            {
                CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetService<Skua.Core.Interfaces.IAdvancedSkillContainer>()?.LoadSkills();
            }
        }
    }

    public void Save()
    {
        StringCollection saveOptions = new();
        foreach (PropertyInfo pi in GetType().GetProperties())
        {
            if (pi.Name == nameof(OptionDictionary))
                continue;
            string key = pi.Name;
            object? value = pi.GetValue(this);
            saveOptions.Add($"{key}={value}");
        }
        _settingsService.Set("UserOptions", saveOptions);
    }

    public void Reset()
    {
        if (_userOptions is null)
        {
            ResetToDefault();
            return;
        }

        foreach (PropertyInfo pi in GetType().GetProperties())
        {
            if (pi.Name == nameof(OptionDictionary))
                continue;
            if (_userOptions.ContainsKey(pi.Name))
            {
                if (pi.PropertyType.BaseType == typeof(Enum))
                {
                    pi.SetValue(this, Enum.Parse(pi.PropertyType, _userOptions[pi.Name], true));
                    continue;
                }
                pi.SetValue(this, Convert.ChangeType(_userOptions[pi.Name], pi.PropertyType));
            }
        }
    }

    public void ResetToDefault()
    {
        ScriptOption defaults = new(_lazyFlash);
        foreach (PropertyInfo pi in GetType().GetProperties())
        {
            if (pi.Name == nameof(OptionDictionary))
                continue;

            pi.SetValue(this, pi.GetValue(defaults), null);
        }
    }

    private void GetOptions()
    {
        StringCollection? userOptions = _settingsService.Get<StringCollection>("UserOptions");
        if (userOptions is null)
            return;
        _userOptions = new();
        foreach (string? option in userOptions)
        {
            if (string.IsNullOrEmpty(option))
                continue;
            string[] optionKeyValue = option.Split('=', StringSplitOptions.TrimEntries);
            _userOptions.Add(optionKeyValue[0], optionKeyValue[1]);
        }
        Reset();
    }

    private Dictionary<string, Func<object>> GenerateDictionary()
    {
        Dictionary<string, Func<object>> dict = new();
        foreach (PropertyInfo pi in GetType().GetProperties())
        {
            if (pi.Name == nameof(OptionDictionary))
                continue;
            MethodCallExpression methodCall = Expression.Call(Expression.Constant(this), pi.GetGetMethod()!, null);
            UnaryExpression convertedExpression = Expression.Convert(methodCall, typeof(object));
            Func<object> function = Expression.Lambda<Func<object>>(convertedExpression).Compile();
            dict.Add(pi.Name, function);
        }
        return dict;
    }
}