using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Autodesk.Navisworks.Api;
using PHDNavisTools.Core;
using NavisApp = Autodesk.Navisworks.Api.Application;

namespace PHDNavisTools.UI
{
    public partial class SetsToPropertyWindow : Window
    {
        private readonly ObservableCollection<SetTreeNode> _rootNodes = new();

        // Todos os nós folha (sem pasta) — usado para filtro e seleção rápida
        private List<SetTreeNode> _allLeaves = new();

        public SetsToPropertyWindow()
        {
            InitializeComponent();
            TreeSets.ItemsSource = _rootNodes;
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            LoadSets();
            LoadKnownTabs();
        }

        // -----------------------------------------------------------------------
        // Carregamento dos sets
        // -----------------------------------------------------------------------

        private void LoadSets()
        {
            _rootNodes.Clear();
            _allLeaves.Clear();

            var doc = NavisApp.ActiveDocument;
            if (doc == null) return;

            try
            {
                foreach (SavedItem item in doc.SelectionSets.Value)
                {
                    var node = BuildNode(item, parent: null, depth: 0);
                    if (node != null)
                    {
                        _rootNodes.Add(node);
                        CollectLeaves(node, _allLeaves);
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Aviso ao carregar sets: {ex.Message}");
            }

            AppendLog($"{_allLeaves.Count} set(s) disponível(is).");
        }

        private static SetTreeNode? BuildNode(SavedItem item, SetTreeNode? parent, int depth)
        {
            if (item is SelectionSet ss)
            {
                return new SetTreeNode
                {
                    Name     = ss.DisplayName,
                    TypeIcon = ss.HasSearch ? "🔍" : "📋",
                    IsFolder = false,
                    Item     = ss,
                    Parent   = parent,
                };
            }

            if (item is GroupItem folder)
            {
                var node = new SetTreeNode
                {
                    Name       = folder.DisplayName,
                    TypeIcon   = "📁",
                    IsFolder   = true,
                    IsExpanded = true,
                    Parent     = parent,
                };
                foreach (SavedItem child in folder.Children)
                {
                    var childNode = BuildNode(child, parent: node, depth: depth + 1);
                    if (childNode != null)
                        node.Children.Add(childNode);
                }
                int count = CountLeaves(node.Children);
                node.CountLabel = count > 0 ? $"  ({count})" : "";
                return node;
            }

            return null;
        }

        private static void CollectLeaves(SetTreeNode node, List<SetTreeNode> list)
        {
            if (!node.IsFolder) { list.Add(node); return; }
            foreach (var child in node.Children)
                CollectLeaves(child, list);
        }

        private static int CountLeaves(IEnumerable<SetTreeNode> nodes)
            => nodes.Sum(n => n.IsFolder ? CountLeaves(n.Children) : 1);

        // -----------------------------------------------------------------------
        // Carrega abas conhecidas do modelo para o ComboBox
        // -----------------------------------------------------------------------

        private void LoadKnownTabs()
        {
            try
            {
                var knownTabs = NavisPropertyScanner.Scan(maxItems: 2000);
                CmbTab.ItemsSource      = knownTabs.Keys.OrderBy(k => k).ToList();
                CmbProperty.ItemsSource = knownTabs.Keys.OrderBy(k => k).ToList();
            }
            catch { }

            CmbTab.Text      = "PHD_Sets";
            CmbProperty.Text = "Set";
        }

        // -----------------------------------------------------------------------
        // Filtro de árvore
        // -----------------------------------------------------------------------

        private void TxtFilter_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            var filter = TxtFilter.Text.Trim();
            if (string.IsNullOrEmpty(filter))
            {
                // Restaura a árvore completa
                TreeSets.ItemsSource = _rootNodes;
                return;
            }

            // Mostra apenas nós folha que batem com o filtro (flat list para simplificar)
            var matched = _allLeaves
                .Where(n => n.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
            TreeSets.ItemsSource = matched;
        }

        // -----------------------------------------------------------------------
        // Seleção rápida
        // -----------------------------------------------------------------------

        private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var node in _rootNodes)
                node.IsChecked = true;
        }

        private void BtnClearAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var node in _rootNodes)
                node.IsChecked = false;
        }

        // -----------------------------------------------------------------------
        // Aplicar
        // -----------------------------------------------------------------------

        private async void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            var tabName  = (CmbTab.Text ?? "").Trim();
            var propName = (CmbProperty.Text ?? "").Trim();

            if (string.IsNullOrEmpty(tabName) || string.IsNullOrEmpty(propName))
            {
                MessageBox.Show("Informe a aba e o nome da propriedade.", "Aviso",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Coleta sets selecionados (nós folha marcados) com suas profundidades
            var selectedSets = new List<(SelectionSet Set, int Depth)>();
            CollectSelected(_rootNodes, depth: 0, selectedSets);

            if (selectedSets.Count == 0)
            {
                MessageBox.Show("Selecione ao menos um set.", "Aviso",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var doc = NavisApp.ActiveDocument;
            if (doc == null) { AppendLog("Nenhum documento aberto."); return; }

            BtnApply.IsEnabled = false;

            var options = new SetPropertyOptions
            {
                TabName           = tabName,
                PropertyName      = propName,
                MultiSetMode      = RdoFirst.IsChecked == true   ? MultiSetMode.FirstOnly
                                  : RdoDeepest.IsChecked == true ? MultiSetMode.DeepestOnly
                                  :                                 MultiSetMode.JoinSeparator,
                Separator         = TxtSeparator.Text,
                SelectionOnly     = RdoSelection.IsChecked == true,
                OverwriteExisting = RdoOverwrite.IsChecked == true,
            };

            AppendLog($"Iniciando — {selectedSets.Count} set(s), " +
                      $"aba '{tabName}', prop '{propName}'.");

            try
            {
                SetPropertyResult result = await Task.Run(() =>
                {
                    var svc = new SetPropertyService();
                    svc.ProgressChanged += (_, msg) => Dispatcher.Invoke(() => AppendLog(msg));
                    return svc.Apply(doc, selectedSets, options);
                });

                foreach (var w in result.Warnings)
                    AppendLog($"⚠ {w}");

                var summary = $"Pronto: {result.ElementsWritten} elemento(s) gravado(s)" +
                              (result.ElementsSkipped > 0 ? $", {result.ElementsSkipped} ignorado(s)" : "") + ".";
                AppendLog(summary);
                MessageBox.Show(summary, "Concluído", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AppendLog($"ERRO: {ex.Message}");
                MessageBox.Show($"Falha ao gravar propriedades:\n\n{ex.Message}", "Erro",
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

        /// <summary>
        /// Coleta recursivamente os nós folha marcados, preservando a profundidade
        /// de cada set na hierarquia (para o modo DeepestOnly).
        /// </summary>
        private static void CollectSelected(
            IEnumerable<SetTreeNode>              nodes,
            int                                   depth,
            List<(SelectionSet Set, int Depth)>   result)
        {
            foreach (var node in nodes)
            {
                if (node.IsFolder)
                {
                    CollectSelected(node.Children, depth + 1, result);
                }
                else if (node.IsChecked == true && node.Item != null)
                {
                    result.Add((node.Item, depth));
                }
            }
        }

        private void AppendLog(string msg)
        {
            TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
            TxtLog.ScrollToEnd();
        }
    }
}
