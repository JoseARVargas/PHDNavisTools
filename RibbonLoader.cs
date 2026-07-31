using System;
using System.Linq;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Threading;
using Autodesk.Navisworks.Api.Plugins;
using Autodesk.Windows;

namespace NavisworksIfcExporter
{
    [Plugin("NavisworksIfcExporter.Ribbon", "PHD",
        DisplayName = "PHD Ribbon Loader")]
    public class RibbonLoader : EventWatcherPlugin
    {
        private static readonly string LogPath =
            System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "PHD_Plugin_Log.txt");

        private static void Log(string msg)
        {
            try { System.IO.File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss}] {msg}\n"); }
            catch { }
        }

        public override void OnLoaded()
        {
            Log("OnLoaded chamado — plugin carregado pelo Navisworks");
            try
            {
                Autodesk.Navisworks.Api.Application.GuiCreated += OnGuiCreated;
                Log("GuiCreated registrado");
            }
            catch (Exception ex) { Log($"ERRO em OnLoaded: {ex}"); }
        }

        private void OnGuiCreated(object sender, EventArgs e)
        {
            Log("GuiCreated disparado");
            Autodesk.Navisworks.Api.Application.GuiCreated -= OnGuiCreated;
            // Navisworks initializes NWRibbonControl after GuiCreated — defer to idle
            Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(AddPhdTab));
        }

        private static System.Windows.Media.ImageSource LoadIcon(string fileName)
        {
            try
            {
                return new System.Windows.Media.Imaging.BitmapImage(
                    new Uri($"pack://application:,,,/NavisworksIfcExporter;component/Resources/{fileName}"));
            }
            catch
            {
                return new System.Windows.Media.Imaging.BitmapImage(
                    new Uri("pack://application:,,,/NavisworksIfcExporter;component/Resources/verificar_propriedades_32x32.png"));
            }
        }

        private static void AddPhdTab()
        {
            try
            {
                var roamer = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "navisworks.gui.roamer");
                if (roamer == null) return;

                var nwType = roamer.GetType("Autodesk.Navisworks.Gui.Roamer.AIRLook.NWRibbonControl");
                var instanceProp = nwType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                var ribbon = instanceProp?.GetValue(null) as RibbonControl;
                if (ribbon == null) return;

                // Remove existing tab so updates to buttons/panels always take effect
                var existing = ribbon.Tabs.FirstOrDefault(t => t.Id == "PHD_Coordination");
                if (existing != null) ribbon.Tabs.Remove(existing);

                var btnExport = new RibbonButton
                {
                    Id             = "IfcExporterCommand.PHD",
                    Text           = "Exportar IFC",
                    ShowText       = true,
                    Size           = RibbonItemSize.Large,
                    Orientation    = Orientation.Vertical,
                    IsEnabled      = true,
                    LargeImage     = LoadIcon("exportar_ifc_32x32.png"),
                    CommandHandler = new RibbonRelayCommand(() =>
                        Autodesk.Navisworks.Api.Application.Plugins.ExecuteAddInPlugin("IfcExporterCommand.PHD")),
                };

                var btnClashCsv = new RibbonButton
                {
                    Id             = "ExportClashCsv.PHD",
                    Text           = "Export Clash\nResults",
                    ShowText       = true,
                    Size           = RibbonItemSize.Large,
                    Orientation    = Orientation.Vertical,
                    IsEnabled      = true,
                    LargeImage     = LoadIcon("exportar_clash_results_32x32.png"),
                    CommandHandler = new RibbonRelayCommand(() =>
                        Autodesk.Navisworks.Api.Application.Plugins.ExecuteAddInPlugin("ExportClashCsv.PHD")),
                };

                var btnFbxSets = new RibbonButton
                {
                    Id             = "ExportFbxSets.PHD",
                    Text           = "Exportar FBX\npor Sets",
                    ShowText       = true,
                    Size           = RibbonItemSize.Large,
                    Orientation    = Orientation.Vertical,
                    IsEnabled      = true,
                    LargeImage     = LoadIcon("exportar_fbx_sets_32x32.png"),
                    CommandHandler = new RibbonRelayCommand(() =>
                        Autodesk.Navisworks.Api.Application.Plugins.ExecuteAddInPlugin("ExportFbxSets.PHD")),
                };

                var panelSource = new RibbonPanelSource { Id = "PHD_IFC_Panel", Title = "IFC Export" };
                panelSource.Items.Add(btnExport);

                var clashPanelSource = new RibbonPanelSource { Id = "PHD_Clash_Panel", Title = "Clash Detection" };
                clashPanelSource.Items.Add(btnClashCsv);

                var btnQtoAttach = new RibbonButton
                {
                    Id             = "QtoAutoAttach.PHD",
                    Text           = "QTO\nAuto Attach",
                    ShowText       = true,
                    Size           = RibbonItemSize.Large,
                    Orientation    = Orientation.Vertical,
                    IsEnabled      = true,
                    LargeImage     = LoadIcon("auto_attach_qto_32x32.png"),
                    CommandHandler = new RibbonRelayCommand(() =>
                        Autodesk.Navisworks.Api.Application.Plugins.ExecuteAddInPlugin("QtoAutoAttach.PHD")),
                };

                var fbxPanelSource = new RibbonPanelSource { Id = "PHD_FBX_Panel", Title = "FBX" };
                fbxPanelSource.Items.Add(btnFbxSets);

                var qtoPanelSource = new RibbonPanelSource { Id = "PHD_QTO_Panel", Title = "QTO" };
                qtoPanelSource.Items.Add(btnQtoAttach);

                var btnHighlight = new RibbonButton
                {
                    Id             = "HighlightSelection.PHD",
                    Text           = "Realçar\nSeleção",
                    ShowText       = true,
                    Size           = RibbonItemSize.Large,
                    Orientation    = Orientation.Vertical,
                    IsEnabled      = true,
                    LargeImage     = LoadIcon("realcar_selecao_32x32.png"),
                    CommandHandler = new RibbonRelayCommand(() =>
                        Autodesk.Navisworks.Api.Application.Plugins.ExecuteAddInPlugin("HighlightSelection.PHD")),
                };

                var btnResetAppearance = new RibbonButton
                {
                    Id             = "ResetAppearance.PHD",
                    Text           = "Restaurar\nAparência",
                    ShowText       = true,
                    Size           = RibbonItemSize.Large,
                    Orientation    = Orientation.Vertical,
                    IsEnabled      = true,
                    LargeImage     = LoadIcon("restaurar_aparencia_32x32.png"),
                    CommandHandler = new RibbonRelayCommand(() =>
                        Autodesk.Navisworks.Api.Application.Plugins.ExecuteAddInPlugin("ResetAppearance.PHD")),
                };

                var btnCheckProps = new RibbonButton
                {
                    Id             = "CheckProperties.PHD",
                    Text           = "Verificar\nPropriedades",
                    ShowText       = true,
                    Size           = RibbonItemSize.Large,
                    Orientation    = Orientation.Vertical,
                    IsEnabled      = true,
                    LargeImage     = LoadIcon("verificar_propriedades_32x32.png"),
                    CommandHandler = new RibbonRelayCommand(() =>
                        Autodesk.Navisworks.Api.Application.Plugins.ExecuteAddInPlugin("CheckProperties.PHD")),
                };

                var btnCheckIds = new RibbonButton
                {
                    Id             = "CheckIDS.PHD",
                    Text           = "Verificar\nIDS",
                    ShowText       = true,
                    Size           = RibbonItemSize.Large,
                    Orientation    = Orientation.Vertical,
                    IsEnabled      = true,
                    LargeImage     = LoadIcon("verificar_propriedades_32x32.png"),
                    CommandHandler = new RibbonRelayCommand(() =>
                        Autodesk.Navisworks.Api.Application.Plugins.ExecuteAddInPlugin("CheckIDS.PHD")),
                };

                var btnWriteProps = new RibbonButton
                {
                    Id             = "WriteProperties.PHD",
                    Text           = "Escrever\nPropriedades",
                    ShowText       = true,
                    Size           = RibbonItemSize.Large,
                    Orientation    = Orientation.Vertical,
                    IsEnabled      = true,
                    LargeImage     = LoadIcon("escrever_propriedades_32x32.png"),
                    CommandHandler = new RibbonRelayCommand(() =>
                        Autodesk.Navisworks.Api.Application.Plugins.ExecuteAddInPlugin("WriteProperties.PHD")),
                };

                var btnExcelImport = new RibbonButton
                {
                    Id             = "ExcelImport.PHD",
                    Text           = "Importar\nExcel",
                    ShowText       = true,
                    Size           = RibbonItemSize.Large,
                    Orientation    = Orientation.Vertical,
                    IsEnabled      = true,
                    LargeImage     = LoadIcon("verificar_propriedades_32x32.png"),
                    CommandHandler = new RibbonRelayCommand(() =>
                        Autodesk.Navisworks.Api.Application.Plugins.ExecuteAddInPlugin("ExcelImport.PHD")),
                };

                var checkPanelSource = new RibbonPanelSource { Id = "PHD_Check_Panel", Title = "Check" };
                checkPanelSource.Items.Add(btnCheckProps);
                checkPanelSource.Items.Add(btnCheckIds);
                checkPanelSource.Items.Add(btnWriteProps);
                checkPanelSource.Items.Add(btnExcelImport);

                var viewPanelSource = new RibbonPanelSource { Id = "PHD_View_Panel", Title = "View" };
                viewPanelSource.Items.Add(btnHighlight);
                viewPanelSource.Items.Add(btnResetAppearance);

                var panel       = new RibbonPanel { Source = panelSource };
                var clashPanel  = new RibbonPanel { Source = clashPanelSource };
                var fbxPanel    = new RibbonPanel { Source = fbxPanelSource };
                var qtoPanel    = new RibbonPanel { Source = qtoPanelSource };
                var checkPanel  = new RibbonPanel { Source = checkPanelSource };
                var viewPanel   = new RibbonPanel { Source = viewPanelSource };

                var tab = new RibbonTab
                {
                    Id        = "PHD_Coordination",
                    Title     = "PHD Eng. Digital",
                    IsVisible = true,
                };
                tab.Panels.Add(panel);
                tab.Panels.Add(clashPanel);
                tab.Panels.Add(fbxPanel);
                tab.Panels.Add(qtoPanel);
                tab.Panels.Add(checkPanel);
                tab.Panels.Add(viewPanel);

                ribbon.Tabs.Add(tab);
                Log("Aba PHD Eng. Digital adicionada com sucesso");
            }
            catch (Exception ex)
            {
                Log($"ERRO em AddPhdTab: {ex}");
            }
        }

        public override void OnUnloading() { }
    }

    internal sealed class RibbonRelayCommand : System.Windows.Input.ICommand
    {
        private readonly Action _execute;
        internal RibbonRelayCommand(Action execute) { _execute = execute; }
        public event EventHandler CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object parameter) => true;
        public void Execute(object parameter) { _execute(); }
    }
}
