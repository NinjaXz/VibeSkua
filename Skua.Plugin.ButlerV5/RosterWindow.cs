using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
    private readonly ComboBox _targetHistory;
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

        Grid header = new()
        {
            Margin = new Thickness(2, 0, 2, 12),
        };
        StackPanel headerLeft = new();
        _meText = AvaloniaUi.TextBlock(16);
        _meText.FontWeight = FontWeight.SemiBold;
        _stateText = AvaloniaUi.TextBlock(12, AvaloniaUi.SubText);
        _stateText.Margin = new Thickness(0, 2, 0, 0);
        headerLeft.Children.Add(_meText);
        headerLeft.Children.Add(_stateText);
        header.Children.Add(headerLeft);
        root.Children.Add(header);

        Grid targetBar = new()
        {
            Margin = new Thickness(2, 16, 2, 12),
            ColumnDefinitions = new ColumnDefinitions("*,150,Auto"),
        };
        _targetPlayerText = new TextBox
        {
            Watermark = "External player name",
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
            MinHeight = 32,
            Margin = new Thickness(0, 0, 6, 0),
        };
        AvaloniaUi.SetToolTip(_targetPlayerText, "Enter an in-game player who is not one of your active ButlerV5 clients.");
        targetBar.Children.Add(_targetPlayerText);

        _targetHistory = new ComboBox
        {
            ItemsSource = _plugin.RecentPlayerTargets,
            MinWidth = 140,
            MaxDropDownHeight = 240,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };
        _targetHistory.SelectionChanged += (_, _) =>
        {
            if (_targetHistory.SelectedItem is string previousTarget)
                _targetPlayerText.Text = previousTarget;
        };
        AvaloniaUi.SetToolTip(_targetHistory, "Recently used external player names. Select one to reuse it.");
        Grid.SetColumn(_targetHistory, 1);
        targetBar.Children.Add(_targetHistory);

        Button sendAllToPlayer = AvaloniaUi.Button("Send all", () =>
        {
            _plugin.SendAllToPlayer(_targetPlayerText.Text ?? "");
            _targetHistory.ItemsSource = _plugin.RecentPlayerTargets;
            _targetHistory.SelectedItem = null;
            Refresh(force: true);
        }, AvaloniaUi.Accent);
        AvaloniaUi.SetToolTip(sendAllToPlayer, "Send every available online ButlerV5 client to this external player.");
        Grid.SetColumn(sendAllToPlayer, 2);
        targetBar.Children.Add(sendAllToPlayer);
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

        Grid footer = new()
        {
            Margin = new Thickness(2, 0, 2, 8),
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto,Auto"),
        };
        _footerText = AvaloniaUi.TextBlock(11, AvaloniaUi.SubText);
        _footerText.VerticalAlignment = VerticalAlignment.Center;
        footer.Children.Add(_footerText);

        _stopButton = AvaloniaUi.Button("Stop following", () =>
        {
            _plugin.StopFollow();
            Refresh(force: true);
        }, AvaloniaUi.Red);
        _stopButton.Margin = new Thickness(0, 0, 6, 0);
        Grid.SetColumn(_stopButton, 1);
        footer.Children.Add(_stopButton);

        _releaseAllButton = AvaloniaUi.Button("Release all", () =>
        {
            _plugin.ReleaseFollowers();
            Refresh(force: true);
        }, AvaloniaUi.OrangeButton);
        AvaloniaUi.SetToolTip(_releaseAllButton, "Clear this account's followers list; released butlers park at their configured location.");
        _releaseAllButton.Margin = new Thickness(0, 0, 6, 0);
        Grid.SetColumn(_releaseAllButton, 2);
        footer.Children.Add(_releaseAllButton);

        Button refreshButton = AvaloniaUi.Button("Refresh", () => Refresh(force: true));
        AvaloniaUi.SetToolTip(refreshButton, "Rescan the shared ButlerV5 sync files now.");
        refreshButton.Margin = new Thickness(0, 0, 6, 0);
        Grid.SetColumn(refreshButton, 3);
        footer.Children.Add(refreshButton);

        _summonAllButton = AvaloniaUi.Button("Summon all", () =>
        {
            _plugin.SummonAll();
            Refresh(force: true);
        });
        _summonAllButton.Margin = new Thickness(0, 0, 6, 0);
        Grid.SetColumn(_summonAllButton, 4);
        footer.Children.Add(_summonAllButton);

        Button settings = AvaloniaUi.Button("Settings", _plugin.OpenSettings);
        AvaloniaUi.SetToolTip(settings, "Open the ButlerV5 settings and debug log.");
        Grid.SetColumn(settings, 5);
        footer.Children.Add(settings);
        StackPanel bottomPanel = new();
        bottomPanel.Children.Add(targetBar);
        bottomPanel.Children.Add(footer);
        Grid.SetRow(bottomPanel, 2);
        root.Children.Add(bottomPanel);

        Content = root;
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

    private void OnSyncFilesChanged()
    {
        try { Dispatcher.UIThread.Post(() => Refresh()); }
        catch { }
    }

    public void RefreshRoster() => Refresh(force: true);

    private void Refresh(bool force = false)
    {
        List<SyncData> online = Roster.GetOnline();
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
            : $"{online.Count} plugin account{(online.Count == 1 ? "" : "s")} online - refreshes every {refreshSeconds}s";

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
            empty.Text = "No ButlerV5 accounts online.\nAccounts appear after they log in with the plugin installed.";
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
        bool isSelf = account.Username.Equals(_plugin.OwnUsername ?? "", StringComparison.OrdinalIgnoreCase);
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
        if (isSelf)
            followBlock = "This is the account displaying the roster.";
        else if (pluginOff)
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
            summonBlock = isSelf ? "This is the account displaying the roster."
                : pluginOff ? "ButlerV5 is disabled."
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
