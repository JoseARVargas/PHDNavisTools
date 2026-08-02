using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Autodesk.Navisworks.Api;
using PHDNavisTools.Core;

namespace PHDNavisTools.UI
{
    public partial class WritePropertiesWindow : Window
    {
        // Mapa aba → lista de propriedades (do NavisPropertyScanner)
        private Dictionary<string, List<string>> _knownTabs = new();

        // Elementos que serão modificados
        private List<ModelItem> _targetItems = new();

        public WritePropertiesWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        // -----------------------------------------------------------------------
        // Inicialização
        // -----------------------------------------------------------------------

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            LoadSelection();
            LoadKnownTabs();
        }

        private void LoadSelection()
        {
            var doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
            if (doc == null) return;

            // Usa os itens exatamente como selecionados, sem descer para os filhos.
            // Propriedades são gravadas no nível que o usuário selecionou.
            _targetItems = doc.CurrentSelection.SelectedItems
                .Distinct()
                .ToList();

            TxtSelectionInfo.Text = _targetItems.Count == 0
                ? "Nenhum elemento selecionado."
                : $"{_targetItems.Count} elemento(s) selecionado(s).";

            BtnApply.IsEnabled = _targetItems.Count > 0;
        }

        private void LoadKnownTabs()
        {
            try
            {
                // Escaneia o modelo para obter abas e propriedades existentes (até 3000 itens)
                _knownTabs = NavisPropertyScanner.Scan(maxItems: 3000);

                // Preenche o ComboBox de abas, ordenado alfabeticamente
                CmbTab.ItemsSource = _knownTabs.Keys.OrderBy(k => k).ToList();

                // Seleciona a primeira aba customizada relevante, se existir
                var customTab = _knownTabs.Keys
                    .FirstOrDefault(k => k.StartsWith("PHD", StringComparison.OrdinalIgnoreCase));
                if (customTab != null)
                    CmbTab.SelectedItem = customTab;
            }
            catch (Exception ex)
            {
                AppendLog($"Aviso: não foi possível escanear o modelo ({ex.Message})");
            }
        }

        // -----------------------------------------------------------------------
        // Eventos de UI
        // -----------------------------------------------------------------------

        private void CmbTab_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            RefreshPropertyList();
        }

        private void CmbTab_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // Quando o usuário digita livremente, atualiza a lista de propriedades
            // com base no texto digitado (via Dispatcher para capturar o texto após a tecla)
            Dispatcher.BeginInvoke(new Action(RefreshPropertyList),
                System.Windows.Threading.DispatcherPriority.Input);
        }

        private void RefreshPropertyList()
        {
            var tabText = CmbTab.Text?.Trim() ?? string.Empty;

            // Tenta encontrar propriedades para a aba digitada/selecionada
            if (_knownTabs.TryGetValue(tabText, out var props))
                CmbProperty.ItemsSource = props.OrderBy(p => p).ToList();
            else
                CmbProperty.ItemsSource = null;
        }

        // -----------------------------------------------------------------------
        // Aplicar
        // -----------------------------------------------------------------------

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            var tabName  = (CmbTab.Text ?? string.Empty).Trim();
            var propName = (CmbProperty.Text ?? string.Empty).Trim();
            var value    = TxtValue.Text ?? string.Empty;

            if (string.IsNullOrEmpty(tabName))
            {
                MessageBox.Show("Informe o nome da aba de propriedades.", "Aviso",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrEmpty(propName))
            {
                MessageBox.Show("Informe o nome da propriedade.", "Aviso",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            TxtLog.Visibility = Visibility.Visible;
            BtnApply.IsEnabled = false;

            try
            {
                var writer = new PropertyWriter();
                writer.Write(_targetItems, tabName, propName, value);

                AppendLog($"OK — '{tabName} > {propName}' = \"{value}\" aplicado em {_targetItems.Count} elemento(s).");

                // Atualiza as abas conhecidas para incluir a nova aba/propriedade
                if (!_knownTabs.ContainsKey(tabName))
                    _knownTabs[tabName] = new List<string>();
                if (!_knownTabs[tabName].Contains(propName, StringComparer.OrdinalIgnoreCase))
                    _knownTabs[tabName].Add(propName);

                // Recarrega o ComboBox de abas para refletir possíveis novas abas
                CmbTab.ItemsSource = _knownTabs.Keys.OrderBy(k => k).ToList();
                CmbTab.Text = tabName;
                RefreshPropertyList();
                CmbProperty.Text = propName;
            }
            catch (Exception ex)
            {
                AppendLog($"ERRO: {ex.GetType().Name}: {ex.Message}");
                if (ex.InnerException != null)
                    AppendLog($"  Inner: {ex.InnerException.Message}");
                MessageBox.Show($"Falha ao gravar propriedades:\n\n{ex.Message}", "Erro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnApply.IsEnabled = _targetItems.Count > 0;
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private static IEnumerable<ModelItem> Flatten(ModelItem item)
        {
            if (!item.Children.Any())
            {
                yield return item;
                yield break;
            }
            foreach (var child in item.Children)
                foreach (var leaf in Flatten(child))
                    yield return leaf;
        }

        private void AppendLog(string msg)
        {
            TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
            TxtLog.ScrollToEnd();
        }
    }
}
