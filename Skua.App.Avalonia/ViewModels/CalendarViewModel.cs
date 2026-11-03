using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Skua.App.Avalonia.Services;
using Skua.Shared.Avalonia.ViewModels;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Skua.App.Avalonia.ViewModels;

public sealed partial class CalendarViewModel : BotControlViewModelBase, IDisposable
{
    private readonly ArtixCalendarService _calendarService;
    private CancellationTokenSource? _refreshCancellation;
    private Task? _refreshTask;
    private bool _isEditing;

    public CalendarViewModel(ArtixCalendarService calendarService)
        : base("AQWorlds Calendar", 920, 680)
    {
        _calendarService = calendarService;
        SelectedDate = DateTime.Today;
    }

    public ObservableCollection<CalendarEvent> Events { get; } = [];
    public ObservableCollection<CalendarEvent> SelectedDateEvents { get; } = [];

    [ObservableProperty] private DateTime _selectedDate;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string _url = string.Empty;
    [ObservableProperty] private DateTimeOffset? _lastSynced;
    [ObservableProperty] private string _status = "Calendar has not been synchronized yet.";
    [ObservableProperty] private CalendarEvent? _selectedEvent;
    [ObservableProperty] private bool _isBusy;

    partial void OnSelectedDateChanged(DateTime value) => RefreshSelectedDate();

    partial void OnSelectedEventChanged(CalendarEvent? value)
    {
        if (value is null)
            return;

        _isEditing = value.IsCustom;
        Title = value.IsCustom ? value.Title : string.Empty;
        Url = value.IsCustom ? value.Url ?? string.Empty : string.Empty;
        Description = string.Empty;
        SelectedDate = value.Start.Date;
    }

    protected override void OnActivated()
    {
        base.OnActivated();
        if (_refreshTask is not null)
            return;

        _refreshCancellation = new CancellationTokenSource();
        _refreshTask = RefreshLoopAsync(_refreshCancellation.Token);
    }

    protected override void OnDeactivated()
    {
        StopRefresh();
        base.OnDeactivated();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        try
        {
            CalendarEvent? previouslySelected = SelectedEvent;
            IReadOnlyList<CalendarEvent> official = await _calendarService.GetOfficialEventsAsync(true);
            IReadOnlyList<CalendarEvent> custom = await _calendarService.GetCustomEventsAsync();

            Events.Clear();
            foreach (CalendarEvent calendarEvent in official.Concat(custom).OrderBy(calendarEvent => calendarEvent.Start))
                Events.Add(calendarEvent);

            LastSynced = _calendarService.LastSuccessfulSync;
            Status = $"Loaded {official.Count} official releases and {custom.Count} custom events.";
            SelectedEvent = previouslySelected is null ? null : Events.FirstOrDefault(calendarEvent => calendarEvent.Id == previouslySelected.Id);
            RefreshSelectedDate();
        }
        catch (Exception exception)
        {
            Status = $"Calendar refresh failed: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveEventAsync()
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            Status = "Enter an event title first.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(Url) &&
            (!Uri.TryCreate(Url, UriKind.Absolute, out Uri? parsedUri) ||
             parsedUri.Scheme is not ("http" or "https")))
        {
            Status = "The event link must be a valid HTTP or HTTPS URL.";
            return;
        }

        CalendarEvent? existing = SelectedEvent is { IsCustom: true } selected ? selected : null;
        CalendarEvent entry = new(
            existing?.Id ?? Guid.NewGuid().ToString("N"),
            Title.Trim(),
            SelectedDate.Date,
            null,
            string.IsNullOrWhiteSpace(Url) ? null : Url.Trim(),
            true);

        List<CalendarEvent> custom = Events.Where(calendarEvent => calendarEvent.IsCustom && calendarEvent.Id != entry.Id).ToList();
        custom.Add(entry);

        try
        {
            await _calendarService.SaveCustomEventsAsync(custom);
            await LoadEventsAsync();
            SelectedEvent = Events.FirstOrDefault(calendarEvent => calendarEvent.Id == entry.Id);
            Status = existing is null ? "Custom event added." : "Custom event updated.";
        }
        catch (Exception exception)
        {
            Status = $"Could not save the custom event: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteEventAsync()
    {
        if (SelectedEvent is not { IsCustom: true } selected)
        {
            Status = "Select one of your custom events to delete.";
            return;
        }

        List<CalendarEvent> custom = Events.Where(calendarEvent => calendarEvent.IsCustom && calendarEvent.Id != selected.Id).ToList();
        try
        {
            await _calendarService.SaveCustomEventsAsync(custom);
            SelectedEvent = null;
            ClearEditor();
            await LoadEventsAsync();
            Status = "Custom event deleted.";
        }
        catch (Exception exception)
        {
            Status = $"Could not delete the custom event: {exception.Message}";
        }
    }

    [RelayCommand]
    private void NewEvent()
    {
        SelectedEvent = null;
        _isEditing = false;
        ClearEditor();
    }

    private async Task LoadEventsAsync()
    {
        IReadOnlyList<CalendarEvent> official = await _calendarService.GetOfficialEventsAsync();
        IReadOnlyList<CalendarEvent> custom = await _calendarService.GetCustomEventsAsync();
        Events.Clear();
        foreach (CalendarEvent calendarEvent in official.Concat(custom).OrderBy(calendarEvent => calendarEvent.Start))
            Events.Add(calendarEvent);
        RefreshSelectedDate();
    }

    private void RefreshSelectedDate()
    {
        SelectedDateEvents.Clear();
        foreach (CalendarEvent calendarEvent in Events.Where(calendarEvent =>
            calendarEvent.Start.Date <= SelectedDate.Date &&
            (calendarEvent.End?.Date ?? calendarEvent.Start.Date) >= SelectedDate.Date))
            SelectedDateEvents.Add(calendarEvent);
    }

    private async Task RefreshLoopAsync(CancellationToken cancellationToken)
    {
        await RefreshAsync();
        using PeriodicTimer timer = new(TimeSpan.FromMinutes(30));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(RefreshAsync);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void ClearEditor()
    {
        Title = string.Empty;
        Description = string.Empty;
        Url = string.Empty;
    }

    private void StopRefresh()
    {
        _refreshCancellation?.Cancel();
        _refreshCancellation?.Dispose();
        _refreshCancellation = null;
        _refreshTask = null;
    }

    public void Dispose()
    {
        StopRefresh();
        GC.SuppressFinalize(this);
    }
}
