using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using NavisworksIfcExporter.Core;
using NavisworksIfcExporter.Models;

namespace NavisworksIfcExporter.UI
{
    public partial class ExcelImportWindow : Window
    {
        private readonly ExcelImportService _service = new();
        private readonly ObservableCollection<ColumnItem> _columns = new();
        private Dictionary<string, List<string>> _knownTabs = new();

        public ExcelImportWindow()
        {
            InitializeComponent();
            LstColumns.ItemsSource = _columns;
            _service.ProgressChanged += (_, msg) =>
                Dispatcher.BeginInvoke(new Action(() => AppendLog(msg)));
        }

        // -----------------------------------------------------------------------
        // Arquivo Excel
        // -----------------------------------------------------------------------

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Planilhas Excel|*.xlsx;*.xls;*.xlsb|Todos os arquivos|*.*",
                Title  = "Selecionar planilha Excel",
            };
            if (dlg.ShowDialog() != true) return;

            TxtFilePath.Text = dlg.FileName;
            CmbSheet.ItemsSource = null;
            CmbExcelKey.ItemsSource = null;
            _columns.Clear();
            TxtColInfo.Text = "Lendo planilha...";

            try
            {
                var info = _service.ReadFileInfo(dlg.FileName);
                CmbSheet.ItemsSource = info.SheetNames;
                if (info.SheetNames.Count > 0)
                    CmbSheet.SelectedIndex = 0;
                TxtColInfo.Text = "Selecione uma planilha e a coluna chave.";
            }
            catch (Exception ex)
            {
                TxtColInfo.Text = $"Erro ao ler arquivo: {ex.Message}";
            }
        }

        private void CmbSheet_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(TxtFilePath.Text) || CmbSheet.SelectedIndex < 0) return;

            try
            {
                var cols = _service.ReadColumnsFromSheet(TxtFilePath.Text, CmbSheet.SelectedIndex);
                CmbExcelKey.ItemsSource = cols;
                if (cols.Count > 0) CmbExcelKey.SelectedIndex = 0;
                PopulateColumnList(cols);
            }
            catch (Exception ex)
            {
                AppendLog($"Erro ao ler planilha: {ex.Message}");
            }
        }

        private void PopulateColumnList(List<string> cols)
        {
            _columns.Clear();
            foreach (var c in cols)
                _columns.Add(new ColumnItem { Name = c, IsChecked = true });

            TxtColInfo.Text = $"{cols.Count} coluna(s) encontrada(s). Desmarque as que não quer importar.";
        }

        // -----------------------------------------------------------------------
        // Chave no Modelo
        // -----------------------------------------------------------------------

        private void BtnScanModel_Click(object sender, RoutedEventArgs e)
        {
            AppendLog("Escaneando propriedades do modelo...");
            try
            {
                _knownTabs = NavisPropertyScanner.Scan(maxItems: 5000);
                CmbModelCategory.ItemsSource = _knownTabs.Keys.OrderBy(k => k).ToList();
                AppendLog($"  {_knownTabs.Count} categoria(s) encontrada(s).");
            }
            catch (Exception ex)
            {
                AppendLog($"Erro ao escanear modelo: {ex.Message}");
            }
        }

        private void CmbModelCategory_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            var cat = CmbModelCategory.SelectedItem as string ?? CmbModelCategory.Text;
            if (_knownTabs.TryGetValue(cat, out var props))
                CmbModelProperty.ItemsSource = props.OrderBy(p => p).ToList();
        }

        // -----------------------------------------------------------------------
        // Seleção de colunas
        // -----------------------------------------------------------------------

        private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var c in _columns) c.IsChecked = true;
        }

        private void BtnClearAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var c in _columns) c.IsChecked = false;
        }

        // -----------------------------------------------------------------------
        // Importação
        // -----------------------------------------------------------------------

        private async void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInputs()) return;

            var excelKeyCol   = CmbExcelKey.Text.Trim();
            var modelCategory = (CmbModelCategory.SelectedItem as string ?? CmbModelCategory.Text).Trim();
            var modelProperty = (CmbModelProperty.SelectedItem as string ?? CmbModelProperty.Text).Trim();
            var targetTab     = TxtTargetTab.Text.Trim();
            var sheetIndex    = CmbSheet.SelectedIndex;
            var selectionOnly = RbSelection.IsChecked == true;

            var columnsToImport = _columns
                .Where(c => c.IsChecked && !string.Equals(c.Name, excelKeyCol, StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Name)
                .ToList();

            if (columnsToImport.Count == 0)
            {
                MessageBox.Show("Selecione pelo menos uma coluna para importar (diferente da coluna chave).",
                    "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var options = new ExcelImportOptions
            {
                FilePath         = TxtFilePath.Text,
                SheetIndex       = sheetIndex,
                ExcelKeyColumn   = excelKeyCol,
                ModelKeyCategory = modelCategory,
                ModelKeyProperty = modelProperty,
                TargetTabName    = targetTab,
                ColumnsToImport  = columnsToImport,
                SelectionOnly    = selectionOnly,
            };

            BtnImport.IsEnabled = false;
            PanelProgress.Visibility = Visibility.Visible;
            TxtLog.Visibility = Visibility.Visible;
            PrgBar.IsIndeterminate = true;
            TxtPct.Text = "...";

            try
            {
                var doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
                ImportResult result = null!;
                await Task.Run(() =>
                {
                    result = _service.Import(options, doc!);
                });

                PrgBar.IsIndeterminate = false;
                PrgBar.Value = 100;
                TxtPct.Text = "100%";

                var summary = $"Concluído — {result.MatchedElements} elemento(s), " +
                              $"{result.WrittenProperties} propriedade(s) gravada(s).";
                if (result.UnmatchedRows > 0)
                    summary += $" {result.UnmatchedRows} linha(s) sem correspondência.";

                AppendLog(summary);

                foreach (var w in result.Warnings)
                    AppendLog($"  AVISO: {w}");

                MessageBox.Show(summary, "Importação concluída",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                PrgBar.IsIndeterminate = false;
                AppendLog($"ERRO: {ex.Message}");
                MessageBox.Show($"Falha na importação:\n\n{ex.Message}", "Erro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnImport.IsEnabled = true;
                PanelProgress.Visibility = Visibility.Collapsed;
            }
        }

        private bool ValidateInputs()
        {
            if (string.IsNullOrEmpty(TxtFilePath.Text))
            {
                MessageBox.Show("Selecione um arquivo Excel.", "Aviso",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            if (CmbSheet.SelectedIndex < 0)
            {
                MessageBox.Show("Selecione uma planilha.", "Aviso",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            if (string.IsNullOrWhiteSpace(CmbExcelKey.Text))
            {
                MessageBox.Show("Selecione a coluna chave do Excel.", "Aviso",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            if (string.IsNullOrWhiteSpace(CmbModelCategory.Text))
            {
                MessageBox.Show("Informe a categoria da propriedade chave no modelo.", "Aviso",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            if (string.IsNullOrWhiteSpace(CmbModelProperty.Text))
            {
                MessageBox.Show("Informe o nome da propriedade chave no modelo.", "Aviso",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            if (string.IsNullOrWhiteSpace(TxtTargetTab.Text))
            {
                MessageBox.Show("Informe o nome da aba de destino.", "Aviso",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            return true;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void AppendLog(string msg)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => AppendLog(msg)));
                return;
            }
            TxtLog.Visibility = Visibility.Visible;
            TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
            TxtLog.ScrollToEnd();
        }
    }

    internal class ColumnItem : INotifyPropertyChanged
    {
        private bool _isChecked;
        public string Name { get; set; } = string.Empty;
        public bool IsChecked
        {
            get => _isChecked;
            set { _isChecked = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked))); }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
