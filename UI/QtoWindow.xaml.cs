using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Autodesk.Navisworks.Api;
using PHDNavisTools.Core;

namespace PHDNavisTools.UI
{
    public partial class QtoWindow : Window
    {
        // View models
        private readonly ObservableCollection<SetRuleVm>  _setRules  = new();
        private readonly ObservableCollection<PropRuleVm> _propRules = new();

        // Cached data
        private List<QtoItemInfo>             _qtoItems   = new();
        private List<string>                  _setNames   = new();
        private Dictionary<string, List<string>> _propCatalog = new();
        private List<string>                  _allProps   = new();
        private List<string>                  _allPsets   = new();

        public QtoWindow()
        {
            InitializeComponent();
            GridSetRules.ItemsSource  = _setRules;
            GridPropRules.ItemsSource = _propRules;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshAll();
        }

        // -----------------------------------------------------------------------
        // Refresh / Scan
        // -----------------------------------------------------------------------

        private void RefreshAll()
        {
            var doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
            if (doc == null) { AppendLog("Nenhum documento aberto."); return; }

            // QTO items
            _qtoItems = QtoService.GetItems(doc);
            AppendLog(_qtoItems.Count > 0
                ? $"QTO: {_qtoItems.Count} item(ns) encontrado(s)."
                : "QTO: workbook não inicializado ou sem itens.");

            // Selection/Search sets
            _setNames = CollectSetNames(doc.SelectionSets.Value);
            AppendLog($"Sets: {_setNames.Count} encontrado(s).");

            // Property catalog
            ScanModel();

            GridSetRules.Items.Refresh();
            GridPropRules.Items.Refresh();
        }

        private void ScanModel()
        {
            try
            {
                var cat = NavisPropertyScanner.Scan();
                _propCatalog = cat;
                _allPsets    = cat.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
                _allProps    = cat.Values.SelectMany(v => v)
                                  .Distinct(StringComparer.OrdinalIgnoreCase)
                                  .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch { }
        }

        private static List<string> CollectSetNames(SavedItemCollection coll)
        {
            var list = new List<string>();
            foreach (var item in coll)
            {
                if (item is SelectionSet ss) list.Add(ss.DisplayName);
                else if (item is Autodesk.Navisworks.Api.GroupItem g) list.AddRange(CollectSetNames(g.Children));
            }
            return list;
        }

        // -----------------------------------------------------------------------
        // Tab switch
        // -----------------------------------------------------------------------

        private void TabMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            bool isSet = TabMode.SelectedIndex == 0;
            if (GridSetRules  != null) GridSetRules.Visibility  = isSet ? Visibility.Visible : Visibility.Collapsed;
            if (GridPropRules != null) GridPropRules.Visibility = isSet ? Visibility.Collapsed : Visibility.Visible;
        }

        // -----------------------------------------------------------------------
        // SearchSet tab buttons
        // -----------------------------------------------------------------------

        private void BtnAddSetRule_Click(object sender, RoutedEventArgs e)
        {
            var rule = new SetRuleVm();
            _setRules.Add(rule);
            GridSetRules.SelectedItem = rule;
            GridSetRules.BeginEdit();
        }

        private void BtnRemoveSetRule_Click(object sender, RoutedEventArgs e)
        {
            if (GridSetRules.SelectedItem is SetRuleVm r) _setRules.Remove(r);
        }

        private void BtnRefreshLists_Click(object sender, RoutedEventArgs e)
        {
            TxtLog.Clear();
            RefreshAll();
        }

        // -----------------------------------------------------------------------
        // Property tab buttons
        // -----------------------------------------------------------------------

        private void BtnAddPropRule_Click(object sender, RoutedEventArgs e)
        {
            var rule = new PropRuleVm();
            _propRules.Add(rule);
            GridPropRules.SelectedItem = rule;
            GridPropRules.BeginEdit();
        }

        private void BtnRemovePropRule_Click(object sender, RoutedEventArgs e)
        {
            if (GridPropRules.SelectedItem is PropRuleVm r) _propRules.Remove(r);
        }

        private void BtnScanModel_Click(object sender, RoutedEventArgs e) => ScanModel();

        // -----------------------------------------------------------------------
        // ComboBox loaders — SearchSet grid
        // -----------------------------------------------------------------------

