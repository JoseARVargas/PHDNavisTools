using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;

namespace PHDNavisTools.Core
{
    public class GroupProperty
    {
        public string TabName      { get; set; } = "";
        public string PropertyName { get; set; } = "";
    }

    public class PropertyGroupOptions
    {
        public List<GroupProperty> Properties    { get; set; } = new();
        public string              FolderName    { get; set; } = "PHD – Agrupamento";
        public string              Separator     { get; set; } = " | ";
        public bool                IncludeMissing  { get; set; } = false;
        public string              MissingLabel    { get; set; } = "(sem valor)";
        public bool                SelectionOnly   { get; set; } = false;
        public bool                OverwriteFolder { get; set; } = true;
    }

    public class PropertyGroupResult
    {
        public int          SetsCreated     { get; set; }
        public int          ElementsGrouped { get; set; }
        public int          ElementsSkipped { get; set; }
        public List<string> Warnings        { get; set; } = new();
    }

    public class PropertyGroupService
    {
        public event EventHandler<string>?                   ProgressChanged;
        public event EventHandler<(int Current, int Total)>? ProgressValue;

        public PropertyGroupResult Apply(Document doc, PropertyGroupOptions options)
        {
            var result = new PropertyGroupResult();

            if (options.Properties.Count == 0)
            {
                Report("Nenhuma propriedade selecionada.");
                return result;
            }

            // ── Fase 1: varrer modelo e coletar combinações únicas de valores ────
            var allItems = options.SelectionOnly
                ? doc.CurrentSelection.SelectedItems.ToList()
                : GetAllItems(doc);

            int total = allItems.Count;
            Report($"Varrendo {total:N0} elemento(s)...");
            ReportValue(0, total);

            // key = valores combinados (ex: "Estrutural | Viga")
            // values = lista de itens com essa combinação (para criar a Search com condições exatas)
            var uniqueKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            int processed = 0;
            int step = Math.Max(500, total / 100);

            foreach (var item in allItems)
            {
                processed++;
                if (processed % step == 0 || processed == total)
                    ReportValue(processed, total);

                var values = new List<string>(options.Properties.Count);
                bool valid = true;

                foreach (var gp in options.Properties)
                {
                    var value = ReadPropertyValue(item, gp.TabName, gp.PropertyName);
                    if (value == null)
                    {
                        if (options.IncludeMissing)
                            values.Add(options.MissingLabel);
                        else { valid = false; break; }
                    }
                    else
                        values.Add(value);
                }

                if (!valid) { result.ElementsSkipped++; continue; }

                uniqueKeys.Add(string.Join(options.Separator, values));
                result.ElementsGrouped++;
            }

            if (uniqueKeys.Count == 0)
            {
                Report("Nenhuma combinação de valores encontrada.");
                return result;
            }

            Report($"{uniqueKeys.Count} valor(es) único(s) encontrado(s). Criando Search Sets...");

            // ── Fase 2: criar Search Sets com critérios dinâmicos ────────────────
            ReportValue(0, uniqueKeys.Count);
            CreateSearchSets(doc, options, uniqueKeys, result);
            ReportValue(uniqueKeys.Count, uniqueKeys.Count);

            return result;
        }

        // ── Criação dos Search Sets ───────────────────────────────────────────────

        private void CreateSearchSets(
            Document doc,
            PropertyGroupOptions options,
            HashSet<string> uniqueKeys,
            PropertyGroupResult result)
        {
            var sets = doc.SelectionSets.Value;

            // Remove pasta existente com mesmo nome
            if (options.OverwriteFolder)
            {
                for (int i = sets.Count - 1; i >= 0; i--)
                {
                    if (string.Equals(sets[i].DisplayName, options.FolderName, StringComparison.OrdinalIgnoreCase)
                        && sets[i].IsGroup)
                    {
                        sets.RemoveAt(i);
                        Report($"Pasta '{options.FolderName}' existente substituída.");
                        break;
                    }
                }
            }

            // FolderItem tem construtor sem parâmetros (ao contrário de GroupItem que é abstrato)
            var folder = new FolderItem { DisplayName = options.FolderName };
            int idx = 0;

            foreach (var key in uniqueKeys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
            {
                var search = new Search();
                search.Selection.SelectAll();
                search.Locations = SearchLocations.DescendantsAndSelf;

                // Divide key de volta nos valores individuais para montar as condições
                var valueParts = key.Split(new[] { options.Separator }, StringSplitOptions.None);

                for (int i = 0; i < options.Properties.Count && i < valueParts.Length; i++)
                {
                    var gp  = options.Properties[i];
                    var val = valueParts[i];

                    if (val == options.MissingLabel) continue;

                    // EqualValue com DisplayString cobre propriedades armazenadas como string
                    var cond = SearchCondition
                        .HasPropertyByDisplayName(gp.TabName, gp.PropertyName)
                        .EqualValue(new VariantData(val));

                    search.SearchConditions.Add(cond);
                }

                // SelectionSet(Search) cria um Search Set dinâmico (re-avaliado a cada uso)
                if (search.SearchConditions.Count > 0)
                {
                    var selSet = new SelectionSet(search) { DisplayName = key };
                    folder.Children.Add(selSet);
                    result.SetsCreated++;
                }

                idx++;
                if (idx % 10 == 0 || idx == uniqueKeys.Count)
                    ReportValue(idx, uniqueKeys.Count);
            }

            sets.Add(folder);
            Report($"{result.SetsCreated} Search Set(s) criado(s) em '{options.FolderName}'.");
        }

        // ── Leitura de propriedade ────────────────────────────────────────────────

        private static string? ReadPropertyValue(ModelItem item, string tabName, string propName)
        {
            foreach (var cat in item.PropertyCategories)
            {
                if (!string.Equals(cat.DisplayName, tabName, StringComparison.OrdinalIgnoreCase))
                    continue;
                foreach (var prop in cat.Properties)
                    if (string.Equals(prop.DisplayName, propName, StringComparison.OrdinalIgnoreCase))
                    {
                        var val = prop.Value?.ToDisplayString();
                        if (!string.IsNullOrWhiteSpace(val))
                            return val.Trim();
                    }
            }
            return null;
        }

        private static List<ModelItem> GetAllItems(Document doc)
        {
            var result = new List<ModelItem>();
            var stack  = new Stack<ModelItem>();

            foreach (var model in doc.Models)
                foreach (var root in model.RootItem.Children)
                    stack.Push(root);

            while (stack.Count > 0)
            {
                var item = stack.Pop();
                result.Add(item);
                foreach (var child in item.Children)
                    stack.Push(child);
            }
            return result;
        }

        private void Report(string msg)        => ProgressChanged?.Invoke(this, msg);
        private void ReportValue(int c, int t) => ProgressValue?.Invoke(this, (c, t));
    }
}
