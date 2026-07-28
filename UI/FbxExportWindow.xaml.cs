using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Autodesk.Navisworks.Api;
using NavisworksIfcExporter.Core;
using NavisworksIfcExporter.Models;
using NavisApp = Autodesk.Navisworks.Api.Application;

namespace NavisworksIfcExporter.UI
{
    public partial class FbxExportWindow : Window
    {
        private readonly ObservableCollection<SetTreeNode> _rootNodes = new();
        private bool _exporting;

        public FbxExportWindow()
        {
            InitializeComponent();
            TreeSets.ItemsSource = _rootNodes;
            LoadSets();
        }

        // -----------------------------------------------------------------------
        // Diagnostic log
        // -----------------------------------------------------------------------

        private void Log(string message)
        {
            TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
            TxtLog.ScrollToEnd();
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
            => TxtLog.Clear();

        // -----------------------------------------------------------------------
        // Build tree
        // -----------------------------------------------------------------------

        private void LoadSets()
        {
            _rootNodes.Clear();
            var doc = NavisApp.ActiveDocument;
            if (doc == null) return;

            try
            {
                foreach (SavedItem item in doc.SelectionSets.Value)
                {
                    var node = BuildNode(item, parent: null);
                    if (node != null) _rootNodes.Add(node);
                }
            }
            catch { }
        }

        private static SetTreeNode? BuildNode(SavedItem item, SetTreeNode? parent)
        {
            if (item is SelectionSet ss)
            {
                return new SetTreeNode {
                    Name = ss.DisplayName, TypeIcon = ss.HasSearch ? "🔍" : "📋",
                    IsFolder = false, Item = ss, Parent = parent,
                };
            }
            if (item is GroupItem folder)
            {
                var node = new SetTreeNode {
                    Name = folder.DisplayName, TypeIcon = "📁",
                    IsFolder = true, IsExpanded = true, Parent = parent,
                };
                foreach (SavedItem child in folder.Children)
                {
                    var childNode = BuildNode(child, parent: node);
                    if (childNode != null) node.Children.Add(childNode);
                }
                var count = CountLeaves(node.Children);
                node.CountLabel = count > 0 ? $"  ({count} set{(count != 1 ? "s" : "")})" : "";
                return node;
            }
            return null;
        }

        private static int CountLeaves(IEnumerable<SetTreeNode> nodes)
            => nodes.Sum(n => n.IsFolder ? CountLeaves(n.Children) : 1);

        private static IEnumerable<SetTreeNode> CollectLeaves(IEnumerable<SetTreeNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (!node.IsFolder) yield return node;
                else foreach (var leaf in CollectLeaves(node.Children)) yield return leaf;
            }
        }

        // -----------------------------------------------------------------------
        // Tree toolbar
        // -----------------------------------------------------------------------

        private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            bool anyUnchecked = CollectLeaves(_rootNodes).Any(n => n.IsChecked != true);
            foreach (var node in _rootNodes) node.IsChecked = anyUnchecked;
        }

