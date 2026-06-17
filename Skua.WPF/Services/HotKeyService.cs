using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using Skua.Core.Models;
using Skua.Core.Utils;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Input;

namespace Skua.WPF.Services;

public class HotKeyService : IHotKeyService, IDisposable
{
    public HotKeyService(Dictionary<string, IRelayCommand> hotKeys, ISettingsService settingsService, IDecamelizer decamelizer)
    {
        _hotKeys = hotKeys;
        _settingsService = settingsService;
        _decamelizer = decamelizer;
        _registeredBindings = new List<KeyBinding>();
    }

    private readonly Dictionary<string, IRelayCommand> _hotKeys;
    private readonly ISettingsService _settingsService;
    private readonly IDecamelizer _decamelizer;
    private readonly List<KeyBinding> _registeredBindings;

    public void Reload()
    {
        // Clear previous bindings
        ClearRegisteredBindings();

        if (Application.Current?.MainWindow == null)
            return;

        StringCollection? hotkeys = _settingsService.Get<StringCollection>("HotKeys");
        bool isFirstRun = hotkeys == null;
        hotkeys ??= new StringCollection();

        if (isFirstRun)
        {
            EnsureAllBindingsExist(hotkeys);
            _settingsService.Set("HotKeys", hotkeys);
        }

        foreach (string? hk in hotkeys)
        {
            if (string.IsNullOrEmpty(hk))
                continue;

            string[] split = hk.Split('|');
            if (_hotKeys.ContainsKey(split[0]))
            {
                string gesture = split[1].Trim();
                if (split.Length < 2 || string.IsNullOrWhiteSpace(gesture) || 
                    gesture.Equals("Unassigned", StringComparison.OrdinalIgnoreCase) || 
                    gesture.Equals("Failed to bind", StringComparison.OrdinalIgnoreCase) || 
                    gesture.Equals("Failed to bind.", StringComparison.OrdinalIgnoreCase))
                    continue;

                KeyBinding? kb = ParseToKeyBinding(gesture);
                if (kb is null)
                {
                    StrongReferenceMessenger.Default.Send<HotKeyErrorMessage>(new(split[0]));
                    continue;
                }
                kb.Command = _hotKeys[split[0]];
                Application.Current.MainWindow.InputBindings.Add(kb);
                _registeredBindings.Add(kb);
            }
        }
    }

    private void ClearRegisteredBindings()
    {
        if (Application.Current?.MainWindow != null)
        {
            foreach (KeyBinding binding in _registeredBindings)
            {
                Application.Current.MainWindow.InputBindings.Remove(binding);
            }
        }
        _registeredBindings.Clear();
    }

    public List<T> GetHotKeys<T>()
        where T : IHotKey, new()
    {
        StringCollection? hotkeys = _settingsService.Get<StringCollection>("HotKeys");
        bool isFirstRun = hotkeys == null;
        hotkeys ??= new StringCollection();

        if (isFirstRun)
        {
            EnsureAllBindingsExist(hotkeys);
            _settingsService.Set("HotKeys", hotkeys);
        }

        List<T> parsed = new();
        foreach (string hk in hotkeys)
        {
            if (string.IsNullOrEmpty(hk))
                continue;
            string[] split = hk.Split('|');
            string gesture = split.Length > 1 ? split[1] : string.Empty;
            parsed.Add(new() { 
                Binding = split[0], 
                Title = Skua.Core.AppStartup.HotKeys.GetFormattedTitle(split[0]), 
                Description = Skua.Core.AppStartup.HotKeys.GetDescription(split[0]),
                KeyGesture = gesture 
            });
        }
        return parsed;
    }

    public HotKey? ParseToHotKey(string keyGesture)
    {
        KeyBinding? kb = ParseToKeyBinding(keyGesture);
        return kb is null
            ? null
            : new HotKey(kb.Key.ToString(), kb.Modifiers.HasFlag(ModifierKeys.Control), kb.Modifiers.HasFlag(ModifierKeys.Alt), kb.Modifiers.HasFlag(ModifierKeys.Shift));
    }

    private KeyBinding? ParseToKeyBinding(string keyGesture)
    {
        string ksc = keyGesture.ToLower();
        KeyBinding kb = new();

        if (ksc.Contains("alt"))
            kb.Modifiers = ModifierKeys.Alt;
        if (ksc.Contains("shift"))
            kb.Modifiers |= ModifierKeys.Shift;
        if (ksc.Contains("ctrl") || ksc.Contains("ctl"))
            kb.Modifiers |= ModifierKeys.Control;

        string key =
            ksc.Replace("+", string.Empty)
               .Replace("alt", string.Empty)
               .Replace("shift", string.Empty)
               .Replace("ctrl", string.Empty)
               .Replace("ctl", string.Empty)
               .Trim();

        key = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(key);
        if (!string.IsNullOrEmpty(key))
        {
            KeyConverter keyConverter = new();
            object? convertedKey;
            try
            {
                convertedKey = keyConverter.ConvertFromString(key);
            }
            catch (NotSupportedException)
            {
                return null;
            }
            catch (FormatException)
            {
                return null;
            }
            catch (ArgumentException)
            {
                return null;
            }

            if (convertedKey is not Key parsedKey)
                return null;

            kb.Key = parsedKey;
        }

        return kb.Key == Key.None ? null : kb;
    }

    private static readonly string[] DefaultHotKeys = new[] 
    { 
        "ToggleScript", "LoadScript", "OpenBank", "OpenConsole", "ToggleAutoAttack", "ToggleAutoHunt", "ToggleLagKiller" 
    };

    private void EnsureAllBindingsExist(StringCollection hotkeys)
    {
        HashSet<string> existing = new();
        HashSet<string> usedGestures = new(StringComparer.OrdinalIgnoreCase);
        foreach (string hk in hotkeys)
        {
            if (string.IsNullOrWhiteSpace(hk))
                continue;
            string[] split = hk.Split('|');
            if (split.Length > 0 && !string.IsNullOrWhiteSpace(split[0]))
                existing.Add(split[0]);
            if (split.Length > 1 && !string.IsNullOrWhiteSpace(split[1]))
                usedGestures.Add(split[1]);
        }

        foreach (string key in DefaultHotKeys)
        {
            if (existing.Contains(key))
                continue;

            string gesture = string.Empty;
            if (string.Equals(key, "ToggleLagKiller", StringComparison.Ordinal) && !usedGestures.Contains("F6"))
                gesture = "F6";

            hotkeys.Add($"{key}|{gesture}");
        }
    }

    private bool _disposed = false;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                ClearRegisteredBindings();
                _hotKeys.Clear();
            }

            _disposed = true;
        }
    }

    ~HotKeyService()
    {
        Dispose(false);
    }
}