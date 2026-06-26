using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Skua.Core.Interfaces;
using Skua.Core.Models;

namespace Skua.Plugin.DailyTracker;

public partial class DailyTrackerWindow : Window
{
    private readonly IScriptInterface _bot;
    private CancellationTokenSource? _cts;
    private Timer? _refreshTimer;
    private static readonly TimeSpan _refreshInterval = TimeSpan.FromMinutes(5);
    private static readonly SemaphoreSlim _joinLock = new(1, 1);

    private static readonly int[] _trackedQuests =
    [
        2091, 2098, 802, 803, 3075, 3076, 1239, 10047,
        3759, 3827, 3965, 3596,
        7156, 7157, 7158, 7159, 7160, 7161, 7162, 7163, 7164, 7165,
        8152, 8153, 8154, 8245, 8300, 8397, 8547, 8692, 8746, 9173, 10301
    ];

    private static readonly string[] _questNames =
    [
        "Mine Crafting", "Hard Core Metals", "Elders' Blood", "Sparrow's Blood",
        "Doom Member Free Spin", "Doom Free Weekly Spin", "Free Member Magic Keys", "A Grain of Dirt",
        "BeastMaster Challenge", "Shadow Shield (Daily)",
        "Glacera Ice Token (Cryomancer)", "Embrace Your Chaos",
        "Heart of Servitude", "Spirit of Justice", "Purification of Chaos", "Steadfast Will",
        "Strike of Order", "Harmony", "Ordinance", "Axiom", "Blessing of Order", "The Final Challenge",
        "Ultra Ezrajal", "Ultra Warden", "Ultra Engineer", "Ultra Tyndarius",
        "Champion Drakath", "Ultra Drago", "Ultra Dage", "Ultra Nulgath",
        "Ultra Darkon", "Ultra Speaker", "Ultra Gramiel"
    ];

    private static readonly Dictionary<int, string> _questScriptNames = new()
    {
        { 2091, "Dailies\\MineCrafting.cs" },
        { 2098, "Dailies\\HardCoreMetals[Mem].cs" },
        { 802,  "Dailies\\EldersBlood.cs" },
        { 803,  "Dailies\\SparrowsBlood.cs" },
        { 3075, "Dailies\\WheelofDoom.cs" },
        { 3076, "Dailies\\WheelofDoom.cs" },
        { 1239, "Dailies\\FreeDailyBoost[Mem].cs" },
        { 10047, "Dailies\\PearlOfNulgath.cs" },
        { 3759, "Dailies\\BeastMasterChallenge[Mem].cs" },
        { 3827, "Dailies\\ShadowShroud.cs" },
        { 3965, "Dailies\\Cryomancer.cs" },
        { 7156, "Dailies\\LordOfOrder.cs" },
        { 7157, "Dailies\\LordOfOrder.cs" },
        { 7158, "Dailies\\LordOfOrder.cs" },
        { 7159, "Dailies\\LordOfOrder.cs" },
        { 7160, "Dailies\\LordOfOrder.cs" },
        { 7161, "Dailies\\LordOfOrder.cs" },
        { 7162, "Dailies\\LordOfOrder.cs" },
        { 7163, "Dailies\\LordOfOrder.cs" },
        { 7164, "Dailies\\LordOfOrder.cs" },
        { 7165, "Dailies\\LordOfOrder.cs" },
        { 3596, "Dailies\\DagesScrollFragment.cs" },
        { 8152, "Ultras\\UltraEzrajal.cs" },
        { 8153, "Ultras\\UltraWarden.cs" },
        { 8154, "Ultras\\UltraEngineer.cs" },
        { 8245, "Ultras\\UltraAvatarTyndarius.cs" },
        { 8300, "Ultras\\ChampionDrakath.cs" },
        { 8397, "Ultras\\UltraDrago.cs" },
        { 8547, "Ultras\\UltraDage.cs" },
        { 8692, "Ultras\\UltraNulgath.cs" },
        { 8746, "Ultras\\UltraDarkon.cs" },
        { 9173, "Ultras\\UltraSpeaker.cs" },
        { 10301, "Ultras\\UltraGramiel.cs" }
    };

