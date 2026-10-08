using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Miche.Mac.Controls;
public partial class WindowButtons : UserControl
{
    public WindowButtons() => InitializeComponent();
    private Window? Owner => TopLevel.GetTopLevel(this) as Window;
    private void Minimize(object? sender, RoutedEventArgs e) { if (Owner is { } w) w.WindowState = WindowState.Minimized; }
    private void Expand(object? sender, RoutedEventArgs e) { if (Owner is { } w) MacWindowChrome.Zoom(w); }
    private void CloseWindow(object? sender, RoutedEventArgs e) => Owner?.Close();
}
