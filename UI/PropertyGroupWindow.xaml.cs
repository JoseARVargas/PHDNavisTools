using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using PHDNavisTools.Core;
using NavisApp = Autodesk.Navisworks.Api.Application;

namespace PHDNavisTools.UI
{
    public class PropPair : INotifyPropertyChanged
    {
        public string TabName  { get; set; } = "";
        public string PropName { get; set; } = "";
        public string Display  => $"{TabName}   →   {PropName}";
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public partial class PropertyGroupWindow : Window
    {
        private readonly ObservableCollection<PropPair> _pairs = new();

        public PropertyGroupWindow()
        {
            InitializeComponent();
            LstProps.ItemsSource = _pairs;
            CmbSep.Text = " | ";
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var known = NavisPropertyScanner.Scan(maxItems: 3000);
                CmbTab.ItemsSource  = known.Keys.OrderBy(k => k).ToList();
                CmbProp.ItemsSource = known.Values
                    .SelectMany(v => v)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(p => p)
                    .ToList();
            }
            catch (Exception ex)
            {
                AppendLog($"Aviso ao ler abas: {ex.Message}");
            }
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            var tab  = (CmbTab.Text  ?? "").Trim();
            var prop = (CmbProp.Text ?? "").Trim();

            if (string.IsNullOrEmpty(tab) || string.IsNullOrEmpty(prop))
            {
                MessageBox.Show("Informe a aba e a propriedade.", "Aviso",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_pairs.Any(p => string.Equals(p.TabName, tab, StringComparison.OrdinalIgnoreCase)
                             && string.Equals(p.PropName, prop, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("Esse par aba/propriedade já foi adicionado.", "Aviso",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _pairs.Add(new PropPair { TabName = tab, PropName = prop });
            CmbTab.Text  = "";
            CmbProp.Text = "";
        }

        private void BtnRemoveProp_Click(object sender, RoutedEventArgs e)
        {
            if (((System.Windows.Controls.Button)sender).Tag is PropPair pair)
                _pairs.Remove(pair);
        }

        private async void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            if (_pairs.Count == 0)
            {
                MessageBox.Show("Adicione ao menos uma propriedade.", "Aviso",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var folderName = (TxtFolder.Text ?? "").Trim();
            if (string.IsNullOrEmpty(folderName)) folderName = "PHD – Agrupamento";

            var separator = CmbSep.Text ?? " | ";

            var doc = NavisApp.ActiveDocument;
            if (doc == null) { AppendLog("Nenhum documento aberto."); return; }

            BtnApply.IsEnabled     = false;
            PnlProgress.Visibility = Visibility.Visible;
            PrgBar.IsIndeterminate = true;
            TxtProgress.Text       = "Varrendo modelo...";

            var options = new PropertyGroupOptions
            {
                Properties      = _pairs.Select(p => new GroupProperty
                                      { TabName = p.TabName, PropertyName = p.PropName }).ToList(),
                FolderName      = folderName,
                Separator       = separator,
                IncludeMissing  = ChkMissing.IsChecked == true,
                MissingLabel    = "(sem valor)",
                SelectionOnly   = RdoSel.IsChecked == true,
                OverwriteFolder = ChkOverwrite.IsChecked == true,
            };

            AppendLog($"Agrupando por: {string.Join(", ", _pairs.Select(p => $"{p.TabName}/{p.PropName}"))}");

            try
            {
                PropertyGroupResult result = await Task.Run(() =>
                {
                    var svc = new PropertyGroupService();

                    svc.ProgressChanged += (_, msg) =>
                        Dispatcher.Invoke(() =>
                        {
                            AppendLog(msg);
                            if (PrgBar.IsIndeterminate)
                                TxtProgress.Text = msg.Length > 70 ? msg.Substring(0, 67) + "..." : msg;
                        });

                    svc.ProgressValue += (_, p) =>
                        Dispatcher.Invoke(() => UpdateProgress(p.Current, p.Total));

                    return svc.Apply(doc, options);
                });

                foreach (var w in result.Warnings)
                    AppendLog($"aviso: {w}");

                var summary = $"Pronto: {result.SetsCreated} Search Set(s) criado(s)" +
                              $" ({result.ElementsGrouped:N0} elemento(s) agrupado(s)" +
                              (result.ElementsSkipped > 0 ? $", {result.ElementsSkipped:N0} sem valor" : "") + ").";
                AppendLog(summary);
                TxtProgress.Text = summary;
                MessageBox.Show(summary, "Concluído", MessageBoxButton.OK, MessageBoxImage.Information);
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
            if (total <= 0) { PrgBar.IsIndeterminate = true; return; }
            PrgBar.IsIndeterminate = false;
            PrgBar.Maximum         = total;
            PrgBar.Value           = current;
            TxtProgress.Text       = $"{current:N0} / {total:N0}";
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void AppendLog(string msg)
        {
            TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
            TxtLog.ScrollToEnd();
        }
    }
}
