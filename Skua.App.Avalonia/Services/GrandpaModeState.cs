using System;

namespace Skua.App.Avalonia.Services;

/// <summary>
/// Publishes the persisted accessibility preset to log views so it can update immediately.
/// </summary>
internal static class GrandpaModeState
{
    public static event Action<bool>? Changed;
    public static bool Enabled { get; private set; }

    public static void Set(bool enabled)
    {
        if (Enabled == enabled)
            return;
        Enabled = enabled;
        Changed?.Invoke(enabled);
    }
}
