using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Miche.Mac.Models;
using Miche.Mac.Controls;
using Miche.Mac.Services;

namespace Miche.Mac;

public sealed class WidgetWindow : Window
{
    private readonly WorkspaceSession _session;
    private bool _transferring;
    public Guid WidgetId { get; }
    public WidgetWindow(WorkspaceSession session, Guid id, string niche, WindowGeometry? saved)
    {
        MacWindowChrome.Apply(this);
        _session = session; WidgetId = id;
        Title = "Dump · " + niche;
        Width = 540; Height = 340; MinWidth = 320; MinHeight = 220;
        Background = Brush.Parse("#2C2033");
        WindowStartupLocation = saved is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.Manual;
        // Unowned: closing home must not close a floating editor.
        Opened += (_, _) => {
            if (saved is null) return;
            var screen = Screens.ScreenFromPoint(new PixelPoint(saved.X, saved.Y)) ?? Screens.Primary;
            var area = screen?.WorkingArea;
            var scaling = Math.Max(.1, RenderScaling);
            Width = Math.Max(MinWidth, Math.Min(saved.Width, (area?.Width ?? 1600) / scaling));
            Height = Math.Max(MinHeight, Math.Min(saved.Height, (area?.Height ?? 1000) / scaling));
            var x = saved.X; var y = saved.Y;
            if (area is not null)
            {
                x = Math.Clamp(x, area.Value.X, Math.Max(area.Value.X, area.Value.Right - (int)(Width * scaling)));
                y = Math.Clamp(y, area.Value.Y, Math.Max(area.Value.Y, area.Value.Bottom - (int)(Height * scaling)));
            }
            Position = new PixelPoint(x, y);
        };
        Closing += (_, e) => {
            if (_transferring || _session.IsQuitting) return;
            e.Cancel = true;
            Dispatcher.UIThread.Post(() => _session.Dock(WidgetId));
        };
        KeyDown += (_, e) => {
            if (e.Key == Key.Q && e.KeyModifiers.HasFlag(KeyModifiers.Meta)) { _session.TryQuit(); e.Handled = true; }
        };
    }
    public WindowGeometry Geometry() => new() { X = Position.X, Y = Position.Y, Width = Width, Height = Height };
    public void CloseForTransfer() { _transferring = true; Close(); }
}
