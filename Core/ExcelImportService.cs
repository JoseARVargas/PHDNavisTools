using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using Autodesk.Navisworks.Api;
using ExcelDataReader;
using NavisworksIfcExporter.Models;

namespace NavisworksIfcExporter.Core
{
    public class ExcelImportService
    {
        public event EventHandler<string>? ProgressChanged;

        // -----------------------------------------------------------------------
        // Leitura de metadados da planilha
        // -----------------------------------------------------------------------

        public ExcelFileInfo ReadFileInfo(string filePath)
        {
            using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = ExcelReaderFactory.CreateReader(stream);

            var info = new ExcelFileInfo();
            do { info.SheetNames.Add(reader.Name); } while (reader.NextResult());

            info.Columns = ReadColumnsFromSheet(filePath, 0);
            return info;
        }

        public List<string> ReadColumnsFromSheet(string filePath, int sheetIndex)
        {
            using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = ExcelReaderFactory.CreateReader(stream);

            for (int i = 0; i < sheetIndex; i++)
                if (!reader.NextResult()) return new List<string>();

            if (!reader.Read()) return new List<string>();

            var cols = new List<string>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var val = reader.GetValue(i)?.ToString()?.Trim() ?? string.Empty;
                if (!string.IsNullOrEmpty(val))
                    cols.Add(val);
            }
            return cols;
        }

        // -----------------------------------------------------------------------
        // Importação principal
        // -----------------------------------------------------------------------

        public ImportResult Import(ExcelImportOptions options, Document document)
        {
            var result = new ImportResult();

            // 1. Lê os dados da planilha indexados pela coluna chave
            Report("Lendo planilha...");
            var excelRows = ReadExcelRows(options);
            result.TotalExcelRows = excelRows.Count;
            Report($"  {excelRows.Count} linha(s) lida(s).");

            if (excelRows.Count == 0)
                return result;

            // 2. Constrói o índice do modelo: valorChave → lista de ModelItems
            Report("Indexando elementos do modelo...");
            IEnumerable<ModelItem> sourceItems = options.SelectionOnly
                ? document.CurrentSelection.SelectedItems.OfType<ModelItem>()
                : FlattenRoots(document.Models.RootItems.OfType<ModelItem>());

            var modelIndex = BuildModelIndex(sourceItems, options.ModelKeyCategory, options.ModelKeyProperty);
            Report($"  {modelIndex.Values.Sum(l => l.Count)} elemento(s) indexado(s).");

            // 3. Monta o lote de escritas: elemento → {propriedade → valor}
            Report("Correspondendo linhas e elementos...");
            var batch = new List<(ModelItem Item, Dictionary<string, string> Props)>();

            foreach (var kvp in excelRows)
            {
                var keyValue = kvp.Key;
                var rowData  = kvp.Value;

                if (!modelIndex.TryGetValue(keyValue, out var matchedItems))
                {
                    result.UnmatchedRows++;
                    if (result.Warnings.Count < 20)
                        result.Warnings.Add($"Sem correspondência para chave: '{keyValue}'");
                    continue;
                }

                // Filtra apenas as colunas selecionadas pelo usuário
                var propsToWrite = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in rowData)
                {
                    if (options.ColumnsToImport.Contains(kv.Key, StringComparer.OrdinalIgnoreCase))
                        propsToWrite[kv.Key] = kv.Value;
                }

                if (propsToWrite.Count == 0) continue;

                foreach (var item in matchedItems)
                {
                    batch.Add((item, propsToWrite));
                    result.MatchedElements++;
                }
            }

            if (batch.Count == 0)
            {
                Report("Nenhum elemento correspondido. Nada gravado.");
                return result;
            }

            // 4. Grava em lote (abre o COM uma única vez)
            Report($"Gravando {batch.Count} elemento(s)...");
            var writer = new PropertyWriter();
            writer.WriteAll(options.TargetTabName, batch);

            result.WrittenProperties = batch.Sum(b => b.Props.Count);
            Report($"Concluído: {result.MatchedElements} elemento(s), {result.WrittenProperties} propriedade(s) gravada(s).");

            return result;
        }

        // -----------------------------------------------------------------------
        // Helpers privados
        // -----------------------------------------------------------------------

        private Dictionary<string, Dictionary<string, string>> ReadExcelRows(ExcelImportOptions options)
        {
            var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            using var stream = File.Open(options.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var ds = ExcelReaderFactory.CreateReader(stream).AsDataSet(new ExcelDataSetConfiguration
            {
                ConfigureDataTable = _ => new ExcelDataTableConfiguration { UseHeaderRow = true }
            });

            if (ds.Tables.Count <= options.SheetIndex) return result;
            var table = ds.Tables[options.SheetIndex];

            if (!table.Columns.Contains(options.ExcelKeyColumn)) return result;

            foreach (DataRow row in table.Rows)
            {
                var keyVal = row[options.ExcelKeyColumn]?.ToString()?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(keyVal)) continue;

                var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (DataColumn col in table.Columns)
                {
                    var colName = col.ColumnName.Trim();
                    if (!string.IsNullOrEmpty(colName))
                        props[colName] = row[col]?.ToString()?.Trim() ?? string.Empty;
                }

                result[keyVal] = props;
            }

            return result;
        }

        private static Dictionary<string, List<ModelItem>> BuildModelIndex(
            IEnumerable<ModelItem> items,
            string keyCategory,
            string keyProperty)
        {
            var index = new Dictionary<string, List<ModelItem>>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in items)
            {
                PropertyCategory? cat = null;
                foreach (var c in item.PropertyCategories)
                {
                    if (string.Equals(c.DisplayName, keyCategory, StringComparison.OrdinalIgnoreCase))
                    {
                        cat = c;
                        break;
                    }
                }
                if (cat == null) continue;

                DataProperty? prop = null;
                foreach (var p in cat.Properties)
                {
                    if (string.Equals(p.DisplayName, keyProperty, StringComparison.OrdinalIgnoreCase))
                    {
                        prop = p;
                        break;
                    }
                }
                if (prop == null) continue;

                var keyVal = prop.Value?.ToDisplayString()?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(keyVal)) continue;

                if (!index.TryGetValue(keyVal, out var list))
                    index[keyVal] = list = new List<ModelItem>();
                list.Add(item);
            }

            return index;
        }

        private static IEnumerable<ModelItem> FlattenRoots(IEnumerable<ModelItem> roots)
        {
            var queue = new Queue<ModelItem>(roots);
            while (queue.Count > 0)
            {
                var item = queue.Dequeue();
                yield return item;
                foreach (var child in item.Children)
                    queue.Enqueue(child);
            }
        }

        private void Report(string msg) => ProgressChanged?.Invoke(this, msg);
    }
}
