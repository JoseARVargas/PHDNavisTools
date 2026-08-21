using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using Autodesk.Navisworks.Api;
using PHDNavisTools.Core;
using NavisApp = Autodesk.Navisworks.Api.Application;

namespace PHDNavisTools.UI
{
    public partial class ClearPropertiesWindow : Window
    {
        private Dictionary<string, List<string>> _knownTabs = new();
        private readonly ObservableCollection<PropItem> _propItems = new();

        public ClearPropertiesWindow()
        {
            InitializeComponent();
            LstProperties.ItemsSource = _propItems;
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _knownTabs = NavisPropertyScanner.Scan(maxItems: 3000);
                CmbTab.ItemsSource = _knownTabs.Keys.OrderBy(k => k).ToList();
            }
            catch (Exception ex)
            {
                AppendLog($"Aviso ao ler abas do modelo: {ex.Message}");
            }

            AppendLog($"{_knownTabs.Count} aba(s) encontrada(s) no modelo.");
        }

        // -----------------------------------------------------------------------
        // Eventos de controle
        // -----------------------------------------------------------------------

        private void CmbTab_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            RefreshPropertyList();
        }

        private void RdoMode_Checked(object sender, RoutedEventArgs e)
        {
            if (BorderProps == null) return;
            BorderProps.Visibility = RdoSpecific.IsChecked == true
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void BtnMarkAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _propItems) item.IsChecked = true;
        }

        private void BtnClearMark_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _propItems) item.IsChecked = false;
        }

        private void RefreshPropertyList()
        {
            _propItems.Clear();
            var tab = CmbTab.Text?.Trim();
            if (string.IsNullOrEmpty(tab)) return;

            if (_knownTabs.TryGetValue(tab, out var props))
            {
                foreach (var p in props.OrderBy(p => p))
                    _propItems.Add(new PropItem { Name = p, IsChecked = false });
            }
        }

        // -----------------------------------------------------------------------
        // Aplicar
        // -----------------------------------------------------------------------

        private async void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            var tab = (CmbTab.Text ?? "").Trim();
            if (string.IsNullOrEmpty(tab))
            {
                MessageBox.Show("Informe o nome da aba.", "Aviso",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool wholeTab      = RdoWholeTab.IsChecked == true;
            bool selectionOnly = RdoSelection.IsChecked == true;

            List<string> selectedProps = new();
            if (!wholeTab)
            {
                selectedProps = _propItems.Where(p => p.IsChecked).Select(p => p.Name).ToList();
                if (selectedProps.Count == 0)
                {
                    MessageBox.Show("Selecione ao menos uma propriedade para remover.", "Aviso",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            var doc = NavisApp.ActiveDocument;
            if (doc == null) { AppendLog("Nenhum documento aberto."); return; }

            BtnApply.IsEnabled = false;

            var scopeLabel = selectionOnly ? "elementos selecionados" : "todos os elementos";
            var modeLabel  = wholeTab ? $"aba '{tab}' inteira" : $"{selectedProps.Count} propriedade(s) da aba '{tab}'";
            AppendLog($"Iniciando remoção de {modeLabel} em {scopeLabel}...");

            try
            {
                EraseResult result = await Task.Run(() =>
                {
                    var items  = GetScope(doc, selectionOnly);
                    var eraser = new PropertyEraser();

                    return wholeTab
                        ? eraser.DeleteTab(items, tab)
                        : eraser.DeleteProperties(items, tab, selectedProps);
                });

                foreach (var w in result.Warnings)
                    AppendLog($"⚠ {w}");

                var summary = $"Pronto: {result.ElementsAffected} elemento(s) limpo(s)" +
                              (result.ElementsSkipped > 0 ? $", {result.ElementsSkipped} sem a aba (ignorados)" : "") + ".";
                AppendLog(summary);
                MessageBox.Show(summary, "Concluído", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AppendLog($"ERRO: {ex.Message}");
                MessageBox.Show($"Falha ao remover propriedades:\n\n{ex.Message}", "Erro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnApply.IsEnabled = true;
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private static IEnumerable<ModelItem> GetScope(Document doc, bool selectionOnly)
        {
            if (selectionOnly)
                return doc.CurrentSelection.SelectedItems;

            return BfsAllItems(doc);
        }

        private static IEnumerable<ModelItem> BfsAllItems(Document doc)
        {
            var queue = new Queue<ModelItem>();
            foreach (Model model in doc.Models)
                if (model.RootItem != null)
                    queue.Enqueue(model.RootItem);

            while (queue.Count > 0)
            {
                var item = queue.Dequeue();
                yield return item;
                foreach (var child in item.Children)
                    queue.Enqueue(child);
            }
        }

        private void AppendLog(string msg)
        {
            TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
            TxtLog.ScrollToEnd();
        }
    }

    public class PropItem : INotifyPropertyChanged
    {
        private bool _isChecked;

        public string Name { get; set; } = "";

        public bool IsChecked
        {
            get => _isChecked;
            set { _isChecked = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
