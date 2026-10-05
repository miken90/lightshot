using System.Reflection;
using System.Windows.Controls;

namespace Lightshot.App.Views.Settings;

public partial class AboutPane : UserControl
{
    public AboutPane()
    {
        InitializeComponent();
        TxtVersion.Text = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
    }
}
