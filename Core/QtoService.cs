using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Interop;
using Autodesk.Navisworks.Api.Takeoff;

namespace PHDNavisTools.Core
{
    public class QtoItemInfo
    {
        public long   RowId;
        public string Code        = "";
        public string Name        = "";
        public string Description = "";

        public string DisplayLabel =>
            string.IsNullOrWhiteSpace(Code) ? Name : $"[{Code}] {Name}";
    }

    public enum QtoUpdateMode
    {
        ClearAndReattach,   // delete existing objects then reinsert
        AppendOnly,         // skip elements already attached (treatDuplicateGuidAsEmpty)
    }

    public class QtoSearchSetRule
    {
        public string SetName  { get; set; } = "";
        public string ItemCode { get; set; } = "";   // WBS code to match
        public string ItemName { get; set; } = "";   // display label (informative)
        public long   ItemRowId { get; set; }        // resolved at run-time
    }

    public class QtoPropertyRule
    {
        public string Category     { get; set; } = "";
        public string PropertyName { get; set; } = "";
        public string Value        { get; set; } = "";
        public string ItemCode     { get; set; } = "";
        public string ItemName     { get; set; } = "";
        public long   ItemRowId    { get; set; }
    }

    public class QtoRunResult
    {
        public int Mapped;
        public int Unmapped;
        public int Duplicates;
        public List<string> Errors = new();
        public List<ModelItem> MappedItems   = new();
        public List<ModelItem> UnmappedItems = new();
    }

    public static class QtoService
    {
        // -----------------------------------------------------------------------
        // Availability
        // -----------------------------------------------------------------------

        public static bool IsAvailable(Document doc)
        {
            if (doc == null) return false;
            try
            {
                var takeoff = DocumentExtensions.GetTakeoff(doc);
                return takeoff.HasDatabaseInitialized;
            }
            catch { return false; }
        }

        // -----------------------------------------------------------------------
        // Enumerate QTO items from the embedded SQLite database
        // -----------------------------------------------------------------------

        public static List<QtoItemInfo> GetItems(Document doc)
        {
            var result = new List<QtoItemInfo>();
            try
            {
                var takeoff = DocumentExtensions.GetTakeoff(doc);
                if (!takeoff.HasDatabaseInitialized) return result;

                var cfgMgr = LcTkConfigManager.Instance;
                if (!cfgMgr.IsDatabaseLoaded()) return result;

                var tableDef  = cfgMgr.GetTableDef(LcTkTableEnum.Item);
                string table  = tableDef.DatabaseTableName;
                var conn      = cfgMgr.DatabaseConnection;

                // Discover column indices via PRAGMA so we aren't tied to positional assumptions
                int wbsIdx = -1, nameIdx = -1, descIdx = -1;
                using (var pragma = new LcUSQLiteStatement())
                {
                    if (pragma.Prepare(conn, $"PRAGMA table_info(\"{table}\")") == LcUSQLiteStatus.eOK)
                    {
                        while (pragma.Step() == LcUSQLiteStatus.eROW)
                        {
                            pragma.ColumnString(1, out var colName);
                            if (colName == null) continue;
                            pragma.ColumnString(2, out var colType);
                            // PRAGMA table_info: col 0=cid, 1=name, 2=type, 3=notnull, 4=dflt, 5=pk
                            // We are iterating columns; save the cid for later SELECT
                            int cid = pragma.ColumnInt32(0);
                            string lower = colName.ToLowerInvariant();
                            if (lower == "wbs")                         wbsIdx  = cid;
                            else if (lower == "name")                   nameIdx = cid;
                            else if (lower.Contains("desc"))            descIdx = cid;
                        }
                    }
                }

                // Build SELECT with explicit known columns (rowid always accessible)
                string wbsCol  = wbsIdx  >= 0 ? "WBS"         : "NULL";
                string nameCol = nameIdx >= 0 ? "Name"        : "NULL";
                string descCol = descIdx >= 0 ? "Description" : "NULL";

                using var stmt = new LcUSQLiteStatement();
                string sql = $"SELECT rowid, {wbsCol}, {nameCol}, {descCol} FROM \"{table}\" ORDER BY rowid";
                if (stmt.Prepare(conn, sql) != LcUSQLiteStatus.eOK) return result;

                while (stmt.Step() == LcUSQLiteStatus.eROW)
                {
                    stmt.ColumnString(1, out var wbs);
                    stmt.ColumnString(2, out var name);
                    stmt.ColumnString(3, out var desc);

                    result.Add(new QtoItemInfo {
                        RowId       = stmt.ColumnInt64(0),
                        Code        = wbs  ?? "",
                        Name        = name ?? "",
                        Description = desc ?? "",
                    });
                }
            }
            catch (Exception ex)
            {
                // Return what we have; caller handles empty list
                _ = ex;
            }
            return result;
        }

