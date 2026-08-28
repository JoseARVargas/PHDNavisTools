using System;
using System.Linq;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Threading;
using Autodesk.Navisworks.Api.Plugins;
using Autodesk.Windows;

namespace PHDNavisTools
{
    [Plugin("PHDNavisTools.Ribbon", "PHD",
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
                    new Uri($"pack://application:,,,/PHDNavisTools;component/Resources/{fileName}"));
            }
            catch
            {
                return new System.Windows.Media.Imaging.BitmapImage(
                    new Uri("pack://application:,,,/PHDNavisTools;component/Resources/verificar_propriedades_32x32.png"));
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
                    ToolTip        = new RibbonToolTip
                    {
                        Title   = "Exportar IFC",
                        Content = "Abre o painel de exportação BIM para IFC 4 ou IFC2x3. Suporta modelo completo, seleção atual ou Search Sets, com geometria tessalada e propriedades.",
                    },
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
                    ToolTip        = new RibbonToolTip
                    {
                        Title   = "Exportar Clash Results",
                        Content = "Exporta os resultados do Clash Detective para um arquivo CSV, compatível com Power BI, Excel e outras ferramentas de análise.",
                    },
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
                    ToolTip        = new RibbonToolTip
                    {
                        Title   = "Exportar FBX por Sets",
                        Content = "Isola elementos de Search Sets na viewport do Navisworks e prepara a cena para exportação no formato FBX.",
                    },
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
                    ToolTip        = new RibbonToolTip
                    {
                        Title   = "QTO Auto Attach",
                        Content = "Vincula automaticamente elementos do modelo ao módulo Quantity Takeoff por Search Set ou por correspondência de propriedade-chave.",
                    },
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
                    ToolTip        = new RibbonToolTip
                    {
                        Title   = "Realçar Seleção",
                        Content = "Aplica cor e transparência nos elementos não selecionados para destacar visualmente a seleção atual na viewport.",
                    },
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
                    ToolTip        = new RibbonToolTip
                    {
                        Title   = "Restaurar Aparência",
                        Content = "Remove todas as sobreposições de cor e transparência temporárias, restaurando a aparência original do modelo.",
                    },
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
                    ToolTip        = new RibbonToolTip
                    {
                        Title   = "Verificar Propriedades",
                        Content = "Verifica o preenchimento de propriedades obrigatórias por disciplina, com base em um arquivo de regras CSV. Gera relatório de conformidade.",
                    },
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
                    ToolTip        = new RibbonToolTip
                    {
                        Title   = "Verificar IDS",
                        Content = "Valida o modelo contra um arquivo IDS (Information Delivery Specification) do buildingSMART. Exibe os elementos que não atendem aos requisitos.",
                    },
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
                    ToolTip        = new RibbonToolTip
                    {
                        Title   = "Escrever Propriedades",
                        Content = "Grava propriedades customizadas nos elementos selecionados. Permite criar ou reutilizar abas existentes. As propriedades ficam persistidas no NWD/NWF.",
                    },
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
                    LargeImage     = LoadIcon("importar_excel_32x32.png"),
                    ToolTip        = new RibbonToolTip
                    {
                        Title   = "Importar Dados Excel",
                        Content = "Importa dados de uma planilha Excel e grava como propriedades nos elementos do modelo, associando linhas via chave de correspondência.",
                    },
                    CommandHandler = new RibbonRelayCommand(() =>
                        Autodesk.Navisworks.Api.Application.Plugins.ExecuteAddInPlugin("ExcelImport.PHD")),
                };

                var btnSetToProperty = new RibbonButton
                {
                    Id             = "SetNameToProperty.PHD",
                    Text           = "Set →\nPropriedade",
                    ShowText       = true,
                    Size           = RibbonItemSize.Large,
                    Orientation    = Orientation.Vertical,
                    IsEnabled      = true,
                    LargeImage     = LoadIcon("escrever_propriedades_32x32.png"),
                    ToolTip        = new RibbonToolTip
                    {
                        Title   = "Set → Propriedade",
                        Content = "Copia o nome do Search Set ou Selection Set como propriedade dos elementos que pertencem a ele. Suporta múltiplos sets com diferentes estratégias de combinação.",
                    },
                    CommandHandler = new RibbonRelayCommand(() =>
                        Autodesk.Navisworks.Api.Application.Plugins.ExecuteAddInPlugin("SetNameToProperty.PHD")),
                };

