using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Dfs.Client.Desktop.ViewModels;
using Dfs.Fakes;

namespace Dfs.Client.Desktop;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var fakeClient = new FakeDfsClient();
            var mainVm = new MainViewModel(fakeClient);
            desktop.MainWindow = new MainWindow(mainVm);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
