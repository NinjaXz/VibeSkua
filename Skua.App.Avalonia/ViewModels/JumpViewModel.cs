using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Skua.Core.Interfaces;
using Skua.Core.Utils;
using Skua.Shared.Avalonia.ViewModels;

namespace Skua.App.Avalonia.ViewModels;

public partial class JumpViewModel : BotControlViewModelBase
{
    public JumpViewModel(IMapService mapService, ISettingsService settingsService)
        : base("Jump")
    {
        _mapService = mapService;
        _settingsService = settingsService;
        _autoJumpOnPositionChange = _settingsService.Get("AutoJumpOnPositionChange", false);
        Pads = _mapService.Pads;
    }

    private readonly IMapService _mapService;
    private readonly ISettingsService _settingsService;
    private readonly SemaphoreSlim _jumpSemaphore = new(1, 1);
    private long _jumpRequestVersion;
    private bool _suppressAutoJump = false;

    [ObservableProperty]
    private string _selectedCell = string.Empty;

    async partial void OnSelectedCellChanged(string value)
    {
        // Invalidate queued jumps whenever the selection changes, including to an invalid value.
        Interlocked.Increment(ref _jumpRequestVersion);

        if (string.IsNullOrWhiteSpace(value) || _suppressAutoJump)
            return;

        // Pick a sensible pad when changing cells without causing a second jump.
        _suppressAutoJump = true;
        try
        {
            if (value == "Enter")
                SelectedPad = "Spawn";
            else if (string.IsNullOrWhiteSpace(SelectedPad))
                SelectedPad = "Left";
        }
        finally
        {
            _suppressAutoJump = false;
        }

        if (AutoJumpOnPositionChange)
            await JumpTo();
    }

    [ObservableProperty]
    private string _selectedPad = string.Empty;

    async partial void OnSelectedPadChanged(string value)
    {
        // A newer pad selection makes any queued jump for the previous selection stale.
        Interlocked.Increment(ref _jumpRequestVersion);

        if (!string.IsNullOrWhiteSpace(value) &&
            !string.IsNullOrWhiteSpace(SelectedCell) &&
            !_suppressAutoJump &&
            AutoJumpOnPositionChange)
        {
            await JumpTo();
        }
    }

    [ObservableProperty]
    private bool _autoJumpOnPositionChange;

    partial void OnAutoJumpOnPositionChangeChanged(bool value)
    {
        try
        {
            _settingsService.Set("AutoJumpOnPositionChange", value);
        }
        catch (Exception ex)
        {
            // Revert the generated property's backing field when persistence fails.
            _autoJumpOnPositionChange = !value;
            OnPropertyChanged(nameof(AutoJumpOnPositionChange));
            Trace.WriteLine($"Failed to save AutoJumpOnPositionChange; toggle reverted: {ex}");
        }
    }

    [ObservableProperty]
    private RangedObservableCollection<string> _cells = new();

    public ImmutableList<string> Pads { get; }

    [RelayCommand]
    private void GetCurrent()
    {
        UpdateCells();
        (string? cell, string? pad) = _mapService.GetCurrentCell();

        _suppressAutoJump = true;
        try
        {
            // Force a property notification even when the current cell matches the
            // view-model value already; the selector may not have applied its initial selection.
            SelectedCell = string.Empty;
            SelectedPad = string.Empty;
            SelectedCell = cell ?? string.Empty;
            SelectedPad = pad ?? string.Empty;
        }
        finally
        {
            _suppressAutoJump = false;
        }
    }

    [RelayCommand]
    private async Task JumpTo()
    {
        string cell = SelectedCell;
        string pad = SelectedPad;
        if (string.IsNullOrWhiteSpace(cell) || string.IsNullOrWhiteSpace(pad))
            return;

        // Each request gets a version so queued stale selections can be discarded.
        long requestVersion = Interlocked.Increment(ref _jumpRequestVersion);

        try
        {
            await _jumpSemaphore.WaitAsync();
            try
            {
                // Serialize map jumps and only execute the newest queued selection.
                if (requestVersion != Volatile.Read(ref _jumpRequestVersion))
                    return;

                await Task.Run(() => _mapService.Jump(cell, pad));
            }
            finally
            {
                _jumpSemaphore.Release();
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Jump menu jump failed: {ex}");
        }
    }

    [RelayCommand]
    public void UpdateCells()
    {
        string[] newCells = _mapService.Cells.ToArray();
        if (Cells.SequenceEqual(newCells))
            return;

        _suppressAutoJump = true;
        try
        {
            string current = SelectedCell;
            // ReplaceRange also clears stale entries when the map reports no cells.
            Cells.ReplaceRange(newCells);
            if (!string.IsNullOrEmpty(current) && Cells.Contains(current))
                SelectedCell = current;
        }
        finally
        {
            _suppressAutoJump = false;
        }
    }
}