using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;

namespace PHDNavisTools.Core
{
    /// <summary>
    /// Walks the active document and collects all unique property category names
    /// and the property names within each category, up to a configurable item limit
    /// so the scan stays fast even on large models.
    /// </summary>
    public static class NavisPropertyScanner
    {
        public static Dictionary<string, List<string>> Scan(int maxItems = 3000)
        {
            var doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
            if (doc == null) return new Dictionary<string, List<string>>();

            var result = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
            int count = 0;

            // BFS so we sample broadly across the model hierarchy before hitting the limit
            var queue = new Queue<ModelItem>();
            foreach (var root in doc.Models.RootItems)
                queue.Enqueue(root);

            while (queue.Count > 0 && count < maxItems)
            {
                var item = queue.Dequeue();
                count++;

                foreach (var cat in item.PropertyCategories)
                {
                    if (!result.TryGetValue(cat.DisplayName, out var propSet))
                    {
                        propSet = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                        result[cat.DisplayName] = propSet;
                    }
                    foreach (var prop in cat.Properties)
                        propSet.Add(prop.DisplayName);
                }

                // Enqueue children; cap the queue so memory stays bounded on huge models
                foreach (var child in item.Children)
                {
                    if (queue.Count < 50_000)
                        queue.Enqueue(child);
                }
            }

            return result.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToList(),
                StringComparer.OrdinalIgnoreCase);
        }
    }
}
