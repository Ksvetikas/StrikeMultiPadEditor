
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

using ComboBox = System.Windows.Controls.ComboBox;
using DataGrid = System.Windows.Controls.DataGrid;
using Button = System.Windows.Controls.Button;
using Orientation = System.Windows.Controls.Orientation;
using Binding = System.Windows.Data.Binding;
using MessageBox = System.Windows.MessageBox;

namespace StrikeMultiPadEditor;

public sealed class KitComparisonWindow : Window
{
    private readonly List<KitFile> _kits;

    private readonly ComboBox _kitABox = new();
    private readonly ComboBox _kitBBox = new();
    private readonly ComboBox _viewBox = new();

    private readonly DataGrid _differenceGrid = new();
    private readonly TextBlock _summaryText = new();

    private KitComparisonResult? _lastResult;
    private KitFile? _lastKitA;
    private KitFile? _lastKitB;

    public KitComparisonWindow(IEnumerable<KitFile> kits)
    {
        _kits = kits.ToList();

        Title = "Strike MultiPad - Kit Comparison";
        Width = 1150;
        Height = 750;
        MinWidth = 850;
        MinHeight = 550;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = System.Windows.Media.Brushes.White;

        BuildInterface();
    }

    private void BuildInterface()
    {
        var root = new Grid
        {
            Margin = new Thickness(16)
        };

        root.RowDefinitions.Add(
            new RowDefinition { Height = GridLength.Auto });

        root.RowDefinitions.Add(
            new RowDefinition { Height = GridLength.Auto });

        root.RowDefinitions.Add(
            new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var selectorPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 12)
        };

        _kitABox.Width = 240;
        _kitABox.Margin = new Thickness(0, 0, 12, 0);
        _kitABox.ItemsSource = _kits;
        _kitABox.DisplayMemberPath = "FileName";

        _kitBBox.Width = 240;
        _kitBBox.Margin = new Thickness(0, 0, 12, 0);
        _kitBBox.ItemsSource = _kits;
        _kitBBox.DisplayMemberPath = "FileName";

        if (_kits.Count > 0)
            _kitABox.SelectedIndex = 0;

        if (_kits.Count > 1)
            _kitBBox.SelectedIndex = 1;

        var compareButton = new Button
        {
            Content = "COMPARE KITS",
            Padding = new Thickness(18, 8, 18, 8),
            FontWeight = FontWeights.Bold
        };

        compareButton.Click += CompareButton_Click;

        selectorPanel.Children.Add(new TextBlock
        {
            Text = "KIT A",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });

        selectorPanel.Children.Add(_kitABox);

        selectorPanel.Children.Add(new TextBlock
        {
            Text = "KIT B",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });

        selectorPanel.Children.Add(_kitBBox);
        selectorPanel.Children.Add(compareButton);

        Grid.SetRow(selectorPanel, 0);
        root.Children.Add(selectorPanel);

