using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using PHDNavisTools.Core;
using NavisApp = Autodesk.Navisworks.Api.Application;

namespace PHDNavisTools.UI
{
    public class TabItem : INotifyPropertyChanged
    {
        private bool _isChecked;
        public string Name { get; set; } = "";
        public bool IsChecked
        {
            get => _isChecked;
            set { _isChecked = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked))); }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }

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

                LstTabs.ItemsSource = _knownTabs.Keys
                    .OrderBy(k => k)
                    .Select(k => new TabItem { Name = k })
                    .ToList();

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

            var checkedTabs = LstTabs.Items.OfType<TabItem>()
                .Where(t => t.IsChecked)
                .Select(t => t.Name)
                .ToList();

            BtnApply.IsEnabled     = false;
            PnlProgress.Visibility = Visibility.Visible;
            PrgBar.IsIndeterminate = true;
            TxtProgress.Text       = "Coletando descendentes...";

            var options = new CascadeOptions
            {
                TabNames        = checkedTabs,
                PropertyName    = prop,
                CreateIfMissing = RdoCreate.IsChecked == true,
            };

            string tabDesc = checkedTabs.Count > 0
                ? $"abas [{string.Join(", ", checkedTabs)}]"
                : "todas as abas";
            AppendLog($"Cascateando '{prop}' ({tabDesc}) de {parents.Count} pai(s)...");

            try
            {
                CascadeResult result = await Task.Run(() =>
                {
                    var svc = new CascadePropertyService();

                    svc.ProgressChanged += (_, msg) =>
                        Dispatcher.Invoke(() =>
                        {
                            AppendLog(msg);
                            if (PrgBar.IsIndeterminate)
                                TxtProgress.Text = msg.Length > 70 ? msg.Substring(0, 67) + "..." : msg;
                        });

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
            if (total <= 0)
            {
                PrgBar.IsIndeterminate = true;
                return;
            }
            PrgBar.IsIndeterminate = false;
            PrgBar.Maximum         = total;
            PrgBar.Value           = current;
            TxtProgress.Text       = $"{current:N0} / {total:N0} elementos";
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void AppendLog(string msg)
        {
            TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
            TxtLog.ScrollToEnd();
        }
    }
}