                var btnClearProps = new RibbonButton
                {
                    Id             = "ClearProperties.PHD",
                    Text           = "Limpar\nPropriedades",
                    ShowText       = true,
                    Size           = RibbonItemSize.Large,
                    Orientation    = Orientation.Vertical,
                    IsEnabled      = true,
                    LargeImage     = LoadIcon("verificar_propriedades_32x32.png"),
                    ToolTip        = new RibbonToolTip
                    {
                        Title   = "Limpar Propriedades",
                        Content = "Remove abas ou propriedades customizadas dos elementos. Pode remover a aba inteira ou apenas propriedades específicas selecionadas.",
                    },
                    CommandHandler = new RibbonRelayCommand(() =>
                        Autodesk.Navisworks.Api.Application.Plugins.ExecuteAddInPlugin("ClearProperties.PHD")),
                };

                var btnCascade = new RibbonButton
                {
                    Id             = "CascadeProperty.PHD",
                    Text           = "Cascatear\nPropriedade",
                    ShowText       = true,
                    Size           = RibbonItemSize.Large,
                    Orientation    = Orientation.Vertical,
                    IsEnabled      = true,
                    LargeImage     = LoadIcon("escrever_propriedades_32x32.png"),
                    ToolTip        = new RibbonToolTip
                    {
                        Title   = "Cascatear Propriedade",
                        Content = "Propaga o valor de uma propriedade dos elementos selecionados (pais) para todos os seus descendentes na hierarquia do modelo.",
                    },
                    CommandHandler = new RibbonRelayCommand(() =>
                        Autodesk.Navisworks.Api.Application.Plugins.ExecuteAddInPlugin("CascadeProperty.PHD")),
                };

                var checkPanelSource = new RibbonPanelSource { Id = "PHD_Check_Panel", Title = "Check" };
                checkPanelSource.Items.Add(btnCheckProps);
                checkPanelSource.Items.Add(btnCheckIds);
                checkPanelSource.Items.Add(btnWriteProps);
                checkPanelSource.Items.Add(btnExcelImport);
                checkPanelSource.Items.Add(btnSetToProperty);
                checkPanelSource.Items.Add(btnClearProps);
                checkPanelSource.Items.Add(btnCascade);

                var viewPanelSource = new RibbonPanelSource { Id = "PHD_View_Panel", Title = "View" };
                viewPanelSource.Items.Add(btnHighlight);
                viewPanelSource.Items.Add(btnResetAppearance);

                // ── Painel PHD ─────────────────────────────────────────────────
                var btnPhdSite = new RibbonButton
                {
                    Id             = "PHD_Website",
                    Text           = "PHD\nEngenharia",
                    ShowText       = true,
                    Size           = RibbonItemSize.Large,
                    Orientation    = Orientation.Vertical,
                    IsEnabled      = true,
                    LargeImage     = LoadIcon("phd_logo_32x32.png"),
                    ToolTip        = new RibbonToolTip
                    {
                        Title   = "PHD Engenharia",
                        Content = "Acesse o site da PHD Engenharia Digital.",
                    },
                    CommandHandler = new RibbonRelayCommand(() =>
                        System.Diagnostics.Process.Start(
                            new System.Diagnostics.ProcessStartInfo("https://phdengenharia.eng.br/")
                            { UseShellExecute = true })),
                };

                var phdPanelSource = new RibbonPanelSource { Id = "PHD_Brand_Panel", Title = "PHD" };
                phdPanelSource.Items.Add(btnPhdSite);

                var panel       = new RibbonPanel { Source = panelSource };
                var clashPanel  = new RibbonPanel { Source = clashPanelSource };
                var fbxPanel    = new RibbonPanel { Source = fbxPanelSource };
                var qtoPanel    = new RibbonPanel { Source = qtoPanelSource };
                var checkPanel  = new RibbonPanel { Source = checkPanelSource };
                var viewPanel   = new RibbonPanel { Source = viewPanelSource };
                var phdPanel    = new RibbonPanel { Source = phdPanelSource };

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
                tab.Panels.Add(phdPanel);

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
