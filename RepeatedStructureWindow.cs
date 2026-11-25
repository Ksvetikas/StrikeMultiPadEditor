
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

using Label = System.Windows.Controls.Label;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;
using Button = System.Windows.Controls.Button;
using Grid = System.Windows.Controls.Grid;
using RowDefinition = System.Windows.Controls.RowDefinition;
using ColumnDefinition = System.Windows.Controls.ColumnDefinition;
using StackPanel = System.Windows.Controls.StackPanel;
using Orientation = System.Windows.Controls.Orientation;
using ScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility;

using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using FontFamily = System.Windows.Media.FontFamily;

using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace StrikeMultiPadEditor;

public sealed class RepeatedStructureWindow : Window
{
    private readonly ComboBox _kitACombo;
    private readonly ComboBox _kitBCombo;
    private readonly TextBox _reportBox;

    public RepeatedStructureWindow(IEnumerable<KitFile> kits)
    {
        var kitList = kits.ToList();

        Title = "Repeated Structure Analysis";
        Width = 1050;
        Height = 750;
        MinWidth = 750;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(
            Color.FromRgb(26, 32, 41));

        var root = new Grid
        {
            Margin = new Thickness(16)
        };

        root.RowDefinitions.Add(
            new RowDefinition { Height = GridLength.Auto });

        root.RowDefinitions.Add(
            new RowDefinition { Height = GridLength.Auto });

        root.RowDefinitions.Add(
            new RowDefinition
            {
                Height = new GridLength(1, GridUnitType.Star)
            });

        var selectors = new Grid
        {
            Margin = new Thickness(0, 0, 0, 12)
        };

        selectors.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Auto)
            });

        selectors.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });

        selectors.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Auto)
            });

        selectors.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });

        var labelA = CreateLabel("KIT A");
        Grid.SetColumn(labelA, 0);
        selectors.Children.Add(labelA);

        _kitACombo = CreateComboBox(kitList);
        Grid.SetColumn(_kitACombo, 1);
        selectors.Children.Add(_kitACombo);

        var labelB = CreateLabel("KIT B");
        labelB.Margin = new Thickness(16, 5, 8, 5);
        Grid.SetColumn(labelB, 2);
        selectors.Children.Add(labelB);

        _kitBCombo = CreateComboBox(kitList);
        Grid.SetColumn(_kitBCombo, 3);
        selectors.Children.Add(_kitBCombo);

        _kitACombo.SelectedIndex = 0;
        _kitBCombo.SelectedIndex = kitList.Count > 1 ? 1 : 0;

        Grid.SetRow(selectors, 0);
        root.Children.Add(selectors);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 12)
        };

        var analyzeButton = new Button
        {
            Content = "ANALYZE STRUCTURES",
            Padding = new Thickness(18, 9, 18, 9),
            Margin = new Thickness(0, 0, 10, 0),
            Background = new SolidColorBrush(
                Color.FromRgb(224, 68, 68)),
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold
        };

        analyzeButton.Click += AnalyzeButton_Click;
        actions.Children.Add(analyzeButton);

        var clearButton = new Button
        {
            Content = "CLEAR REPORT",
            Padding = new Thickness(18, 9, 18, 9)
        };

        clearButton.Click += ClearButton_Click;
        actions.Children.Add(clearButton);

        Grid.SetRow(actions, 1);
        root.Children.Add(actions);

        _reportBox = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility =
                ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility =
                ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            Padding = new Thickness(12),
            Background = new SolidColorBrush(
                Color.FromRgb(18, 23, 30)),
            Foreground = new SolidColorBrush(
                Color.FromRgb(220, 225, 232)),
            BorderBrush = new SolidColorBrush(
                Color.FromRgb(55, 65, 80))
        };

        _reportBox.Text =
            "Select two kits and click ANALYZE STRUCTURES.";

        Grid.SetRow(_reportBox, 2);
        root.Children.Add(_reportBox);

        Content = root;
    }

    private static Label CreateLabel(string text)
    {
        return new Label
        {
            Content = text,
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 5, 8, 5)
        };
    }

    private static ComboBox CreateComboBox(List<KitFile> kits)
    {
        return new ComboBox
        {
            ItemsSource = kits,
            DisplayMemberPath = nameof(KitFile.FileName),
            MinWidth = 220,
            Height = 30,
            VerticalContentAlignment = VerticalAlignment.Center
        };
    }

    private void ClearButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        _reportBox.Clear();
    }

    private void AnalyzeButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_kitACombo.SelectedItem is not KitFile kitA ||
            _kitBCombo.SelectedItem is not KitFile kitB)
        {
            MessageBox.Show(
                this,
                "Please select both kits.",
                "Repeated Structure Analysis",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        if (ReferenceEquals(kitA, kitB))
        {
            MessageBox.Show(
                this,
                "Please select two different kits.",
                "Repeated Structure Analysis",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        try
        {
            _reportBox.Text =
                RepeatedStructureAnalyzer.CreateReport(
                    kitA.Data,
                    kitB.Data);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Analysis Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
