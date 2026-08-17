using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;

namespace PHDNavisTools.Core
{
    public enum MultiSetMode
    {
        FirstOnly,       // grava apenas o primeiro set encontrado
        JoinSeparator,   // grava todos os sets unidos por um separador
        DeepestOnly,     // grava o set mais profundo na hierarquia (mais específico)
    }

    public class SetPropertyOptions
    {
        public string       TabName           { get; set; } = "PHD_Sets";
        public string       PropertyName      { get; set; } = "Set";
        public MultiSetMode MultiSetMode      { get; set; } = MultiSetMode.JoinSeparator;
        public string       Separator         { get; set; } = ";";
        public bool         SelectionOnly     { get; set; } = false;
        public bool         OverwriteExisting { get; set; } = true;
    }

    public class SetPropertyResult
    {
        public int          SetsProcessed     { get; set; }
        public int          ElementsWritten   { get; set; }
        public int          ElementsSkipped   { get; set; }
        public List<string> Warnings          { get; set; } = new();
    }

    public class SetPropertyService
    {
        public event EventHandler<string>? ProgressChanged;

        /// <summary>
        /// Para cada elemento alcançado pelos <paramref name="selectedSets"/>, grava o nome
        /// do(s) set(s) como propriedade no modelo.
        /// </summary>
        /// <param name="selectedSets">
        ///   Pares (SelectionSet, depth) onde depth é a profundidade na hierarquia de pastas
        ///   (usado para o modo DeepestOnly).
        /// </param>
        public SetPropertyResult Apply(
            Document                          document,
            IEnumerable<(SelectionSet Set, int Depth)> selectedSets,
            SetPropertyOptions                options)
        {
            var result   = new SetPropertyResult();
            var setList  = selectedSets.ToList();

            if (setList.Count == 0)
            {
                Report("Nenhum set selecionado.");
                return result;
            }

            // Escopo: todos os elementos ou apenas a seleção atual
            HashSet<ModelItem>? scopeFilter = null;
            if (options.SelectionOnly)
            {
                var sel = document.CurrentSelection.SelectedItems;
                scopeFilter = new HashSet<ModelItem>(sel);
                Report($"Escopo: {scopeFilter.Count} elemento(s) selecionado(s).");
            }

            // ── Passo 1: índice inverso ModelItem → lista de (setName, depth) ──────
            Report("Indexando elementos nos sets...");

            // Usa identity equality de ModelItem (referência), que é o padrão.
            var index = new Dictionary<ModelItem, List<(string Name, int Depth)>>(
                ReferenceEqualityComparer.Instance);

            foreach (var (set, depth) in setList)
            {
                ModelItemCollection setItems;
                try
                {
                    setItems = set.GetSelectedItems(document);
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Erro ao ler set '{set.DisplayName}': {ex.Message}");
                    continue;
                }

                int count = 0;
                foreach (var item in setItems)
                {
                    if (scopeFilter != null && !scopeFilter.Contains(item))
                        continue;

                    if (!index.TryGetValue(item, out var nameList))
                        index[item] = nameList = new List<(string, int)>();

                    // Evita duplicatas (um elemento pode estar em sub-pastas repetidas)
                    if (!nameList.Any(e => e.Name == set.DisplayName))
                        nameList.Add((set.DisplayName, depth));

                    count++;
                }

                Report($"  '{set.DisplayName}': {count} elemento(s).");
                result.SetsProcessed++;
            }

            if (index.Count == 0)
            {
                Report("Nenhum elemento encontrado nos sets selecionados.");
                return result;
            }

            // ── Passo 2: resolve o valor final por elemento ──────────────────────
            Report($"Calculando valores para {index.Count} elemento(s)...");

            var batch = new List<(ModelItem Item, Dictionary<string, string> Props)>(index.Count);

            foreach (var kvp in index)
            {
                var item    = kvp.Key;
                var entries = kvp.Value;

                // Pula elemento se já tiver valor e opção de não sobrescrever estiver ativa
                if (!options.OverwriteExisting && HasExistingValue(item, options.TabName, options.PropertyName))
                {
                    result.ElementsSkipped++;
                    continue;
                }

                string value;
                switch (options.MultiSetMode)
                {
                    case MultiSetMode.FirstOnly:
                        value = entries[0].Name;
                        break;
                    case MultiSetMode.DeepestOnly:
                        value = entries.OrderByDescending(e => e.Depth).First().Name;
                        break;
                    default: // JoinSeparator
                        value = string.Join(options.Separator,
                            entries.OrderBy(e => e.Name).Select(e => e.Name));
                        break;
                }

                batch.Add((item, new Dictionary<string, string> { [options.PropertyName] = value }));
            }

            // ── Passo 3: grava em lote ───────────────────────────────────────────
            Report($"Gravando propriedade '{options.PropertyName}' em {batch.Count} elemento(s)...");

            try
            {
                var writer = new PropertyWriter();
                writer.WriteAll(options.TabName, batch);
                result.ElementsWritten = batch.Count;
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Erro ao gravar propriedades: {ex.Message}");
                result.ElementsSkipped = batch.Count;
            }

            Report($"Concluído: {result.ElementsWritten} elemento(s) gravado(s), " +
                   $"{result.SetsProcessed} set(s) processado(s).");

            return result;
        }

        private static bool HasExistingValue(ModelItem item, string tabName, string propertyName)
        {
            foreach (var cat in item.PropertyCategories)
            {
                if (!string.Equals(cat.DisplayName, tabName, StringComparison.OrdinalIgnoreCase))
                    continue;
                foreach (var prop in cat.Properties)
                {
                    if (string.Equals(prop.DisplayName, propertyName, StringComparison.OrdinalIgnoreCase)
                        && prop.Value != null
                        && !string.IsNullOrWhiteSpace(prop.Value.ToDisplayString()))
                        return true;
                }
            }
            return false;
        }

        private void Report(string msg) => ProgressChanged?.Invoke(this, msg);
    }

    // Comparer de identidade de referência para ModelItem como chave de dicionário
    internal sealed class ReferenceEqualityComparer : IEqualityComparer<ModelItem>
    {
        public static readonly ReferenceEqualityComparer Instance = new();
        private ReferenceEqualityComparer() { }
        public bool Equals(ModelItem? x, ModelItem? y)   => ReferenceEquals(x, y);
        public int  GetHashCode(ModelItem obj)            => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