        // -----------------------------------------------------------------------
        // Resolve item row ID by code (WBS) or name
        // -----------------------------------------------------------------------

        public static long ResolveItemRowId(List<QtoItemInfo> items, string codeOrName)
        {
            if (string.IsNullOrWhiteSpace(codeOrName)) return -1;
            // 1. Exact code match
            foreach (var i in items)
                if (i.Code.Equals(codeOrName, StringComparison.OrdinalIgnoreCase)) return i.RowId;
            // 2. Exact name match
            foreach (var i in items)
                if (i.Name.Equals(codeOrName, StringComparison.OrdinalIgnoreCase)) return i.RowId;
            return -1;
        }

        // -----------------------------------------------------------------------
        // Clear all Objects attached to a specific Item row
        // -----------------------------------------------------------------------

        private static void ClearItemAttachments(DocumentTakeoff takeoff, long itemRowId)
        {
            var cfgMgr = LcTkConfigManager.Instance;
            var objDef = cfgMgr.GetTableDef(LcTkTableEnum.Object);
            string objTable = objDef.DatabaseTableName;
            var conn = cfgMgr.DatabaseConnection;

            // Discover parent-item reference column via PRAGMA
            string? itemRefCol = null;
            using (var pragma = new LcUSQLiteStatement())
            {
                if (pragma.Prepare(conn, $"PRAGMA table_info(\"{objTable}\")") == LcUSQLiteStatus.eOK)
                {
                    while (pragma.Step() == LcUSQLiteStatus.eROW)
                    {
                        pragma.ColumnString(1, out var colName);
                        if (colName == null) continue;
                        string lower = colName.ToLowerInvariant();
                        if (lower.Contains("item") || lower == "parent_id")
                        {
                            itemRefCol = colName;
                            break;
                        }
                    }
                }
            }

            itemRefCol ??= "item_id";

            using var del = new LcUSQLiteStatement();
            if (del.Prepare(conn, $"DELETE FROM \"{objTable}\" WHERE \"{itemRefCol}\" = ?") == LcUSQLiteStatus.eOK)
            {
                del.BindInt64(1, itemRowId);
                del.Step();
            }
        }

        // -----------------------------------------------------------------------
        // Attach a collection of elements to an Item
        // -----------------------------------------------------------------------

        private static (int attached, int duplicates, int errors) AttachToItem(
            DocumentTakeoff takeoff, long itemRowId,
            IEnumerable<ModelItem> elements, QtoUpdateMode mode)
        {
            if (mode == QtoUpdateMode.ClearAndReattach)
                ClearItemAttachments(takeoff, itemRowId);

            var defaultVars = takeoff.Objects.CreateDefaultInputVariables();
            int attached = 0, duplicates = 0, errors = 0;
            bool skipDuplicates = mode == QtoUpdateMode.AppendOnly;

            foreach (var elem in elements)
            {
                try
                {
                    long objRowId = takeoff.Objects.InsertModelItemTakeoff(
                        itemRowId, elem.InstanceGuid, defaultVars,
                        treatDuplicateGuidAsEmpty: skipDuplicates);

                    if (objRowId > 0) attached++;
                    else              duplicates++;
                }
                catch { errors++; }
            }
            return (attached, duplicates, errors);
        }

        // -----------------------------------------------------------------------
        // Main: run SearchSet-based mapping (async com progresso)
        // -----------------------------------------------------------------------

        public static async Task<QtoRunResult> RunSearchSetMappingAsync(
            Document doc, IList<QtoSearchSetRule> rules, QtoUpdateMode mode,
            List<QtoItemInfo> items, Func<int, int, Task>? progress = null)
        {
            var result  = new QtoRunResult();
            var takeoff = DocumentExtensions.GetTakeoff(doc);
            if (!takeoff.HasDatabaseInitialized) { result.Errors.Add("QTO não inicializado."); return result; }

            var seen      = new HashSet<Guid>();
            var mappedSet = new HashSet<Guid>();
            int done = 0, total = rules.Count;

            foreach (var rule in rules)
            {
                if (progress != null) await progress(done++, total);

                long rowId = rule.ItemRowId > 0 ? rule.ItemRowId : ResolveItemRowId(items, rule.ItemCode);
                if (rowId <= 0) { result.Errors.Add($"Item QTO não encontrado: \"{rule.ItemCode}\""); continue; }

                var set = FindSet(doc, rule.SetName);
                if (set == null) { result.Errors.Add($"Set não encontrado: \"{rule.SetName}\""); continue; }

                var elements = new List<ModelItem>();
                foreach (var item in set.GetSelectedItems(doc))
                    if (seen.Add(item.InstanceGuid)) elements.Add(item);

                var (att, dup, err) = AttachToItem(takeoff, rowId, elements, mode);
                result.Mapped     += att;
                result.Duplicates += dup;
                if (err > 0) result.Errors.Add($"[{rule.ItemCode}] {err} erro(s) ao vincular.");
                foreach (var e in elements) { mappedSet.Add(e.InstanceGuid); result.MappedItems.Add(e); }
            }

            // Collect unmapped (iterate all geometry items with yield every 200)
            var allGeom = IterateAllModelItems(doc).ToList();
            for (int i = 0; i < allGeom.Count; i++)
            {
                if (!mappedSet.Contains(allGeom[i].InstanceGuid)) result.UnmappedItems.Add(allGeom[i]);
                if (i % 200 == 0 && progress != null) await progress(total, total);
            }

            result.Unmapped = result.UnmappedItems.Count;
            return result;
        }

