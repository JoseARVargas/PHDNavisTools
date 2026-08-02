using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Autodesk.Navisworks.Api;
using NwColor = Autodesk.Navisworks.Api.Color;

namespace PHDNavisTools.UI
{
    public partial class HighlightSelectionWindow : Window
    {
        private System.Windows.Media.Color _pickedColor = System.Windows.Media.Color.FromRgb(180, 180, 180);
        private bool _overrideActive;

        public HighlightSelectionWindow()
        {
            InitializeComponent();
            UpdateSwatch();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e) => RefreshStatus();

        private void RefreshStatus()
        {
            var doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
            if (doc == null) { TxtStatus.Text = "Nenhum documento aberto."; return; }

            int selected  = doc.CurrentSelection.SelectedItems.Count;
            int totalGeom = WalkGeometry(doc.Models.RootItems).Count();

            TxtStatus.Text = selected > 0
                ? $"Seleção atual: {selected} elemento(s)  |  Total com geometria: {totalGeom}"
                : "Nenhum elemento selecionado. Faça uma seleção no Navisworks e clique em Aplicar.";

            BtnApply.IsEnabled = selected > 0;
        }

        // -----------------------------------------------------------------------
        // Color picker
        // -----------------------------------------------------------------------

        private void ColorSwatch_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => OpenColorDialog();

        private void BtnPickColor_Click(object sender, RoutedEventArgs e)
            => OpenColorDialog();

        private async void OpenColorDialog()
        {
            var dlg = new System.Windows.Forms.ColorDialog
            {
                Color         = System.Drawing.Color.FromArgb(_pickedColor.R, _pickedColor.G, _pickedColor.B),
                FullOpen      = true,
                AllowFullOpen = true,
            };

            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            var owner  = new Win32Window(helper.Handle);

            if (dlg.ShowDialog(owner) == System.Windows.Forms.DialogResult.OK)
            {
                var c = dlg.Color;
                _pickedColor = System.Windows.Media.Color.FromRgb(c.R, c.G, c.B);
                UpdateSwatch();
                if (ChkLivePreview.IsChecked == true && _overrideActive) await ApplyOverrideAsync();
            }
        }

