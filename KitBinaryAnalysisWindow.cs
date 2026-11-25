
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace StrikeMultiPadEditor;

public sealed class KitBinaryAnalysisWindow : Window
{
    public KitBinaryAnalysisWindow(KitFile kit)
    {
        Title = $"Binary Analysis - {kit.FileName}";
        Width = 1050;
        Height = 800;
        MinWidth = 750;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Background = new SolidColorBrush(
            System.Windows.Media.Color.FromRgb(17, 21, 27));

        Foreground = System.Windows.Media.Brushes.White;

        var viewer = new System.Windows.Controls.TextBox
        {
            Text = KitBinaryAnalyzer.Analyze(kit),
            IsReadOnly = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.NoWrap,

            VerticalScrollBarVisibility =
                ScrollBarVisibility.Auto,

            HorizontalScrollBarVisibility =
                ScrollBarVisibility.Auto,

            Background = new SolidColorBrush(
                System.Windows.Media.Color.FromRgb(17, 21, 27)),

            Foreground = new SolidColorBrush(
                System.Windows.Media.Color.FromRgb(190, 205, 220)),

            BorderThickness = new Thickness(0),

            FontFamily = new System.Windows.Media.FontFamily(
                "Consolas"),

            FontSize = 13,
            Padding = new Thickness(16)
        };

        Content = viewer;
    }
}
