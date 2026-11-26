using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Material.Icons;
using Skua.Core.Interfaces;
using Skua.Core.Models.Items;
using Skua.Plugin.Cosmetics.Models;
using Skua.Plugin.Cosmetics.Services;

namespace Skua.Plugin.Cosmetics.Views;

public partial class CosmeticsWindow : Window
{
    private readonly IScriptInterface _bot;
    private readonly CosmeticsService _service;

    private List<CosmeticItem> _filteredItems = new();
    private CosmeticItem? _selectedItem;
    private CosmeticSet? _inspectedSet;

    private int _currentPage = 1;
    private int _pageSize = 50;
    private int _totalPages = 1;
    private string _activeCategory = "All";
    private string _activeWeaponSubcategory = "All Weapons";
    private CancellationTokenSource? _searchDebounceCts;

    public CosmeticsWindow()
    {
        InitializeComponent();
        _bot = null!;
        _service = null!;
    }

    public CosmeticsWindow(IScriptInterface bot, CosmeticsService service)
    {
        _bot = bot;
        _service = service;

        InitializeComponent();

        _service.CatalogUpdated += OnServiceCatalogUpdated;
        _service.FavoritesUpdated += OnServiceFavoritesUpdated;
        _service.SetsUpdated += OnServiceSetsUpdated;

        Loaded += CosmeticsWindow_Loaded;
        Closing += CosmeticsWindow_Closing;
    }

    private async void CosmeticsWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        Loaded -= CosmeticsWindow_Loaded;
        if (_service == null)
            return;

