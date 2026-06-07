using System;
using AxShockwaveFlashObjects;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using System.Windows;
using System.Windows.Controls;

namespace Skua.WPF.UserControls;

/// <summary>
/// Interaction logic for GameContainerUserControl.xaml
/// </summary>
public partial class GameContainerUserControl : UserControl
{
    private IScriptInterface _bot;
    private readonly System.Timers.Timer _memoryTrimTimer;
    private readonly System.Windows.Threading.DispatcherTimer _keepAliveTimer;

    public GameContainerUserControl()
    {
        InitializeComponent();
        _bot = Ioc.Default.GetRequiredService<IScriptInterface>();
        gameContainer.Visibility = Visibility.Hidden;
        WeakReferenceMessenger.Default.Register<GameContainerUserControl, FlashChangedMessage<AxShockwaveFlash>>(this, FlashChanged);
        Loaded += GameContainer_Loaded;
        gameContainer.SizeChanged += (s, e) =>
        {
            try
            {
                var flash = gameContainer.Child as AxShockwaveFlash;
                if (flash != null)
                {
                    flash.ScaleMode = 2;
                }
                _bot.Flash?.SetGameObject("stage.scaleMode", "exactFit");
            }
            catch { }
        };
        if (_bot.Options is System.ComponentModel.INotifyPropertyChanged npc)
        {
            npc.PropertyChanged += Options_PropertyChanged;
        }

        _memoryTrimTimer = new System.Timers.Timer(TimeSpan.FromMinutes(30).TotalMilliseconds);
        _memoryTrimTimer.Elapsed += (s, e) => Skua.Core.Utils.MemoryUtils.TrimWorkingSet();
        _memoryTrimTimer.Start();

        _keepAliveTimer = new System.Windows.Threading.DispatcherTimer();
        _keepAliveTimer.Interval = TimeSpan.FromMilliseconds(500);
        _keepAliveTimer.Tick += (s, e) =>
        {
            try
            {
                // Force the Flash ActiveX control to process its message pump
                // This prevents Windows DWM from fully suspending inactive/occluded tabs
                if (_bot.Flash != null)
                    _bot.Flash.Call("isLoggedIn");
            }
            catch { }
        };
        _keepAliveTimer.Start();
    }

    private void Options_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "HeadlessMode")
        {
            Dispatcher.Invoke(() =>
            {
                if (_bot.Options.HeadlessMode)
                {
                    gameContainer.Width = 1;
                    gameContainer.Height = 1;
                    gameContainer.Margin = new Thickness(-5000, 0, 0, 0);
                    HeadlessOverlay.Visibility = Visibility.Visible;
                }
                else
                {
                    gameContainer.Width = double.NaN;
                    gameContainer.Height = double.NaN;
                    gameContainer.Margin = new Thickness(1, 0, 1, 1);
                    HeadlessOverlay.Visibility = Visibility.Collapsed;
                }
            });
        }
    }

    private void FlashChanged(GameContainerUserControl recipient, FlashChangedMessage<AxShockwaveFlash> message)
    {
        recipient.gameContainer.Child = message.Flash;
    }

    private void GameContainer_Loaded(object sender, RoutedEventArgs e)
    {
        _bot.Flash.FlashCall += LoadingFlash;
        _bot.Flash.InitializeFlash();
        Loaded -= GameContainer_Loaded;
    }

    private void LoadingFlash(string function, params object[] args)
    {
        if (function == "loaded")
        {
            LoadingBar.Visibility = Visibility.Hidden;
            gameContainer.Visibility = Visibility.Visible;
            _bot.Flash.FlashCall -= LoadingFlash;
            
            try
            {
                var flash = gameContainer.Child as AxShockwaveFlash;
                if (flash != null)
                {
                    flash.ScaleMode = 2;
                    flash.CtlScale = "ExactFit";
                }
                _bot.Flash.SetGameObject("stage.scaleMode", "exactFit");
            }
            catch { }
        }
    }
}