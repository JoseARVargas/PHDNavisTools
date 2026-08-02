using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Navisworks.Api;
using ExcelDataReader;

namespace PHDNavisTools.Core
{
    public class CheckRule
    {
        public string Disciplina       { get; set; } = "";
        public string Categoria        { get; set; } = "";
        public string Propriedade      { get; set; } = "";
        /// <summary>Categoria da propriedade de filtro de aplicabilidade (opcional).</summary>
        public string CategoriaFiltro  { get; set; } = "";
        /// <summary>Nome da propriedade de filtro de aplicabilidade (opcional).</summary>
        public string PropriedadeFiltro { get; set; } = "";

        public bool TemFiltro =>
            !string.IsNullOrWhiteSpace(CategoriaFiltro) &&
            !string.IsNullOrWhiteSpace(PropriedadeFiltro);
    }

    public class CheckResult
    {
        public string Disciplina     { get; set; } = "";
        public string SourceFile     { get; set; } = "";
        public string Guid           { get; set; } = "";
        public string Categoria      { get; set; } = "";
        public string Propriedade    { get; set; } = "";
        public string Valor          { get; set; } = "";
        public string Resultado      { get; set; } = "";
    }

    public class CheckSummaryRow
    {
        public string Nome    { get; set; } = "";
        public int    Total   { get; set; }
        public int    Ok      { get; set; }
        public int    Vazia   { get; set; }
        public int    Ausente { get; set; }
        public string PctOk   => Total > 0 ? $"{Ok * 100 / Total}%" : "—";
        public string BarOk
        {
            get
            {
                int pct = Total > 0 ? Ok * 100 / Total : 0;
                return new string('█', pct / 5) + new string('░', 20 - pct / 5) + $"  {pct}%";
            }
        }
    }

    public static class CheckService
    {
        public const string OK      = "✓ Preenchida";
        public const string EMPTY   = "⚠ Vazia";
        public const string MISSING = "✗ Ausente";

        // -----------------------------------------------------------------------
        // Load rules from file
        // -----------------------------------------------------------------------

        public static List<CheckRule> LoadRules(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".xlsx" or ".xls" or ".xlsm" => LoadRulesFromExcel(path),
                _                             => LoadRulesFromCsv(path),
            };
        }

