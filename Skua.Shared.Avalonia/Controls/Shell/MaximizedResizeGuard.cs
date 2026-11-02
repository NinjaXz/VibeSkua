using Avalonia;
using Avalonia.Controls;
using System;
using System.Runtime.CompilerServices;

namespace Skua.Shared.Avalonia.Controls.Shell;

/// <summary>
/// Avalonia's Windows hit-testing keeps reporting resize edges for a maximized window that extends its client area
/// into the frame. This hook answers those edge hits as client area while the window is maximized.
/// </summary>
internal static class MaximizedResizeGuard
{
    private const uint WM_NCHITTEST = 0x0084;
    private const int HTCLIENT = 1;
    private const double EdgeBandDip = 10;

    private static readonly ConditionalWeakTable<Window, Win32Properties.CustomWndProcHookCallback> Hooks = new();

    public static void Attach(Window window)
    {
        if (!OperatingSystem.IsWindows() || Hooks.TryGetValue(window, out _))
            return;

        Win32Properties.CustomWndProcHookCallback hook = (IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            if (msg != WM_NCHITTEST || window.WindowState != WindowState.Maximized || !IsOnEdge(window, lParam))
                return IntPtr.Zero;

            handled = true;
            return (IntPtr)HTCLIENT;
        };

        Hooks.Add(window, hook);
        Win32Properties.AddWndProcHookCallback(window, hook);
    }

    public static void Detach(Window window)
    {
        if (!Hooks.TryGetValue(window, out var hook))
            return;

        Win32Properties.RemoveWndProcHookCallback(window, hook);
        Hooks.Remove(window);
    }

    private static bool IsOnEdge(Window window, IntPtr lParam)
    {
        long packed = lParam.ToInt64();
        int x = (short)(packed & 0xFFFF);
        int y = (short)((packed >> 16) & 0xFFFF);

        double scale = window.RenderScaling;
        Size size = window.FrameSize ?? window.ClientSize;
        PixelRect bounds = new(window.Position, new PixelSize((int)(size.Width * scale), (int)(size.Height * scale)));
        int band = (int)Math.Ceiling(EdgeBandDip * scale);

        return x < bounds.X + band || x >= bounds.Right - band
            || y < bounds.Y + band || y >= bounds.Bottom - band;
    }
}
