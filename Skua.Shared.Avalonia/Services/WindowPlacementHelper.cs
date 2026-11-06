using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace Skua.Shared.Avalonia.Services;

public static class WindowPlacementHelper
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    public static Screen? GetTargetScreen(Window window)
    {
        return GetTargetScreen(window, out _);
    }

    public static Screen? GetTargetScreen(Window window, out PixelRect? hostBounds)
    {
        hostBounds = null;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // 1. Try host window handle (used in embedded child processes and main host window)
            try
            {
                IntPtr hostHwnd = Skua.Core.AppStartup.HotKeys.HostHwnd;
                if (hostHwnd != IntPtr.Zero && IsWindow(hostHwnd) && !IsIconic(hostHwnd) && GetWindowRect(hostHwnd, out RECT hostRect))
                {
                    if (hostRect.Width > 100 && hostRect.Height > 100)
                    {
                        PixelPoint center = new(hostRect.Left + hostRect.Width / 2, hostRect.Top + hostRect.Height / 2);
                        var screen = window.Screens?.ScreenFromPoint(center);
                        if (screen != null)
                        {
                            hostBounds = new PixelRect(hostRect.Left, hostRect.Top, hostRect.Width, hostRect.Height);
                            return screen;
                        }
                    }
                }
            }
            catch { }
        }

        // 2. Try desktop MainWindow if visible and on a real screen (host process or standalone mode)
        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                desktop.MainWindow != null &&
                desktop.MainWindow.IsVisible &&
                desktop.MainWindow.Position.X > -10000 &&
                desktop.MainWindow.Bounds.Width > 100)
            {
                int mwX = desktop.MainWindow.Position.X;
                int mwY = desktop.MainWindow.Position.Y;
                int mwW = (int)desktop.MainWindow.Bounds.Width;
                int mwH = (int)desktop.MainWindow.Bounds.Height;
                PixelPoint center = new(mwX + mwW / 2, mwY + mwH / 2);
                var screen = window.Screens?.ScreenFromPoint(center);
                if (screen != null)
                {
                    hostBounds = new PixelRect(mwX, mwY, mwW, mwH);
                    return screen;
                }
            }
        }
        catch { }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // 3. Try foreground window (if user was actively clicking a Skua window)
            try
            {
                IntPtr fg = GetForegroundWindow();
                if (fg != IntPtr.Zero && IsWindow(fg) && !IsIconic(fg) && GetWindowRect(fg, out RECT fgRect))
                {
                    if (fgRect.Width > 100 && fgRect.Height > 100)
                    {
                        PixelPoint center = new(fgRect.Left + fgRect.Width / 2, fgRect.Top + fgRect.Height / 2);
                        var screen = window.Screens?.ScreenFromPoint(center);
                        if (screen != null)
                        {
                            hostBounds = new PixelRect(fgRect.Left, fgRect.Top, fgRect.Width, fgRect.Height);
                            return screen;
                        }
                    }
                }
            }
            catch { }

            // 4. Try current mouse cursor position
            try
            {
                if (GetCursorPos(out POINT cursorPt))
                {
                    PixelPoint pt = new(cursorPt.X, cursorPt.Y);
                    var screen = window.Screens?.ScreenFromPoint(pt);
                    if (screen != null)
                        return screen;
                }
            }
            catch { }
        }

        // 5. Fallback to Primary screen or first screen
        return window.Screens?.Primary ?? window.Screens?.All?.FirstOrDefault();
    }

    public static PixelPoint CalculatePosition(Screen screen, PixelRect? hostBounds, int width, int height)
    {
        int posX;
        int posY;

        if (hostBounds.HasValue && hostBounds.Value.Width > 0 && hostBounds.Value.Height > 0)
        {
            var hb = hostBounds.Value;
            posX = hb.X + (hb.Width - width) / 2;
            posY = hb.Y + (hb.Height - height) / 2;
        }
        else
        {
            posX = screen.WorkingArea.X + (screen.WorkingArea.Width - width) / 2;
            posY = screen.WorkingArea.Y + (screen.WorkingArea.Height - height) / 2;
        }

        // Clamp inside screen working area
        int minX = screen.WorkingArea.X;
        int maxX = Math.Max(minX, screen.WorkingArea.Right - width);
        int minY = screen.WorkingArea.Y;
        int maxY = Math.Max(minY, screen.WorkingArea.Bottom - height);

        posX = Math.Clamp(posX, minX, maxX);
        posY = Math.Clamp(posY, minY, maxY);

        return new PixelPoint(posX, posY);
    }

    /// <summary>
    /// Caps the window's size at the working area of the screen it will open on, so a window that sizes to its
    /// content (or asks for more than fits) never runs under the taskbar or off the screen.
    /// </summary>
    public static void FitToWorkingArea(Window window)
    {
        Screen? screen = GetTargetScreen(window);
        if (screen is null || screen.Scaling <= 0)
            return;

        double maxWidth = screen.WorkingArea.Width / screen.Scaling;
        double maxHeight = screen.WorkingArea.Height / screen.Scaling;
        window.MaxWidth = Math.Min(window.MaxWidth, maxWidth);
        window.MaxHeight = Math.Min(window.MaxHeight, maxHeight);
        if (window.MinWidth > window.MaxWidth)
            window.MinWidth = window.MaxWidth;
        if (window.MinHeight > window.MaxHeight)
            window.MinHeight = window.MaxHeight;
    }

    public static void CenterOnActiveScreen(Window window)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;

        double targetW = window.Width;
        if (double.IsNaN(targetW) || targetW <= 0)
            targetW = window.MinWidth > 0 ? window.MinWidth : 500;

        double targetH = window.Height;
        if (double.IsNaN(targetH) || targetH <= 0)
            targetH = window.MinHeight > 0 ? window.MinHeight : 400;

        var screen = GetTargetScreen(window, out var hostBounds);
        if (screen != null)
        {
            window.Position = CalculatePosition(screen, hostBounds, (int)targetW, (int)targetH);
        }

        // Refine positioning once measured on Opened
        window.Opened += (s, e) =>
        {
            int actualW = (int)window.Bounds.Width;
            int actualH = (int)window.Bounds.Height;
            if (actualW > 0 && actualH > 0 && (Math.Abs(actualW - (int)targetW) > 10 || Math.Abs(actualH - (int)targetH) > 10))
            {
                var sc = GetTargetScreen(window, out var hb);
                if (sc != null)
                {
                    window.Position = CalculatePosition(sc, hb, actualW, actualH);
                }
            }
        };
    }
}