    public DailyTrackerWindow(IScriptInterface bot)
    {
        InitializeComponent();
        _bot = bot;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await InitializeAsync();

    private async Task InitializeAsync()
    {
        _cts = new CancellationTokenSource();
        try
        {
            await LoadQuestsAsync();
            StartLiveUpdates();
        }
        catch (TaskCanceledException) { }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"InitializeAsync failed: {ex}"); }
    }

    private List<QuestCategory> _categories = [];

    private async Task LoadQuestsAsync()
    {
        bool[] doneStates = await Task.Run(() =>
        {
            bool[] results = new bool[_trackedQuests.Length];
            for (int i = 0; i < _trackedQuests.Length; i++)
            {
                try { results[i] = _bot.Quests.IsDailyComplete(_trackedQuests[i]); }
                catch { results[i] = false; }
            }
            return results;
        });

        List<QuestItem> questItems = [];
        for (int i = 0; i < _trackedQuests.Length; i++)
        {
            QuestItem item = new(_trackedQuests[i], _questNames[i], doneStates[i]);
            if (_questScriptNames.TryGetValue(_trackedQuests[i], out string? script))
                item.ScriptName = script;
            questItems.Add(item);
        }

        _categories =
        [
            new() { Name = "Resources & Miscellaneous", Quests = [.. questItems.Take(8)] },
            new() { Name = "Classes & Factions", Quests = [.. questItems.Skip(8).Take(4)] },
            new() { Name = "Lord of Order", Quests = [.. questItems.Skip(12).Take(10)] },
            new() { Name = "Ultra Bosses (Weekly)", Quests = [.. questItems.Skip(22)] }
        ];

        CategoriesControl.ItemsSource = _categories;
    }

    private void StartLiveUpdates()
    {
        _refreshTimer = new Timer(async _ =>
        {
            try
            {
                if (_cts?.IsCancellationRequested != true)
                    await RefreshQuestStatesAsync();
            }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"Live update failed: {ex}"); }
        }, null, _refreshInterval, _refreshInterval);
    }

    private Task RefreshQuestStatesAsync()
    {
        return Task.Run(() =>
        {
            foreach (QuestCategory category in _categories)
            {
                foreach (QuestItem quest in category.Quests)
                {
                    bool newState;
                    try { newState = _bot.Quests.IsDailyComplete(quest.ID); }
                    catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"Quest refresh failed {quest.ID}: {ex}"); continue; }
                    if (quest.IsDone != newState)
                        Application.Current.Dispatcher.Invoke(() => quest.IsDone = newState);
                }
            }
        });
    }

    private void StartScript_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn) return;
        if (btn.Tag is not string scriptName || string.IsNullOrWhiteSpace(scriptName)) return;
        StartScript(scriptName);
    }

    private void StartCategory_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn) return;
        string? categoryName = btn.Tag as string;
        if (categoryName == "Lord of Order")
            StartScript("Dailies\\LordOfOrder.cs");
    }

    private void StartScript(string scriptName)
    {
        string path = System.IO.Path.Combine(ClientFileSources.SkuaScriptsDIR, scriptName);
        if (!System.IO.File.Exists(path))
        {
            System.Diagnostics.Trace.WriteLine($"Script not found: {path}");
            return;
        }
        Task.Run(async () =>
        {
            try
            {
                _bot.Manager.SetLoadedScript(path);
                await _bot.Manager.StartScript();
            }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"Failed to start script {scriptName}: {ex}"); }
        });
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = LoadQuestsAsync();

    private void OnClosed(object sender, EventArgs e)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _refreshTimer?.Dispose();
        _refreshTimer = null;
    }
}

public class QuestCategory : System.ComponentModel.INotifyPropertyChanged
{
    private string _name = string.Empty;

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasCategoryScript)); }
    }

    public List<QuestItem> Quests { get; set; } = [];
    public bool HasCategoryScript => Name == "Lord of Order";

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}

public class QuestItem : System.ComponentModel.INotifyPropertyChanged
{
    public int ID { get; set; }
    public string Name { get; set; } = string.Empty;

    private bool _isDone;

    public bool IsDone
    {
        get => _isDone;
        set { _isDone = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowScriptVisibility)); }
    }

    private string _scriptName = string.Empty;

    public string ScriptName
    { get => _scriptName; set { _scriptName = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowScriptVisibility)); } }

    public bool HasScript => !string.IsNullOrEmpty(ScriptName);

    public Visibility ShowScriptVisibility => HasScript && !IsDone && (ID < 7156 || ID > 7165) ? Visibility.Visible : Visibility.Collapsed;
    public bool IsLordOfOrder => ID >= 7156 && ID <= 7165;
    public string StatusColor => IsDone ? "#4CAF50" : "#F44336";
    public string StatusText => IsDone ? "Completed" : "Incomplete";

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

    public QuestItem(int id, string name, bool isDone)
    {
        ID = id;
        Name = name;
        IsDone = isDone;
    }
}