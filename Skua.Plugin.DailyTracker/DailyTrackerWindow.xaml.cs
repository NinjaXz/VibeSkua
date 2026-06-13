using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Skua.Core.Interfaces;

namespace Skua.Plugin.DailyTracker;

public partial class DailyTrackerWindow : Window
{
    private readonly IScriptInterface _bot;

    private readonly int[] _trackedQuests = new[] 
    { 
        2091, 2098, 802, 803, 3075, 3076, 1239, 10047, 
        7156, 7165, 3759, 3827, 3965, 3596, 
        8152, 8153, 8154, 8245, 8300, 8397, 8692, 8733, 9173, 10301 
    };

    public DailyTrackerWindow(IScriptInterface bot)
    {
        InitializeComponent();
        _bot = bot;
        
        Loaded += async (s, e) => 
        {
            // ONLY execute the batch load and popup closer when the plugin window is first displayed!
            await Task.Run(() => 
            {
                try { _bot.Quests.Load(_trackedQuests); } catch { }
            });

            // Wait 3000ms for the UI to actually render, then forcefully close the popup
            Task.Delay(3000).ContinueWith(_ => 
            {
                try 
                { 
                    // The game might ignore toggleQuestLog without a MouseEvent, or the popup might be named differently.
                    // Shotgun approach to forcefully hide/close the Quest UI!
                    try { _bot.Flash.SetGameObject("ui.mcPopup.visible", false); } catch { }
                    try { _bot.Flash.SetGameObject("ui.ModalStack.visible", false); } catch { }
                    try { _bot.Flash.CallGameFunction("toggleQuestLog", ""); } catch { } // Pass empty string to bypass ArgumentError
                    try { _bot.Flash.CallGameFunction("world.cancelQuest", 0); } catch { }
                    try { _bot.Flash.CallGameFunction("world.toggleQuestLog"); } catch { }
                    try { _bot.Flash.CallGameFunction("ui.mcPopup.onClose"); } catch { }
                } 
                catch { }
            });

            await LoadQuestsAsync();
        };
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        // Refresh purely checks the loaded statuses invisibly
        _ = LoadQuestsAsync();
    }

    private async Task LoadQuestsAsync()
    {
        var categories = await Task.Run(() =>
        {
            var cats = new List<QuestCategory>
            {
                new QuestCategory
                {
                    Name = "Resources & Miscellaneous",
                    Quests = new List<QuestItem>
                    {
                        new(2091, "Mine Crafting", _bot),
                        new(2098, "Hard Core Metals", _bot),
                        new(802, "Elders' Blood", _bot),
                        new(803, "Sparrow's Blood", _bot),
                        new(3075, "Doom Member Free Spin", _bot),
                        new(3076, "Doom Free Weekly Spin", _bot),
                        new(1239, "Free Member Magic Keys", _bot),
                        new(10047, "A Grain of Dirt", _bot)
                    }
                },
                new QuestCategory
                {
                    Name = "Classes & Factions",
                    Quests = new List<QuestItem>
                    {
                        new(7156, "Lord of Order (1)", _bot),
                        new(7165, "Lord of Order (10)", _bot),
                        new(3759, "BeastMaster Challenge", _bot),
                        new(3827, "Shadow Shield (Daily)", _bot),
                        new(3965, "Glacera Ice Token (Cryomancer)", _bot),
                        new(3596, "Embrace Your Chaos", _bot)
                    }
                },
                new QuestCategory
                {
                    Name = "Ultra Bosses (Weekly)",
                    Quests = new List<QuestItem>
                    {
                        new(8152, "Ultra Ezrajal", _bot),
                        new(8153, "Ultra Warden", _bot),
                        new(8154, "Ultra Engineer", _bot),
                        new(8245, "Ultra Tyndarius", _bot),
                        new(8300, "Champion Drakath", _bot),
                        new(8397, "Ultra Drago", _bot),
                        new(8692, "Ultra Nulgath", _bot),
                        new(8733, "Ultra Darkon", _bot),
                        new(9173, "Ultra Speaker", _bot),
                        new(10301, "Ultra Gramiel", _bot)
                    }
                }
            };
            return cats;
        });

        CategoriesControl.ItemsSource = categories;
    }
}

public class QuestCategory
{
    public string Name { get; set; } = string.Empty;
    public List<QuestItem> Quests { get; set; } = new();
}

public class QuestItem
{
    public int ID { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsDone { get; set; }

    public QuestItem(int id, string name, IScriptInterface bot)
    {
        ID = id;
        Name = name;
        try
        {
            // Now that all quests are batch-loaded into memory, IsDailyComplete will accurately 
            // read from the cached data without popping up any new windows!
            IsDone = bot.Quests.IsDailyComplete(id);
        }
        catch
        {
            IsDone = false;
        }
    }

    public string StatusColor => IsDone ? "#4CAF50" : "#F44336"; 
    public string StatusText => IsDone ? "Completed" : "Incomplete";
}
