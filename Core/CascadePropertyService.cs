using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;

namespace PHDNavisTools.Core
{
    public class CascadeOptions
    {
        /// <summary>Abas onde procurar a propriedade. Vazio = busca em todas as abas.</summary>
        public List<string> TabNames        { get; set; } = new();
        public string       PropertyName    { get; set; } = "";
        public bool         CreateIfMissing { get; set; } = true;
    }

    public class CascadeResult
    {
        public int          ParentsProcessed { get; set; }
        public int          ElementsWritten  { get; set; }
        public int          ElementsSkipped  { get; set; }
        public List<string> Warnings         { get; set; } = new();
    }

    public class CascadePropertyService
    {
        public event EventHandler<string>? ProgressChanged;
        public event EventHandler<(int Current, int Total)>? ProgressValue;

        public CascadeResult Apply(
            Document               document,
            IEnumerable<ModelItem> parents,
            CascadeOptions         options)
        {
            var result     = new CascadeResult();
            var parentList = parents.Distinct().ToList();

            if (parentList.Count == 0)
            {
                Report("Nenhum elemento selecionado.");
                return result;
            }

            bool   tabsSpecified = options.TabNames.Count > 0;
            string tabDesc       = tabsSpecified
                ? $"abas: [{string.Join(", ", options.TabNames)}]"
                : "todas as abas";

            Report($"{parentList.Count} pai(s) | propriedade: '{options.PropertyName}' | {tabDesc}");

            // ── Fase 1: coleta descendentes ──────────────────────────────────────
            var parentData = new List<(string Label,
                                       List<(string Tab, string Value)> Pairs,
                                       List<ModelItem> Descendants)>();

            for (int i = 0; i < parentList.Count; i++)
            {
                var parent = parentList[i];
                var label  = GetLabel(parent);
                Report($"Coletando pai {i + 1}/{parentList.Count}: '{label}'...");

                var pairs = tabsSpecified
                    ? ReadInTabs(parent, options.TabNames, options.PropertyName)
                    : ReadAllTabs(parent, options.PropertyName);

                if (pairs.Count == 0)
                {
                    result.Warnings.Add(
                        $"'{label}' nao tem '{options.PropertyName}'" +
                        (tabsSpecified
                            ? $" nas abas [{string.Join(", ", options.TabNames)}]"
                            : " em nenhuma aba") +
                        " — ignorado.");
                    continue;
                }

                int capturedIdx  = i;
                var descendants  = GetDescendantsIterative(parent, count =>
                    Report($"  [{capturedIdx + 1}/{parentList.Count}] '{label}': {count:N0} descendentes..."));

                Report($"  Pai '{label}': {descendants.Count:N0} descendente(s) encontrado(s).");
                parentData.Add((label, pairs, descendants));
                result.ParentsProcessed++;
            }

            if (parentData.Count == 0)
            {
                Report("Nenhum pai com a propriedade encontrado.");
                return result;
            }

            int totalDesc = parentData.Sum(p => p.Descendants.Count);
            Report($"Total: {totalDesc:N0} descendente(s) a verificar em {parentData.Count} pai(s).");
            ReportValue(0, totalDesc);

            // ── Fase 2: monta batch ──────────────────────────────────────────────
            var batchByTab = new Dictionary<string, List<(ModelItem, Dictionary<string, string>)>>(
                StringComparer.OrdinalIgnoreCase);

            int processed  = 0;
            int reportStep = Math.Max(500, totalDesc / 100);

            foreach (var pEntry in parentData)
            {
                var label       = pEntry.Label;
                var pairs       = pEntry.Pairs;
                var descendants = pEntry.Descendants;

                foreach (var desc in descendants)
                {
                    processed++;
                    if (processed % reportStep == 0 || processed == totalDesc)
                        ReportValue(processed, totalDesc);

                    foreach (var pair in pairs)
                    {
                        var tab   = pair.Tab;
                        var value = pair.Value;

                        if (!options.CreateIfMissing && !HasProperty(desc, tab, options.PropertyName))
                        {
                            result.ElementsSkipped++;
                            continue;
                        }

                        if (!batchByTab.TryGetValue(tab, out var list))
                            batchByTab[tab] = list = new List<(ModelItem, Dictionary<string, string>)>();

                        list.Add((desc, new Dictionary<string, string>
                            { [options.PropertyName] = value }));
                    }
                }
            }

            ReportValue(totalDesc, totalDesc);

            // ── Fase 3: grava ────────────────────────────────────────────────────
            int totalToWrite = batchByTab.Values.Sum(l => l.Count);
            if (totalToWrite == 0)
            {
                Report("Nenhum elemento a gravar.");
                return result;
            }

            ReportValue(0, totalToWrite);
            int written = 0;

            foreach (var kv in batchByTab)
            {
                var tab   = kv.Key;
                var items = kv.Value;
                Report($"Gravando '{options.PropertyName}' em '{tab}' ({items.Count:N0} elemento(s))...");
                try
                {
                    new PropertyWriter().WriteAll(tab, items);
                    result.ElementsWritten += items.Count;
                    written += items.Count;
                    ReportValue(written, totalToWrite);
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Erro ao gravar na aba '{tab}': {ex.Message}");
                    result.ElementsSkipped += items.Count;
                }
            }

            return result;
        }

