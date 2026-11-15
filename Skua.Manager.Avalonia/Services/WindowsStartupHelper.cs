using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Skua.Manager.Avalonia.Services;

public static class WindowsStartupHelper
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "VibeSkuaManager";

    public static bool IsStartupEnabled()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            return key?.GetValue(ValueName) != null;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[WindowsStartupHelper] Failed to read startup registry key: {ex.Message}");
            return false;
        }
    }

    public static bool SetStartupEnabled(bool enable)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
            if (key == null)
                return false;

            if (enable)
            {
                string exePath = Environment.ProcessPath
                    ?? Process.GetCurrentProcess().MainModule?.FileName
                    ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
                {
                    key.SetValue(ValueName, $"\"{exePath}\" --minimized");
                    return true;
                }
                return false;
            }
            else
            {
                key.DeleteValue(ValueName, false);
                return true;
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[WindowsStartupHelper] Failed to modify startup registry key: {ex.Message}");
            return false;
        }
    }
}