        try
        {
            _service.CaptureOriginalStateIfEmpty();
            RefreshSetsList();
            RefreshCcTabFromAvatar();

            if (_service.IsCatalogLoaded)
            {
                CatalogStatusText.Text = $"{_service.AllItems.Count:N0} items (Local Machine)";
                FilterCatalog();
                return;
            }

            CatalogStatusText.Text = "Streaming local catalog...";
            IProgress<string> progress = new Progress<string>(message =>
                Dispatcher.UIThread.Post(() => CatalogStatusText.Text = message));

            await _service.EnsureCatalogLoadedAsync(progress);

            if (!IsVisible)
                return;

            CatalogStatusText.Text = $"{_service.AllItems.Count:N0} items (Local Machine)";
            FilterCatalog();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Cosmetics window initialization failed: {ex}");
            _bot?.Log($"[Cosmetics] Window initialization failed: {ex}");

            if (IsVisible)
                CatalogStatusText.Text = $"Initialization failed: {ex.Message}";
        }
    }
    private void CosmeticsWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (_service != null)
        {
            _service.CatalogUpdated -= OnServiceCatalogUpdated;
            _service.FavoritesUpdated -= OnServiceFavoritesUpdated;
            _service.SetsUpdated -= OnServiceSetsUpdated;
        }
    }

    #region Global Quick Action Bar

    private async void BtnCopyTarget_Click(object? sender, RoutedEventArgs e)
    {
        BtnCopyTarget.IsEnabled = false;
        QuickActionBarStatus.Text = "Reading target avatar...";

        var (success, message) = await _service.CopyTargetLookAsync();

        Dispatcher.UIThread.Post(() =>
        {
            BtnCopyTarget.IsEnabled = true;
            QuickActionBarStatus.Text = message;
            if (success)
                RefreshCcTabFromAvatar();
        });
    }

    private void RoomPlayersCombo_DropDownOpened(object? sender, EventArgs e)
    {
        var players = _service.GetRoomPlayerNames();
        RoomPlayersCombo.ItemsSource = players;
        if (players.Count > 0 && RoomPlayersCombo.SelectedItem == null)
            RoomPlayersCombo.SelectedIndex = 0;
    }

    private async void BtnCopyRoomPlayer_Click(object? sender, RoutedEventArgs e)
    {
        string? name = RoomPlayersCombo.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(name))
        {
            QuickActionBarStatus.Text = "Please select a room player first.";
            return;
        }

        QuickActionBarStatus.Text = $"Copying look from {name}...";
        var (success, message) = await _service.CopyPlayerLookByNameAsync(name);
        QuickActionBarStatus.Text = message;
        if (success)
            RefreshCcTabFromAvatar();
    }

    private async void BtnResetRealGear_Click(object? sender, RoutedEventArgs e)
    {
        QuickActionBarStatus.Text = "Reverting to real inventory gear & colors...";
        await _service.RevertToRealGearAsync();
        RefreshCcTabFromAvatar();
        QuickActionBarStatus.Text = "Reverted to actual equipped gear & colors!";
    }

    private void RefreshCcTabFromAvatar()
    {
        var (b, t, a, eye, skin, hair) = _service.GetCurrentColors();
        if (b != 0 || t != 0 || a != 0 || eye != 0 || skin != 0 || hair != 0)
        {
            CcBaseHex.Text = $"#{b:X6}";
            CcTrimHex.Text = $"#{t:X6}";
            CcAccHex.Text = $"#{a:X6}";
            CcEyeHex.Text = $"#{eye:X6}";
            CcSkinHex.Text = $"#{skin:X6}";
            CcHairHex.Text = $"#{hair:X6}";
        }
    }


    #endregion

    #region Catalog & Zero-Allocation Filtering & Sorting

    private void OnServiceCatalogUpdated()
    {
        Dispatcher.UIThread.Post(() =>
        {
            CatalogStatusText.Text = $"{_service.AllItems.Count:N0} items (Local Machine)";
            FilterCatalog();
        });
    }

    private void OnServiceFavoritesUpdated()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_activeCategory == "Favorites")
                FilterCatalog();
            else if (_selectedItem != null)
                UpdateDetailView(_selectedItem);
        });
    }

    private void FilterCatalog()
    {
        if (_service == null || !_service.IsCatalogLoaded) return;

        string query = SearchBox?.Text?.Trim() ?? "";
        bool hasQuery = !string.IsNullOrEmpty(query);
        bool isNumber = int.TryParse(query, out int searchId);

        // Instant pre-indexed category retrieval (0ms)
        IEnumerable<CosmeticItem> source = _activeCategory switch
        {
            "Favorites" => _service.FavoriteItems,
            "Armor" => _service.ArmorItems,
            "Weapon" => FilterWeapons(),
            "Helm" => _service.HelmItems,
            "Cape" => _service.CapeItems,
            "Pet" => _service.PetItems,
            "Rune" => _service.RuneItems,
            _ => _service.AllItems
        };

        // Zero-allocation search filtering
        if (hasQuery)
        {
            if (isNumber)
            {
                source = source.Where(x => x.ID == searchId || x.ID.ToString().Contains(query, StringComparison.Ordinal));
            }
            else
            {
                source = source.Where(x => x.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
            }
        }

        string sortTag = (SortCombo?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Newest";

        // Performance Optimization: If "Newest" is selected and no search query was entered,
        // the pre-indexed collections are already in exact ID-descending order!
        if (sortTag == "Newest" && !hasQuery && _activeCategory != "Favorites")
        {
            _filteredItems = source as List<CosmeticItem> ?? source.ToList();
        }
        else
        {
            _filteredItems = sortTag switch
            {
                "Oldest" => source.OrderBy(x => x.ID).ToList(),
                "NameAsc" => source.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList(),
                "NameDesc" => source.OrderByDescending(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList(),
                _ => source.OrderByDescending(x => x.ID).ToList() // "Newest"
            };
        }

        _currentPage = 1;
        UpdatePagination();
    }

    private IEnumerable<CosmeticItem> FilterWeapons()
    {
        var weapons = _service.WeaponItems;
        if (_activeWeaponSubcategory == "All Weapons")
            return weapons;

        if (Enum.TryParse<ItemCategory>(_activeWeaponSubcategory, true, out var targetCat))
        {
            return weapons.Where(x => x.Category == targetCat);
        }

        return weapons;
    }

    private void UpdatePagination()
    {
        int totalItems = _filteredItems.Count;
        _totalPages = Math.Max(1, (int)Math.Ceiling((double)totalItems / _pageSize));
        if (_currentPage > _totalPages)
            _currentPage = _totalPages;

        var pageItems = _filteredItems
            .Skip((_currentPage - 1) * _pageSize)
            .Take(_pageSize)
            .ToList();

        ItemsListBox.ItemsSource = pageItems;
        PaginationInfoText.Text = $"Page {_currentPage} of {_totalPages} ({totalItems:N0} items)";
    }

    private void CategoryChip_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;

        ChipAll.Classes.Set("active", tag == "All");
        ChipArmor.Classes.Set("active", tag == "Armor");
        ChipWeapon.Classes.Set("active", tag == "Weapon");
        ChipHelm.Classes.Set("active", tag == "Helm");
        ChipCape.Classes.Set("active", tag == "Cape");
        ChipPet.Classes.Set("active", tag == "Pet");
        ChipRune.Classes.Set("active", tag == "Rune");
        ChipFav.Classes.Set("active", tag == "Favorites");

        _activeCategory = tag;
        WeaponSubcategoryCombo.IsVisible = tag == "Weapon";
        FilterCatalog();
    }

    private void WeaponSubcategoryCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo || combo.SelectedItem is not ComboBoxItem item)
            return;

        _activeWeaponSubcategory = item.Content?.ToString() ?? "All Weapons";

        // Avalonia can raise this event while InitializeComponent is still populating controls.
        // The Loaded handler performs the initial catalog filter once the window is ready.
        if (IsVisible)
            FilterCatalog();
    }

    private void SortCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        FilterCatalog();
    }

    private void SearchBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        _searchDebounceCts?.Cancel();
        _searchDebounceCts = new CancellationTokenSource();
        var token = _searchDebounceCts.Token;

        Task.Delay(180, token).ContinueWith(t =>
        {
            if (!t.IsCanceled)
            {
                Dispatcher.UIThread.Post(FilterCatalog);
            }
        }, TaskScheduler.Default);
    }

    private void SearchBox_KeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _searchDebounceCts?.Cancel();
            FilterCatalog();
        }
    }

    private void ClearSearch_Click(object? sender, RoutedEventArgs e)
    {
        SearchBox.Text = string.Empty;
        FilterCatalog();
    }

    private async void CheckCatalogUpdates_Click(object? sender, RoutedEventArgs e)
    {
        BtnSyncUpdates.IsEnabled = false;
        CatalogStatusText.Text = "Checking for updates...";

        var progress = new Progress<string>(msg =>
        {
            Dispatcher.UIThread.Post(() => CatalogStatusText.Text = msg);
        });

        var (success, message, newCount) = await _service.CheckAndUpdateCatalogAsync(progress);

        Dispatcher.UIThread.Post(() =>
        {
            BtnSyncUpdates.IsEnabled = true;
            CatalogStatusText.Text = message;
            if (newCount > 0)
            {
                FilterCatalog();
            }
        });
    }

    private void PageFirst_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentPage > 1)
        {
            _currentPage = 1;
            UpdatePagination();
        }
    }

    private void PagePrev_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentPage > 1)
        {
            _currentPage--;
            UpdatePagination();
        }
    }

    private void PageNext_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentPage < _totalPages)
        {
            _currentPage++;
            UpdatePagination();
        }
    }

    private void PageLast_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentPage < _totalPages)
        {
            _currentPage = _totalPages;
            UpdatePagination();
        }
    }

    private void PageSizeCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (PageSizeCombo?.SelectedItem is ComboBoxItem item && int.TryParse(item.Content?.ToString(), out int size))
        {
            _pageSize = size;
            _currentPage = 1;
            UpdatePagination();
        }
    }

    #endregion

    #region Item Selection & Equipping

    private void ItemsListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ItemsListBox.SelectedItem is CosmeticItem item)
        {
            _selectedItem = item;
            UpdateDetailView(item);
        }
    }

    private void ItemsListBox_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ItemsListBox.SelectedItem is CosmeticItem item)
        {
            _service.Equip(item);
        }
    }

    private void UpdateDetailView(CosmeticItem item)
    {
        DetailItemName.Text = item.Name;
        DetailCategoryBadge.IsVisible = true;
        DetailCategoryText.Text = item.DisplayCategory;
        DetailItemID.Text = $"ID: {item.ID}";
        DetailItemPath.Text = $"File: {item.Path}";
        DetailItemLink.Text = $"Link: {item.Link}";

        if (item.IsFavorite)
        {
            FavIcon.Kind = MaterialIconKind.Heart;
            FavIcon.Foreground = Brushes.Red;
            FavBtnText.Text = "Remove Favorite";
        }
        else
        {
            FavIcon.Kind = MaterialIconKind.HeartOutline;
            FavIcon.Foreground = Brushes.White;
            FavBtnText.Text = "Add to Favorites";
        }
    }

    private void BtnEquip_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedItem != null)
        {
            _service.Equip(_selectedItem);
        }
    }

    private void BtnFavorite_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedItem != null)
        {
            _service.ToggleFavorite(_selectedItem);
            UpdateDetailView(_selectedItem);
        }
    }

    private void BtnWiki_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedItem != null)
        {
            string url = $"http://aqwwiki.wikidot.com/{Uri.EscapeDataString(_selectedItem.Name)}";
            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _bot.Log($"[Cosmetics] Failed to open browser: {ex.Message}");
            }
        }
    }

    private void GenderMale_Click(object? sender, RoutedEventArgs e) => _service.SwapGender('M');
    private void GenderFemale_Click(object? sender, RoutedEventArgs e) => _service.SwapGender('F');
    private void GenderSwap_Click(object? sender, RoutedEventArgs e) => _service.SwapGender();

    #endregion

    #region Outfits / Saved Sets & Inspector

    private void OnServiceSetsUpdated()
    {
        Dispatcher.UIThread.Post(RefreshSetsList);
    }

    private void RefreshSetsList()
    {
        if (SetsListBox.ItemsSource != _service.SavedSets)
            SetsListBox.ItemsSource = _service.SavedSets;

        SetsCountText.Text = $"{_service.SavedSets.Count} Outfits Saved";
    }

    private void SetsListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (SetsListBox.SelectedItem is CosmeticSet set)
        {
            _inspectedSet = set;
            UpdateInspectorView(set);
        }
    }

    private void UpdateInspectorView(CosmeticSet set)
    {
        InspectorSetName.Text = set.Name;
        InspectorSetDate.Text = $"Saved: {set.CreatedAt:yyyy-MM-dd HH:mm}";

        InspectArmorName.Text = set.Armor?.Name ?? "None";
        BtnInspectEquipArmor.IsEnabled = set.Armor != null;

        InspectHelmName.Text = set.Helm?.Name ?? "None";
        BtnInspectEquipHelm.IsEnabled = set.Helm != null;

        InspectCapeName.Text = set.Cape?.Name ?? "None";
        BtnInspectEquipCape.IsEnabled = set.Cape != null;

        InspectWeaponName.Text = set.Weapon?.Name ?? "None";
        BtnInspectEquipWeapon.IsEnabled = set.Weapon != null;

        InspectPetName.Text = set.Pet?.Name ?? "None";
        BtnInspectEquipPet.IsEnabled = set.Pet != null;

        InspectRuneName.Text = set.Misc?.Name ?? "None";
        BtnInspectEquipRune.IsEnabled = set.Misc != null;

        BtnInspectEquipAll.IsEnabled = true;
        BtnInspectDelete.IsEnabled = true;
    }

    private void BtnInspectEquipSlot_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string slotTag || _inspectedSet == null) return;

        CosmeticItem? itemToEquip = slotTag switch
        {
            "Armor" => _inspectedSet.Armor,
            "Helm" => _inspectedSet.Helm,
            "Cape" => _inspectedSet.Cape,
            "Weapon" => _inspectedSet.Weapon,
            "Pet" => _inspectedSet.Pet,
            "Rune" => _inspectedSet.Misc,
            _ => null
        };

        if (itemToEquip != null)
        {
            _service.Equip(itemToEquip);
        }
    }

    private void CaptureLook_Click(object? sender, RoutedEventArgs e)
    {
        string defaultName = $"Outfit #{_service.SavedSets.Count + 1}";
        var set = _service.CaptureCurrentLook(defaultName);
        _service.AddSavedSet(set);
    }

    private async void EquipSelectedSet_Click(object? sender, RoutedEventArgs e)
    {
        if (_inspectedSet != null)
        {
            await _service.EquipSetAsync(_inspectedSet);
        }
        else if (SetsListBox.SelectedItem is CosmeticSet set)
        {
            await _service.EquipSetAsync(set);
        }
    }

    private void DeleteSelectedSet_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var setToDelete = _inspectedSet ?? (SetsListBox.SelectedItem as CosmeticSet);
            if (setToDelete == null) return;

            _service.RemoveSavedSet(setToDelete);
            _inspectedSet = null;
            SetsListBox.SelectedItem = null;

            InspectorSetName.Text = "Select an Outfit";
            InspectorSetDate.Text = "-";
            InspectArmorName.Text = "None";
            InspectHelmName.Text = "None";
            InspectCapeName.Text = "None";
            InspectWeaponName.Text = "None";
            InspectPetName.Text = "None";
            InspectRuneName.Text = "None";
            BtnInspectEquipArmor.IsEnabled = false;
            BtnInspectEquipHelm.IsEnabled = false;
            BtnInspectEquipCape.IsEnabled = false;
            BtnInspectEquipWeapon.IsEnabled = false;
            BtnInspectEquipPet.IsEnabled = false;
            BtnInspectEquipRune.IsEnabled = false;
            BtnInspectEquipAll.IsEnabled = false;
            BtnInspectDelete.IsEnabled = false;
        }
        catch (Exception ex)
        {
            _bot.Log($"[Cosmetics] Delete set error: {ex.Message}");
        }
    }

    private async void ApplySetItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is CosmeticSet set)
        {
            await _service.EquipSetAsync(set);
        }
    }

    private async void SetsListBox_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (SetsListBox.SelectedItem is CosmeticSet set)
        {
            await _service.EquipSetAsync(set);
        }
    }

    #endregion

    #region Color Custom (CC)

    private void CcHex_TextChanged(object? sender, TextChangedEventArgs e)
    {
        UpdateSwatchFromBox(CcBaseHex, CcBaseSwatch);
        UpdateSwatchFromBox(CcTrimHex, CcTrimSwatch);
        UpdateSwatchFromBox(CcAccHex, CcAccSwatch);
        UpdateSwatchFromBox(CcEyeHex, CcEyeSwatch);
        UpdateSwatchFromBox(CcSkinHex, CcSkinSwatch);
        UpdateSwatchFromBox(CcHairHex, CcHairSwatch);
    }

    private void UpdateSwatchFromBox(TextBox? box, Border? swatch)
    {
        if (box == null || swatch == null) return;
        string hex = box.Text?.Trim() ?? "";
        if (!hex.StartsWith('#')) hex = "#" + hex;

        if (Color.TryParse(hex, out var color))
        {
            swatch.Background = new SolidColorBrush(color);
        }
    }

    private void ApplyColors_Click(object? sender, RoutedEventArgs e)
    {
        int? b = ParseHexToInt(CcBaseHex.Text);
        int? t = ParseHexToInt(CcTrimHex.Text);
        int? a = ParseHexToInt(CcAccHex.Text);
        int? eye = ParseHexToInt(CcEyeHex.Text);
        int? skin = ParseHexToInt(CcSkinHex.Text);
        int? hair = ParseHexToInt(CcHairHex.Text);

        _service.SetColors(b, t, a, eye, skin, hair, reloadGear: true);
        CcStatusText.Text = "Applied palette colors to character!";
    }

    private void ReadCurrentColors_Click(object? sender, RoutedEventArgs e)
    {
        var (b, t, a, eye, skin, hair) = _service.GetCurrentColors();
        CcBaseHex.Text = $"#{b:X6}";
        CcTrimHex.Text = $"#{t:X6}";
        CcAccHex.Text = $"#{a:X6}";
        CcEyeHex.Text = $"#{eye:X6}";
        CcSkinHex.Text = $"#{skin:X6}";
        CcHairHex.Text = $"#{hair:X6}";
        CcStatusText.Text = "Read colors from avatar.";
    }

    private void Preset_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;
        var parts = tag.Split('|');
        if (parts.Length >= 4)
        {
            CcBaseHex.Text = parts[0];
            CcTrimHex.Text = parts[1];
            CcAccHex.Text = parts[2];
            CcEyeHex.Text = parts[3];

            int? b = ParseHexToInt(parts[0]);
            int? t = ParseHexToInt(parts[1]);
            int? a = ParseHexToInt(parts[2]);
            int? eye = ParseHexToInt(parts[3]);
            int? skin = ParseHexToInt(CcSkinHex.Text);
            int? hair = ParseHexToInt(CcHairHex.Text);

            _service.SetColors(b, t, a, eye, skin, hair, reloadGear: true);
            CcStatusText.Text = $"Preset applied!";
        }
    }

    private static int? ParseHexToInt(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        string clean = hex.Trim().TrimStart('#');
        if (int.TryParse(clean, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int val))
            return val;
        return null;
    }

    #endregion

    #region Manual Injector

    private void ManualInject_Click(object? sender, RoutedEventArgs e)
    {
        string sFile = ManualFileText?.Text?.Trim() ?? "";
        string sLink = ManualLinkText?.Text?.Trim() ?? "";
        string sMeta = ManualMetaText?.Text?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(sFile) || string.IsNullOrWhiteSpace(sLink))
        {
            ManualStatusText.Text = "Error: File path and Linkage ID cannot be empty.";
            return;
        }

        string slotTag = (ManualSlotCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "co";

        string category = slotTag switch
        {
            "he" => "Helm",
            "ba" => "Cape",
            "pe" => "Pet",
            "mi" => "Misc",
            "Weapon" => "Sword",
            _ => "Armor"
        };

        var customItem = new CosmeticItem("Manual Injected Item", 0, sFile, category, slotTag, sLink, sMeta);
        _service.Equip(customItem);

        ManualStatusText.Text = $"Injected '{sLink}' into slot '{slotTag}'!";
    }

    #endregion
}
