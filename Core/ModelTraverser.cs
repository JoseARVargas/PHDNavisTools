using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using PHDNavisTools.Models;

namespace PHDNavisTools.Core
{
    public class ModelTraverser
    {
        private readonly PropertyExtractor _propertyExtractor = new();
        private readonly GeometryExtractor _geometryExtractor = new();
        private readonly IList<MappingRule>? _mappingRules;

        public ModelTraverser(IList<MappingRule>? mappingRules = null)
        {
            _mappingRules = mappingRules;
        }

        public event EventHandler<string>? ProgressChanged;

        public IEnumerable<ElementData> Traverse(IEnumerable<ModelItem> items, bool includeHidden, bool exportGeometry)
        {
            var results      = new List<ElementData>();
            bool comFailed   = false;
            bool diagDone    = false;
            int withProps    = 0;
            int withoutProps = 0;

            foreach (var item in items)
                TraverseItem(item, results, includeHidden, exportGeometry,
                             ref comFailed, ref diagDone, ref withProps, ref withoutProps);

            Report($"  {results.Count} elementos exportados ({withProps} com propriedades, {withoutProps} sem).");
            return results;
        }

        private void TraverseItem(ModelItem item, List<ElementData> results, bool includeHidden, bool exportGeometry,
            ref bool comFailed, ref bool diagDone, ref int withProps, ref int withoutProps)
        {
            if (!includeHidden && item.IsHidden)
                return;

            if (item.HasGeometry || IsLeafWithProperties(item))
            {
                var rawProps = CollectProperties(item);
                var props    = (_mappingRules?.Count > 0)
                    ? PropertyMapper.Apply(rawProps, _mappingRules)
                    : rawProps;

                // Diagnóstico: loga categorias e contagem do primeiro elemento exportado
                if (!diagDone)
                {
                    diagDone = true;
                    var cats = string.Join(", ", props.Keys.Take(10));
                    var total = props.Values.Sum(p => p.Count);
                    Report($"  [diag] 1º elemento: \"{item.DisplayName}\" | {props.Count} psets ({total} props) | {cats}");

                    // Também mostra categorias brutas do Navisworks antes da extração
                    var rawCats = string.Join(", ", item.PropertyCategories
                        .Select(c => $"{c.Name}({c.DisplayName})")
                        .Take(8));
                    Report($"  [diag] categorias brutas: {rawCats}");
                }

                var element = new ElementData
                {
                    Id           = item.InstanceGuid.ToString(),
                    Name         = item.DisplayName ?? "(sem nome)",
                    Category     = GetCategory(item, props),
                    IfcType      = IfcTypeMapper.Map(props, item),
                    PropertySets = props,
                };

                if (props.Count > 0) withProps++; else withoutProps++;

                if (exportGeometry && item.HasGeometry)
                {
                    // Após primeira falha COM confirmada, vai direto para bounding box
                    if (!comFailed)
                    {
                        element.Geometry = _geometryExtractor.Extract(item);
                        if (_geometryExtractor.LastComError.Length > 0)
                        {
                            Report($"  Aviso: tessellação COM indisponível ({_geometryExtractor.LastComError}). Restante usará bounding box.");
                            comFailed = true;
                        }
                    }
                    else
                    {
                        element.Geometry = GeometryExtractor.ExtractBoundingBox(item);
                    }
                }

                results.Add(element);
            }

            foreach (var child in item.Children)
                TraverseItem(child, results, includeHidden, exportGeometry,
                             ref comFailed, ref diagDone, ref withProps, ref withoutProps);
        }

        // Sempre mescla propriedades do item com as de seus ancestrais (máx. 8 níveis).
        // Os ancestrais têm menor prioridade — as props do próprio item sempre prevalecem.
        // Funciona para qualquer formato: Revit, AutoCAD, DGN, IFC, NWC, etc.
        private Dictionary<string, Dictionary<string, string>> CollectProperties(ModelItem item)
        {
            var result = new Dictionary<string, Dictionary<string, string>>();
            int depth = 0;

            // Ancestrais em ordem: imediato → raiz (imediato tem prioridade sobre raiz)
            foreach (var ancestor in item.Ancestors)
            {
                if (++depth > 8) break;
                foreach (var kvp in _propertyExtractor.Extract(ancestor))
                    if (!result.ContainsKey(kvp.Key))
                        result[kvp.Key] = kvp.Value;
            }

            // Props do item sobrepõem ancestrais
            foreach (var kvp in _propertyExtractor.Extract(item))
                result[kvp.Key] = kvp.Value;

            return result;
        }

        private static bool IsLeafWithProperties(ModelItem item) =>
            !item.Children.Any() && item.PropertyCategories.Any();

        private void Report(string message) => ProgressChanged?.Invoke(this, message);

        private static string GetCategory(ModelItem item, Dictionary<string, Dictionary<string, string>> props)
        {
            if (props.TryGetValue("Item", out var itemPset))
            {
                if (itemPset.TryGetValue("Category", out var cat) && !string.IsNullOrWhiteSpace(cat))
                    return cat;
                if (itemPset.TryGetValue("Layer", out var layer) && !string.IsNullOrWhiteSpace(layer))
                    return layer;
            }

            var className = item.ClassDisplayName ?? item.ClassName;
            if (!string.IsNullOrWhiteSpace(className))
                return className;

            return "Unknown";
        }
    }
}
