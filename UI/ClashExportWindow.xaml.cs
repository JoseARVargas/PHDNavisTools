using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using PHDNavisTools.Core;

namespace PHDNavisTools.UI
{
    public partial class ClashExportWindow : Window
    {
        private readonly ObservableCollection<ClashTestEntry>    _entries   = new();
        private readonly ObservableCollection<ClashPropertyRule> _propRules = new();

        // Property scanner cache (STA thread only)
        private Dictionary<string, List<string>> _catalog     = new();
        private List<string>                      _allPsets    = new();
        private List<string>                      _allProps    = new();

        public ClashExportWindow()
        {
            InitializeComponent();
            ListTests.ItemsSource = _entries;
            GridProps.ItemsSource = _propRules;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LoadTests();
            ScanModel();
        }

        // -----------------------------------------------------------------------
        // Clash tests
        // -----------------------------------------------------------------------

        private void LoadTests()
        {
            _entries.Clear();
            var doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
            if (doc == null) { AppendLog("Nenhum documento aberto."); return; }

            try
            {
                foreach (var test in ClashExportService.GetAllTests(doc.GetClash().TestsData.Value.TestsRoot.Children))
                {
                    _entries.Add(new ClashTestEntry
                    {
                        Test        = test,
                        IsSelected  = true,
                        DisplayName = test.DisplayName,
                        ResultCount = ClashExportService.CountResults(test),
                        StatusLabel = TranslateTestStatus(test.Status),
                    });
                }
                if (_entries.Count == 0) AppendLog("Nenhum Clash Test encontrado.");
                else AppendLog($"{_entries.Count} teste(s) carregado(s).");
            }
            catch (Exception ex) { AppendLog($"Erro ao carregar testes: {ex.Message}"); }
        }

        private static string TranslateTestStatus(ClashTestStatus s) => s switch {
            ClashTestStatus.New      => "Não executado",
            ClashTestStatus.Old      => "Desatualizado",
            ClashTestStatus.Partial  => "Parcial",
            ClashTestStatus.Complete => "Completo",
            _                        => s.ToString(),
        };

        // -----------------------------------------------------------------------
        // Model property scanner (STA thread — never Task.Run)
        // -----------------------------------------------------------------------

        private void ScanModel()
        {
            TxtScanStatus.Text = "Escaneando...";
            try
            {
                var catalog  = NavisPropertyScanner.Scan();
                _catalog     = catalog;
                _allPsets    = catalog.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
                _allProps    = catalog.Values.SelectMany(v => v)
                                     .Distinct(StringComparer.OrdinalIgnoreCase)
                                     .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
                TxtScanStatus.Text = _allPsets.Count > 0
                    ? $"{_allPsets.Count} categorias"
                    : "Nenhum modelo";
            }
            catch (Exception ex) { TxtScanStatus.Text = $"Erro: {ex.Message}"; }
        }

        private void BtnScanModel_Click(object sender, RoutedEventArgs e) => ScanModel();

        // -----------------------------------------------------------------------
        // ComboBox loaders for property DataGrid
        // -----------------------------------------------------------------------

