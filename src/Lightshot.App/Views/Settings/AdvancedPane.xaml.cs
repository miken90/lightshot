using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Lightshot.Platform.Windows.Windows;

namespace Lightshot.App.Views.Settings;

public partial class AdvancedPane : UserControl
{
    public AdvancedPane()
    {
        InitializeComponent();
    }

    private void OnFlushAndOpenLogsClick(object sender, RoutedEventArgs e)
    {
        Trace.Flush();
        try
        {
            var logsDir = AppPaths.Logs;
            Directory.CreateDirectory(logsDir);
            if (Directory.Exists(logsDir))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{logsDir}\"",
                    UseShellExecute = true
                });
            }
        }
        catch
        {
            // Suppress Explorer launch failure
        }
    }
}
