using System.Windows;
using Lightshot.App.Theming;

namespace Lightshot.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ThemeService.Instance.Apply(ThemeService.Instance.CurrentPreference);
    }
}