        private void CmbPropCategory_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is ComboBox cmb) cmb.ItemsSource = _allPsets;
        }

        private void CmbPropProperty_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ComboBox cmb) return;
            var cat = cmb.Tag as string;
            cmb.ItemsSource = (!string.IsNullOrEmpty(cat) && _catalog.TryGetValue(cat!, out var props))
                ? props : _allProps;
        }

        // -----------------------------------------------------------------------
        // Property grid buttons
        // -----------------------------------------------------------------------

        private void BtnAddProp_Click(object sender, RoutedEventArgs e)
        {
            var rule = new ClashPropertyRule();
            _propRules.Add(rule);
            GridProps.SelectedItem = rule;
            GridProps.CurrentItem  = rule;
            GridProps.BeginEdit();
        }

        private void BtnRemoveProp_Click(object sender, RoutedEventArgs e)
        {
            if (GridProps.SelectedItem is ClashPropertyRule r) _propRules.Remove(r);
        }

        // -----------------------------------------------------------------------
        // Test list buttons
        // -----------------------------------------------------------------------

        private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var en in _entries) en.IsSelected = true;
            ListTests.Items.Refresh();
        }

        private void BtnDeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var en in _entries) en.IsSelected = false;
            ListTests.Items.Refresh();
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            TxtLog.Clear();
            LoadTests();
        }

        // -----------------------------------------------------------------------
        // Export
        // -----------------------------------------------------------------------

        private async void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            var selectedTests = _entries.Where(en => en.IsSelected).Select(en => en.Test).ToList();
            if (selectedTests.Count == 0)
            {
                MessageBox.Show("Selecione ao menos um teste de clash.",
                    "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(TxtOutputPath.Text))
            {
                MessageBox.Show("Selecione o caminho do arquivo de saída.",
                    "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Collect active status filters
            var allowedStatuses = new HashSet<string>();
            if (ChkNew.IsChecked      == true) allowedStatuses.Add("Novo");
            if (ChkActive.IsChecked   == true) allowedStatuses.Add("Ativo");
            if (ChkReviewed.IsChecked == true) allowedStatuses.Add("Revisado");
            if (ChkApproved.IsChecked == true) allowedStatuses.Add("Aprovado");
            if (ChkResolved.IsChecked == true) allowedStatuses.Add("Resolvido");

            if (allowedStatuses.Count == 0)
            {
                MessageBox.Show("Selecione ao menos um status para incluir no export.",
                    "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            char separator = RbSemicolon.IsChecked == true ? ';' : ',';
            string outputPath = TxtOutputPath.Text;
            var propRules = _propRules.Where(r => !string.IsNullOrEmpty(r.ColumnName)).ToList();

            BtnExport.IsEnabled      = false;
            TxtLog.Clear();
            PanelProgress.Visibility = Visibility.Visible;
            TxtPct.Text              = "Coletando...";
            // Yield so WPF renders the progress bar before the synchronous collection starts
            await System.Threading.Tasks.Task.Yield();

            // Snapshot all data on UI/STA thread before async write
            AppendLog("Coletando dados dos clashes...");
            var snapshots = selectedTests
                .SelectMany(t => CollectSnapshots(t, allowedStatuses, propRules))
                .ToList();
            AppendLog($"{snapshots.Count} clash(es) após filtro de status. Escrevendo CSV...");
            TxtPct.Text = "Gravando CSV...";

            try
            {
                await Task.Run(() => WriteSnapshotsCsv(snapshots, outputPath, separator, propRules));
                TxtPct.Text = "Concluído";
                MessageBox.Show("CSV exportado com sucesso!", "Sucesso",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                TxtPct.Text = "Erro";
                AppendLog($"ERRO: {ex.Message}");
                MessageBox.Show($"Erro ao exportar:\n{ex.Message}", "Erro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnExport.IsEnabled      = true;
                PanelProgress.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog { Title = "Salvar CSV", Filter = "CSV (*.csv)|*.csv",
                DefaultExt = ".csv", FileName = "clashes_export.csv" };
            if (dlg.ShowDialog() == true) TxtOutputPath.Text = dlg.FileName;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        // -----------------------------------------------------------------------
        // Snapshot (STA thread)
        // -----------------------------------------------------------------------

        private sealed class ClashRow
        {
            public string TestName = "", GroupName = "", ClashName = "";
            public string Status = "", Type = "", Distance = "", Priority = "";
            public string Description = "", AssignedTo = "";
            public string CreatedTime = "", ApprovedTime = "", ResolvedTime = "";
            public string GuidA = "", NameA = "", ClassA = "", FileA = "";
            public string GuidB = "", NameB = "", ClassB = "", FileB = "";
            public string CenterX = "", CenterY = "", CenterZ = "";
            public string[] ExtraA = System.Array.Empty<string>();
            public string[] ExtraB = System.Array.Empty<string>();
        }

        private static List<ClashRow> CollectSnapshots(
            ClashTest test,
            HashSet<string> allowedStatuses,
            IList<ClashPropertyRule> propRules)
        {
            var rows = new List<ClashRow>();
            string testName = test.DisplayName;

            foreach (SavedItem item in test.Children)
            {
                if (item is ClashResult r)
                {
                    var row = SnapshotResult(testName, "", r, propRules);
                    if (allowedStatuses.Contains(row.Status)) rows.Add(row);
                }
                else if (item is ClashResultGroup g)
                {
                    foreach (SavedItem child in g.Children)
                    {
                        if (child is ClashResult cr)
                        {
                            var row = SnapshotResult(testName, g.DisplayName, cr, propRules);
                            if (allowedStatuses.Contains(row.Status)) rows.Add(row);
                        }
                    }
                }
            }
            return rows;
        }

        private static ClashRow SnapshotResult(
            string testName, string groupName, ClashResult r,
            IList<ClashPropertyRule> propRules)
        {
            string F4(double v) => v.ToString("F4", System.Globalization.CultureInfo.InvariantCulture);
            var row = new ClashRow {
                TestName     = testName,
                GroupName    = groupName,
                ClashName    = r.DisplayName,
                Status       = TranslateStatus(r.Status),
                Type         = TranslateType(r.TestType),
                Distance     = F4(r.Distance),
                Priority     = r.Priority.ToString(),
                Description  = r.Description ?? "",
                AssignedTo   = r.AssignedTo?.DisplayName ?? "",
                CreatedTime  = FormatDate(r.CreatedTime),
                ApprovedTime = FormatDate(r.ApprovedTime),
                ResolvedTime = FormatDate(r.ResolvedTime),
                GuidA  = ResolveGuid(r.Item1),
                NameA  = r.Item1?.DisplayName ?? "",
                ClassA = r.Item1?.ClassDisplayName ?? "",
                FileA  = GetSourceFile(r.Item1),
                GuidB  = ResolveGuid(r.Item2),
                NameB  = r.Item2?.DisplayName ?? "",
                ClassB = r.Item2?.ClassDisplayName ?? "",
                FileB  = GetSourceFile(r.Item2),
                CenterX = F4(r.Center.X),
                CenterY = F4(r.Center.Y),
                CenterZ = F4(r.Center.Z),
            };
            row.ExtraA = propRules.Select(p => GetPropValue(r.Item1, p.Category, p.Property)).ToArray();
            row.ExtraB = propRules.Select(p => GetPropValue(r.Item2, p.Category, p.Property)).ToArray();
            return row;
        }

        // Item1/Item2 do clash geralmente apontam para um nó de geometria folha (ex.: "Solid"),
        // que não carrega as propriedades do elemento — essas vivem num ancestral (Insert/Revit
        // element). Sobe a árvore (o próprio item tem prioridade) até achar a categoria/propriedade.
        private const int MaxAncestorDepth = 8;

        private static string GetPropValue(ModelItem? item, string category, string propName)
        {
            if (item == null || string.IsNullOrEmpty(category) || string.IsNullOrEmpty(propName)) return "";
            foreach (var node in item.AncestorsAndSelf.Take(MaxAncestorDepth + 1))
            {
                foreach (var cat in node.PropertyCategories)
                {
                    if (!cat.DisplayName.Equals(category, StringComparison.OrdinalIgnoreCase)) continue;
                    foreach (var p in cat.Properties)
                        if (p.DisplayName.Equals(propName, StringComparison.OrdinalIgnoreCase))
                        {
                            var val = p.Value?.ToDisplayString();
                            if (!string.IsNullOrEmpty(val)) return val!;
                        }
                }
            }
            return "";
        }

        // Real persistent GUID (Revit UniqueId / IFC GlobalId) lives in the model's own
        // properties, not in ModelItem.InstanceGuid (which is Guid.Empty outside Revit-native
        // models). Search item + ancestors for common GUID property names; InstanceGuid is a
        // last-resort fallback for Revit-native models that don't expose it as a property.
        private static readonly string[] GuidPropertyNames =
            { "GUID", "Global Id", "GlobalId", "IFC GUID", "Unique Id", "UniqueId" };

        private static string ResolveGuid(ModelItem? item)
        {
            if (item == null) return "";
            foreach (var node in item.AncestorsAndSelf.Take(MaxAncestorDepth + 1))
            {
                foreach (var cat in node.PropertyCategories)
                {
                    foreach (var p in cat.Properties)
                    {
                        foreach (var name in GuidPropertyNames)
                        {
                            if (!p.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                            var val = p.Value?.ToDisplayString();
                            if (!string.IsNullOrWhiteSpace(val)) return val!;
                        }
                    }
                }
            }
            return item.InstanceGuid != Guid.Empty ? item.InstanceGuid.ToString() : "";
        }

        private static string GetSourceFile(ModelItem? item)
            => item?.AncestorsAndSelf.LastOrDefault()?.DisplayName ?? "";

        // -----------------------------------------------------------------------
        // CSV writer (background thread)
        // -----------------------------------------------------------------------

        private static void WriteSnapshotsCsv(
            IEnumerable<ClashRow> rows, string path, char sep,
            IList<ClashPropertyRule> propRules)
        {
            string[] baseHeader = {
                "TesteName","GrupoNome","ClashNome","Status","Tipo",
                "Distancia_m","Prioridade","Descricao","AtribuidoPara",
                "DataCriacao","DataAprovacao","DataResolucao",
                "ElementoA_GUID","ElementoA_Nome","ElementoA_Classe","ElementoA_Arquivo",
                "ElementoB_GUID","ElementoB_Nome","ElementoB_Classe","ElementoB_Arquivo",
                "Centro_X","Centro_Y","Centro_Z",
            };

            var extraHeader = propRules
                .SelectMany(r => new[] { $"ElementoA_{r.ColumnName}", $"ElementoB_{r.ColumnName}" })
                .ToArray();

            string sepStr = sep.ToString();
            string Q(string v) {
                if (string.IsNullOrEmpty(v)) return "";
                if (v.Contains(sep) || v.Contains('"') || v.Contains('\n'))
                    return "\"" + v.Replace("\"", "\"\"") + "\"";
                return v;
            }

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            using var w = new System.IO.StreamWriter(path, false, new System.Text.UTF8Encoding(true));

            w.WriteLine(string.Join(sepStr, baseHeader.Concat(extraHeader).Select(Q)));

            foreach (var r in rows)
            {
                string[] baseCols = {
                    r.TestName, r.GroupName, r.ClashName, r.Status, r.Type,
                    r.Distance, r.Priority, r.Description, r.AssignedTo,
                    r.CreatedTime, r.ApprovedTime, r.ResolvedTime,
                    r.GuidA, r.NameA, r.ClassA, r.FileA,
                    r.GuidB, r.NameB, r.ClassB, r.FileB,
                    r.CenterX, r.CenterY, r.CenterZ,
                };

                // Interleave ExtraA[i], ExtraB[i] for each rule
                var extraCols = new List<string>();
                for (int i = 0; i < r.ExtraA.Length; i++)
                {
                    extraCols.Add(r.ExtraA[i]);
                    extraCols.Add(r.ExtraB[i]);
                }

                w.WriteLine(string.Join(sepStr, baseCols.Concat(extraCols).Select(Q)));
            }
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private static string TranslateStatus(ClashResultStatus s) => s switch {
            ClashResultStatus.New      => "Novo",
            ClashResultStatus.Active   => "Ativo",
            ClashResultStatus.Reviewed => "Revisado",
            ClashResultStatus.Approved => "Aprovado",
            ClashResultStatus.Resolved => "Resolvido",
            _                          => s.ToString(),
        };

        private static string TranslateType(ClashTestType t) => t switch {
            ClashTestType.Hard             => "Duro",
            ClashTestType.HardConservative => "Duro conservador",
            ClashTestType.Clearance        => "Folga",
            ClashTestType.Duplicate        => "Duplicado",
            ClashTestType.Custom           => "Personalizado",
            _                              => t.ToString(),
        };

        private static string FormatDate(DateTime? dt) => dt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "";

        private void AppendLog(string msg)
        {
            TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
            TxtLog.ScrollToEnd();
        }
    }

    // -----------------------------------------------------------------------
    // ClashPropertyRule view-model
    // -----------------------------------------------------------------------

    public class ClashPropertyRule : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private string _col = "", _cat = "", _prop = "";

        public string ColumnName { get => _col;  set { _col  = value; Notify(); } }
        public string Category   { get => _cat;  set { _cat  = value; Notify(); } }
        public string Property   { get => _prop; set { _prop = value; Notify(); } }

        private void Notify([System.Runtime.CompilerServices.CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    // -----------------------------------------------------------------------
    // ClashTestEntry view-model (unchanged)
    // -----------------------------------------------------------------------

    public class ClashTestEntry : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private bool _isSelected;

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
        }

        public string    DisplayName { get; set; } = "";
        public int       ResultCount { get; set; }
        public string    StatusLabel { get; set; } = "";
        public ClashTest Test        { get; set; } = null!;

        public string Label      => DisplayName;
        public string CountLabel => $"{ResultCount} clash{(ResultCount != 1 ? "es" : "")}";
    }
}
