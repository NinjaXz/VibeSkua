using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Utils;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Skua.Shared.Avalonia.Controls.Options;

/// <summary>
/// A dropdown for long lists that filters as you type: any part of a name matches, case ignored.
/// Only a name from the list is ever taken: <see cref="Value"/> changes on a pick (click, Enter,
/// or typing a full name), and leaving the box with other text puts the picked name back.
/// Arrowing through the list picks nothing until Enter; Escape keeps the earlier pick.
/// Enter with no row highlighted takes the first match.
/// </summary>
public class SearchComboBox : AutoCompleteBox
{
    public static readonly StyledProperty<string?> ValueProperty =
        AvaloniaProperty.Register<SearchComboBox, string?>(nameof(Value), defaultBindingMode: BindingMode.TwoWay);

    public string? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(AutoCompleteBox);

    private TextBox? _textBox;

    public SearchComboBox()
    {
        FilterMode = AutoCompleteFilterMode.Custom;
        MinimumPrefixLength = 0;
        MinimumPopulateDelay = TimeSpan.Zero;
        MaxDropDownHeight = 320;
        // The whole list while the box still shows the picked name, else the names containing the text.
        ItemFilter = (search, item) => string.IsNullOrEmpty(search)
            || string.Equals(search, Value, StringComparison.OrdinalIgnoreCase)
            || (item?.ToString()?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);
        // Clicks on the text field open the list; clicks on the list's rows bubble here too and must not.
        AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if (e.Source is Visual v && _textBox is not null && (v == _textBox || _textBox.IsVisualAncestorOf(v)))
            {
                _letGo++;
                Open();
            }
        }, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private DateTime _closedAt;

    // Set when a row is picked while the list is open; the box then lets go of focus once the list closes.
    private bool _picked;

    protected override void OnDropDownOpened(EventArgs e)
    {
        base.OnDropDownOpened(e);
        _picked = false;
    }

    protected override void OnDropDownClosed(EventArgs e)
    {
        base.OnDropDownClosed(e);
        _closedAt = DateTime.UtcNow;
        if (_picked)
            LetGo();
    }

    /// <summary>
    /// Done with the box: drop focus so the pick shows plainly, not as selected text. A little later,
    /// not at once: after a row is clicked, AutoCompleteBox focuses its text field again once the
    /// list has closed, and that would take the focus straight back. The user clicking or tabbing
    /// into the box in the meantime cancels it (<see cref="_letGo"/>).
    /// </summary>
    private void LetGo()
    {
        int call = ++_letGo;
        DispatcherTimer.RunOnce(() =>
        {
            if (call != _letGo)
                return;
            _textBox?.ClearSelection();
            // Only this box's own focus: never a control the user moved on to.
            IFocusManager? focus = TopLevel.GetTopLevel(this)?.FocusManager;
            if (focus?.GetFocusedElement() is Visual focused && (focused == this || this.IsVisualAncestorOf(focused)))
                focus.ClearFocus();
        }, TimeSpan.FromMilliseconds(120));
    }

    // Counts LetGo calls and user interactions: a pending LetGo only runs if nothing came after it.
    private int _letGo;

    // A row clicked (or Enter in the list) commits, even the row already picked, which changes no
    // selection; AutoCompleteBox closes the list first, so this lets go itself.
    protected override ISelectionAdapter? GetSelectionAdapterPart(INameScope nameScope)
    {
        ISelectionAdapter? adapter = base.GetSelectionAdapterPart(nameScope);
        if (adapter is not null)
            adapter.Commit += (_, _) =>
            {
                _picked = true;
                LetGo();
            };
        return adapter;
    }