        var summaryPanel = new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 12)
        };

        _summaryText.Text =
            "Choose two kits and click COMPARE KITS.";

        _summaryText.FontSize = 14;
        _summaryText.FontWeight = FontWeights.SemiBold;
        _summaryText.Margin = new Thickness(0, 0, 0, 8);

        summaryPanel.Children.Add(_summaryText);

        var viewPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal
        };

        viewPanel.Children.Add(new TextBlock
        {
            Text = "DISPLAY:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });

        _viewBox.Width = 270;
        _viewBox.Items.Add("KITI RECORD DIFFERENCES");
        _viewBox.Items.Add("COMPLETE KIT FILE DIFFERENCES");
        _viewBox.Items.Add("BYTE CONTEXT (8 BYTES EACH SIDE)");
        _viewBox.SelectedIndex = 0;
        _viewBox.SelectionChanged += ViewBox_SelectionChanged;

        viewPanel.Children.Add(_viewBox);
        summaryPanel.Children.Add(viewPanel);

        Grid.SetRow(summaryPanel, 1);
        root.Children.Add(summaryPanel);

        _differenceGrid.AutoGenerateColumns = false;
        _differenceGrid.IsReadOnly = true;
        _differenceGrid.CanUserAddRows = false;
        _differenceGrid.HeadersVisibility =
            DataGridHeadersVisibility.Column;

        Grid.SetRow(_differenceGrid, 2);
        root.Children.Add(_differenceGrid);

        Content = root;

        ConfigureColumns(0);
    }

    private void ConfigureColumns(int view)
    {
        _differenceGrid.Columns.Clear();

        if (view == 2)
        {
            _differenceGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "CHANGED OFFSET",
                Binding = new Binding(nameof(DifferenceRow.RelativeOffset)),
                Width = 130
            });

            _differenceGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "KIT A: SURROUNDING BYTES",
                Binding = new Binding(nameof(DifferenceRow.ContextA)),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star)
            });

            _differenceGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "KIT B: SURROUNDING BYTES",
                Binding = new Binding(nameof(DifferenceRow.ContextB)),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star)
            });

            return;
        }

        if (view == 1)
        {
            _differenceGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "FILE OFFSET",
                Binding = new Binding(nameof(DifferenceRow.RelativeOffset)),
                Width = 150
            });
        }
        else
        {
            _differenceGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "KITI RECORD",
                Binding = new Binding(nameof(DifferenceRow.Record)),
                Width = 100
            });

            _differenceGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "RELATIVE OFFSET",
                Binding = new Binding(nameof(DifferenceRow.RelativeOffset)),
                Width = 130
            });

            _differenceGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "KIT A OFFSET",
                Binding = new Binding(nameof(DifferenceRow.OffsetA)),
                Width = 130
            });
        }

        _differenceGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "KIT A BYTE",
            Binding = new Binding(nameof(DifferenceRow.ValueA)),
            Width = 110
        });

        if (view == 1)
        {
            _differenceGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "KIT B OFFSET",
                Binding = new Binding(nameof(DifferenceRow.OffsetB)),
                Width = 130
            });
        }
        else
        {
            _differenceGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "KIT B OFFSET",
                Binding = new Binding(nameof(DifferenceRow.OffsetB)),
                Width = 130
            });
        }

        _differenceGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "KIT B BYTE",
            Binding = new Binding(nameof(DifferenceRow.ValueB)),
            Width = 110
        });
    }

    private void CompareButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_kitABox.SelectedItem is not KitFile kitA ||
            _kitBBox.SelectedItem is not KitFile kitB)
        {
            MessageBox.Show(
                "Please select two kits.",
                "Kit Comparison");

            return;
        }

        if (ReferenceEquals(kitA, kitB))
        {
            MessageBox.Show(
                "Please select two different kits.",
                "Kit Comparison");

            return;
        }

        try
        {
            _lastKitA = kitA;
            _lastKitB = kitB;

            _lastResult = KitComparison.Compare(kitA, kitB);

            DisplaySelectedComparison();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Comparison Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ViewBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_lastResult != null)
            DisplaySelectedComparison();
    }

    private void DisplaySelectedComparison()
    {
        if (_lastResult == null ||
            _lastKitA == null ||
            _lastKitB == null)
        {
            return;
        }

        int view = _viewBox.SelectedIndex;

        ConfigureColumns(view);

        var rows = new List<DifferenceRow>();

        if (view == 2)
        {
            // Show eight bytes before and after each changed byte.
            foreach (var difference in _lastResult.FullFileDifferences)
            {
                int changedOffset = difference.RelativeOffset;

                int start = Math.Max(0, changedOffset - 8);

                int endA = Math.Min(
                    _lastKitA.Data.Length - 1,
                    changedOffset + 8);

                int endB = Math.Min(
                    _lastKitB.Data.Length - 1,
                    changedOffset + 8);

                int end = Math.Min(endA, endB);

                rows.Add(new DifferenceRow
                {
                    RelativeOffset = $"0x{changedOffset:X4}",

                    ContextA = FormatContext(
                        _lastKitA.Data,
                        start,
                        end,
                        changedOffset),

                    ContextB = FormatContext(
                        _lastKitB.Data,
                        start,
                        end,
                        changedOffset)
                });
            }

            _summaryText.Text =
                $"BYTE CONTEXT: Showing surrounding bytes for " +
                $"{_lastResult.FullFileDifferences.Count:N0} changed positions. " +
                "The changed byte is marked with [brackets].";
        }
        else if (view == 1)
        {
            foreach (var difference in _lastResult.FullFileDifferences)
            {
                rows.Add(new DifferenceRow
                {
                    RelativeOffset =
                        $"0x{difference.RelativeOffset:X4}",

                    ValueA = difference.ValueA.ToString("X2"),

                    OffsetB =
                        $"0x{difference.RelativeOffset:X4}",

                    ValueB = difference.ValueB.ToString("X2")
                });
            }

            _summaryText.Text =
                $"COMPLETE FILE: Kit A = {_lastResult.FileLengthA:N0} bytes | " +
                $"Kit B = {_lastResult.FileLengthB:N0} bytes | " +
                $"Different byte positions = " +
                $"{_lastResult.FullFileDifferences.Count:N0}";

            if (_lastResult.FileLengthA != _lastResult.FileLengthB)
            {
                _summaryText.Text +=
                    $" | File size difference = " +
                    $"{Math.Abs(_lastResult.FileLengthA - _lastResult.FileLengthB):N0} bytes";
            }

            if (rows.Count == 0 &&
                _lastResult.FileLengthA == _lastResult.FileLengthB)
            {
                _summaryText.Text +=
                    " | COMPLETE FILES ARE IDENTICAL.";
            }
        }
        else
        {
            foreach (var record in _lastResult.Records)
            {
                foreach (var difference in record.Differences)
                {
                    rows.Add(new DifferenceRow
                    {
                        Record = $"#{record.RecordIndex:D2}",

                        RelativeOffset =
                            $"0x{difference.RelativeOffset:X3}",

                        OffsetA =
                            $"0x{record.OffsetA + difference.RelativeOffset:X4}",

                        ValueA = difference.ValueA.ToString("X2"),

                        OffsetB =
                            $"0x{record.OffsetB + difference.RelativeOffset:X4}",

                        ValueB = difference.ValueB.ToString("X2")
                    });
                }
            }

            _summaryText.Text =
                $"KITI RECORDS: Kit A = {_lastResult.RecordCountA} | " +
                $"Kit B = {_lastResult.RecordCountB} | " +
                $"Compared = {_lastResult.Records.Count} | " +
                $"Different records = {_lastResult.DifferentRecordCount} | " +
                $"Changed bytes = {_lastResult.TotalDifferences}";

            if (rows.Count == 0)
                _summaryText.Text += " | No KITI differences found.";
        }

        _differenceGrid.ItemsSource = rows;
    }

    private static string FormatContext(
        byte[] data,
        int start,
        int end,
        int changedOffset)
    {
        var parts = new List<string>();

        for (int offset = start; offset <= end; offset++)
        {
            string value = data[offset].ToString("X2");

            if (offset == changedOffset)
                value = $"[{value}]";

            parts.Add(value);
        }

        return string.Join(" ", parts);
    }

    private sealed class DifferenceRow
    {
        public string Record { get; init; } = "";
        public string RelativeOffset { get; init; } = "";
        public string OffsetA { get; init; } = "";
        public string ValueA { get; init; } = "";
        public string OffsetB { get; init; } = "";
        public string ValueB { get; init; } = "";
        public string ContextA { get; init; } = "";
        public string ContextB { get; init; } = "";
    }
}
