using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PHDNavisTools.Core;
using PHDNavisTools.Models;

namespace PHDNavisTools.UI
{
    public partial class MappingWindow : Window
    {
        private ObservableCollection<MappingRule> _rules;

        // Populated by ScanModel() on the UI thread — pset name → sorted property names
        private Dictionary<string, List<string>> _catalog = new Dictionary<string, List<string>>();
        private List<string> _allPsetNames = new List<string>();
        private List<string> _allPropNames = new List<string>();

        private static readonly string DefaultSavePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Autodesk", "Navisworks 2026", "Plugins", "PHDNavisTools", "mapping.json");

        public MappingWindow(IEnumerable<MappingRule>? initialRules = null)
        {
            InitializeComponent();

            if (initialRules != null)
                _rules = new ObservableCollection<MappingRule>(initialRules);
            else
                _rules = new ObservableCollection<MappingRule>(LoadFromFile(DefaultSavePath));

            GridRules.ItemsSource = _rules;

            // Scan after the window is rendered so "Escaneando..." is visible first
            Loaded += (_, __) => ScanModel();
        }

        public List<MappingRule> GetRules() => _rules.ToList();

        // -----------------------------------------------------------------------
        // Model property scanner — must run on the UI/STA thread (Navisworks API)
        // -----------------------------------------------------------------------

        private void ScanModel()
        {
            TxtScanStatus.Text = "Escaneando...";
            BtnScan.IsEnabled  = false;
            try
            {
                // NavisPropertyScanner uses the Navisworks managed API which must be
                // called from the STA thread — do NOT move to Task.Run / background thread.
                var catalog = NavisPropertyScanner.Scan();
                _catalog      = catalog;
                _allPsetNames = catalog.Keys
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                _allPropNames = catalog.Values
                    .SelectMany(v => v)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                TxtScanStatus.Text = _allPsetNames.Count > 0
                    ? $"{_allPsetNames.Count} categorias, {_allPropNames.Count} propriedades"
                    : "Nenhum modelo aberto";
            }
            catch (Exception ex)
            {
                TxtScanStatus.Text = $"Erro: {ex.Message}";
            }
            finally
            {
                BtnScan.IsEnabled = true;
            }
        }

        private void BtnScan_Click(object sender, RoutedEventArgs e) => ScanModel();

        // -----------------------------------------------------------------------
        // ComboBox Loaded events — called when cell enters edit mode
        // -----------------------------------------------------------------------

        private void CmbSourcePset_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is ComboBox cmb)
                cmb.ItemsSource = _allPsetNames;
        }

        private void CmbSourceProperty_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ComboBox cmb) return;
            // Tag carries the current SourcePset from the row binding
            var pset = cmb.Tag as string;
            cmb.ItemsSource = (!string.IsNullOrEmpty(pset) && _catalog.TryGetValue(pset!, out var props))
                ? props
                : _allPropNames;
        }

        // -----------------------------------------------------------------------
        // Target Pset combo — IFC standard catalog (unchanged)
        // -----------------------------------------------------------------------

        private void CmbTargetPset_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is ComboBox cmb && cmb.ItemsSource == null)
                cmb.ItemsSource = PsetCatalog.PsetNames();
        }

        // -----------------------------------------------------------------------
        // Grid row actions
        // -----------------------------------------------------------------------

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            var rule = new MappingRule();
            _rules.Add(rule);
            GridRules.ScrollIntoView(rule);
            GridRules.SelectedItem  = rule;
            GridRules.CurrentItem   = rule;
            GridRules.BeginEdit();
        }

        private void BtnRemove_Click(object sender, RoutedEventArgs e)
        {
            var selected = GridRules.SelectedItems.Cast<MappingRule>().ToList();
            foreach (var r in selected) _rules.Remove(r);
        }

        private void BtnMoveUp_Click(object sender, RoutedEventArgs e)
        {
            if (GridRules.SelectedItem is not MappingRule rule) return;
            int idx = _rules.IndexOf(rule);
            if (idx > 0) _rules.Move(idx, idx - 1);
        }

        private void BtnMoveDown_Click(object sender, RoutedEventArgs e)
        {
            if (GridRules.SelectedItem is not MappingRule rule) return;
            int idx = _rules.IndexOf(rule);
            if (idx < _rules.Count - 1) _rules.Move(idx, idx + 1);
        }

        // -----------------------------------------------------------------------
        // Import / Export JSON
        // -----------------------------------------------------------------------

        private void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title      = "Importar mapeamento",
                Filter     = "JSON (*.json)|*.json|Todos (*.*)|*.*",
                DefaultExt = ".json",
            };
            if (dlg.ShowDialog() != true) return;
            var loaded = LoadFromFile(dlg.FileName);
            _rules.Clear();
            foreach (var r in loaded) _rules.Add(r);
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title      = "Exportar mapeamento",
                Filter     = "JSON (*.json)|*.json",
                DefaultExt = ".json",
                FileName   = "mapping.json",
            };
            if (dlg.ShowDialog() != true) return;
            SaveToFile(_rules, dlg.FileName);
        }

        // -----------------------------------------------------------------------
        // OK / Cancel
        // -----------------------------------------------------------------------

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            GridRules.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
            SaveToFile(_rules, DefaultSavePath);
            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        // -----------------------------------------------------------------------
        // Persistence (DataContractJsonSerializer — no NuGet needed)
        // -----------------------------------------------------------------------

        private static List<MappingRule> LoadFromFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return new List<MappingRule>();
                var ser = new DataContractJsonSerializer(typeof(List<MappingRule>));
                using var fs = File.OpenRead(path);
                return (List<MappingRule>)ser.ReadObject(fs) ?? new List<MappingRule>();
            }
            catch { return new List<MappingRule>(); }
        }

        private static void SaveToFile(IEnumerable<MappingRule> rules, string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var ser = new DataContractJsonSerializer(typeof(List<MappingRule>));
                using var fs = File.Create(path);
                ser.WriteObject(fs, rules.ToList());
            }
            catch { }
        }
    }
}
