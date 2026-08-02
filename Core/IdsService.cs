using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.Navisworks.Api;

namespace PHDNavisTools.Core
{
    internal static class IdsService
    {
        private const int MaxResults = 10_000;

        // Cache de propriedades por item: catName → propName → value
        // Construído uma única vez por item via COM; todas as avaliações de facet usam o dict.
        private static Dictionary<string, Dictionary<string, string>> BuildPropCache(ModelItem item)
        {
            var cache = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var cat in item.PropertyCategories)
            {
                if (!cache.TryGetValue(cat.DisplayName, out var props))
                    cache[cat.DisplayName] = props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in cat.Properties)
                    if (!props.ContainsKey(prop.DisplayName))
                        props[prop.DisplayName] = prop.Value?.ToDisplayString() ?? "";
            }
            return cache;
        }

        // ── ponto de entrada ─────────────────────────────────────────────────

        public static async Task<List<IdsCheckResult>> RunAsync(
            Document doc,
            IdsDocument ids,
            bool onlyFailures,
            Func<int, int, Task>? progress = null)
        {
            var results = new List<IdsCheckResult>();
            using var perfTotal = PluginLogger.Perf("IDS.RunAsync");
            PluginLogger.Info($"  {ids.Specifications.Count} spec(s) | onlyFailures={onlyFailures}");

            List<(ModelItem item, string sourceFile)> allItems;
            using (PluginLogger.Perf("IDS.ColetarItens"))
                allItems = CheckService.GetGeometryItems(doc);

            int total = allItems.Count;
            perfTotal.Mark($"{total} itens coletados");

            // Log de diagnóstico: exibe as categorias/props do 1º item para ajudar a calibrar GetEntityType
            if (total > 0)
                LogFirstItemProps(allItems[0].item);

            using var perfCheck = PluginLogger.Perf("IDS.Validar", total);
            for (int i = 0; i < total; i++)
            {
                if (results.Count >= MaxResults)
                {
                    PluginLogger.Warn($"Limite de {MaxResults} resultados atingido. Execução interrompida.");
                    break;
                }

                var (item, src) = allItems[i];

                // UMA única leitura COM por item — tudo mais usa o dicionário
                var cache      = BuildPropCache(item);
                string? entity = GetEntityTypeFromCache(cache);

                foreach (var spec in ids.Specifications)
                {
                    bool applicable = MatchesApplicability(cache, entity, spec.Applicability);
                    if (!applicable)
                    {
                        if (!onlyFailures)
                            results.Add(Make(spec, item, src, IdsStatus.NotApplicable, ""));
                        continue;
                    }

                    var (pass, reason) = CheckRequirements(cache, entity, spec.Requirements);
                    var status = pass ? IdsStatus.Pass : IdsStatus.Fail;

                    if (!onlyFailures || status == IdsStatus.Fail)
                        results.Add(Make(spec, item, src, status, reason));
                }

                if (i % 100 == 0 && progress != null)
                    await progress(i, total);

                if (i > 0 && i % 500 == 0)
                    perfCheck.Mark($"{i}/{total} itens  ({results.Count} resultados até agora)");
            }

            int nPass = results.Count(r => r.Status == IdsStatus.Pass);
            int nFail = results.Count(r => r.Status == IdsStatus.Fail);
            int nNA   = results.Count(r => r.Status == IdsStatus.NotApplicable);
            PluginLogger.Info($"  Resultados: ✓{nPass} ✗{nFail} —{nNA}");
            return results;
        }

        // ── applicability ────────────────────────────────────────────────────

        internal static bool MatchesApplicability(
            Dictionary<string, Dictionary<string, string>> cache,
            string? entity, List<IdsFacet> facets)
        {
            if (facets.Count == 0) return true;
            return facets.All(f => EvalFacet(cache, entity, f).pass);
        }

        // ── requirements ─────────────────────────────────────────────────────

        internal static (bool pass, string reason) CheckRequirements(
            Dictionary<string, Dictionary<string, string>> cache,
            string? entity, List<IdsFacet> facets)
        {
            foreach (var facet in facets)
            {
                if (facet.Cardinality == "optional") continue;

                var (pass, reason) = EvalFacet(cache, entity, facet);

                bool prohibited = facet.Cardinality == "prohibited";
                bool effective  = prohibited ? pass : !pass;

                if (effective)
                    return (false, prohibited ? $"Proibido mas presente: {reason}" : reason);
            }
            return (true, "");
        }

        // ── avaliação de facet ───────────────────────────────────────────────

        internal static (bool pass, string reason) EvalFacet(
            Dictionary<string, Dictionary<string, string>> cache,
            string? entity, IdsFacet facet)
            => facet switch
            {
                IdsEntityFacet         e => EvalEntity(cache, entity, e),
                IdsPropertyFacet       p => EvalProperty(cache, p),
                IdsAttributeFacet      a => EvalAttribute(cache, a),
                IdsClassificationFacet c => EvalClassification(cache, c),
                _                        => (true, ""),
            };

        // entity
        private static (bool pass, string reason) EvalEntity(
            Dictionary<string, Dictionary<string, string>> cache,
            string? entity, IdsEntityFacet facet)
        {
            if (entity == null)
                return (false, "Tipo de entidade IFC não encontrado no elemento");

            if (!facet.Name.Matches(entity))
                return (false, $"Entidade '{entity}' ≠ '{facet.Name}'");

            if (facet.PredefinedType != null && facet.PredefinedType.IsConstrained)
            {
                string? predef = GetFromCache(cache, "PredefinedType");
                if (!facet.PredefinedType.Matches(predef ?? ""))
                    return (false, $"PredefinedType '{predef}' ≠ '{facet.PredefinedType}'");
            }

            return (true, "");
        }

        // property
        private static (bool pass, string reason) EvalProperty(
            Dictionary<string, Dictionary<string, string>> cache,
            IdsPropertyFacet facet)
        {
            foreach (var catKv in cache)
            {
                string catName = catKv.Key;
                if (!facet.PropertySet.Matches(catName)) continue;

                foreach (var propKv in catKv.Value)
                {
                    string propName = propKv.Key;
                    string val      = propKv.Value;
                    if (!facet.BaseName.Matches(propName)) continue;

                    if (facet.Value == null || !facet.Value.IsConstrained)
                        return (true, "");

                    if (string.IsNullOrWhiteSpace(val))
                        return (false, $"{catName}.{propName}: valor vazio");

                    if (!facet.Value.Matches(val))
                        return (false, $"{catName}.{propName}: '{val}' ≠ '{facet.Value}'");

                    return (true, "");
                }

                return (false, $"{catName}.{facet.BaseName}: ausente no Pset");
            }

            return (false, $"Pset '{facet.PropertySet}' não encontrado | prop '{facet.BaseName}'");
        }

        // attribute
        private static (bool pass, string reason) EvalAttribute(
            Dictionary<string, Dictionary<string, string>> cache,
            IdsAttributeFacet facet)
        {
            string attrName = facet.Name.SimpleValue ?? facet.Name.ToString();
            string? val = GetFromCache(cache, attrName);

            if (val == null)
                return (false, $"Atributo '{attrName}': não encontrado");

            if (string.IsNullOrWhiteSpace(val))
                return (false, $"Atributo '{attrName}': vazio");

            if (facet.Value != null && facet.Value.IsConstrained && !facet.Value.Matches(val))
                return (false, $"Atributo '{attrName}': '{val}' ≠ '{facet.Value}'");

            return (true, "");
        }

        // classification
        private static (bool pass, string reason) EvalClassification(
            Dictionary<string, Dictionary<string, string>> cache,
            IdsClassificationFacet facet)
        {
            if (facet.Value == null && facet.System == null) return (true, "");

            foreach (var catKv in cache)
            {
                bool systemMatch = facet.System == null || !facet.System.IsConstrained
                                   || facet.System.Matches(catKv.Key);
                if (!systemMatch) continue;

                if (facet.Value == null || !facet.Value.IsConstrained) return (true, "");

                foreach (var val in catKv.Value.Values)
                    if (facet.Value.Matches(val)) return (true, "");
            }

            string reason = "Classificação não encontrada";
            if (facet.Value?.IsConstrained  == true) reason += $": '{facet.Value}'";
            if (facet.System?.IsConstrained == true) reason += $" (sistema '{facet.System}')";
            return (false, reason);
        }

        // ── helpers de cache ─────────────────────────────────────────────────

        private static void LogFirstItemProps(ModelItem item)
        {
            PluginLogger.Info($"  [Diagnóstico] 1º item: \"{item.DisplayName}\"");
            foreach (var cat in item.PropertyCategories)
            {
                PluginLogger.Info($"    Categoria: \"{cat.DisplayName}\"");
                foreach (var prop in cat.Properties)
                    PluginLogger.Info($"      \"{prop.DisplayName}\" = \"{prop.Value?.ToDisplayString()}\"");
            }
        }

        internal static string? GetEntityTypeFromCache(Dictionary<string, Dictionary<string, string>> cache)
        {
            foreach (var catKv in cache)
                foreach (var propKv in catKv.Value)
                {
                    string name = propKv.Key;
                    if (name.Equals("Entity",     StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("IFC Type",   StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("IfcType",    StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("IFC Entity", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("EntityType", StringComparison.OrdinalIgnoreCase))
                        return string.IsNullOrWhiteSpace(propKv.Value) ? null : propKv.Value;
                }
            return null;
        }

        // Busca um valor por nome de propriedade em qualquer categoria do cache
        private static string? GetFromCache(
            Dictionary<string, Dictionary<string, string>> cache, string propName)
        {
            foreach (var catKv in cache)
                if (catKv.Value.TryGetValue(propName, out var val))
                    return val;
            return null;
        }

        // ── resultado ────────────────────────────────────────────────────────

        private static IdsCheckResult Make(
            IdsSpecification spec, ModelItem item, string src,
            IdsStatus status, string reason) => new IdsCheckResult
        {
            SpecName      = spec.Name,
            SpecId        = spec.Identifier,
            Status        = status,
            Identifier    = item.InstanceGuid != Guid.Empty
                                ? item.InstanceGuid.ToString()
                                : item.DisplayName ?? "",
            SourceFile    = Path.GetFileName(src),
            FailureReason = reason,
        };

        // ── exportação CSV ───────────────────────────────────────────────────

        public static string ExportCsv(IList<IdsCheckResult> results, string outputDir)
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string path  = Path.Combine(outputDir, $"ids_result_{stamp}.csv");
            Directory.CreateDirectory(outputDir);

            using var w = new StreamWriter(path, false, new System.Text.UTF8Encoding(true));
            w.WriteLine("Status;Spec;ID;Identificador;Source File;Motivo");
            foreach (var r in results)
                w.WriteLine(string.Join(";", Esc(r.StatusIcon), Esc(r.SpecName), Esc(r.SpecId),
                                             Esc(r.Identifier), Esc(r.SourceFile), Esc(r.FailureReason)));
            return path;
        }

        private static string Esc(string s)
            => (s.Contains(';') || s.Contains('"') || s.Contains('\n'))
               ? "\"" + s.Replace("\"", "\"\"") + "\""
               : s;
    }
}
