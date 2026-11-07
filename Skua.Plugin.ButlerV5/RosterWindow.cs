using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace ButlerV5;

/// <summary>Native Avalonia roster window for ButlerV5.</summary>
public sealed class RosterWindow : Window
{
    private readonly ButlerV5Plugin _plugin;
    private readonly DispatcherTimer _timer;
    private readonly StackPanel _rows;
    private readonly TextBlock _meText;
    private readonly TextBlock _stateText;
    private readonly Button _stopButton;
    private readonly Button _releaseAllButton;
    private readonly Button _summonAllButton;
    private readonly TextBlock _footerText;
    private readonly TextBox _targetPlayerText;
    private readonly WrapPanel _targetHistory;
    private readonly TextBlock _targetHistoryLabel;
    private readonly Flyout _targetFlyout;
    private readonly Button _sendToPlayerButton;
    private string _lastSnapshot = "";

    public RosterWindow(ButlerV5Plugin plugin)
    {
        _plugin = plugin;
        Title = "ButlerV5 Avalonia";
        Width = 560;
        Height = 510;
        MinWidth = 450;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        AvaloniaUi.PrepareWindow(this);

        Grid root = new()
        {
            Margin = new Thickness(14),
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
        };

        // Your account and follow state on the left; Stop following / Release all on the right, as in the original.
        Grid header = new()
        {
            Margin = new Thickness(2, 0, 2, 12),
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
        };
        StackPanel headerLeft = new();
        _meText = AvaloniaUi.TextBlock(16);
        _meText.FontWeight = FontWeight.SemiBold;
        _stateText = AvaloniaUi.TextBlock(12, AvaloniaUi.SubText);
        _stateText.Margin = new Thickness(0, 2, 0, 0);
        headerLeft.Children.Add(_meText);
        headerLeft.Children.Add(_stateText);
        header.Children.Add(headerLeft);

        StackPanel headerButtons = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };
        _stopButton = AvaloniaUi.Button("Stop following", () =>
        {
            _plugin.StopFollow();
            Refresh(force: true);
        }, AvaloniaUi.Red);
        headerButtons.Children.Add(_stopButton);

        _releaseAllButton = AvaloniaUi.Button("Release all", () =>
        {
            _plugin.ReleaseFollowers();
            Refresh(force: true);
        }, AvaloniaUi.OrangeButton);
        AvaloniaUi.SetToolTip(_releaseAllButton, "Clear this account's followers list; released butlers park at their configured location.");
        headerButtons.Children.Add(_releaseAllButton);
        Grid.SetColumn(headerButtons, 1);
        header.Children.Add(headerButtons);
        root.Children.Add(header);

        // "Send to player" lives in a flyout off its own footer button: name box, Send, and recent names.
        StackPanel targetPanel = new() { Width = 340 };
        TextBlock targetCaption = AvaloniaUi.TextBlock(12, AvaloniaUi.SubText);
        targetCaption.Text = "/goto a player with this account and every available butler.";
        targetCaption.TextWrapping = TextWrapping.Wrap;
        targetCaption.Margin = new Thickness(0, 0, 0, 8);
        targetPanel.Children.Add(targetCaption);

