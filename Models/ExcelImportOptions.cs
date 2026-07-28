using System.Collections.Generic;

namespace NavisworksIfcExporter.Models
{
    public class ExcelImportOptions
    {
        public string FilePath          { get; set; } = string.Empty;
        public int    SheetIndex        { get; set; } = 0;

        /// <summary>Nome da coluna da planilha usada como chave de correspondência.</summary>
        public string ExcelKeyColumn    { get; set; } = string.Empty;

        /// <summary>Categoria (aba) da propriedade do modelo usada como chave.</summary>
        public string ModelKeyCategory  { get; set; } = string.Empty;

        /// <summary>Nome da propriedade do modelo usada como chave.</summary>
        public string ModelKeyProperty  { get; set; } = string.Empty;

        /// <summary>Nome da aba customizada onde os dados serão gravados no modelo.</summary>
        public string TargetTabName     { get; set; } = "PHD_Excel";

        /// <summary>Colunas da planilha a importar (excluindo a coluna chave).</summary>
        public List<string> ColumnsToImport { get; set; } = new();

        public bool OverwriteExisting   { get; set; } = true;
        public bool SelectionOnly       { get; set; } = false;
    }

    public class ExcelFileInfo
    {
        public List<string> SheetNames { get; set; } = new();
        public List<string> Columns    { get; set; } = new();
    }

    public class ImportResult
    {
        public int          TotalExcelRows      { get; set; }
        public int          MatchedElements     { get; set; }
        public int          UnmatchedRows       { get; set; }
        public int          WrittenProperties   { get; set; }
        public List<string> Warnings            { get; set; } = new();
    }
}