    private bool JustPicked => _picked && DateTime.UtcNow - _closedAt < TimeSpan.FromMilliseconds(400);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _textBox = e.NameScope.Find<TextBox>("PART_TextBox");
        Text = Value;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty)
        {
            // Posted: a pick from the list lands here inside the list's selection change, and setting
            // Text there rebuilds the list mid-change (AutoCompleteBox then throws).
            if (!IsKeyboardFocusWithin)
                ShowValue();
        }
        else if (change.Property == SelectedItemProperty && !_arrowing && change.NewValue is string picked && Has(picked) is { } name)
        {
            // A row arrowed to is only highlighted: Enter takes it, Escape must leave the earlier pick.
            Value = name;
            if (IsDropDownOpen)
                _picked = true;
        }
        else if (change.Property == TextProperty && !IsKeyboardFocusWithin && Text != Value && _repairs < 3)
        {
            // AutoCompleteBox clears Text when its list is rebuilt with nothing selected; a box not
            // being used always shows the pick (a few times at most, so a theme fighting it can't loop).
            _repairs++;
            ShowValue();
        }
    }

    private int _repairs;

    // Only the box being used opens its list: themes that bind the inner text box to Text make the
    // starting value look typed, which would open every list in the window at once.
    protected override void OnDropDownOpening(System.ComponentModel.CancelEventArgs e)
    {
        base.OnDropDownOpening(e);
        if (!IsKeyboardFocusWithin)
            e.Cancel = true;
    }

    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        base.OnGotFocus(e);
        _repairs = 0;
        // Focus coming back from the list right after a pick (AutoCompleteBox focuses its text field
        // in code): let it go again rather than select the text. The user clicking or tabbing in
        // starts a new search instead.
        if (JustPicked && e.NavigationMethod == NavigationMethod.Unspecified)
        {
            LetGo();
            return;
        }
        _letGo++;
        Dispatcher.UIThread.Post(() => _textBox?.SelectAll());
        Open();
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        if (IsKeyboardFocusWithin)
            return;
        // Focus went elsewhere: a pending LetGo has nothing left to let go of.
        _letGo++;
        if (Has(Text) is { } name)
            Value = name;
        ShowValue();
        Dispatcher.UIThread.Post(() => _textBox?.ClearSelection(), DispatcherPriority.Background);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Before AutoCompleteBox, which takes Enter only to close the list.
        if (e.Key == Key.Enter)
        {
            // Arrowing to a row puts its name in Text, so Text covers a highlighted row too.
            if ((Has(Text) ?? FirstMatch(Text)) is { } name)
                Value = name;
            IsDropDownOpen = false;
            ShowValue();
            LetGo();
            e.Handled = true;
            return;
        }
        _letGo++;
        if (e.Key == Key.Escape)
            ShowValue();
        // The list's selection follows the arrow keys inside this call.
        _arrowing = e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown;
        try
        {
            base.OnKeyDown(e);
        }
        finally
        {
            _arrowing = false;
        }
    }

    private bool _arrowing;

    private void ShowValue() => Dispatcher.UIThread.Post(() =>
    {
        if (Text != Value)
            Text = Value;
    });

    private void Open()
    {
        // Not straight after a pick closed it: focus coming back to the text field would reopen it.
        if (IsEnabled && !IsDropDownOpen && DateTime.UtcNow - _closedAt > TimeSpan.FromMilliseconds(300))
            IsDropDownOpen = true;
    }

    /// <summary>
    /// The list's spelling of a name typed in full, or null. Case is ignored, except that an exact
    /// match wins (enums can have names that differ only in case).
    /// </summary>
    private string? Has(string? text) =>
        string.IsNullOrEmpty(text) ? null
            : Names().FirstOrDefault(n => n == text) ?? Names().FirstOrDefault(n => string.Equals(n, text, StringComparison.OrdinalIgnoreCase));

    private string? FirstMatch(string? text) =>
        string.IsNullOrEmpty(text) ? null : Names().FirstOrDefault(n => n.Contains(text, StringComparison.OrdinalIgnoreCase));

    private IEnumerable<string> Names() =>
        ItemsSource?.Cast<object?>().Select(i => i?.ToString()).OfType<string>() ?? Enumerable.Empty<string>();
}