        private async void BtnPreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn) return;
            var parts = (btn.Tag as string)?.Split(',');
            if (parts?.Length != 3) return;
            _pickedColor = System.Windows.Media.Color.FromRgb(
                byte.Parse(parts[0].Trim()),
                byte.Parse(parts[1].Trim()),
                byte.Parse(parts[2].Trim()));
            UpdateSwatch();
            if (ChkLivePreview.IsChecked == true && _overrideActive) await ApplyOverrideAsync();
        }

        private void UpdateSwatch() =>
            ColorSwatch.Background = new SolidColorBrush(_pickedColor);

        // -----------------------------------------------------------------------
        // Transparency slider
        // -----------------------------------------------------------------------

        private async void SliderTransp_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtTranspPct == null) return;
            TxtTranspPct.Text = $"{(int)SliderTransp.Value}%";
            if (ChkLivePreview?.IsChecked == true && _overrideActive) await ApplyOverrideAsync();
        }

        private async void ChkLivePreview_Changed(object sender, RoutedEventArgs e)
        {
            if (ChkLivePreview.IsChecked == true && _overrideActive) await ApplyOverrideAsync();
        }

        // -----------------------------------------------------------------------
        // Apply / Restore
        // -----------------------------------------------------------------------

        private async void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            RefreshStatus();
            var doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
            if (doc == null) return;

            if (doc.CurrentSelection.SelectedItems.Count == 0)
            {
                MessageBox.Show(
                    "Faça uma seleção no Navisworks antes de aplicar o realce.",
                    "Sem seleção", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            BtnApply.IsEnabled = false;
            await ApplyOverrideAsync();
            BtnApply.IsEnabled = true;
        }

        private async Task ApplyOverrideAsync()
        {
            var doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
            if (doc == null) return;

            try
            {
                SetProgress(true, 0);
                doc.Models.ResetAllTemporaryMaterials();

                // Fase 1: expandir seleção com todos os descendentes
                TxtStatus.Text = "Expandindo seleção...";
                var covered = new HashSet<ModelItem>();
                var selList = doc.CurrentSelection.SelectedItems.ToList();
                for (int i = 0; i < selList.Count; i++)
                {
                    foreach (var desc in WalkAll(new[] { selList[i] }))
                        covered.Add(desc);

                    if (i % 10 == 0)
                    {
                        SetProgress(true, selList.Count > 0 ? (double)i / selList.Count * 40 : 0);
                        await Dispatcher.Yield(DispatcherPriority.Background);
                    }
                }

                if (covered.Count == 0) { TxtStatus.Text = "Nenhum elemento coberto pela seleção."; return; }

                // Fase 2: coletar todos os elementos geométricos não cobertos
                TxtStatus.Text = "Coletando elementos não selecionados...";
                SetProgress(true, 40);
                await Dispatcher.Yield(DispatcherPriority.Background);

                var allGeom = WalkGeometry(doc.Models.RootItems).ToList();
                var unselected = new List<ModelItem>(allGeom.Count);

                for (int i = 0; i < allGeom.Count; i++)
                {
                    if (!covered.Contains(allGeom[i])) unselected.Add(allGeom[i]);

                    if (i % 200 == 0)
                    {
                        SetProgress(true, 40 + (allGeom.Count > 0 ? (double)i / allGeom.Count * 50 : 0));
                        await Dispatcher.Yield(DispatcherPriority.Background);
                    }
                }

                if (unselected.Count == 0)
                {
                    TxtStatus.Text = "Todos os elementos com geometria estão na seleção — nada a realçar.";
                    return;
                }

                // Fase 3: aplicar overrides
                SetProgress(true, 90);
                TxtStatus.Text = "Aplicando overrides...";
                await Dispatcher.Yield(DispatcherPriority.Background);

                double r = _pickedColor.R / 255.0;
                double g = _pickedColor.G / 255.0;
                double b = _pickedColor.B / 255.0;

                doc.Models.OverrideTemporaryColor(unselected, new NwColor(r, g, b));
                doc.Models.OverrideTemporaryTransparency(unselected, SliderTransp.Value / 100.0);

                _overrideActive      = true;
                BtnRestore.IsEnabled = true;
                TxtStatus.Text = $"Realce aplicado: {unselected.Count} elemento(s) não selecionado(s) com cor/transparência alteradas.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao aplicar realce:\n{ex.Message}", "Erro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetProgress(false, 100);
            }
        }

        private void SetProgress(bool visible, double value)
        {
            PanelProgress.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            PrgBar.Value = value;
            TxtPct.Text  = $"{(int)value}%";
        }

        private void BtnRestore_Click(object sender, RoutedEventArgs e)
        {
            Autodesk.Navisworks.Api.Application.ActiveDocument?.Models.ResetAllTemporaryMaterials();
            _overrideActive      = false;
            BtnRestore.IsEnabled = false;
            TxtStatus.Text       = "Aparência restaurada.";
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void Window_Closed(object sender, EventArgs e) { }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private static IEnumerable<ModelItem> WalkAll(IEnumerable<ModelItem> items)
        {
            foreach (var item in items)
            {
                yield return item;
                foreach (var child in WalkAll(item.Children))
                    yield return child;
            }
        }

        private static IEnumerable<ModelItem> WalkGeometry(IEnumerable<ModelItem> items)
        {
            foreach (var item in items)
            {
                if (item.HasGeometry) yield return item;
                foreach (var child in WalkGeometry(item.Children)) yield return child;
            }
        }

        // Minimal IWin32Window wrapper for WinForms dialogs
        private sealed class Win32Window : System.Windows.Forms.IWin32Window
        {
            private readonly IntPtr _handle;
            public Win32Window(IntPtr handle) { _handle = handle; }
            public IntPtr Handle => _handle;
        }
    }
}
