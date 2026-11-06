using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Skua.Shared.Avalonia.Services;

/// <summary>
/// Releases the mouse capture left over from the click that opened a blocking dialog. Avalonia raises Click
/// (menu items, buttons, their commands) during the pointer-release event and only releases the capture after the
/// handler returns. A dialog that blocks inside the handler would otherwise send its first click to the control that
/// opened it, such as a menu item that has already closed.
/// </summary>
public static class PointerCaptureReset
{
    private static IPointer? s_pointer;
    private static bool s_tracking;

    /// <summary>Remembers the pointer of each press, so <see cref="Release"/> can reach it. Call once at startup.</summary>
    public static void Track()
    {
        if (s_tracking)
            return;
        s_tracking = true;
        InputElement.PointerPressedEvent.AddClassHandler<TopLevel>(
            (_, e) => s_pointer = e.Pointer, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    /// <summary>Drops the current capture, if any. Call before showing a dialog that blocks the caller.</summary>
    public static void Release()
    {
        if (s_pointer?.Captured is not null)
            s_pointer.Capture(null);
    }
}
