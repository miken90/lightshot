// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Windows;

namespace Lightshot.App.Views.Notices;

public partial class ErrorDialog : Window
{
    public ErrorDialog()
    {
        InitializeComponent();
    }

    public static void ShowNotice(Window? owner, string title, string message, string? details = null)
    {
        var dlg = new ErrorDialog
        {
            Owner = owner,
            Title = title
        };

        dlg.TitleTextBlock.Text = title;
        dlg.MessageTextBlock.Text = message;
        if (!string.IsNullOrWhiteSpace(details))
        {
            dlg.DetailsTextBlock.Text = details;
            dlg.DetailsTextBlock.Visibility = Visibility.Visible;
        }

        dlg.ShowDialog();
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
