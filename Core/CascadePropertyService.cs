using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;

namespace PHDNavisTools.Core
{
    public class CascadeOptions
    {
        /// <summary>Aba onde procurar a propriedade. Vazio = busca em todas as abas.</summary>
        public string TabName         { get; set; } = "";
        public string PropertyName    { get; set; } = "";
        public bool   CreateIfMissing { get; set; } = true;
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
        /// <summary>Mensagens de log para exibir no TextBox da janela.</summary>
        public event EventHandler<string>? ProgressChanged;

        /// <summary>Progresso numérico (Current, Total) para atualizar a barra.</summary>
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

            bool tabFixed = !string.IsNullOrWhiteSpace(options.TabName);
            Report($"{parentList.Count} pai(s) | propriedade: '{options.PropertyName}'" +
                   (tabFixed ? $" | aba: '{options.TabName}'" : " | buscando em todas as abas"));

            // ── Fase 1: coleta descendentes e calcula total ─────────────────────
            Report("Coletando descendentes...");

            // parentData: (label, pares tab/valor, lista de descendentes já materializada)
            var parentData = new List<(string Label,
                                       List<(string Tab, string Value)> Pairs,
                                       List<ModelItem> Descendants)>();

            foreach (var parent in parentList)
            {
                var pairs = tabFixed
                    ? ReadInTab(parent, options.TabName, options.PropertyName)
                    : ReadAllTabs(parent, options.PropertyName);

                if (pairs.Count == 0)
                {
                    result.Warnings.Add(
                        $"'{GetLabel(parent)}' não tem '{options.PropertyName}'" +
                        (tabFixed ? $" na aba '{options.TabName}'" : " em nenhuma aba") + " — ignorado.");
                    continue;
                }

                // Iterativo (evita stack overflow em hierarquias muito profundas)
                var descendants = GetDescendantsIterative(parent);
                parentData.Add((GetLabel(parent), pairs, descendants));
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

            // ── Fase 2: monta batch com progresso ───────────────────────────────
            var batchByTab = new Dictionary<string, List<(ModelItem, Dictionary<string, string>)>>(
                StringComparer.OrdinalIgnoreCase);

            int processed = 0;
            // Reporta a cada 1% ou a cada 500 elementos, o que for maior
            int reportStep = Math.Max(500, totalDesc / 100);

            foreach (var (label, pairs, descendants) in parentData)
            {
                Report($"  Pai '{label}': {descendants.Count:N0} descendente(s).");

                foreach (var desc in descendants)
                {
                    processed++;
                    if (processed % reportStep == 0 || processed == totalDesc)
                        ReportValue(processed, totalDesc);

                    foreach (var (tab, value) in pairs)
                    {
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

        // -----------------------------------------------------------------------
        // Leitura de propriedades
        // -----------------------------------------------------------------------

        private static List<(string Tab, string Value)> ReadInTab(
            ModelItem item, string tabName, string propName)
        {
            foreach (var cat in item.PropertyCategories)
            {
                if (!string.Equals(cat.DisplayName, tabName, StringComparison.OrdinalIgnoreCase))
                    continue;
                foreach (var prop in cat.Properties)
                    if (string.Equals(prop.DisplayName, propName, StringComparison.OrdinalIgnoreCase))
                    {
                        var val = prop.Value?.ToDisplayString();
                        if (val != null)
                            return new List<(string, string)> { (tabName, val) };
                    }
            }
            return new List<(string, string)>();
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

        // Iterativo: evita stack overflow em hierarquias muito profundas
        private static List<ModelItem> GetDescendantsIterative(ModelItem root)
        {
            var result = new List<ModelItem>();
            var stack  = new Stack<ModelItem>();

            foreach (var child in root.Children)
                stack.Push(child);

            while (stack.Count > 0)
            {
                var item = stack.Pop();
                result.Add(item);
                foreach (var child in item.Children)
                    stack.Push(child);
            }

            return result;
        }

        private static string GetLabel(ModelItem item) =>
            string.IsNullOrWhiteSpace(item.DisplayName) ? "(sem nome)" : item.DisplayName;

        private void Report(string msg)      => ProgressChanged?.Invoke(this, msg);
        private void ReportValue(int c, int t) => ProgressValue?.Invoke(this, (c, t));
    }
}
