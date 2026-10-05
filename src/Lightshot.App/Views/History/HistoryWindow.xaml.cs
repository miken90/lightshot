// Ported from LightshotKit/Sources/LightshotKit/HistoryStore.swift and App/Sources/HistoryView.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Lightshot.Core;

namespace Lightshot.App.Views.History;

public partial class HistoryWindow : Window
{
    private HistoryViewModel? _viewModel;

    public HistoryViewModel? ViewModel => _viewModel;

    public HistoryWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Activated += (s, e) => UpdateVisibility();
    }

    public HistoryWindow(HistoryViewModel viewModel) : this()
    {
        SetViewModel(viewModel);
    }

    public void SetViewModel(HistoryViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = _viewModel;
        _viewModel.PropertyChanged += (s, e) => UpdateVisibility();
        UpdateVisibility();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        if (_viewModel == null) return;
        bool isEmpty = _viewModel.IsEmpty;
        EmptyStatePanel.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
        CardsScrollViewer.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
        ClearAllButton.IsEnabled = !isEmpty;
    }

    private void OnItemMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && sender is FrameworkElement element && element.DataContext is CaptureRecord record)
        {
            _viewModel?.Reopen(record);
        }
    }

    private void OnItemOpenClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is CaptureRecord record)
        {
            _viewModel?.Reopen(record);
        }
    }

    private void OnItemCopyClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is CaptureRecord record)
        {
            _viewModel?.Copy(record);
        }
    }

    private void OnItemRevealClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is CaptureRecord record)
        {
            _viewModel?.Reveal(record);
        }
    }

    private void OnItemDeleteClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is CaptureRecord record)
        {
            _viewModel?.Delete(record);
        }
    }

    private void OnClearAllClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null || _viewModel.IsEmpty) return;

        var result = MessageBox.Show(
            this,
            "Clear all captures? This permanently deletes every capture in your history.",
            "Clear History",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.OK)
        {
            _viewModel.ClearAll();
        }
    }
}
