using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Skua.Core.Interfaces;

namespace Skua.Plugin.Omniscope;

/// <summary>
/// Hosts the OMNISCOPE live game intelligence workspace.
/// </summary>
public sealed class OmniscopePlugin : ISkuaPlugin
{
    private IServiceProvider? _provider;
    private IPluginHelper? _helper;
    private OmniscopeWindow? _window;

    public string Name => "OMNISCOPE";
    public string Author => "VibeSkua";
    public string Description => "Live game-state intelligence and world inspection.";
    public List<IOption>? Options => [];

    /// <summary>
    /// Registers the OMNISCOPE launcher with the VibeSkua plugin menu.
    /// </summary>
    public void Load(IServiceProvider provider, IPluginHelper helper)
    {
        _provider = provider;
        _helper = helper;
        _helper.AddMenuButton("OMNISCOPE", ShowWindow);
    }

    /// <summary>
    /// Releases the workspace and removes its menu entry.
    /// </summary>
    public void Unload()
    {
        _helper?.RemoveMenuButton("OMNISCOPE");
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
                if (_provider is null)
                    return;

                if (_window is null || !_window.IsVisible)
                {
                    IScriptInterface bot = _provider.GetRequiredService<IScriptInterface>();
                    _window = new OmniscopeWindow(bot);
                    _window.Closed += (_, _) => _window = null;
                }

                _window.Show();
                _window.Activate();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"OMNISCOPE window launch failed: {ex}");
                _window = null;

                try
                {
                    Window errorWindow = new()
                    {
                        Title = "OMNISCOPE failed to open",
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
                    System.Diagnostics.Trace.WriteLine($"OMNISCOPE error window failed: {dialogException}");
                }
            }
        });
    }
}