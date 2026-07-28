using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using NavisworksIfcExporter.Core;
using NavisworksIfcExporter.Models;

namespace NavisworksIfcExporter.UI
{
    public partial class ExportWindow : Window
    {
        private List<MappingRule> _mappingRules = new List<MappingRule>();

        public ExportWindow(bool selectionOnly = false)
        {
            InitializeComponent();
            if (selectionOnly) ChkSelection.IsChecked = true;
        }

        private void BtnMapping_Click(object sender, RoutedEventArgs e)
        {
            var win = new MappingWindow(_mappingRules) { Owner = this };
            if (win.ShowDialog() == true)
                _mappingRules = win.GetRules();
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Title      = "Salvar arquivo IFC",
                Filter     = "IFC Files (*.ifc)|*.ifc",
                DefaultExt = ".ifc",
            };

            if (dialog.ShowDialog() == true)
                TxtOutputPath.Text = dialog.FileName;
        }

        private async void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtOutputPath.Text))
            {
                MessageBox.Show("Selecione o caminho do arquivo de saída.", "Aviso",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            BtnExport.IsEnabled      = false;
            TxtLog.Clear();
            PanelProgress.Visibility = Visibility.Visible;
            TxtPct.Text              = "Exportando...";

            var options = new ExportOptions
            {
                OutputPath       = TxtOutputPath.Text,
                ExportGeometry   = ChkGeometry.IsChecked == true,
                IncludeHidden    = ChkHidden.IsChecked == true,
                SelectionOnly    = ChkSelection.IsChecked == true,
                AuthorName       = TxtAuthor.Text,
                OrganizationName = TxtOrganization.Text,
                MappingRules     = _mappingRules,
                Schema           = CmbSchema.SelectedIndex == 1 ? IfcSchema.Ifc2x3 : IfcSchema.Ifc4,
                CoordDecimals    = CmbQuality.SelectedIndex switch { 1 => 3, 2 => 6, _ => 4 },
            };

            try
            {
                await Task.Run(() =>
                {
                    var service = new ExportService();
                    service.ProgressChanged += (_, msg) =>
                        Dispatcher.Invoke(() => {
                            AppendLog(msg);
                            TxtPct.Text = msg.TrimStart().Length > 18
                                ? msg.TrimStart().Substring(0, 18) + "…"
                                : msg.TrimStart();
                        });

                    service.Export(options);
                });

                TxtPct.Text = "Concluído";
                MessageBox.Show("Exportação concluída com sucesso!", "Sucesso",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                TxtPct.Text = "Erro";
                AppendLog($"ERRO: {ex.Message}");
                MessageBox.Show($"Erro durante a exportação:\n{ex.Message}", "Erro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnExport.IsEnabled      = true;
                PanelProgress.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void AppendLog(string message)
        {
            TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
            TxtLog.ScrollToEnd();
        }
    }
}
