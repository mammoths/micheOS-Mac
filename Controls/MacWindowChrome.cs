using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.VisualTree;

namespace Miche.Mac.Controls;

public static class MacWindowChrome
{
    public static void Apply(Window window)
    {
        // On pinned macOS 11.3.22, None drops NSResizable and BeginResizeDrag is
        // a no-op. BorderOnly retains native resize/shadow while NoChrome hides
        // the title region and traffic lights. Native tabbing is already disabled.
        window.SystemDecorations = SystemDecorations.BorderOnly;
        window.ExtendClientAreaToDecorationsHint = true;
        window.ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome;
        window.ExtendClientAreaTitleBarHeightHint = 0;
        window.CanResize = true;
        window.KeyDown += (_,e) => {
            if (e.Key == Key.W && e.KeyModifiers == KeyModifiers.Meta) { window.Close(); e.Handled = true; }
            else if (e.Key == Key.F && e.KeyModifiers == (KeyModifiers.Meta | KeyModifiers.Control))
            { window.WindowState = window.WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen; e.Handled = true; }
            else if (window is not MainWindow && e.Key == Key.Escape && window.WindowState == WindowState.FullScreen)
            { window.WindowState = WindowState.Normal; e.Handled = true; }
        };
    }
    public static void Zoom(Window window) => window.WindowState = window.WindowState == WindowState.Normal
        ? WindowState.Maximized : WindowState.Normal;
    public static void HeaderPressed(Window window, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(window).Properties.IsLeftButtonPressed) return;
        if (e.Source is Control control && (control is Button or TextBox || control.GetVisualAncestors().Any(c => c is Button or TextBox))) return;
        if (e.ClickCount == 2) Zoom(window);
        else if (window.WindowState != WindowState.FullScreen) window.BeginMoveDrag(e);
        e.Handled = true;
    }
}
