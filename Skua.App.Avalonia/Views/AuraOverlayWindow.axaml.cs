using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Material.Icons;
using Skua.App.Avalonia.ViewModels;
using Skua.Core.Models.Auras;
using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;

namespace Skua.App.Avalonia.Views;

/// <summary>
/// One Aura Tracker panel (You or Target) floating over the game. Flash draws above anything inside
/// the main window, so the panel is its own small window owned by it: it stays above the game, minimizes
/// with it and moves with it. Its offset from the main window is remembered.
/// </summary>
public partial class AuraOverlayWindow : Window
{
    private readonly AuraTrackerViewModel? _vm;
    private readonly Window? _owner;
    private readonly SubjectType _subject;
    private PixelPoint _offset;

    public AuraOverlayWindow()
    {
        InitializeComponent();
    }

    public AuraOverlayWindow(AuraTrackerViewModel vm, Window owner, SubjectType subject) : this()
    {
        _vm = vm;
        _owner = owner;
        _subject = subject;
        DataContext = vm;

        bool self = subject == SubjectType.Self;
        HeaderIcon.Kind = self ? MaterialIconKind.AccountOutline : MaterialIconKind.Crosshairs;
        Rows.ItemsSource = self ? vm.SelfAuras : vm.TargetAuras;
        _offset = LoadOffset();

        Header.PointerPressed += Header_PointerPressed;
        FoldButton.Click += (_, _) =>
        {
            Body.IsVisible = !Body.IsVisible;
            FoldButton.Content = Body.IsVisible ? "–" : "+";
        };
        (self ? vm.SelfAuras : vm.TargetAuras).CollectionChanged += Rows_CollectionChanged;
        vm.PropertyChanged += Vm_PropertyChanged;
        owner.PositionChanged += Owner_Moved;
        owner.Resized += Owner_Resized;
        foreach ((AuraFilter filter, string label) in new[]
                 {
                     (AuraFilter.All, "All"), (AuraFilter.Buffs, "Buffs"),
                     (AuraFilter.Debuffs, "Debuffs"), (AuraFilter.ScrollsAndPotions, "Scrolls & potions")
                 })
        {
            Button chip = new() { Content = label, Tag = filter };
            chip.Classes.Add("chip");
            chip.Click += (_, _) =>
            {
                if (_subject == SubjectType.Self)
                    vm.SelfFilter = filter;
                else
                    vm.TargetFilter = filter;
            };
            Chips.Children.Add(chip);
        }
        UpdateChips();
        UpdateHeader();
    }

    private void UpdateChips()
    {
        if (_vm is null)
            return;
        AuraFilter current = _subject == SubjectType.Self ? _vm.SelfFilter : _vm.TargetFilter;
        foreach (Control c in Chips.Children)
            c.Classes.Set("on", c.Tag is AuraFilter f && f == current);
    }

    public void Place()
    {
        if (_owner is null)
            return;
        Position = new PixelPoint(_owner.Position.X + _offset.X, _owner.Position.Y + _offset.Y);
    }

    private PixelPoint DefaultOffset()
    {
        double scale = _owner?.RenderScaling ?? 1;
        int ownerWidth = (int)((_owner?.ClientSize.Width ?? 900) * scale);
        int top = (int)(110 * scale);
        return _subject == SubjectType.Self
            ? new PixelPoint((int)(16 * scale), top)
            : new PixelPoint(Math.Max(0, ownerWidth - (int)((Width + 16) * scale)), top);
    }

    private PixelPoint LoadOffset()
    {
        string saved = _vm?.GetOverlayPosition(_subject) ?? string.Empty;
        string[] parts = saved.Split(',');
        if (parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y))
            return new PixelPoint(x, y);
        return DefaultOffset();
    }

    private void Header_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_owner is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        BeginMoveDrag(e);
        _offset = new PixelPoint(Position.X - _owner.Position.X, Position.Y - _owner.Position.Y);
        _vm?.SetOverlayPosition(_subject, string.Create(CultureInfo.InvariantCulture, $"{_offset.X},{_offset.Y}"));
    }

    private void Owner_Moved(object? sender, PixelPointEventArgs e) => Place();

    private void Owner_Resized(object? sender, WindowResizedEventArgs e) => Place();

    private void Rows_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateHeader();

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AuraTrackerViewModel.TargetTitle) or nameof(AuraTrackerViewModel.HasTarget))
            UpdateHeader();
        else if (e.PropertyName is nameof(AuraTrackerViewModel.SelfFilter) or nameof(AuraTrackerViewModel.TargetFilter))
            UpdateChips();
    }

    private void UpdateHeader()
    {
        if (_vm is null)
            return;
        if (_subject == SubjectType.Self)
        {
            HeaderText.Text = "YOU";
            EmptyText.Text = "No auras";
            EmptyText.IsVisible = _vm.SelfAuras.Count == 0;
        }
        else
        {
            HeaderText.Text = _vm.TargetTitle.ToUpperInvariant();
            EmptyText.Text = _vm.HasTarget ? "No auras" : "No target";
            EmptyText.IsVisible = _vm.TargetAuras.Count == 0;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_vm is not null)
        {
            (_subject == SubjectType.Self ? _vm.SelfAuras : _vm.TargetAuras).CollectionChanged -= Rows_CollectionChanged;
            _vm.PropertyChanged -= Vm_PropertyChanged;
        }
        if (_owner is not null)
        {
            _owner.PositionChanged -= Owner_Moved;
            _owner.Resized -= Owner_Resized;
        }
        base.OnClosed(e);
    }
}

/// <summary>Opens or closes the two overlay panels to match the Aura Tracker's "Show over game".</summary>
public static class AuraOverlay
{
    private static AuraOverlayWindow? _self;
    private static AuraOverlayWindow? _target;

    public static void Attach(Window owner, AuraTrackerViewModel vm)
    {
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AuraTrackerViewModel.ShowOverlay))
                Sync(owner, vm);
        };
        owner.Closed += (_, _) => Close();
        Sync(owner, vm);
    }

    private static void Sync(Window owner, AuraTrackerViewModel vm)
    {
        if (!vm.ShowOverlay)
        {
            Close();
            return;
        }
        if (_self is not null)
            return;
        _self = new AuraOverlayWindow(vm, owner, SubjectType.Self);
        _target = new AuraOverlayWindow(vm, owner, SubjectType.Target);
        foreach (AuraOverlayWindow w in new[] { _self, _target })
        {
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Place();
            w.Show(owner);
        }
    }

    private static void Close()
    {
        _self?.Close();
        _target?.Close();
        _self = null;
        _target = null;
    }
}