        Grid targetRow = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        _targetPlayerText = new TextBox
        {
            Watermark = "Player name (not one of your butlers)",
            VerticalContentAlignment = VerticalAlignment.Center,
            MinHeight = 32,
            Margin = new Thickness(0, 0, 6, 0),
        };
        _targetPlayerText.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
                return;
            e.Handled = true;
            SendAllToPlayer();
        };
        targetRow.Children.Add(_targetPlayerText);
        Button sendAllToPlayer = AvaloniaUi.Button("Send", SendAllToPlayer, AvaloniaUi.Accent);
        Grid.SetColumn(sendAllToPlayer, 1);
        targetRow.Children.Add(sendAllToPlayer);
        targetPanel.Children.Add(targetRow);

        _targetHistoryLabel = AvaloniaUi.TextBlock(11, AvaloniaUi.SubText);
        _targetHistoryLabel.Text = "Recent";
        _targetHistoryLabel.Margin = new Thickness(0, 10, 0, 4);
        targetPanel.Children.Add(_targetHistoryLabel);
        _targetHistory = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        targetPanel.Children.Add(_targetHistory);

        _targetFlyout = new Flyout { Content = targetPanel, Placement = PlacementMode.TopEdgeAlignedRight };
        _targetFlyout.Opened += (_, _) =>
        {
            UpdateTargetHistory();
            _targetPlayerText.Focus();
        };
        _rows = new StackPanel();
        ScrollViewer scroll = new()
        {
            Content = _rows,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        Border listPanel = new()
        {
            Child = scroll,
            Background = AvaloniaUi.PanelRaised,
            BorderBrush = AvaloniaUi.Divider,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(6, 4),
        };
        Grid.SetRow(listPanel, 1);
        root.Children.Add(listPanel);

        // Status on the left, buttons on the right; FooterBar stacks them only when one row is too narrow.
        FooterBar footer = new();
        _footerText = AvaloniaUi.TextBlock(11, AvaloniaUi.SubText);
        _footerText.TextWrapping = TextWrapping.Wrap;
        _footerText.Background = Brushes.Transparent; // hit-testable across its whole box, for the tooltip
        footer.Children.Add(_footerText);
        WrapPanel footerActions = new()
        {
            Orientation = Orientation.Horizontal,
            ItemsAlignment = WrapPanelItemsAlignment.End,
            ItemSpacing = 6,
            LineSpacing = 6,
        };

        _sendToPlayerButton = AvaloniaUi.Button("Send to player", () => { });
        _sendToPlayerButton.Flyout = _targetFlyout;
        footerActions.Children.Add(_sendToPlayerButton);

        _summonAllButton = AvaloniaUi.Button("Summon all", () =>
        {
            _plugin.SummonAll();
            Refresh(force: true);
        });
        footerActions.Children.Add(_summonAllButton);

        Button settings = AvaloniaUi.Button("Settings", _plugin.OpenSettings);
        AvaloniaUi.SetToolTip(settings, "Open the ButlerV5 settings and debug log.");
        footerActions.Children.Add(settings);
        footer.Children.Add(footerActions);
        footer.Margin = new Thickness(2, 12, 2, 2);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        AvaloniaUi.SetContent(this, root);
        _plugin.SyncFilesChanged += OnSyncFilesChanged;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        Refresh(force: true);

        Closed += (_, _) =>
        {
            _timer.Stop();
            _plugin.SyncFilesChanged -= OnSyncFilesChanged;
        };
    }

    private void SendAllToPlayer()
    {
        string target = _targetPlayerText.Text?.Trim() ?? "";
        _plugin.SendAllToPlayer(target);
        if (target.Length > 0)
            _targetFlyout.Hide();
        Refresh(force: true);
    }

    private void UpdateTargetHistory()
    {
        _targetHistory.Children.Clear();
        foreach (string name in _plugin.RecentPlayerTargets.Take(8))
        {
            Button chip = new()
            {
                Content = name,
                FontSize = 11,
                Padding = new Thickness(8, 2),
                MinHeight = 24,
            };
            chip.Classes.Add("skua-button");
            AvaloniaUi.SetToolTip(chip, $"Use {name}.");
            chip.Click += (_, _) =>
            {
                _targetPlayerText.Text = name;
                _targetPlayerText.CaretIndex = name.Length;
                _targetPlayerText.Focus();
            };
            _targetHistory.Children.Add(chip);
        }
        _targetHistoryLabel.IsVisible = _targetHistory.Children.Count > 0;
    }

    private void OnSyncFilesChanged()
    {
        try { Dispatcher.UIThread.Post(() => Refresh()); }
        catch { }
    }

    public void RefreshRoster() => Refresh(force: true);

    private void Refresh(bool force = false)
    {
        // Your own account is the header; the list is everyone else.
        List<SyncData> online = Roster.GetOnline(_plugin.OwnUsername);
        HashSet<string> myFollowers = new(_plugin.CurrentFollowers, StringComparer.OrdinalIgnoreCase);
        string? followingWho = _plugin.FollowingWho;
        bool pluginOff = !_plugin.PluginEnabled;

        _meText.Text = _plugin.OwnUsername ?? "(not logged in)";
        _stateText.Text = followingWho != null
            ? $"Following {followingWho} ({(_plugin.FollowingHow == FollowSource.Ordered ? "summoned" : "manual")})"
            : "Not following anyone";
        _stateText.Foreground = followingWho != null ? AvaloniaUi.Green : AvaloniaUi.SubText;
        _stopButton.IsVisible = followingWho != null;
        _stopButton.IsEnabled = !pluginOff;
        _releaseAllButton.IsVisible = myFollowers.Count > 0;
        _releaseAllButton.IsEnabled = !pluginOff;
        _summonAllButton.IsEnabled = !pluginOff && followingWho == null;
        _sendToPlayerButton.IsEnabled = !pluginOff;
        AvaloniaUi.SetToolTip(_sendToPlayerButton, pluginOff
            ? "ButlerV5 is disabled. Turn it on in Settings first."
            : "Send this account and every available butler to another player.");
        AvaloniaUi.SetToolTip(_summonAllButton, pluginOff
            ? "ButlerV5 is disabled. Turn it on in Settings first."
            : followingWho == null
                ? "Order every available ButlerV5 account on this server to follow this one."
                : $"You're following {followingWho}; stop before summoning anyone.");

        int refreshSeconds = Math.Clamp(_plugin.RosterRefreshSeconds, 1, 30);
        TimeSpan interval = TimeSpan.FromSeconds(refreshSeconds);
        if (_timer.Interval != interval)
            _timer.Interval = interval;
        _footerText.Text = pluginOff
            ? "ButlerV5 is DISABLED - turn it on in Settings"
            : $"{online.Count} other plugin account{(online.Count == 1 ? "" : "s")} online";
        AvaloniaUi.SetToolTip(_footerText, pluginOff
            ? "Open Settings and turn on 'Enable ButlerV5'."
            : $"The list updates as soon as an account changes, and checks again every {refreshSeconds}s.");

        string myServer = _plugin.MyServerName;
        string snapshot = string.Join(";", online.Select(d =>
            $"{d.Username}|{d.MapWithRoom}|{(d.Attacking ? 1 : 0)}|{(myFollowers.Contains(d.Username) ? 1 : 0)}" +
            $"|{d.Following}|{(d.ScriptOn ? 1 : 0)}|{d.ScriptName}|{(d.Parking ? 1 : 0)}|{d.Followers.Count}|{d.Server}|{d.ClassName}")) +
            $"@{followingWho}@{myServer}@{pluginOff}";
        if (!force && snapshot == _lastSnapshot)
            return;
        _lastSnapshot = snapshot;
        _rows.Children.Clear();

        if (online.Count == 0)
        {
            TextBlock empty = AvaloniaUi.TextBlock(12, AvaloniaUi.SubText);
            empty.Text = "No other ButlerV5 accounts online.\nAccounts appear after they log in with the plugin installed.";
            empty.TextAlignment = TextAlignment.Center;
            empty.TextWrapping = TextWrapping.Wrap;
            empty.Margin = new Thickness(0, 40, 0, 0);
            _rows.Children.Add(empty);
            return;
        }

        bool firstRow = true;
        foreach (SyncData account in online)
        {
            if (!firstRow)
                _rows.Children.Add(MakeSeparator());
            firstRow = false;
            _rows.Children.Add(BuildRow(account, myFollowers.Contains(account.Username),
                account.Username.Equals(followingWho, StringComparison.OrdinalIgnoreCase)));
        }
    }

    private static Border MakeSeparator() => new()
    {
        Height = 1,
        Margin = new Thickness(4, 0),
        Background = AvaloniaUi.Divider,
    };

    private Border BuildRow(SyncData account, bool summonedByMe, bool amFollowing)
    {
        bool iAmButler = _plugin.FollowingWho != null;
        bool pluginOff = !_plugin.PluginEnabled;
        bool followingMe = !string.IsNullOrEmpty(account.Following) &&
            account.Following.Equals(_plugin.OwnUsername ?? "", StringComparison.OrdinalIgnoreCase);
        bool followingOther = !string.IsNullOrEmpty(account.Following) && !followingMe;
        string myServer = _plugin.MyServerName;
        bool differentServer = !string.IsNullOrEmpty(account.Server) && !string.IsNullOrEmpty(myServer) &&
            !account.Server.Equals(myServer, StringComparison.OrdinalIgnoreCase);

        Grid grid = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
        StackPanel info = new() { VerticalAlignment = VerticalAlignment.Center };
        WrapPanel nameLine = new() { Orientation = Orientation.Horizontal };
        TextBlock name = AvaloniaUi.TextBlock(14);
        name.Text = account.Username;
        name.FontWeight = FontWeight.SemiBold;
        nameLine.Children.Add(name);

        if (account.Attacking)
            nameLine.Children.Add(MakeChip("fighting", AvaloniaUi.Orange));
        if (followingMe)
            nameLine.Children.Add(MakeChip("following me", AvaloniaUi.Green));
        else if (followingOther)
            nameLine.Children.Add(MakeChip($"following {account.Following}", AvaloniaUi.Accent));
        else if (summonedByMe)
            nameLine.Children.Add(MakeChip("summoned", AvaloniaUi.SubText));
        if (account.ScriptOn)
            nameLine.Children.Add(MakeChip($"busy: {(string.IsNullOrEmpty(account.ScriptName) ? "script" : account.ScriptName)}", AvaloniaUi.Orange));
        if (account.Parking)
            nameLine.Children.Add(MakeChip("parking", AvaloniaUi.SubText));
        if (differentServer)
            nameLine.Children.Add(MakeChip("different server", AvaloniaUi.Red));
        info.Children.Add(nameLine);

        TextBlock location = AvaloniaUi.TextBlock(11, AvaloniaUi.SubText);
        // Older plugin versions omit class=; only show values they actually publish.
        location.Text = string.Join("   ·   ", new[] { account.MapWithRoom, account.Server, account.ClassName }
            .Where(value => !string.IsNullOrEmpty(value)));
        location.TextTrimming = TextTrimming.CharacterEllipsis;
        ToolTip.SetTip(location, location.Text);
        location.Margin = new Thickness(0, 2, 0, 0);
        info.Children.Add(location);
        grid.Children.Add(info);

        Button follow = amFollowing
            ? AvaloniaUi.Button("Stop", () => { _plugin.StopFollow(); Refresh(force: true); }, AvaloniaUi.Red)
            : AvaloniaUi.Button("Follow", () =>
            {
                if (_plugin.IsScriptRunning)
                {
                    _plugin.ShowMessage($"A script is running on this account ({_plugin.RunningScriptName}).\nStop it before following someone.");
                    return;
                }
                _plugin.StartFollow(account.Username, FollowSource.Manual);
                Refresh(force: true);
            });
        follow.Margin = new Thickness(8, 0, 0, 0);
        follow.VerticalAlignment = VerticalAlignment.Center;
        string? followBlock = null;
        if (pluginOff)
            followBlock = "ButlerV5 is disabled.";
        else if (!amFollowing)
            followBlock = iAmButler ? $"You're following {_plugin.FollowingWho}; stop first."
                : summonedByMe ? $"Release {account.Username} before following them."
                : followingMe ? $"{account.Username} is following you."
                : followingOther ? $"{account.Username} is following {account.Following}."
                : differentServer ? $"{account.Username} is on {account.Server}; cross-server following is unavailable."
                : null;
        if (followBlock != null)
        {
            follow.IsEnabled = false;
            AvaloniaUi.SetToolTip(follow, followBlock);
        }
        else
        {
            AvaloniaUi.SetToolTip(follow, amFollowing ? "Stop following this account." : $"Follow {account.Username}.");
        }
        Grid.SetColumn(follow, 1);
        grid.Children.Add(follow);

        Button summon = summonedByMe
            ? AvaloniaUi.Button("Release", () => { _plugin.ToggleFollower(account.Username); Refresh(force: true); }, AvaloniaUi.OrangeButton)
            : AvaloniaUi.Button("Summon", () => { _plugin.ToggleFollower(account.Username); Refresh(force: true); }, AvaloniaUi.Green);
        summon.Margin = new Thickness(6, 0, 0, 0);
        summon.VerticalAlignment = VerticalAlignment.Center;
        string? summonBlock = null;
        if (!summonedByMe)
            summonBlock = pluginOff ? "ButlerV5 is disabled."
                : iAmButler ? $"You're following {_plugin.FollowingWho}; stop before summoning anyone."
                : amFollowing ? $"You're following {account.Username}; stop first."
                : followingMe ? $"{account.Username} is already following you manually."
                : followingOther ? $"{account.Username} is already following {account.Following}."
                : differentServer ? $"{account.Username} is on {account.Server}; cross-server summoning is unavailable."
                : account.ScriptOn ? $"{account.Username} is running a script."
                : account.Followers.Count > 0 ? $"{account.Username} has summoned butlers of their own."
                : account.Parking ? $"{account.Username} is parking; wait until it settles."
                : null;
        if (summonBlock != null)
        {
            summon.IsEnabled = false;
            AvaloniaUi.SetToolTip(summon, summonBlock);
        }
        else
        {
            AvaloniaUi.SetToolTip(summon, summonedByMe
                ? $"Release {account.Username}."
                : $"Order {account.Username} to follow me.");
        }
        Grid.SetColumn(summon, 2);
        grid.Children.Add(summon);

        return new Border { Padding = new Thickness(8, 9), Child = grid };
    }

    private static Border MakeChip(string text, IBrush color) => new()
    {
        BorderBrush = color,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(7, 1),
        Margin = new Thickness(8, 0, 0, 0),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = text, FontSize = 10, Foreground = color },
    };
}
