using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using PHDNavisTools.Core;

namespace PHDNavisTools.UI
{
    public partial class CheckWindow : Window
    {
        private List<CheckRule>   _rules   = new();
        private List<CheckResult> _results = new();
        private bool _processing;

        public CheckWindow()
        {
            InitializeComponent();
            TxtOutputDir.Text = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "PHD_Check_Resultados");
        }

        // -----------------------------------------------------------------------
        // Browse + Load rules
        // -----------------------------------------------------------------------

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title  = "Selecionar arquivo de regras",
                Filter = "Planilhas e CSV|*.csv;*.xlsx;*.xls;*.xlsm|CSV|*.csv|Excel|*.xlsx;*.xls;*.xlsm",
            };
            if (dlg.ShowDialog() != true) return;
            TxtFilePath.Text  = dlg.FileName;
            BtnLoad.IsEnabled = true;
        }

        private void BtnLoad_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _rules = CheckService.LoadRules(TxtFilePath.Text);
                _results.Clear();
                GridRules.ItemsSource   = _rules;
                GridResults.ItemsSource = null;
                BtnRun.IsEnabled        = _rules.Count > 0;
                BtnExport.IsEnabled     = false;
                SetStatus($"{_rules.Count} regra(s) carregada(s). Clique em \"Executar Verificação\".");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao carregar arquivo:\n{ex.Message}", "Erro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // -----------------------------------------------------------------------
        // Browse output dir
        // -----------------------------------------------------------------------

        private void BtnOutputDir_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description         = "Selecione a pasta de saída dos resultados",
                SelectedPath        = TxtOutputDir.Text,
                ShowNewFolderButton = true,
            };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                TxtOutputDir.Text = dlg.SelectedPath;
        }

        // -----------------------------------------------------------------------
        // Run (async — usa Task.Yield() para liberar a UI a cada 100 elementos)
        // -----------------------------------------------------------------------

        private async void BtnRun_Click(object sender, RoutedEventArgs e)
        {
            var doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
            if (doc == null) { SetStatus("Nenhum documento aberto."); return; }
            if (_rules.Count == 0) { SetStatus("Carregue as regras antes de executar."); return; }

            _processing         = true;
            BtnRun.IsEnabled    = false;
            BtnExport.IsEnabled = false;
            GridResults.ItemsSource = null;

            SetProgress(true, 0);
            SetStatus("Coletando elementos do modelo...");
            PHDNavisTools.Core.PluginLogger.Clear();
            PHDNavisTools.Core.PluginLogger.Info(
                $"Check iniciado — Regras: {_rules.Count}  SomenteErros: {ChkOnlyFailures.IsChecked}");
            await Dispatcher.Yield(DispatcherPriority.Background);

            try
            {
                bool onlyFail = ChkOnlyFailures.IsChecked == true;
                var results   = new List<CheckResult>();

                // Contadores de resumo — acumulados independentemente do filtro
                var byDisc = new Dictionary<string, (int ok, int empty, int missing)>(StringComparer.OrdinalIgnoreCase);
                var bySrc  = new Dictionary<string, (int ok, int empty, int missing)>(StringComparer.OrdinalIgnoreCase);

                CheckService.ResetRuleStats();

                // Fase 1: coletar itens geométricos
                List<(Autodesk.Navisworks.Api.ModelItem, string)> allItems;
                using (PHDNavisTools.Core.PluginLogger.Perf("Fase1_ColetarItens"))
                    allItems = CheckService.GetGeometryItems(doc);

                int total = allItems.Count;
                SetStatus($"Verificando {total} elemento(s)...");
                await Dispatcher.Yield(DispatcherPriority.Background);

                // Fase 2: verificar propriedades
                using var perfCheck = PHDNavisTools.Core.PluginLogger.Perf("Fase2_VerificarPropriedades", total);
                for (int i = 0; i < total; i++)
                {
                    var (item, src) = allItems[i];
                    var itemResults = new List<CheckResult>();
                    CheckService.ProcessItem(item, src, _rules, itemResults, onlyFailures: false);

                    foreach (var r in itemResults)
                    {
                        // Acumula resumo (sempre, independente do filtro)
                        Accumulate(byDisc, r.Disciplina, r.Resultado);
                        Accumulate(bySrc,  r.SourceFile, r.Resultado);

                        if (!onlyFail || r.Resultado != CheckService.OK)
                            results.Add(r);
                    }

                    if (i % 100 == 0)
                    {
                        double pct = total > 0 ? (double)i / total * 100.0 : 0;
                        SetProgress(true, pct);
                        SetStatus($"Verificando... {i}/{total} elementos");
                        await Dispatcher.Yield(DispatcherPriority.Background);
                    }
                }
                perfCheck.Dispose();

                _results = results;
                GridResults.ItemsSource = results;

                // Popula grids de resumo
                GridSummaryDisc.ItemsSource = BuildSummary(byDisc);
                GridSummarySrc.ItemsSource  = BuildSummary(bySrc);

                int okTotal      = byDisc.Values.Sum(v => v.ok);
                int emptyTotal   = byDisc.Values.Sum(v => v.empty);
                int missingTotal = byDisc.Values.Sum(v => v.missing);
                int totalChecked = okTotal + emptyTotal + missingTotal;
                int pctGlobal    = totalChecked > 0 ? okTotal * 100 / totalChecked : 0;

                string summary;
                if (totalChecked == 0)
                {
                    var srcFiles = CheckService.GetDistinctSourceFiles(doc);
                    string fileList = srcFiles.Count == 0
                        ? "nenhum arquivo-fonte detectado"
                        : string.Join("  |  ", srcFiles);
                    summary = $"Nenhum resultado. {total} elemento(s) percorrido(s).\n" +
                              $"Source files no modelo: {fileList}\n" +
                              $"A coluna Disciplina deve ser substring desse nome (ex: se o arquivo é \"ARQ_modelo.rvt\", use \"ARQ\").";
                }
                else
                {
                    summary = $"{totalChecked} verificação(ões)  |  ✓ {okTotal} Preenchidas ({pctGlobal}%)  |  ⚠ {emptyTotal} Vazias  |  ✗ {missingTotal} Ausentes";
                    summary += $"  |  {results.Count} linha(s) na tabela";
                }

                CheckService.LogRuleStats();
                PHDNavisTools.Core.PluginLogger.Info(
                    $"Check concluído — ✓{okTotal} ⚠{emptyTotal} ✗{missingTotal}  ({pctGlobal}% correto)");
                SetStatus(summary);
                BtnExport.IsEnabled = results.Count > 0;
            }
            catch (Exception ex)
            {
                PHDNavisTools.Core.PluginLogger.Error("Check falhou", ex);
                SetStatus($"ERRO: {ex.Message}");
                MessageBox.Show(ex.Message, "Erro na verificação",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetProgress(false, 100);
                BtnRun.IsEnabled = true;
                _processing      = false;
            }
        }

        // -----------------------------------------------------------------------
        // Export CSV
        // -----------------------------------------------------------------------

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            if (_results.Count == 0) return;
            try
            {
                string path = CheckService.ExportCsv(_results, TxtOutputDir.Text);
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

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            if (_processing)
            {
                MessageBox.Show("Aguarde o término da verificação antes de fechar.",
                    "Em processamento", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            Close();
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private void SetStatus(string msg) => TxtStatus.Text = msg;

        private void SetProgress(bool visible, double value)
        {
            PanelProgress.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            PrgBar.Value  = value;
            TxtPct.Text   = $"{(int)value}%";
        }

        private static void Accumulate(
            Dictionary<string, (int ok, int empty, int missing)> dict,
            string key, string resultado)
        {
            if (string.IsNullOrWhiteSpace(key)) key = "(sem filtro)";
            dict.TryGetValue(key, out var cur);
            if      (resultado == CheckService.OK)      dict[key] = (cur.ok + 1, cur.empty,     cur.missing);
            else if (resultado == CheckService.EMPTY)   dict[key] = (cur.ok,     cur.empty + 1, cur.missing);
            else                                        dict[key] = (cur.ok,     cur.empty,     cur.missing + 1);
        }

        private static List<CheckSummaryRow> BuildSummary(
            Dictionary<string, (int ok, int empty, int missing)> dict)
        {
            return dict
                .OrderByDescending(kv => kv.Value.ok + kv.Value.empty + kv.Value.missing)
                .Select(kv => new CheckSummaryRow
                {
                    Nome    = kv.Key,
                    Total   = kv.Value.ok + kv.Value.empty + kv.Value.missing,
                    Ok      = kv.Value.ok,
                    Vazia   = kv.Value.empty,
                    Ausente = kv.Value.missing,
                })
                .ToList();
        }
    }
}
