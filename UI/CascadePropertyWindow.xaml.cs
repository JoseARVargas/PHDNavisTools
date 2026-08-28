using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using PHDNavisTools.Core;
using NavisApp = Autodesk.Navisworks.Api.Application;

namespace PHDNavisTools.UI
{
    public partial class CascadePropertyWindow : Window
    {
        private Dictionary<string, List<string>> _knownTabs = new();

        public CascadePropertyWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _knownTabs = NavisPropertyScanner.Scan(maxItems: 3000);

                CmbTab.ItemsSource = _knownTabs.Keys.OrderBy(k => k).ToList();
                CmbProperty.ItemsSource = _knownTabs.Values
                    .SelectMany(v => v)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(p => p)
                    .ToList();
            }
            catch (Exception ex)
            {
                AppendLog($"Aviso ao ler abas: {ex.Message}");
            }

            var doc = NavisApp.ActiveDocument;
            int sel = doc?.CurrentSelection.SelectedItems.Count ?? 0;
            AppendLog($"{sel} elemento(s) selecionado(s) como pai(s).");
        }

        private async void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            var prop = (CmbProperty.Text ?? "").Trim();
            var tab  = (CmbTab.Text ?? "").Trim();

            if (string.IsNullOrEmpty(prop))
            {
                MessageBox.Show("Informe o nome da propriedade.", "Aviso",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var doc = NavisApp.ActiveDocument;
            if (doc == null) { AppendLog("Nenhum documento aberto."); return; }

            var parents = doc.CurrentSelection.SelectedItems.ToList();
            if (parents.Count == 0)
            {
                MessageBox.Show("Selecione ao menos um elemento pai no Navisworks.", "Aviso",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            BtnApply.IsEnabled  = false;
            PnlProgress.Visibility = Visibility.Visible;
            PrgBar.Value           = 0;
            TxtProgress.Text       = "Iniciando...";

            var options = new CascadeOptions
            {
                TabName         = tab,
                PropertyName    = prop,
                CreateIfMissing = RdoCreate.IsChecked == true,
            };

            AppendLog(string.IsNullOrEmpty(tab)
                ? $"Cascateando '{prop}' (todas as abas) de {parents.Count} pai(s)..."
                : $"Cascateando '{prop}' (aba '{tab}') de {parents.Count} pai(s)...");

            try
            {
                CascadeResult result = await Task.Run(() =>
                {
                    var svc = new CascadePropertyService();

                    svc.ProgressChanged += (_, msg) =>
                        Dispatcher.Invoke(() => AppendLog(msg));

                    svc.ProgressValue += (_, p) =>
                        Dispatcher.Invoke(() => UpdateProgress(p.Current, p.Total));

                    return svc.Apply(doc, parents, options);
                });

                foreach (var w in result.Warnings)
                    AppendLog($"aviso: {w}");

                var summary = $"Pronto: {result.ElementsWritten:N0} descendente(s) gravado(s)" +
                              (result.ElementsSkipped > 0 ? $", {result.ElementsSkipped:N0} ignorado(s)" : "") +
                              $" ({result.ParentsProcessed} pai(s) processado(s)).";
                AppendLog(summary);
                TxtProgress.Text = summary;
                MessageBox.Show(summary, "Concluido", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AppendLog($"ERRO: {ex.Message}");
                MessageBox.Show($"Falha:\n\n{ex.Message}", "Erro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnApply.IsEnabled = true;
            }
        }

        private void UpdateProgress(int current, int total)
        {
            if (total <= 0) return;
            PrgBar.Maximum   = total;
            PrgBar.Value     = current;
            TxtProgress.Text = $"{current:N0} / {total:N0} elementos";
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void AppendLog(string msg)
        {
            TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
            TxtLog.ScrollToEnd();
        }
    }
}