        // -----------------------------------------------------------------------
        // Main: run Property-value-based mapping (async com progresso)
        // -----------------------------------------------------------------------

        public static async Task<QtoRunResult> RunPropertyMappingAsync(
            Document doc, IList<QtoPropertyRule> rules, QtoUpdateMode mode,
            List<QtoItemInfo> items, Func<int, int, Task>? progress = null)
        {
            var result      = new QtoRunResult();
            var takeoff     = DocumentExtensions.GetTakeoff(doc);
            if (!takeoff.HasDatabaseInitialized) { result.Errors.Add("QTO não inicializado."); return result; }

            var byItem      = new Dictionary<long, List<ModelItem>>();
            var mappedGuids = new HashSet<Guid>();
            var allGeom     = IterateAllModelItems(doc).ToList();
            int total       = allGeom.Count;

            for (int i = 0; i < total; i++)
            {
                var modelItem = allGeom[i];
                if (!modelItem.HasGeometry) continue;

                foreach (var rule in rules)
                {
                    long rowId = rule.ItemRowId > 0 ? rule.ItemRowId : ResolveItemRowId(items, rule.ItemCode);
                    if (rowId <= 0) continue;

                    string propVal = GetPropertyValue(modelItem, rule.Category, rule.PropertyName);
                    if (string.IsNullOrEmpty(propVal)) continue;
                    if (!propVal.Equals(rule.Value, StringComparison.OrdinalIgnoreCase)) continue;

                    if (!byItem.TryGetValue(rowId, out var list)) byItem[rowId] = list = new List<ModelItem>();
                    list.Add(modelItem);
                    mappedGuids.Add(modelItem.InstanceGuid);
                    break;
                }

                if (i % 200 == 0 && progress != null) await progress(i, total);
            }

            foreach (var kvp in byItem)
            {
                var (att, dup, err) = AttachToItem(takeoff, kvp.Key, kvp.Value, mode);
                result.Mapped     += att;
                result.Duplicates += dup;
                result.MappedItems.AddRange(kvp.Value);
                if (err > 0) result.Errors.Add($"[rowId={kvp.Key}] {err} erro(s) ao vincular.");
            }

            for (int i = 0; i < allGeom.Count; i++)
                if (!mappedGuids.Contains(allGeom[i].InstanceGuid)) result.UnmappedItems.Add(allGeom[i]);

            result.Unmapped = result.UnmappedItems.Count;
            return result;
        }

        // -----------------------------------------------------------------------
        // Export historical CSV snapshot
        // -----------------------------------------------------------------------

        public static void ExportHistoryCsv(Document doc, List<QtoItemInfo> items, string outputDir)
        {
            var takeoff = DocumentExtensions.GetTakeoff(doc);
            if (!takeoff.HasDatabaseInitialized) return;

            var cfgMgr   = LcTkConfigManager.Instance;
            var objDef   = cfgMgr.GetTableDef(LcTkTableEnum.Object);
            string objTable = objDef.DatabaseTableName;
            var conn     = cfgMgr.DatabaseConnection;

            // Discover columns
            string itemRefCol = "item_id"; string? guidCol = null;
            using (var pragma = new LcUSQLiteStatement())
            {
                if (pragma.Prepare(conn, $"PRAGMA table_info(\"{objTable}\")") == LcUSQLiteStatus.eOK)
                    while (pragma.Step() == LcUSQLiteStatus.eROW)
                    {
                        pragma.ColumnString(1, out var col);
                        if (col == null) continue;
                        string lower = col.ToLowerInvariant();
                        if (lower.Contains("item") || lower == "parent_id") itemRefCol = col;
                        if (lower.Contains("guid") || lower.Contains("model_item")) guidCol = col;
                    }
            }

            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string path  = Path.Combine(outputDir, $"qto_snapshot_{stamp}.csv");
            Directory.CreateDirectory(outputDir);

            var itemById = new Dictionary<long, QtoItemInfo>();
            foreach (var it in items) itemById[it.RowId] = it;

            using var w = new StreamWriter(path, false, new UTF8Encoding(true));
            w.WriteLine("Snapshot;Codigo;Nome;ElementoGUID");

            using var stmt = new LcUSQLiteStatement();
            string gcol = guidCol ?? "model_item_id";
            string sql = $"SELECT \"{itemRefCol}\", \"{gcol}\" FROM \"{objTable}\"";
            if (stmt.Prepare(conn, sql) != LcUSQLiteStatus.eOK) return;

            while (stmt.Step() == LcUSQLiteStatus.eROW)
            {
                long itemId = stmt.ColumnInt64(0);
                stmt.ColumnString(1, out var guid);
                if (!itemById.TryGetValue(itemId, out var info)) continue;
                w.WriteLine($"{stamp};{info.Code};{info.Name};{guid}");
            }
        }

