using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using PHDNavisTools.Models;

namespace PHDNavisTools.Core
{
    public class ExportOptions
    {
        public string OutputPath       { get; set; } = string.Empty;
        public bool   IncludeHidden    { get; set; } = false;
        public bool   ExportGeometry   { get; set; } = true;
        public bool   SelectionOnly    { get; set; } = false;
        public string AuthorName       { get; set; } = "Exportador";
        public string OrganizationName { get; set; } = "PHD";
        public List<MappingRule> MappingRules { get; set; } = new List<MappingRule>();
        // When set, overrides SelectionOnly and exports exactly these items
        public IEnumerable<ModelItem>? ExplicitItems { get; set; }
        // IFC schema and coordinate precision
        public IfcSchema Schema        { get; set; } = IfcSchema.Ifc4;
        public int CoordDecimals       { get; set; } = 4; // 4 = Balanced (0.1 mm)
    }

    public class ExportService
    {
        public event EventHandler<string>? ProgressChanged;

        public void Export(ExportOptions options)
        {
            var doc = Autodesk.Navisworks.Api.Application.ActiveDocument
                      ?? throw new InvalidOperationException("Nenhum documento aberto.");

            IEnumerable<ModelItem> sourceItems;
            if (options.ExplicitItems != null)
            {
                sourceItems = options.ExplicitItems;
            }
            else if (options.SelectionOnly)
            {
                var selection = doc.CurrentSelection.SelectedItems;
                if (!selection.Any())
                    throw new InvalidOperationException("Nenhum elemento selecionado.");
                sourceItems = selection;
            }
            else
            {
                sourceItems = doc.Models.RootItems;
            }

            Report("Percorrendo modelo...");
            var traverser = new ModelTraverser(options.MappingRules);
            traverser.ProgressChanged += (_, msg) => Report(msg);
            var elements  = traverser.Traverse(sourceItems, options.IncludeHidden, options.ExportGeometry);

            string schemaLabel = options.Schema == IfcSchema.Ifc2x3 ? "IFC2x3" : "IFC4";
            Report($"Escrevendo arquivo {schemaLabel}...");
            var writer = new IfcWriter(options.AuthorName, options.OrganizationName);
            writer.Write(elements, options.OutputPath, options.Schema, options.CoordDecimals);

            Report($"Concluído. Arquivo salvo em: {options.OutputPath}");
        }

        private void Report(string message) => ProgressChanged?.Invoke(this, message);
    }
}