        private void CmbSetName_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is ComboBox cmb) cmb.ItemsSource = _setNames;
        }

        private void CmbQtoItem_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is ComboBox cmb) cmb.ItemsSource = _qtoItems;
        }

        private void CmbQtoItem_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox cmb && cmb.SelectedItem is QtoItemInfo info
                && cmb.DataContext is SetRuleVm vm)
            {
                vm.ItemCode = info.Code;
                vm.ItemName = info.DisplayLabel;
                vm.ItemRowId = info.RowId;
            }
        }

        // -----------------------------------------------------------------------
        // ComboBox loaders — Property grid
        // -----------------------------------------------------------------------

        private void CmbPropCategory_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is ComboBox cmb) cmb.ItemsSource = _allPsets;
        }

        private void CmbPropProperty_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ComboBox cmb) return;
            var cat = cmb.Tag as string;
            cmb.ItemsSource = (!string.IsNullOrEmpty(cat) && _propCatalog.TryGetValue(cat!, out var props))
                ? props : _allProps;
        }

        private void CmbQtoItemProp_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is ComboBox cmb) cmb.ItemsSource = _qtoItems;
        }

        private void CmbQtoItemProp_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox cmb && cmb.SelectedItem is QtoItemInfo info
                && cmb.DataContext is PropRuleVm vm)
            {
                vm.ItemCode = info.Code;
                vm.ItemName = info.DisplayLabel;
                vm.ItemRowId = info.RowId;
            }
        }

        // -----------------------------------------------------------------------
        // Run
        // -----------------------------------------------------------------------

        private async void BtnRun_Click(object sender, RoutedEventArgs e)
        {
            var doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
            if (doc == null) { AppendLog("Nenhum documento aberto."); return; }

            if (!QtoService.IsAvailable(doc))
            {
                MessageBox.Show(
                    "O Quantity Takeoff não está inicializado neste documento.\n\n" +
                    "Abra o painel Quantification no Navisworks e crie/carregue um workbook antes de usar esta função.",
                    "QTO não disponível", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool isSetMode  = TabMode.SelectedIndex == 0;
            var  updateMode = RbClearReattach.IsChecked == true
                              ? QtoUpdateMode.ClearAndReattach
                              : QtoUpdateMode.AppendOnly;

            if (ChkExportHistory.IsChecked == true && updateMode == QtoUpdateMode.ClearAndReattach)
            {
                try
                {
                    string dir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        "PHD_QTO_Historico");
                    QtoService.ExportHistoryCsv(doc, _qtoItems, dir);
                    AppendLog($"Histórico exportado para: {dir}");
                }
                catch (Exception ex) { AppendLog($"Aviso: não foi possível exportar histórico: {ex.Message}"); }
            }

            BtnRun.IsEnabled = false;
            TxtLog.Clear();
            SetProgress(true, 0);

            try
            {
                QtoRunResult result;

                if (isSetMode)
                {
                    var rules = _setRules
                        .Where(r => !string.IsNullOrEmpty(r.SetName) && !string.IsNullOrEmpty(r.ItemCode))
                        .Select(r => new QtoSearchSetRule {
                            SetName   = r.SetName,
                            ItemCode  = r.ItemCode,
                            ItemName  = r.ItemName,
                            ItemRowId = r.ItemRowId,
                        }).ToList();

                    if (rules.Count == 0)
                    {
                        AppendLog("Nenhuma regra de mapeamento definida.");
                        return;
                    }

                    result = await QtoService.RunSearchSetMappingAsync(doc, rules, updateMode, _qtoItems,
                        async (done, total) => {
                            SetProgress(true, total > 0 ? (double)done / total * 100 : 0);
                            await Dispatcher.Yield(DispatcherPriority.Background);
                        });
                }
                else
                {
                    var rules = _propRules
                        .Where(r => !string.IsNullOrEmpty(r.Category)
                                 && !string.IsNullOrEmpty(r.PropertyName)
                                 && !string.IsNullOrEmpty(r.Value)
                                 && !string.IsNullOrEmpty(r.ItemCode))
                        .Select(r => new QtoPropertyRule {
                            Category     = r.Category,
                            PropertyName = r.PropertyName,
                            Value        = r.Value,
                            ItemCode     = r.ItemCode,
                            ItemName     = r.ItemName,
                            ItemRowId    = r.ItemRowId,
                        }).ToList();

                    if (rules.Count == 0)
                    {
                        AppendLog("Nenhuma regra de propriedade definida.");
                        return;
                    }

                    result = await QtoService.RunPropertyMappingAsync(doc, rules, updateMode, _qtoItems,
                        async (done, total) => {
                            SetProgress(true, total > 0 ? (double)done / total * 100 : 0);
                            await Dispatcher.Yield(DispatcherPriority.Background);
                        });
                }

                AppendLog($"Mapeados:      {result.Mapped} elemento(s)");
                AppendLog($"Não mapeados:  {result.Unmapped} elemento(s)");
                if (result.Duplicates > 0)
                    AppendLog($"Ignorados (já vinculados): {result.Duplicates}");
                foreach (var err in result.Errors)
                    AppendLog($"⚠ {err}");

                try
                {
                    QtoService.UpdateSelectionSets(doc, result.MappedItems, result.UnmappedItems);
                    AppendLog("Sets de seleção atualizados.");
                }
                catch (Exception ex) { AppendLog($"Aviso: não foi possível atualizar sets: {ex.Message}"); }
            }
            catch (Exception ex)
            {
                AppendLog($"ERRO: {ex.Message}");
            }
            finally
            {
                BtnRun.IsEnabled = true;
                SetProgress(false, 100);
            }
        }

        private void SetProgress(bool visible, double value)
        {
            PanelProgress.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            PrgBar.Value = value;
            TxtPct.Text  = $"{(int)value}%";
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void AppendLog(string msg)
        {
            TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
            TxtLog.ScrollToEnd();
        }
    }

    // -----------------------------------------------------------------------
    // View models
    // -----------------------------------------------------------------------

    public class SetRuleVm : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private string _setName = "", _itemCode = "", _itemName = "";
        private long   _itemRowId;

        public string SetName  { get => _setName;  set { _setName  = value; Notify(); } }
        public string ItemCode { get => _itemCode; set { _itemCode = value; Notify(); } }
        public string ItemName { get => _itemName; set { _itemName = value; Notify(); } }
        public long   ItemRowId { get => _itemRowId; set { _itemRowId = value; Notify(); } }

        private void Notify([System.Runtime.CompilerServices.CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public class PropRuleVm : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private string _cat = "", _prop = "", _val = "", _code = "", _name = "";
        private long   _rowId;

        public string Category     { get => _cat;   set { _cat   = value; Notify(); } }
        public string PropertyName { get => _prop;  set { _prop  = value; Notify(); } }
        public string Value        { get => _val;   set { _val   = value; Notify(); } }
        public string ItemCode     { get => _code;  set { _code  = value; Notify(); } }
        public string ItemName     { get => _name;  set { _name  = value; Notify(); } }
        public long   ItemRowId    { get => _rowId; set { _rowId = value; Notify(); } }

        private void Notify([System.Runtime.CompilerServices.CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
