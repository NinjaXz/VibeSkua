using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Skua.Core.Interfaces;
using Skua.Plugin.Cosmetics.Services;
using Skua.Plugin.Cosmetics.Views;

namespace Skua.Plugin.Cosmetics;

public class CosmeticsPlugin : ISkuaPlugin
{
    public string Name => "Cosmetics";
    public string Author => "NinjaXz & Lord Exelot";
    public string Description => "Visually loads any cosmetic item without owning it and manages saved outfit loadouts.";
    public List<IOption>? Options => [];

    private IServiceProvider? _provider;
    private IPluginHelper? _helper;
    private IScriptInterface? _bot;
    private CosmeticsService? _service;
    private CosmeticsWindow? _window;

    public void Load(IServiceProvider provider, IPluginHelper helper)
    {
        _provider = provider;
        _helper = helper;

        IScriptInterface bot = _provider.GetRequiredService<IScriptInterface>();
        _bot = bot;
        _service = new CosmeticsService(bot);

        _helper.AddMenuButton("Cosmetics", ShowWindow);
    }

    public void Unload()
    {
        _helper?.RemoveMenuButton("Cosmetics");

        _service?.Dispose();

        Dispatcher.UIThread.Post(() =>
        {
            _window?.Close();
            _window = null;
        });
    }

    private void ShowWindow()
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (_provider is null || _service is null)
                    throw new InvalidOperationException("Cosmetics has not finished initializing.");

                if (_window is null || !_window.IsVisible)
                {
                    IScriptInterface bot = _provider.GetRequiredService<IScriptInterface>();
                    _window = new CosmeticsWindow(bot, _service);
                    _window.Closed += (_, _) => _window = null;
                }

                _window.Show();
                _window.Activate();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"Cosmetics window launch failed: {ex}");
                _bot?.Log($"[Cosmetics] Window launch failed: {ex}");

                _window = null;

                try
                {
                    Window errorWindow = new()
                    {
                        Title = "Cosmetics failed to open",
                        Width = 760,
                        Height = 420,
                        MinWidth = 520,
                        MinHeight = 280,
                        WindowStartupLocation = WindowStartupLocation.CenterScreen,
                        Content = new TextBox
                        {
                            Text = ex.ToString(),
                            IsReadOnly = true,
                            AcceptsReturn = true,
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                            Margin = new Avalonia.Thickness(12)
                        }
                    };

                    errorWindow.Show();
                }
                catch (Exception dialogException)
                {
                    System.Diagnostics.Trace.WriteLine($"Cosmetics error window failed: {dialogException}");
                    _bot?.Log($"[Cosmetics] Error window failed: {dialogException.Message}");
                }
            }
        });
    }
}
