using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Navisworks.Api;
using Microsoft.Win32;
using NavisworksIfcExporter.Core;
using NavisworksIfcExporter.Models;

namespace NavisworksIfcExporter.UI
{
    public partial class IfcExporterView : UserControl
    {
        private ObservableCollection<MappingRule> _rules = new ObservableCollection<MappingRule>();
        private Dictionary<string, List<string>> _catalog = new Dictionary<string, List<string>>();
        private List<string> _allPsetNames = new List<string>();
        private List<string> _allPropNames = new List<string>();
        private readonly ObservableCollection<SetCheckItem> _sets = new ObservableCollection<SetCheckItem>();

        private static readonly string DefaultMappingPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Autodesk", "Navisworks 2026", "Plugins", "NavisworksIfcExporter", "mapping.json");

        public IfcExporterView()
        {
            InitializeComponent();
            _rules = new ObservableCollection<MappingRule>(LoadMappingFromFile(DefaultMappingPath));
            GridRules.ItemsSource = _rules;
            CmbSingleSet.ItemsSource = _sets;
            LstBatch.ItemsSource = _sets;
            Loaded += (_, __) => LoadSets();
        }

        // ── Scope ─────────────────────────────────────────────────────────────

        private void Scope_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            bool isBatch = RbBatch.IsChecked == true;
            PanelBatch.Visibility  = isBatch ? Visibility.Visible : Visibility.Collapsed;
            CmbSingleSet.IsEnabled = RbSingleSet.IsChecked == true;
            GrpOutput.Header       = isBatch ? "Pasta de destino (um IFC por set)" : "Arquivo de saída";
            TxtOutputPath.Text     = string.Empty;
        }

        // ── Sets ──────────────────────────────────────────────────────────────

        private void LoadSets()
        {
            _sets.Clear();
            var doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
            if (doc == null) return;
            try { CollectSets(doc.SelectionSets.Value, _sets); }
            catch { }
        }

        private static void CollectSets(IEnumerable<SavedItem> items, ObservableCollection<SetCheckItem> result)
        {
            foreach (var item in items)
            {
                if (item is SelectionSet ss) result.Add(new SetCheckItem(ss.DisplayName, ss));
                else if (item is Autodesk.Navisworks.Api.GroupItem folder) CollectSets(folder.Children, result);
            }
        }

        private void BtnRefreshSets_Click(object sender, RoutedEventArgs e) => LoadSets();
        private void BtnBatchAll_Click(object sender, RoutedEventArgs e) { foreach (var s in _sets) s.IsChecked = true; }
        private void BtnBatchNone_Click(object sender, RoutedEventArgs e) { foreach (var s in _sets) s.IsChecked = false; }