        // ── Leitura ──────────────────────────────────────────────────────────────

        private static List<(string Tab, string Value)> ReadInTabs(
            ModelItem item, List<string> tabNames, string propName)
        {
            var result = new List<(string, string)>();
            foreach (var cat in item.PropertyCategories)
            {
                bool matched = false;
                foreach (var t in tabNames)
                    if (string.Equals(cat.DisplayName, t, StringComparison.OrdinalIgnoreCase))
                    { matched = true; break; }
                if (!matched) continue;

                foreach (var prop in cat.Properties)
                    if (string.Equals(prop.DisplayName, propName, StringComparison.OrdinalIgnoreCase))
                    {
                        var val = prop.Value?.ToDisplayString();
                        if (val != null)
                            result.Add((cat.DisplayName, val));
                    }
            }
            return result;
        }

        private static List<(string Tab, string Value)> ReadAllTabs(
            ModelItem item, string propName)
        {
            var result = new List<(string, string)>();
            foreach (var cat in item.PropertyCategories)
                foreach (var prop in cat.Properties)
                    if (string.Equals(prop.DisplayName, propName, StringComparison.OrdinalIgnoreCase))
                    {
                        var val = prop.Value?.ToDisplayString();
                        if (val != null)
                            result.Add((cat.DisplayName, val));
                    }
            return result;
        }

        private static bool HasProperty(ModelItem item, string tabName, string propName)
        {
            foreach (var cat in item.PropertyCategories)
            {
                if (!string.Equals(cat.DisplayName, tabName, StringComparison.OrdinalIgnoreCase))
                    continue;
                foreach (var prop in cat.Properties)
                    if (string.Equals(prop.DisplayName, propName, StringComparison.OrdinalIgnoreCase))
                        return true;
            }
            return false;
        }

        // Iterativo com callback de progresso a cada 5.000 items
        private static List<ModelItem> GetDescendantsIterative(
            ModelItem root, Action<int>? onProgress = null)
        {
            var result = new List<ModelItem>();
            var stack  = new Stack<ModelItem>();

            foreach (var child in root.Children)
                stack.Push(child);

            while (stack.Count > 0)
            {
                var item = stack.Pop();
                result.Add(item);
                if (onProgress != null && result.Count % 5000 == 0)
                    onProgress(result.Count);
                foreach (var child in item.Children)
                    stack.Push(child);
            }

            return result;
        }

        private static string GetLabel(ModelItem item) =>
            string.IsNullOrWhiteSpace(item.DisplayName) ? "(sem nome)" : item.DisplayName;

        private void Report(string msg)          => ProgressChanged?.Invoke(this, msg);
        private void ReportValue(int c, int t)   => ProgressValue?.Invoke(this, (c, t));
    }
}