        // -----------------------------------------------------------------------
        // Create/update QTO Selection Sets
        // -----------------------------------------------------------------------

        public static void UpdateSelectionSets(Document doc, List<ModelItem> mapped, List<ModelItem> unmapped)
        {
            try
            {
                // Find or create "QTO" folder
                var sets   = doc.SelectionSets.Value;
                GroupItem? folder = null;

                for (int i = 0; i < sets.Count; i++)
                {
                    if (sets[i] is GroupItem g && g.DisplayName == "QTO")
                    { folder = g; break; }
                }

                if (folder == null)
                {
                    // Try to clone an existing GroupItem
                    GroupItem? template = null;
                    foreach (var item in sets)
                        if (item is GroupItem gi) { template = gi; break; }

                    if (template != null)
                    {
                        folder = (GroupItem)template.CreateNewInstance();
                        folder.DisplayName = "QTO";
                        sets.Add(folder);
                    }
                }

                var mappedCollection   = new ModelItemCollection(); mappedCollection.AddRange(mapped);
                var unmappedCollection = new ModelItemCollection(); unmappedCollection.AddRange(unmapped);

                var mappedSet   = new SelectionSet(mappedCollection)   { DisplayName = "Elementos mapeados"   };
                var unmappedSet = new SelectionSet(unmappedCollection) { DisplayName = "Elementos não mapeados" };

                if (folder != null)
                {
                    // Remove existing sets with same names
                    for (int i = folder.Children.Count - 1; i >= 0; i--)
                    {
                        var name = folder.Children[i].DisplayName;
                        if (name == "Elementos mapeados" || name == "Elementos não mapeados")
                            folder.Children.RemoveAt(i);
                    }
                    folder.Children.Add(mappedSet);
                    folder.Children.Add(unmappedSet);
                }
                else
                {
                    // Fallback: root-level sets
                    for (int i = sets.Count - 1; i >= 0; i--)
                    {
                        var name = sets[i].DisplayName;
                        if (name == "QTO — Elementos mapeados" || name == "QTO — Elementos não mapeados")
                            sets.RemoveAt(i);
                    }
                    mappedSet.DisplayName   = "QTO — Elementos mapeados";
                    unmappedSet.DisplayName = "QTO — Elementos não mapeados";
                    sets.Add(mappedSet);
                    sets.Add(unmappedSet);
                }
            }
            catch { }
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private static SelectionSet? FindSet(Document doc, string name)
        {
            return FindSetInCollection(doc.SelectionSets.Value, name);
        }

        private static SelectionSet? FindSetInCollection(SavedItemCollection coll, string name)
        {
            foreach (var item in coll)
            {
                if (item is SelectionSet ss && ss.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return ss;
                if (item is GroupItem g)
                {
                    var found = FindSetInCollection(g.Children, name);
                    if (found != null) return found;
                }
            }
            return null;
        }

        private static IEnumerable<ModelItem> IterateAllModelItems(Document doc)
        {
            return WalkItems(doc.Models.RootItems);
        }

        private static IEnumerable<ModelItem> WalkItems(IEnumerable<ModelItem> items)
        {
            foreach (var item in items)
            {
                if (item.HasGeometry) yield return item;
                foreach (var child in WalkItems(item.Children)) yield return child;
            }
        }

        private static string GetPropertyValue(ModelItem item, string category, string propName)
        {
            foreach (var cat in item.PropertyCategories)
            {
                if (!cat.DisplayName.Equals(category, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var p in cat.Properties)
                    if (p.DisplayName.Equals(propName, StringComparison.OrdinalIgnoreCase))
                        return p.Value?.ToDisplayString() ?? "";
            }
            return "";
        }
    }
}
