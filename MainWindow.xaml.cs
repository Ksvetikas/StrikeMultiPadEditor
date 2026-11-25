
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Data;

using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using MessageBox = System.Windows.MessageBox;

namespace StrikeMultiPadEditor;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<KitFile> _kits = new();
    private readonly ObservableCollection<CandidateLayer> _candidateLayers = new();

    private readonly ICollectionView _kitView;
    private string? _currentBackupPath;
    private Dictionary<string, LayerSampleNameMatch> _sampleNameLookup = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, WaveSampleNameMatch> _factorySampleNameLookup = new(StringComparer.OrdinalIgnoreCase);

    public MainWindow()
    {
        InitializeComponent();

        _kitView = CollectionViewSource.GetDefaultView(_kits);
        _kitView.Filter = FilterKits;

        KitGrid.ItemsSource = _kitView;
        CandidateLayerGrid.ItemsSource = _candidateLayers;

        ClearPadParameterDisplay("Select a kit and then a physical pad.");
    }

    // ---------------------------------------------------------
    // KIT LIBRARY AND SEARCH
    // ---------------------------------------------------------

    private bool FilterKits(object item)
    {
        if (item is not KitFile kit)
            return false;

        string search = SearchBox.Text.Trim();

        return string.IsNullOrWhiteSpace(search) ||
               kit.FileName.Contains(
                   search,
                   StringComparison.OrdinalIgnoreCase);
    }

    private void SearchBox_TextChanged(
        object sender,
        System.Windows.Controls.TextChangedEventArgs e)
    {
        _kitView.Refresh();
    }

    // ---------------------------------------------------------
    // OPEN BACKUP
    // ---------------------------------------------------------

    private void OpenBackup_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Strike Backup (*.bkf)|*.bkf|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true)
            return;

        _currentBackupPath = dialog.FileName;

        try
        {
            var loadedKits =
                BackupKitExtractor.ReadKits(dialog.FileName);

            _sampleNameLookup = LayerSampleNameResolver.BuildLookup(dialog.FileName);
            _factorySampleNameLookup = WaveSampleNameDecoder.BuildLookup(dialog.FileName);

            _kits.Clear();
            _candidateLayers.Clear();

            foreach (KitFile kit in loadedKits)
                _kits.Add(kit);

            _kitView.Refresh();

            KitCountText.Text =
                $"{_kits.Count} kit records loaded";

            StatusText.Text =
                $"Loaded {Path.GetFileName(dialog.FileName)} — " +
                $"{_kits.Count} kit records detected.";

            if (KitGrid.Items.Count > 0)
            {
                KitGrid.SelectedIndex = 0;
            }
            else
            {
                ClearSelectedKit();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Backup Read Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    // ---------------------------------------------------------
    // KIT SELECTION AND CANDIDATE LAYER DECODING
    // ---------------------------------------------------------

    private void KitGrid_SelectionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (KitGrid.SelectedItem is not KitFile kit)
        {
            ClearSelectedKit();
            return;
        }

        string displayName =
            Path.GetFileNameWithoutExtension(kit.FileName);

        SelectedKitName.Text = displayName;
        SelectedKitSubtext.Text = "Kit selected";

        InspectorKitName.Text = displayName;
        KitTypeText.Text = kit.KitType;
        KitOffsetText.Text = $"0x{kit.RecordOffset:X8}";

        _candidateLayers.Clear();

        foreach (CandidateLayer layer in CandidateLayerDecoder.Decode(kit))
        {
            if (_sampleNameLookup.TryGetValue(
                    layer.SampleReference,
                    out LayerSampleNameMatch? match))
            {
                // Layer metadata is the primary source for references
                // found in the kit's assigned layer blocks.
                layer.SampleName = match.SampleName;
                layer.SampleNameSource = "Layer metadata";
            }
            else if (_factorySampleNameLookup.TryGetValue(
                         layer.SampleReference,
                         out WaveSampleNameMatch? factoryMatch))
            {
                // Factory translation table is a fallback only. Some
                // two-byte values overlap across reference namespaces.
                layer.SampleName = factoryMatch.SampleName;
                layer.SampleNameSource = "Factory table (fallback)";
            }
            else if (layer.IsAssigned)
            {
                layer.SampleName = "Unknown reference";
                layer.SampleNameSource = "Unresolved";
            }
            else
            {
                layer.SampleName = "Unassigned";
                layer.SampleNameSource = "—";
            }

            _candidateLayers.Add(layer);
        }

        ClearPadParameterDisplay(
            "Select one of the nine physical pads. " +
            "Pad parameter decoding is still in progress.");

        StatusText.Text =
            $"Selected {kit.FileName} | " +
            $"{_candidateLayers.Count} candidate sound layers found.";
    }

    private void ClearSelectedKit()
    {
        SelectedKitName.Text = "No kit selected";
        SelectedKitSubtext.Text = "Select a kit from the library";

        InspectorKitName.Text = "—";
        KitTypeText.Text = "—";
        KitOffsetText.Text = "—";

        _candidateLayers.Clear();

        ClearPadParameterDisplay("Select a kit to begin.");
    }

    // ---------------------------------------------------------
    // PAD PARAMETER DISPLAY
    // ---------------------------------------------------------

    private void ClearPadParameterDisplay(string message)
    {
        PadParameterTitle.Text = "No pad selected";
        PadParameterStatusText.Text = message;

        PadSensitivityText.Text = "—";
        PadCurveText.Text = "—";
        PadThresholdText.Text = "—";
        PadFixedLevelText.Text = "—";

        WaveASampleText.Text = "—";
        WaveAVolumeText.Text = "—";
        WaveAPanText.Text = "—";
        WaveAPlaybackText.Text = "—";

        WaveBSampleText.Text = "—";
        WaveBVolumeText.Text = "—";
        WaveBPanText.Text = "—";
        WaveBPlaybackText.Text = "—";
    }

    // ---------------------------------------------------------
    // EXPORT SELECTED KIT
    // ---------------------------------------------------------

    private void ExtractKit_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (KitGrid.SelectedItem is not KitFile kit)
        {
            MessageBox.Show(
                "Select a kit first.",
                "Export Kit",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        var dialog = new SaveFileDialog
        {
            FileName = Path.GetFileName(kit.FileName),
            Filter = "Kit File (*.kit)|*.kit"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            File.WriteAllBytes(dialog.FileName, kit.Data);

            StatusText.Text =
                $"Exported {kit.FileName}.";

            MessageBox.Show(
                $"Kit exported successfully:\n{dialog.FileName}",
                "Export Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Export Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    // ---------------------------------------------------------
    // HARDWARE POSITION SELECTION
    // ---------------------------------------------------------

    private void HardwarePosition_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button ||
            button.Tag is not string positionName)
            return;

        SelectedPositionName.Text = positionName;

        bool isPad = positionName.StartsWith(
            "Pad ",
            StringComparison.OrdinalIgnoreCase);

        SelectedPositionType.Text = isPad
            ? "Physical sample pad"
            : positionName == "HH" ||
              positionName.StartsWith(
                  "Trigger In ",
                  StringComparison.OrdinalIgnoreCase)
                ? "Trigger input"
                : "Foot control";

        PadParameterTitle.Text = isPad
            ? positionName.ToUpperInvariant()
            : "No physical pad selected";

        PadParameterStatusText.Text = isPad
            ? "Pad selected — parameter values are not yet decoded."
            : "This control is not one of the nine physical pads.";

        StatusText.Text =
            $"Selected hardware position: {positionName}";
    }

    // ---------------------------------------------------------
    // COPY ALL CANDIDATE LAYER DATA
    // ---------------------------------------------------------

    private void CopyAllLayerData_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_candidateLayers.Count == 0)
        {
            StatusText.Text = "No layer data available.";
            return;
        }

        var output = new System.Text.StringBuilder();

        foreach (CandidateLayer layer in _candidateLayers)
        {
            output.AppendLine(
                $"KITI RECORD {layer.RecordNumber} - {layer.LayerName}");

            output.AppendLine(
                $"Record offset: 0x{layer.RecordOffset:X4}");

            output.AppendLine(
                $"Layer offset: 0x{layer.DataOffset:X4}");

            output.AppendLine(
                $"Sample reference: {layer.SampleReference}");
            output.AppendLine(
                $"Sample name: {layer.SampleName}");
            output.AppendLine(
                $"Sample name source: {layer.SampleNameSource}");
            output.AppendLine(
                $"Velocity range: {layer.VelocityRangeText}");
            output.AppendLine(
                $"Auxiliary bytes +04..+07: {layer.AuxiliaryBytesHex}");
            output.AppendLine(
                $"Auxiliary byte +05: {layer.AuxiliaryByte05Value:X2}");
            output.AppendLine();
            output.AppendLine(
                StrikeParameterDecoder.FormatTable(
                    layer.PositionParameters,
                    "KITI POSITION PARAMETERS:"));
            output.AppendLine();
            output.AppendLine(
                StrikeParameterDecoder.FormatTable(
                    layer.LayerParameters,
                    "LAYER PARAMETERS:"));

            string hex = layer.RawBlockHex;

            for (int i = 0; i < hex.Length; i += 32)
            {
                int count = Math.Min(32, hex.Length - i);

                output.Append($"+0x{i / 2:X2}: ");

                for (int j = i; j < i + count; j += 2)
                {
                    if (j > i)
                        output.Append(' ');

                    output.Append(hex.Substring(j, 2));
                }

                output.AppendLine();
            }

            output.AppendLine();
        }

        try
        {
            System.Windows.Clipboard.SetText(output.ToString());

            StatusText.Text =
                $"Copied {_candidateLayers.Count} complete layer blocks.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Clipboard Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
    private void CopySampleReferenceScan_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_currentBackupPath) ||
                !File.Exists(_currentBackupPath))
            {
                MessageBox.Show(
                    "Please open a backup file first.",
                    "Sample Reference Scan",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            var matches = BackupReferenceScanner.Scan(
                _currentBackupPath);

            var report = new StringBuilder();

            report.AppendLine(
                "STRIKE MULTIPAD - SAMPLE REFERENCE SCAN");

            report.AppendLine(
                $"Backup: {Path.GetFileName(_currentBackupPath)}");

            report.AppendLine(
                $"Backup size: {new FileInfo(_currentBackupPath).Length:N0} bytes");

            report.AppendLine(
                $"Total matches: {matches.Count}");

            report.AppendLine();

            string[] references =
            [
                "1A D1", "1A CD", "19 34", "1B E0",
                "1B DE", "1B E2", "01 F4", "01 BF",
                "03 73", "03 74", "01 B3"
            ];

            foreach (string reference in references)
            {
                var found = matches
                    .Where(x => x.Reference == reference)
                    .ToList();

                report.AppendLine(
                    $"REFERENCE {reference}: {found.Count} matches");

                foreach (var match in found.Take(30))
                {
                    report.AppendLine(
                        $"  Offset: {match.OffsetText} | " +
                        $"Order: {match.ByteOrder}");

                    report.AppendLine(
                        $"  HEX: {match.HexContext}");

                    report.AppendLine(
                        $"  TEXT: {match.TextContext}");
                }

                if (found.Count > 30)
                {
                    report.AppendLine(
                        $"  ... {found.Count - 30} additional matches omitted");
                }

                report.AppendLine();
            }

            System.Windows.Clipboard.SetText(report.ToString());

            MessageBox.Show(
                $"Sample reference scan copied.\n\n" +
                $"Total matches: {matches.Count}\n\n" +
                "Paste the results into this chat.",
                "Sample Reference Scan",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not complete the scan:\n\n{ex.Message}",
                "Sample Reference Scan",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    // private void CopySampleReferenceResults_Click(
    //     object sender,
    //     RoutedEventArgs e)
    // {
    //     try
    //     {
    //         if (string.IsNullOrWhiteSpace(_currentBackupPath) ||
    //             !System.IO.File.Exists(_currentBackupPath))
    //         {
    //             MessageBox.Show(
    //                 "Open a backup file first.",
    //                 "Sample Reference Results",
    //                 MessageBoxButton.OK,
    //                 MessageBoxImage.Information);

    //             return;
    //         }

    //         var results =
    //             BackupSampleMetadataScanner.Scan(_currentBackupPath);

    //         var report = new System.Text.StringBuilder();

    //         report.AppendLine("STRIKE MULTIPAD - SAMPLE METADATA RESULTS");
    //         report.AppendLine($"Backup: {_currentBackupPath}");
    //         report.AppendLine($"HAD records found: {results.Count}");
    //         report.AppendLine();

    //         report.AppendLine(
    //             "HAD FILE | SAMPLE NAME | WAV PATH | HAD OFFSET | WAV METADATA OFFSET | STATUS");

    //         report.AppendLine(
    //             "--------------------------------------------------------------------------------");

    //         foreach (var item in results)
    //         {
    //             report.AppendLine(
    //                 $"{item.HadFileName} | " +
    //                 $"{(string.IsNullOrWhiteSpace(item.SampleName) ? "(unknown)" : item.SampleName)} | " +
    //                 $"{(string.IsNullOrWhiteSpace(item.SamplePath) ? "(none)" : item.SamplePath)} | " +
    //                 $"0x{item.HadMetadataOffset:X8} | " +
    //                 $"{(item.SampleMetadataOffset == 0 ? "(none)" : $"0x{item.SampleMetadataOffset:X8}")} | " +
    //                 $"{item.Status}");
    //         }

    //         System.Windows.Clipboard.SetText(report.ToString());

    //         MessageBox.Show(
    //             $"Copied {results.Count} sample metadata results to the clipboard.\n\n" +
    //             "Paste the results into this chat.",
    //             "Sample Reference Results",
    //             MessageBoxButton.OK,
    //             MessageBoxImage.Information);
    //     }
    //     catch (Exception ex)
    //     {
    //         MessageBox.Show(
    //             $"Could not scan backup sample metadata:\n\n{ex.Message}",
    //             "Sample Reference Results",
    //             MessageBoxButton.OK,
    //             MessageBoxImage.Error);
    //     }
    // }


    // private void CopySampleReferenceResults_Click(
    //     object sender,
    //     RoutedEventArgs e)
    // {
    //     if (string.IsNullOrWhiteSpace(_currentBackupPath) ||
    //         !File.Exists(_currentBackupPath))
    //     {
    //         MessageBox.Show(
    //             "Open a backup file first.",
    //             "Factory Sample Scan");

    //         return;
    //     }

    //     string results =
    //         FactorySampleReferenceScanner.Scan(_currentBackupPath);

    //     System.Windows.Clipboard.SetText(results);

    //     MessageBox.Show(
    //         "Factory sample index results copied to the clipboard.",
    //         "Factory Sample Scan");
    // }

    // private void CopySampleReferenceResults_Click(
    //     object sender,
    //     RoutedEventArgs e)
    // {
    //     if (string.IsNullOrWhiteSpace(_currentBackupPath) ||
    //         !File.Exists(_currentBackupPath))
    //     {
    //         MessageBox.Show(
    //             "Open a backup file first.",
    //             "Factory Sample Lookup");

    //         return;
    //     }

    //     try
    //     {
    //         string results =
    //             FactorySampleNameTableScanner.Scan(_currentBackupPath);

    //         System.Windows.Clipboard.SetText(results);

    //         MessageBox.Show(
    //             "Factory sample name table copied to the clipboard.",
    //             "Factory Sample Lookup");
    //     }
    //     catch (Exception ex)
    //     {
    //         MessageBox.Show(
    //             ex.Message,
    //             "Factory Sample Lookup Error");
    //     }
    // }

    private void CopySampleReferenceResults_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentBackupPath) ||
            !File.Exists(_currentBackupPath))
        {
            MessageBox.Show(
                "Open a backup file first.",
                "Layer Sample Name Lookup");

            return;
        }

        try
        {
            string results =
                CombinedSampleNameResolver.Scan(_currentBackupPath);

            System.Windows.Clipboard.SetText(results);

            MessageBox.Show(
                "Layer-reference and factory sample-name lookup results copied to the clipboard.",
                "Sample Reference Results",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            StatusText.Text =
                "Layer and factory sample-name lookup completed. Results copied.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Sample Reference Search Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    // ---------------------------------------------------------
    // EXPORT COMPACT BACKUP ANALYSIS PACKAGE
    // ---------------------------------------------------------

    private void ExportAnalysisPackage_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentBackupPath) ||
            !File.Exists(_currentBackupPath))
        {
            MessageBox.Show(
                "Open a backup file first.",
                "Export Analysis Package",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Save Strike MultiPad Analysis Package",
            Filter = "ZIP archive (*.zip)|*.zip",
            DefaultExt = ".zip",
            AddExtension = true,
            FileName = "StrikeMultiPad_AnalysisPackage.zip"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            ExportAnalysisPackageButton.IsEnabled = false;
            StatusText.Text = "Creating compact analysis package...";

            BackupAnalysisPackageExporter.Export(
                _currentBackupPath,
                dialog.FileName);

            var info = new FileInfo(dialog.FileName);

            StatusText.Text =
                $"Analysis package created: {info.Length:N0} bytes";

            MessageBox.Show(
                "The compact analysis package has been created.\n\n" +
                $"File:\n{dialog.FileName}\n\n" +
                $"Size: {info.Length:N0} bytes\n\n" +
                "Attach this ZIP file to the chat. You do not need to attach the original BKF backup.",
                "Analysis Package Ready",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not create the analysis package:\n\n{ex.Message}",
                "Export Analysis Package Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            ExportAnalysisPackageButton.IsEnabled = true;
        }
    }

}
