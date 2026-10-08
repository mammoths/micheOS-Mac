using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Miche.Mac.Services;

namespace Miche.Mac;

public sealed partial class App : Application
{
    public WorkspaceSession? Session { get; private set; }
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try
            {
                Session = new WorkspaceSession(Environment.GetEnvironmentVariable("MICHE_MAC_DATA_DIR") ?? WorkspaceStore.DefaultPath);
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                Session.QuitCompleted += () => desktop.Shutdown();
                desktop.ShutdownRequested += (_, e) => { if (!Session.IsQuitting && !Session.TryQuit()) e.Cancel = true; };
                desktop.MainWindow = Session.OpenHome();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;
                desktop.MainWindow = new Window { Title = "Miche · saved data", Width = 620, Height = 320,
                    Content = new TextBlock { Margin = new Avalonia.Thickness(32), TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        Text = "Miche couldn't safely open this workspace. Your saved file has not been reset. If Miche is already open, use that window.\n\n" + ex.Message + "\n\n" +
                            (Environment.GetEnvironmentVariable("MICHE_MAC_DATA_DIR") ?? WorkspaceStore.DefaultPath) } };
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}