        private static List<CheckRule> LoadRulesFromCsv(string path)
        {
            var rules = new List<CheckRule>();
            var lines = File.ReadAllLines(path, Encoding.UTF8);
            if (lines.Length < 2) return rules;

            char sep = lines[0].Contains(';') ? ';' : ',';
            string[] headers = SplitLine(lines[0], sep);

            int iDisc  = FindHeader(headers, "disciplina");
            int iCat   = FindHeader(headers, "categoria");
            int iProp  = FindHeader(headers, "propriedade", "property");
            // colunas opcionais de filtro de aplicabilidade
            int iCatF  = FindHeader(headers, "categoriafiltro",   "categoria filtro",   "filter category",  "filtercategory");
            int iPropF = FindHeader(headers, "propriedadefiltro", "propriedade filtro", "filter property",  "filterproperty");

            if (iDisc < 0 || iCat < 0 || iProp < 0)
                throw new InvalidDataException(
                    "CSV deve ter colunas: Disciplina, Categoria, Propriedade");

            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                var cols = SplitLine(lines[i], sep);
                if (cols.Length <= Math.Max(iDisc, Math.Max(iCat, iProp))) continue;
                rules.Add(new CheckRule {
                    Disciplina        = cols[iDisc].Trim(),
                    Categoria         = cols[iCat].Trim(),
                    Propriedade       = cols[iProp].Trim(),
                    CategoriaFiltro   = iCatF  >= 0 && iCatF  < cols.Length ? cols[iCatF].Trim()  : "",
                    PropriedadeFiltro = iPropF >= 0 && iPropF < cols.Length ? cols[iPropF].Trim() : "",
                });
            }
            return rules;
        }

        private static List<CheckRule> LoadRulesFromExcel(string path)
        {
            var rules = new List<CheckRule>();
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = ExcelDataReader.ExcelReaderFactory.CreateReader(stream);
            var ds = reader.AsDataSet(new ExcelDataReader.ExcelDataSetConfiguration
            {
                ConfigureDataTable = _ => new ExcelDataReader.ExcelDataTableConfiguration
                    { UseHeaderRow = true }
            });

            if (ds.Tables.Count == 0) return rules;
            var table = ds.Tables[0];

            // Find columns by name
            int iDisc  = FindColumn(table, "disciplina");
            int iCat   = FindColumn(table, "categoria");
            int iProp  = FindColumn(table, "propriedade", "property");
            int iCatF  = FindColumn(table, "categoriafiltro",   "categoria filtro",   "filter category",  "filtercategory");
            int iPropF = FindColumn(table, "propriedadefiltro", "propriedade filtro", "filter property",  "filterproperty");

            if (iDisc < 0 || iCat < 0 || iProp < 0)
                throw new InvalidDataException(
                    "Excel deve ter colunas: Disciplina, Categoria, Propriedade");

            foreach (DataRow row in table.Rows)
            {
                string disc  = row[iDisc]?.ToString()?.Trim() ?? "";
                string cat   = row[iCat]?.ToString()?.Trim()  ?? "";
                string prop  = row[iProp]?.ToString()?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(cat) && string.IsNullOrWhiteSpace(prop)) continue;
                rules.Add(new CheckRule {
                    Disciplina        = disc,
                    Categoria         = cat,
                    Propriedade       = prop,
                    CategoriaFiltro   = iCatF  >= 0 ? row[iCatF]?.ToString()?.Trim()  ?? "" : "",
                    PropriedadeFiltro = iPropF >= 0 ? row[iPropF]?.ToString()?.Trim() ?? "" : "",
                });
            }
            return rules;
        }

        // -----------------------------------------------------------------------
        // Run property checks — called per-item from the async window loop
        // -----------------------------------------------------------------------

        // Returns all geometry items paired with their source file name.
        // Source file is taken from doc.Models (always reliable) and propagated
        // down the tree — avoids relying on ModelItem.Model which only works on root items.
        public static List<(ModelItem item, string sourceFile)> GetGeometryItems(Document doc)
        {
            using var perf = PluginLogger.Perf("GetGeometryItems");
            PluginLogger.Info($"  {doc.Models.Count} model(s) no documento");

            var result = new List<(ModelItem, string)>();
            foreach (var model in doc.Models)
            {
                string src = model.SourceFileName ?? model.FileName ?? "";
                int before = result.Count;
                CollectGeometry(model.RootItem.Children, src, result);
                PluginLogger.Info($"  Model \"{Path.GetFileName(src)}\": {result.Count - before} itens geométricos" +
                                  $"  (SourceFileName=\"{model.SourceFileName}\"  FileName=\"{model.FileName}\")");
            }

            perf.Mark($"{result.Count} itens coletados no total");
            return result;
        }

        // Returns the distinct source file names (just filenames, no path) found in doc.Models
        public static List<string> GetDistinctSourceFiles(Document doc)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var model in doc.Models)
            {
                string full = model.SourceFileName ?? model.FileName ?? "";
                string name = Path.GetFileName(full);
                if (!string.IsNullOrEmpty(name)) seen.Add(name);
            }
            return seen.OrderBy(x => x).ToList();
        }

        private static void CollectGeometry(
            IEnumerable<ModelItem> items, string sourceFile,
            List<(ModelItem, string)> result)
        {
            foreach (var item in items)
            {
                if (item.HasGeometry) result.Add((item, sourceFile));
                CollectGeometry(item.Children, sourceFile, result);
            }
        }

        // Contador de itens verificados por regra — para log de performance
        private static readonly Dictionary<string, (int filtered, int checked_)> _ruleStats
            = new Dictionary<string, (int, int)>();

        public static void ResetRuleStats() => _ruleStats.Clear();

        public static void LogRuleStats()
        {
            if (_ruleStats.Count == 0) return;
            PluginLogger.Info("  Estatísticas por regra (filtered=passou filtro disciplina, checked=propriedade verificada):");
            foreach (var kv in _ruleStats)
                PluginLogger.Info($"    [{kv.Key}] filtered={kv.Value.filtered}  checked={kv.Value.checked_}");
        }

        // Processes a single item against all rules, appending to results list
        public static void ProcessItem(ModelItem item, string sourceFile,
            IList<CheckRule> rules, List<CheckResult> results, bool onlyFailures)
        {
            // Uma única leitura COM por item — todas as regras usam o cache
            var cache = BuildPropCache(item);

            foreach (var rule in rules)
            {
                // Filtro 1 — disciplina (substring do source file)
                if (!string.IsNullOrWhiteSpace(rule.Disciplina) &&
                    sourceFile.IndexOf(rule.Disciplina, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                // Filtro 2 — aplicabilidade por propriedade (CategoriaFiltro + PropriedadeFiltro)
                if (rule.TemFiltro && !ItemMatchesFilterFromCache(cache, rule.CategoriaFiltro, rule.PropriedadeFiltro))
                    continue;

                // Conta itens que passaram os filtros
                string key = $"{rule.Disciplina}/{rule.Categoria}/{rule.Propriedade}";
                if (!_ruleStats.TryGetValue(key, out var s)) s = (0, 0);
                _ruleStats[key] = (s.filtered + 1, s.checked_);

                var (resultado, valor) = CheckPropertyFromCache(cache, rule.Categoria, rule.Propriedade);

                s = _ruleStats[key];
                _ruleStats[key] = (s.filtered, s.checked_ + 1);

                if (onlyFailures && resultado == OK) continue;

                results.Add(new CheckResult {
                    Disciplina  = rule.Disciplina,
                    SourceFile  = Path.GetFileName(sourceFile),
                    Guid        = item.InstanceGuid != Guid.Empty
                                      ? item.InstanceGuid.ToString()
                                      : item.DisplayName ?? "",
                    Categoria   = rule.Categoria,
                    Propriedade = rule.Propriedade,
                    Valor       = valor,
                    Resultado   = resultado,
                });
            }
        }

        // Constrói cache catName → propName → valor a partir de uma única leitura COM
        internal static Dictionary<string, Dictionary<string, string>> BuildPropCache(ModelItem item)
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

        internal static bool ItemMatchesFilterFromCache(
            Dictionary<string, Dictionary<string, string>> cache,
            string filterCat, string filterProp)
        {
            if (!cache.TryGetValue(filterCat, out var props)) return false;
            if (!props.TryGetValue(filterProp, out var val)) return false;
            return !string.IsNullOrWhiteSpace(val);
        }

        internal static (string resultado, string valor) CheckPropertyFromCache(
            Dictionary<string, Dictionary<string, string>> cache,
            string category, string propName)
        {
            if (!cache.TryGetValue(category, out var props))
                return (MISSING, "");
            if (!props.TryGetValue(propName, out var val))
                return (MISSING, "");
            return (string.IsNullOrWhiteSpace(val) ? EMPTY : OK, val);
        }

        // -----------------------------------------------------------------------
        // Export results to CSV
        // -----------------------------------------------------------------------

        public static string ExportCsv(IList<CheckResult> results, string outputDir)
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string path  = Path.Combine(outputDir, $"check_result_{stamp}.csv");
            Directory.CreateDirectory(outputDir);

            using var w = new StreamWriter(path, false, new UTF8Encoding(true));
            w.WriteLine("Disciplina;Source File;Identificador;Categoria;Propriedade;Valor;Resultado");
            foreach (var r in results)
            {
                w.WriteLine(string.Join(";", new[]
                {
                    Esc(r.Disciplina), Esc(r.SourceFile), Esc(r.Guid),
                    Esc(r.Categoria),  Esc(r.Propriedade), Esc(r.Valor),
                    Esc(r.Resultado),
                }));
            }
            return path;
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private static IEnumerable<ModelItem> WalkGeometry(IEnumerable<ModelItem> items)
        {
            foreach (var item in items)
            {
                if (item.HasGeometry) yield return item;
                foreach (var child in WalkGeometry(item.Children)) yield return child;
            }
        }

        private static string[] SplitLine(string line, char sep)
        {
            // Handles quoted fields
            var cols = new List<string>();
            bool inQuote = false;
            var cur = new StringBuilder();
            foreach (char c in line)
            {
                if (c == '"')  { inQuote = !inQuote; continue; }
                if (c == sep && !inQuote) { cols.Add(cur.ToString()); cur.Clear(); }
                else cur.Append(c);
            }
            cols.Add(cur.ToString());
            return cols.ToArray();
        }

        private static int FindHeader(string[] headers, params string[] names)
        {
            for (int i = 0; i < headers.Length; i++)
                foreach (var n in names)
                    if (headers[i].Trim().Equals(n, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        private static int FindColumn(DataTable table, params string[] names)
        {
            for (int i = 0; i < table.Columns.Count; i++)
                foreach (var n in names)
                    if (table.Columns[i].ColumnName.Trim().Equals(n, StringComparison.OrdinalIgnoreCase))
                        return i;
            return -1;
        }

        private static string Esc(string s)
        {
            if (s.Contains(';') || s.Contains('"') || s.Contains('\n'))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }
}