        private void BtnDeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var node in _rootNodes) node.IsChecked = false;
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e) => LoadSets();

        // -----------------------------------------------------------------------
        // Folder picker
        // -----------------------------------------------------------------------

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Escolha a pasta de destino para os arquivos FBX",
                ShowNewFolderButton = true,
            };
            if (!string.IsNullOrWhiteSpace(TxtFolder.Text))
                dlg.SelectedPath = TxtFolder.Text;

            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                TxtFolder.Text = dlg.SelectedPath;
        }

        // -----------------------------------------------------------------------
        // Batch FBX export
        // -----------------------------------------------------------------------

        private async void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            if (_exporting) return;

            var folder = TxtFolder.Text.Trim();
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                MessageBox.Show("Escolha uma pasta de destino válida.", "Aviso",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var checkedLeaves = CollectLeaves(_rootNodes)
                .Where(n => n.IsChecked == true && n.Item != null)
                .ToList();

            if (checkedLeaves.Count == 0)
            {
                MessageBox.Show("Selecione ao menos um set.", "Aviso",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var doc = NavisApp.ActiveDocument;
            if (doc == null)
            {
                MessageBox.Show("Nenhum documento aberto.", "Aviso",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _exporting = true;
            BtnExport.IsEnabled  = false;
            PbExport.Value       = 0;
            PbExport.Visibility  = Visibility.Visible;
            TxtStatus.Visibility = Visibility.Visible;

            // ── Diagnóstico: informações do documento ───────────────────────
            string rawFileName = "(exceção)";
            try { rawFileName = doc.FileName ?? "(null)"; } catch (Exception ex) { rawFileName = $"(erro: {ex.Message})"; }
            Log($"=== Início da exportação ===");
            Log($"doc.FileName  = {rawFileName}");

            string docName = "Modelo";
            try { docName = Path.GetFileNameWithoutExtension(doc.FileName) ?? "Modelo"; } catch { }
            Log($"docName usado = {docName}");
            Log($"Pasta destino = {folder}");
            Log($"Sets selecionados: {checkedLeaves.Count}");

            int done   = 0;
            int errors = 0;
            int total  = checkedLeaves.Count;

            try
            {
                foreach (var node in checkedLeaves)
                {
                    TxtStatus.Text = $"Preparando {done + 1}/{total}: {node.Name}...";
                    await Dispatcher.Yield(DispatcherPriority.Background);

                    var safeName   = SanitizeFileName(node.Name);
                    var outputPath = Path.Combine(folder, $"{docName} {safeName}.fbx");

                    Log($"\n--- Set {done + 1}/{total}: {node.Name} ---");
                    Log($"Output path: {outputPath}");

                    try
                    {
                        var setItems = node.Item!.GetSelectedItems(doc).ToList();
                        Log($"GetSelectedItems: {setItems.Count} item(s)");

                        var (verts, tris) = await ExtractGeometryAsync(setItems, node.Name, done, total);

                        if (verts.Count == 0)
                        {
                            Log("RESULTADO: sem geometria — set ignorado");
                            TxtStatus.Text = $"'{node.Name}' sem geometria — ignorado.";
                            done++;
                            PbExport.Value = done * 100.0 / total;
                            continue;
                        }

                        TxtStatus.Text = $"Gravando {node.Name} ({tris.Count:#,0} tri)...";
                        await Dispatcher.Yield(DispatcherPriority.Background);

                        FbxWriter.WriteAscii(outputPath, node.Name, verts, tris);

                        // Confirma que o arquivo existe e tem tamanho
                        var fi = new FileInfo(outputPath);
                        if (fi.Exists)
                            Log($"ARQUIVO GRAVADO: {fi.Length:#,0} bytes em {outputPath}");
                        else
                            Log($"AVISO: WriteAscii não lançou exceção mas arquivo não existe em {outputPath}");
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        Log($"ERRO: {ex.GetType().Name}: {ex.Message}");
                        var cont = MessageBox.Show(
                            $"Erro ao exportar '{node.Name}':\n{ex.Message}\n\n" +
                            "Continuar com os próximos sets?",
                            "Erro", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                        if (cont == MessageBoxResult.No) break;
                    }

                    done++;
                    PbExport.Value = done * 100.0 / total;
                }
            }
            finally
            {
                _exporting          = false;
                BtnExport.IsEnabled = true;
            }

            var summary = errors == 0
                ? $"Concluído: {done}/{total} set(s) exportado(s)."
                : $"Concluído com erros: {done - errors}/{total} ok, {errors} erro(s).";
            TxtStatus.Text = summary;
            Log($"\n=== {summary} ===");

            if (done > errors)
                System.Diagnostics.Process.Start("explorer.exe", $"\"{folder}\"");
        }

        // Extracts and combines geometry for all items in a set.
        // GetSelectedItems returns container-level IFC objects (HasGeometry=false);
        // we expand each to its geometry leaves before extracting.
        // Yields to the UI dispatcher every 50 leaves to stay responsive.
        private async Task<(List<double[]> Verts, List<int[]> Tris)> ExtractGeometryAsync(
            List<ModelItem> items, string setName, int setIndex, int setTotal)
        {
            var geoLeaves = CollectGeometryLeaves(items);
            Log($"  {items.Count} itens → {geoLeaves.Count} folhas com HasGeometry=true");

            var extractor = new GeometryExtractor();
            var allVerts  = new List<double[]>();
            var allTris   = new List<int[]>();
            int offset    = 0;
            int withGeo   = 0;
            int noGeo     = 0;
            int comErrors = 0;

            for (int i = 0; i < geoLeaves.Count; i++)
            {
                GeometryData? geo = null;
                try
                {
                    geo = extractor.Extract(geoLeaves[i]);
                }
                catch (Exception ex)
                {
                    // COM interop can throw OverflowException for items with
                    // malformed SAFEARRAY metadata (common in complex IFC geometry)
                    noGeo++;
                    comErrors++;
                    if (comErrors <= 5)
                        Log($"  [folha {i + 1}] EXCECAO: {ex.GetType().Name}: {ex.Message}");
                    else if (comErrors == 6)
                        Log($"  ... (mais exceções suprimidas)");

                    if ((i + 1) % 50 == 0)
                        await Dispatcher.Yield(DispatcherPriority.Background);
                    continue;
                }

                if (geo != null && geo.Vertices.Count > 0)
                {
                    withGeo++;
                    allVerts.AddRange(geo.Vertices);
                    foreach (var tri in geo.Triangles)
                        allTris.Add(new[] { tri[0] + offset, tri[1] + offset, tri[2] + offset });
                    offset += geo.Vertices.Count;

                    if (!string.IsNullOrEmpty(extractor.LastComError))
                    {
                        comErrors++;
                        if (comErrors <= 5)
                            Log($"  [folha {i + 1}] bbox fallback: {extractor.LastComError}");
                    }
                }
                else
                {
                    noGeo++;
                    if (!string.IsNullOrEmpty(extractor.LastComError) && comErrors <= 5)
                    {
                        comErrors++;
                        Log($"  [folha {i + 1}] sem geo: {extractor.LastComError}");
                    }
                }

                if ((i + 1) % 50 == 0)
                {
                    TxtStatus.Text = $"Extraindo {setIndex + 1}/{setTotal}: {setName} ({i + 1}/{geoLeaves.Count}, {allTris.Count:#,0} tri)...";
                    await Dispatcher.Yield(DispatcherPriority.Background);
                }
            }

            if (comErrors > 5)
                Log($"  ... ({comErrors} ocorrências COM no total)");

            Log($"  Com geo: {withGeo} | Sem geo: {noGeo} | COM issues: {comErrors}");
            Log($"  Total: {allVerts.Count:#,0} vértices, {allTris.Count:#,0} triângulos");

            return (allVerts, allTris);
        }

        // Iterative DFS that expands container nodes down to geometry leaves.
        // Iterative (not recursive) to avoid stack overflow on deep IFC hierarchies.
        // Per-item try/catch skips items whose COM array metadata is malformed.
        private static List<ModelItem> CollectGeometryLeaves(List<ModelItem> roots)
        {
            var result = new List<ModelItem>();
            var seen   = new HashSet<ModelItem>();
            var stack  = new Stack<ModelItem>(roots.Count);

            foreach (var r in roots) stack.Push(r);

            while (stack.Count > 0)
            {
                var item = stack.Pop();
                try
                {
                    if (item.HasGeometry)
                    {
                        if (seen.Add(item)) result.Add(item);
                        continue;
                    }
                    foreach (var child in item.Children)
                        stack.Push(child);
                }
                catch
                {
                    // OverflowException or COM error on this item — skip and continue
                }
            }

            return result;
        }

        // -----------------------------------------------------------------------
        // Close
        // -----------------------------------------------------------------------

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            if (_exporting)
            {
                var r = MessageBox.Show(
                    "Exportação em andamento. Fechar vai interromper o processo.\n\nFechar mesmo assim?",
                    "Exportação em andamento", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (r == MessageBoxResult.No) return;
            }
            Close();
        }

        // -----------------------------------------------------------------------

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        }
    }
}
