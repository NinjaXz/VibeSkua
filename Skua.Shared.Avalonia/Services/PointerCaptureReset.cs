using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System.Collections.Generic;

namespace Skua.Shared.Avalonia.Services;

/// <summary>
/// Releases the mouse capture left over from the click that opened a blocking dialog. Avalonia raises Click
/// (menu items, buttons, their commands) during the pointer-release event and only releases the capture after the
/// handler returns. A dialog that blocks inside the handler would otherwise send its first click to the control that
/// opened it, such as a menu item that has already closed.
/// </summary>
public static class PointerCaptureReset
{
    // Pointers whose release event is being routed right now; a dialog opened from that event's handler blocks
    // inside it. A list, not a set: a release inside the dialog's nested loop can add the same pointer again.
    private static readonly List<IPointer> s_releasing = [];
    private static bool s_tracking;

    /// <summary>Starts following pointer releases, so <see cref="Release"/> knows which pointer opened a dialog.
    /// Call once at startup.</summary>
    public static void Track()
    {
        if (s_tracking)
            return;
        s_tracking = true;

        // Every release route starts and ends at its TopLevel (a window or popup): tunnel runs before the control's
        // own handlers raise Click, bubble after they return.
        InputElement.PointerReleasedEvent.AddClassHandler<TopLevel>(
            (_, e) => s_releasing.Add(e.Pointer), RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.PointerReleasedEvent.AddClassHandler<TopLevel>(
            (_, e) => s_releasing.Remove(e.Pointer), RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <summary>Drops the capture of the pointers whose release is opening a blocking dialog, and no others.
    /// Call before showing a dialog that blocks the caller.</summary>
    public static void Release()
    {
        foreach (IPointer pointer in s_releasing.ToArray())
        {
            if (pointer.Captured is not null)
                pointer.Capture(null);
        }
    }
}
