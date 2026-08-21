using System.Windows.Forms;
using System.Windows.Forms.Integration;
using Autodesk.Navisworks.Api.Plugins;
using PHDNavisTools.UI;

namespace PHDNavisTools
{
    // ── Unified IFC Exporter DockPane ──────────────────────────────────────────

    [Plugin("IfcExporterPane", "PHD", DisplayName = "Exportar IFC — PHD")]
    [DockPanePlugin(480, 650, FixedSize = false)]
    public class IfcExporterDockPane : DockPanePlugin
    {
        public override Control CreateControlPane()
        {
            return new ElementHost
            {
                Dock = DockStyle.Fill,
                Child = new IfcExporterView(),
            };
        }

        public override void DestroyControlPane(Control pane) => pane?.Dispose();
    }

    [Plugin("IfcExporterCommand", "PHD",
        DisplayName = "Exportar IFC",
        ToolTip     = "Abre o painel de exportação IFC (IFC4 / IFC2x3, modelo completo / seleção / sets)")]
    [AddInPlugin(AddInLocation.None)]
    public class IfcExporterCommandPlugin : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            var record = Autodesk.Navisworks.Api.Application.Plugins.FindPlugin("IfcExporterPane.PHD");
            if (record is DockPanePluginRecord dr)
            {
                if (!dr.IsLoaded) dr.LoadPlugin();
                if (dr.LoadedPlugin is DockPanePlugin pane)
                    pane.Visible = !pane.Visible;
            }
            return 0;
        }
    }

    // ── Remaining commands ─────────────────────────────────────────────────────

    [Plugin("ExportClashCsv", "PHD",
        DisplayName = "Exportar Clashes CSV",
        ToolTip     = "Exporta os resultados do Clash Detective para CSV (compatível com Power BI)")]
    [AddInPlugin(AddInLocation.None)]
    public class ExportClashCsvPlugin : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            new ClashExportWindow().ShowDialog();
            return 0;
        }
    }

    [Plugin("ExportFbxSets", "PHD",
        DisplayName = "Exportar FBX por Sets",
        ToolTip     = "Isola elementos de Search Sets na viewport para exportação FBX")]
    [AddInPlugin(AddInLocation.None)]
    public class ExportFbxSetsPlugin : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            new FbxExportWindow().ShowDialog();
            return 0;
        }
    }

    [Plugin("QtoAutoAttach", "PHD",
        DisplayName = "QTO Auto Attach",
        ToolTip     = "Vincula elementos do modelo ao Quantity Takeoff por Search Set ou propriedade")]
    [AddInPlugin(AddInLocation.None)]
    public class QtoAutoAttachPlugin : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            new QtoWindow().ShowDialog();
            return 0;
        }
    }

    [Plugin("HighlightSelection", "PHD",
        DisplayName = "Realçar Seleção",
        ToolTip     = "Aplica cor e transparência aos elementos não selecionados para destacar a seleção na viewport")]
    [AddInPlugin(AddInLocation.None)]
    public class HighlightSelectionPlugin : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            new HighlightSelectionWindow().ShowDialog();
            return 0;
        }
    }

    [Plugin("CheckProperties", "PHD",
        DisplayName = "Verificar Propriedades",
        ToolTip     = "Verifica o preenchimento de propriedades por disciplina a partir de um arquivo de regras")]
    [AddInPlugin(AddInLocation.None)]
    public class CheckPropertiesPlugin : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            new CheckWindow().ShowDialog();
            return 0;
        }
    }

    [Plugin("ResetAppearance", "PHD",
        DisplayName = "Restaurar Aparência",
        ToolTip     = "Remove todas as sobreposições de cor e transparência temporárias do modelo")]
    [AddInPlugin(AddInLocation.None)]
    public class ResetAppearancePlugin : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            Autodesk.Navisworks.Api.Application.ActiveDocument
                ?.Models.ResetAllTemporaryMaterials();
            return 0;
        }
    }

    [Plugin("CheckIDS", "PHD",
        DisplayName = "Verificar IDS",
        ToolTip     = "Valida o modelo contra um arquivo IDS (Information Delivery Specification) buildingSMART")]
    [AddInPlugin(AddInLocation.None)]
    public class CheckIdsPlugin : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            new UI.IdsWindow().ShowDialog();
            return 0;
        }
    }

    [Plugin("WriteProperties", "PHD",
        DisplayName = "Escrever Propriedades",
        ToolTip     = "Grava propriedades customizadas nos elementos selecionados (abas persistidas no NWD/NWF)")]
    [AddInPlugin(AddInLocation.None)]
    public class WritePropertiesPlugin : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            new UI.WritePropertiesWindow().ShowDialog();
            return 0;
        }
    }

    [Plugin("ExcelImport", "PHD",
        DisplayName = "Importar Dados Excel",
        ToolTip     = "Enriquece o modelo com dados de uma planilha Excel via correspondência por propriedade")]
    [AddInPlugin(AddInLocation.None)]
    public class ExcelImportPlugin : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            new UI.ExcelImportWindow().ShowDialog();
            return 0;
        }
    }

    [Plugin("SetNameToProperty", "PHD",
        DisplayName = "Set → Propriedade",
        ToolTip     = "Copia o nome do Search Set / Selection Set como propriedade nos elementos que pertencem a ele")]
    [AddInPlugin(AddInLocation.None)]
    public class SetNameToPropertyPlugin : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            new UI.SetsToPropertyWindow().ShowDialog();
            return 0;
        }
    }

    [Plugin("ClearProperties", "PHD",
        DisplayName = "Limpar Propriedades",
        ToolTip     = "Remove abas ou propriedades customizadas dos elementos do modelo")]
    [AddInPlugin(AddInLocation.None)]
    public class ClearPropertiesPlugin : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            new UI.ClearPropertiesWindow().ShowDialog();
            return 0;
        }
    }
}