        // ── Browse ────────────────────────────────────────────────────────────

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            if (RbBatch.IsChecked == true)
            {
                var dlg = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = "Pasta de destino para os arquivos IFC",
                    ShowNewFolderButton = true,
                };
                if (!string.IsNullOrWhiteSpace(TxtOutputPath.Text))
                    dlg.SelectedPath = TxtOutputPath.Text;
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    TxtOutputPath.Text = dlg.SelectedPath;
            }
            else
            {
                var dlg = new SaveFileDialog
                {
                    Title = "Salvar arquivo IFC",
                    Filter = "IFC Files (*.ifc)|*.ifc",
                    DefaultExt = ".ifc",
                };
                if (dlg.ShowDialog() == true)
                    TxtOutputPath.Text = dlg.FileName;
            }
        }

        // ── Export ────────────────────────────────────────────────────────────

        private async void OnExport(object sender, RoutedEventArgs e)
        {
            GridRules.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
            SaveMappingToFile(_rules, DefaultMappingPath);

            bool isBatch     = RbBatch.IsChecked == true;
            bool isSingle    = RbSingleSet.IsChecked == true;
            bool isSelection = RbSelection.IsChecked == true;
            string outputPath = TxtOutputPath.Text.Trim();

            if (string.IsNullOrWhiteSpace(outputPath))
            {
                MessageBox.Show(
                    isBatch ? "Selecione a pasta de destino." : "Selecione o arquivo de saída.",
                    "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SetCheckItem? singleSet = null;
            if (isSingle)
            {
                singleSet = CmbSingleSet.SelectedItem as SetCheckItem;
                if (singleSet == null)
                {
                    MessageBox.Show("Selecione um set.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            var checkedSets = isBatch ? _sets.Where(s => s.IsChecked).ToList() : null;
            if (isBatch && (checkedSets == null || checkedSets.Count == 0))
            {
                MessageBox.Show("Selecione ao menos um set.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var schema        = CmbSchema.SelectedIndex == 1 ? IfcSchema.Ifc2x3 : IfcSchema.Ifc4;
            var coordDecimals = CmbQuality.SelectedIndex switch { 1 => 3, 2 => 6, _ => 4 };
            var baseOpts = new ExportOptions
            {
                ExportGeometry   = ChkGeometry.IsChecked == true,
                IncludeHidden    = ChkHidden.IsChecked == true,
                SelectionOnly    = isSelection,
                AuthorName       = TxtAuthor.Text,
                OrganizationName = TxtOrganization.Text,
                MappingRules     = _rules.ToList(),
                Schema           = schema,
                CoordDecimals    = coordDecimals,
            };

            BtnExport.IsEnabled      = false;
            PanelProgress.Visibility = Visibility.Visible;
            PrgBar.IsIndeterminate   = !isBatch;
            PrgBar.Value             = 0;
            TxtPct.Text              = isBatch ? $"0/{checkedSets!.Count}" : "Exportando...";
            TxtLog.Clear();

            try
            {
                if (isBatch)
                {
                    var doc = Autodesk.Navisworks.Api.Application.ActiveDocument!;
                    string docName = "Modelo";
                    try { docName = Path.GetFileNameWithoutExtension(doc.FileName) ?? "Modelo"; } catch { }
                    int done = 0, errors = 0;

                    foreach (var set in checkedSets!)
                    {
                        AppendLog($"[{done + 1}/{checkedSets.Count}] {set.Name}");
                        var filePath = Path.Combine(outputPath, $"{docName} {SanitizeFileName(set.Name)}.ifc");

                        // Resolve items on UI/STA thread with HashSet<ModelItem> dedup
                        List<ModelItem> items;
                        try
                        {
                            var seen = new HashSet<ModelItem>();
                            var merged = new List<ModelItem>();
                            if (set.Item != null)
                                foreach (var mi in set.Item.GetSelectedItems(doc))
                                    if (seen.Add(mi)) merged.Add(mi);
                            items = merged;
                        }
                        catch (Exception ex)
                        {
                            AppendLog($"  ERRO ao resolver set: {ex.Message}");
                            errors++; done++; continue;
                        }

                        if (items.Count == 0)
                        {
                            AppendLog("  Set vazio — ignorado.");
                            done++; continue;
                        }

                        var opts = new ExportOptions
                        {
                            OutputPath       = filePath,
                            ExportGeometry   = baseOpts.ExportGeometry,
                            IncludeHidden    = baseOpts.IncludeHidden,
                            AuthorName       = baseOpts.AuthorName,
                            OrganizationName = baseOpts.OrganizationName,
                            MappingRules     = baseOpts.MappingRules,
                            Schema           = baseOpts.Schema,
                            CoordDecimals    = baseOpts.CoordDecimals,
                            ExplicitItems    = items,
                        };

                        try
                        {
                            await Task.Run(() =>
                            {
                                var svc = new ExportService();
                                svc.ProgressChanged += (_, msg) => Dispatcher.Invoke(() => AppendLog($"  {msg}"));
                                svc.Export(opts);
                            });
                            AppendLog("  OK");
                        }
                        catch (Exception ex)
                        {
                            errors++;
                            AppendLog($"  ERRO: {ex.Message}");
                        }

                        done++;
                        PrgBar.Value = done * 100.0 / checkedSets.Count;
                        TxtPct.Text  = $"{done}/{checkedSets.Count}";
                    }

                    AppendLog(errors == 0
                        ? $"Concluído: {done} arquivo(s) exportado(s)."
                        : $"Concluído com {errors} erro(s).");

                    if (done > errors)
                        System.Diagnostics.Process.Start("explorer.exe", $"\"{outputPath}\"");
                }
                else
                {
                    baseOpts.OutputPath = outputPath;

                    if (isSingle && singleSet?.Item != null)
                    {
                        var doc = Autodesk.Navisworks.Api.Application.ActiveDocument!;
                        var seen = new HashSet<ModelItem>();
                        var merged = new List<ModelItem>();
                        foreach (var mi in singleSet.Item.GetSelectedItems(doc))
                            if (seen.Add(mi)) merged.Add(mi);
                        baseOpts.ExplicitItems = merged;
                    }

                    await Task.Run(() =>
                    {
                        var svc = new ExportService();
                        svc.ProgressChanged += (_, msg) => Dispatcher.Invoke(() =>
                        {
                            AppendLog(msg);
                            TxtPct.Text = msg.TrimStart().Length > 16
                                ? msg.TrimStart().Substring(0, 16) + "…"
                                : msg.TrimStart();
                        });
                        svc.Export(baseOpts);
                    });

                    TxtPct.Text = "Concluído";
                    AppendLog("Exportação concluída.");
                }
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
                PrgBar.IsIndeterminate   = false;
            }
        }

        // ── Mapping tab ───────────────────────────────────────────────────────

        private void CmbSourcePset_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is ComboBox cmb) cmb.ItemsSource = _allPsetNames;
        }

        private void CmbSourceProperty_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ComboBox cmb) return;
            var pset = cmb.Tag as string;
            cmb.ItemsSource = (!string.IsNullOrEmpty(pset) && _catalog.TryGetValue(pset!, out var props))
                ? props : _allPropNames;
        }

        private void CmbTargetPset_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is ComboBox cmb && cmb.ItemsSource == null)
                cmb.ItemsSource = PsetCatalog.PsetNames();
        }

        private void BtnMappingAdd_Click(object sender, RoutedEventArgs e)
        {
            var rule = new MappingRule();
            _rules.Add(rule);
            GridRules.ScrollIntoView(rule);
            GridRules.SelectedItem = rule;
            GridRules.BeginEdit();
        }

        private void BtnMappingRemove_Click(object sender, RoutedEventArgs e)
        {
            var selected = GridRules.SelectedItems.Cast<MappingRule>().ToList();
            foreach (var r in selected) _rules.Remove(r);
        }

        private void BtnMappingUp_Click(object sender, RoutedEventArgs e)
        {
            if (GridRules.SelectedItem is not MappingRule rule) return;
            int idx = _rules.IndexOf(rule);
            if (idx > 0) _rules.Move(idx, idx - 1);
        }

        private void BtnMappingDown_Click(object sender, RoutedEventArgs e)
        {
            if (GridRules.SelectedItem is not MappingRule rule) return;
            int idx = _rules.IndexOf(rule);
            if (idx < _rules.Count - 1) _rules.Move(idx, idx + 1);
        }

        private void BtnScan_Click(object sender, RoutedEventArgs e)
        {
            TxtScanStatus.Text = "Escaneando...";
            BtnScan.IsEnabled  = false;
            try
            {
                var catalog    = NavisPropertyScanner.Scan();
                _catalog       = catalog;
                _allPsetNames  = catalog.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
                _allPropNames  = catalog.Values.SelectMany(v => v)
                                        .Distinct(StringComparer.OrdinalIgnoreCase)
                                        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
                TxtScanStatus.Text = _allPsetNames.Count > 0
                    ? $"{_allPsetNames.Count} cat, {_allPropNames.Count} props"
                    : "Nenhum modelo";
            }
            catch (Exception ex) { TxtScanStatus.Text = $"Erro: {ex.Message}"; }
            finally { BtnScan.IsEnabled = true; }
        }

        private void BtnMappingImport_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Title = "Importar mapeamento", Filter = "JSON (*.json)|*.json" };
            if (dlg.ShowDialog() != true) return;
            var loaded = LoadMappingFromFile(dlg.FileName);
            _rules.Clear();
            foreach (var r in loaded) _rules.Add(r);
        }

        private void BtnMappingExport_Click(object sender, RoutedEventArgs e)
        {
            GridRules.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
            var dlg = new SaveFileDialog { Title = "Exportar mapeamento", Filter = "JSON (*.json)|*.json", FileName = "mapping.json" };
            if (dlg.ShowDialog() != true) return;
            SaveMappingToFile(_rules, dlg.FileName);
        }

        // ── helpers ───────────────────────────────────────────────────────────

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        }

        private void AppendLog(string message)
        {
            TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
            TxtLog.ScrollToEnd();
        }

        private static List<MappingRule> LoadMappingFromFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return new List<MappingRule>();
                using var fs = File.OpenRead(path);
                return (List<MappingRule>)(new DataContractJsonSerializer(typeof(List<MappingRule>))).ReadObject(fs)
                    ?? new List<MappingRule>();
            }
            catch { return new List<MappingRule>(); }
        }

        private static void SaveMappingToFile(IEnumerable<MappingRule> rules, string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var fs = File.Create(path);
                (new DataContractJsonSerializer(typeof(List<MappingRule>))).WriteObject(fs, rules.ToList());
            }
            catch { }
        }
    }

    // ── SetCheckItem ─────────────────────────────────────────────────────────

    internal sealed class SetCheckItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public string Name { get; }
        public SelectionSet? Item { get; }

        private bool _isChecked = true;
        public bool IsChecked
        {
            get => _isChecked;
            set { _isChecked = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked))); }
        }

        public SetCheckItem(string name, SelectionSet? item) { Name = name; Item = item; }
    }
}
