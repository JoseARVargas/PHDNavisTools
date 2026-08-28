using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Autodesk.Navisworks.Api;
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
        // Tab name → property names (case-insensitive key)
        private readonly Dictionary<string, List<string>> _knownTabs =
            new(StringComparer.OrdinalIgnoreCase);

        // Parallel list of TabItem objects; same objects reused across refreshes
        // so IsChecked state survives when new tabs are merged in from background scan
        private readonly List<TabItem> _tabItems = new();

        public CascadePropertyWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var doc = NavisApp.ActiveDocument;
            var selected = doc?.CurrentSelection.SelectedItems.ToList()
                           ?? new List<ModelItem>();

            AppendLog($"{selected.Count} elemento(s) selecionado(s) como pai(s). Lendo abas...");

            // ── Lê os pais selecionados diretamente (sempre inclui as abas relevantes) ──
            try
            {
                foreach (var item in selected)
                    MergeItemTabs(item);
            }
            catch (Exception ex)
            {
                AppendLog($"Aviso ao ler abas dos pais: {ex.Message}");
            }

            RefreshUI();

            // ── BFS mais amplo em background para completar a lista ──────────────
            Task.Run(() =>
            {
                try   { return NavisPropertyScanner.Scan(maxItems: 10000); }
                catch { return new Dictionary<string, List<string>>(); }
            }).ContinueWith(task =>
            {
                Dispatcher.Invoke(() =>
                {
                    int before = _tabItems.Count;
                    foreach (var kvp in task.Result)
                        MergeTab(kvp.Key, kvp.Value);

                    if (_tabItems.Count > before)
                    {
                        RebindTabs();
                        RefreshPropertyCombo();
                        AppendLog($"Varredura ampliada: {_tabItems.Count} aba(s) detectada(s) no total.");
                    }
                });
            });
        }

        // ── Merge helpers ────────────────────────────────────────────────────────

        private void MergeItemTabs(ModelItem item)
        {
            foreach (var cat in item.PropertyCategories)
            {
                var propNames = cat.Properties.Select(p => p.DisplayName).ToList();
                MergeTab(cat.DisplayName, propNames);
            }
        }

        private void MergeTab(string tabName, IEnumerable<string> propNames)
        {
            if (!_knownTabs.TryGetValue(tabName, out var list))
            {
                list = new List<string>();
                _knownTabs[tabName] = list;
                _tabItems.Add(new TabItem { Name = tabName });
            }
            foreach (var p in propNames)
                if (!list.Any(x => string.Equals(x, p, StringComparison.OrdinalIgnoreCase)))
                    list.Add(p);
        }

        // ── UI refresh ───────────────────────────────────────────────────────────

        private void RefreshUI()
        {
            RebindTabs();
            RefreshPropertyCombo();
        }

        // Rebuilds ItemsSource preserving existing TabItem objects (and their IsChecked state)
        private void RebindTabs()
        {
            LstTabs.ItemsSource = _tabItems.OrderBy(t => t.Name).ToList();
        }

        private void RefreshPropertyCombo()
        {
            CmbProperty.ItemsSource = _knownTabs.Values
                .SelectMany(v => v)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p)
                .ToList();
        }

        // ── Apply ────────────────────────────────────────────────────────────────

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
