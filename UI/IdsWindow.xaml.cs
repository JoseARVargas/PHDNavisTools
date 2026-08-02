using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using PHDNavisTools.Core;

namespace PHDNavisTools.UI
{
    public partial class IdsWindow : Window
    {
        private IdsDocument?      _ids;
        private List<IdsCheckResult> _results = new List<IdsCheckResult>();

        public IdsWindow()
        {
            InitializeComponent();
            TxtOutputDir.Text = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "PHD_IDS_Resultados");
        }

        // ── arquivo IDS ──────────────────────────────────────────────────────

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title  = "Selecionar arquivo IDS",
                Filter = "Information Delivery Specification|*.ids|XML|*.xml",
            };
            if (dlg.ShowDialog() != true) return;
            TxtFilePath.Text  = dlg.FileName;
            BtnLoad.IsEnabled = true;
        }

        private void BtnLoad_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _ids = IdsParser.ParseFile(TxtFilePath.Text);
                _results.Clear();
                GridResults.ItemsSource = null;

                string info = $"Título: {_ids.Info.Title}";
                if (!string.IsNullOrEmpty(_ids.Info.Author))    info += $"  |  Autor: {_ids.Info.Author}";
                if (!string.IsNullOrEmpty(_ids.Info.Date))      info += $"  |  Data: {_ids.Info.Date}";
                if (!string.IsNullOrEmpty(_ids.Info.Milestone)) info += $"  |  Milestone: {_ids.Info.Milestone}";
                info += $"\n{_ids.Specifications.Count} especificação(ões) carregada(s).";
                if (!string.IsNullOrEmpty(_ids.Info.Description))
                    info += $"\n{_ids.Info.Description}";

                TxtIdsInfo.Text        = info;
                PanelIdsInfo.Visibility = Visibility.Visible;
                BtnRun.IsEnabled       = _ids.Specifications.Count > 0;
                BtnExport.IsEnabled    = false;
                SetStatus($"{_ids.Specifications.Count} especificação(ões) carregada(s). Clique em \"Verificar IDS\".");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao carregar IDS:\n{ex.Message}", "Erro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnOutputDir_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description  = "Selecione a pasta de saída dos resultados",
                SelectedPath = TxtOutputDir.Text,
                ShowNewFolderButton = true,
            };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                TxtOutputDir.Text = dlg.SelectedPath;
        }

        // ── verificação ──────────────────────────────────────────────────────

        private async void BtnRun_Click(object sender, RoutedEventArgs e)
        {
            if (_ids == null) return;
            var doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
            if (doc == null) { SetStatus("Nenhum documento aberto."); return; }

            BtnRun.IsEnabled    = false;
            BtnExport.IsEnabled = false;
            GridResults.ItemsSource = null;
            SetProgress(true, 0);
            SetStatus("Coletando elementos do modelo...");

            PluginLogger.Clear();
            PluginLogger.Info($"IDS iniciado — arquivo: {Path.GetFileName(TxtFilePath.Text)}");
            await Dispatcher.Yield(DispatcherPriority.Background);

            try
            {
                bool onlyFail = ChkOnlyFailures.IsChecked == true;

                _results = await IdsService.RunAsync(doc, _ids, onlyFail,
                    async (done, total) =>
                    {
                        SetProgress(true, total > 0 ? (double)done / total * 100 : 0);
                        SetStatus($"Verificando... {done}/{total} elementos");
                        await Dispatcher.Yield(DispatcherPriority.Background);
                    });

                GridResults.ItemsSource = _results;

                int nPass = 0, nFail = 0, nNA = 0;
                foreach (var r in _results)
                {
                    if      (r.Status == IdsStatus.Pass)          nPass++;
                    else if (r.Status == IdsStatus.Fail)          nFail++;
                    else if (r.Status == IdsStatus.NotApplicable) nNA++;
                }

                string summary = _results.Count == 0
                    ? "Nenhum resultado. Verifique se o modelo tem elementos IFC e se os facets de applicability correspondem."
                    : $"{_results.Count} resultado(s)  |  ✓ {nPass} PASS  |  ✗ {nFail} FAIL  |  — {nNA} N/A";

                if (_results.Count >= 10_000)
                    summary += "\n⚠ Limite de 10 000 resultados atingido. Ative \"Exibir apenas falhas\" para ver todos os erros.";

                PluginLogger.Info($"IDS concluído — {summary}");
                SetStatus(summary);
                BtnExport.IsEnabled = _results.Count > 0;
            }
            catch (Exception ex)
            {
                PluginLogger.Error("IDS falhou", ex);
                SetStatus($"ERRO: {ex.Message}");
                MessageBox.Show(ex.Message, "Erro na verificação IDS",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnRun.IsEnabled = true;
                SetProgress(false, 100);
            }
        }

        // ── exportar ─────────────────────────────────────────────────────────

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            if (_results.Count == 0) return;
            try
            {
                string path = IdsService.ExportCsv(_results, TxtOutputDir.Text);
                SetStatus($"CSV exportado: {path}");
                if (MessageBox.Show($"Arquivo exportado:\n{path}\n\nAbrir pasta?",
                        "Exportação concluída", MessageBoxButton.YesNo,
                        MessageBoxImage.Information) == MessageBoxResult.Yes)
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao exportar:\n{ex.Message}", "Erro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        // ── helpers ──────────────────────────────────────────────────────────

        private void SetStatus(string msg) => TxtStatus.Text = msg;

        private void SetProgress(bool visible, double value)
        {
            PanelProgress.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            PrgBar.Value = value;
            TxtPct.Text  = $"{(int)value}%";
        }
    }
}
