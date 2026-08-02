using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using PHDNavisTools.Core;

namespace PHDNavisTools.Tests
{
    /// <summary>
    /// Testa CheckService sem instanciar nenhum tipo Navisworks:
    /// - LoadRules: parsing de CSV com diferentes formatos
    /// - CheckPropertyFromCache: lógica OK / EMPTY / MISSING
    /// - ItemMatchesFilterFromCache: filtro de aplicabilidade
    /// </summary>
    public class CheckServiceTests
    {
        private static string Data(string name)
            => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData", name);

        private static Dictionary<string, Dictionary<string, string>> Cache(
            params (string cat, string prop, string val)[] entries)
        {
            var cache = new Dictionary<string, Dictionary<string, string>>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                if (!cache.TryGetValue(entry.cat, out var props))
                    cache[entry.cat] = props = new Dictionary<string, string>(
                        StringComparer.OrdinalIgnoreCase);
                props[entry.prop] = entry.val;
            }
            return cache;
        }

        // ── CheckPropertyFromCache ────────────────────────────────────────────

        [Fact]
        public void CheckProperty_CategoryAndPropPresent_ReturnsOk()
        {
            var cache = Cache(("Pset_ColumnCommon", "LoadBearing", "YES"));
            var (result, val) = CheckService.CheckPropertyFromCache(
                cache, "Pset_ColumnCommon", "LoadBearing");

            Assert.Equal(CheckService.OK, result);
            Assert.Equal("YES", val);
        }

        [Fact]
        public void CheckProperty_PropExistsButEmpty_ReturnsEmpty()
        {
            var cache = Cache(("Pset_ColumnCommon", "LoadBearing", ""));
            var (result, _) = CheckService.CheckPropertyFromCache(
                cache, "Pset_ColumnCommon", "LoadBearing");

            Assert.Equal(CheckService.EMPTY, result);
        }

        [Fact]
        public void CheckProperty_PropExistsWithWhitespace_ReturnsEmpty()
        {
            var cache = Cache(("Pset_ColumnCommon", "LoadBearing", "   "));
            var (result, _) = CheckService.CheckPropertyFromCache(
                cache, "Pset_ColumnCommon", "LoadBearing");

            Assert.Equal(CheckService.EMPTY, result);
        }

        [Fact]
        public void CheckProperty_CategoryMissing_ReturnsMissing()
        {
            var cache = Cache(("OutroSet", "Prop", "Val"));
            var (result, _) = CheckService.CheckPropertyFromCache(
                cache, "Pset_ColumnCommon", "LoadBearing");

            Assert.Equal(CheckService.MISSING, result);
        }

        [Fact]
        public void CheckProperty_CategoryPresentPropMissing_ReturnsMissing()
        {
            var cache = Cache(("Pset_ColumnCommon", "OutraProp", "Val"));
            var (result, _) = CheckService.CheckPropertyFromCache(
                cache, "Pset_ColumnCommon", "LoadBearing");

            Assert.Equal(CheckService.MISSING, result);
        }

        [Fact]
        public void CheckProperty_CategoryLookupIsCaseInsensitive()
        {
            var cache = Cache(("pset_columncommon", "loadbearing", "YES"));
            var (result, _) = CheckService.CheckPropertyFromCache(
                cache, "Pset_ColumnCommon", "LoadBearing");

            Assert.Equal(CheckService.OK, result);
        }

        // ── ItemMatchesFilterFromCache ────────────────────────────────────────

        [Fact]
        public void Filter_CategoryAndPropWithValue_ReturnsTrue()
        {
            var cache = Cache(("IFC", "Entity", "IfcColumn"));
            Assert.True(CheckService.ItemMatchesFilterFromCache(cache, "IFC", "Entity"));
        }

        [Fact]
        public void Filter_CategoryMissing_ReturnsFalse()
        {
            var cache = Cache(("OutroSet", "Prop", "Val"));
            Assert.False(CheckService.ItemMatchesFilterFromCache(cache, "IFC", "Entity"));
        }

        [Fact]
        public void Filter_PropMissingInCategory_ReturnsFalse()
        {
            var cache = Cache(("IFC", "OutraProp", "Val"));
            Assert.False(CheckService.ItemMatchesFilterFromCache(cache, "IFC", "Entity"));
        }

        [Fact]
        public void Filter_PropExistsButEmpty_ReturnsFalse()
        {
            var cache = Cache(("IFC", "Entity", ""));
            Assert.False(CheckService.ItemMatchesFilterFromCache(cache, "IFC", "Entity"));
        }

        [Fact]
        public void Filter_CaseInsensitiveLookup()
        {
            var cache = Cache(("ifc", "entity", "IfcBeam"));
            Assert.True(CheckService.ItemMatchesFilterFromCache(cache, "IFC", "Entity"));
        }

        // ── LoadRules — CSV separador ponto-e-vírgula ─────────────────────────

        [Fact]
        public void LoadRules_Semicolon_ReturnsCorrectCount()
        {
            var rules = CheckService.LoadRules(Data("rules_basic.csv"));
            Assert.Equal(4, rules.Count);
        }

        [Fact]
        public void LoadRules_Semicolon_FirstRule()
        {
            var rules = CheckService.LoadRules(Data("rules_basic.csv"));
            Assert.Equal("EST",              rules[0].Disciplina);
            Assert.Equal("Pset_ColumnCommon", rules[0].Categoria);
            Assert.Equal("LoadBearing",       rules[0].Propriedade);
        }

        [Fact]
        public void LoadRules_Semicolon_EmptyDisciplinaAllowed()
        {
            var rules = CheckService.LoadRules(Data("rules_basic.csv"));
            Assert.Equal("", rules[3].Disciplina);
        }

        // ── LoadRules — CSV separador vírgula ─────────────────────────────────

        [Fact]
        public void LoadRules_Comma_ReturnsCorrectCount()
        {
            var rules = CheckService.LoadRules(Data("rules_comma.csv"));
            Assert.Equal(2, rules.Count);
        }

        [Fact]
        public void LoadRules_Comma_ParsedCorrectly()
        {
            var rules = CheckService.LoadRules(Data("rules_comma.csv"));
            Assert.Equal("EST",               rules[0].Disciplina);
            Assert.Equal("Pset_ColumnCommon", rules[0].Categoria);
        }

        // ── LoadRules — colunas de filtro opcionais ───────────────────────────

        [Fact]
        public void LoadRules_WithFilterColumns_ParsedCorrectly()
        {
            var rules = CheckService.LoadRules(Data("rules_with_filter.csv"));
            Assert.Equal(3, rules.Count);

            // Linha 1: com filtro
            Assert.Equal("IFC",    rules[0].CategoriaFiltro);
            Assert.Equal("Entity", rules[0].PropriedadeFiltro);
            Assert.True(rules[0].TemFiltro);

            // Linha 2: colunas presentes mas vazias → sem filtro
            Assert.False(rules[1].TemFiltro);
        }

        [Fact]
        public void LoadRules_BasicCsv_NoFilterColumns_TemFiltroFalse()
        {
            var rules = CheckService.LoadRules(Data("rules_basic.csv"));
            Assert.All(rules, r => Assert.False(r.TemFiltro));
        }

        // ── LoadRules — erros ──────────────────────────────────────────────────

        [Fact]
        public void LoadRules_MissingRequiredColumns_ThrowsInvalidDataException()
        {
            Assert.Throws<InvalidDataException>(
                () => CheckService.LoadRules(Data("rules_missing_columns.csv")));
        }

        [Fact]
        public void LoadRules_NonExistentFile_ThrowsException()
        {
            Assert.ThrowsAny<Exception>(
                () => CheckService.LoadRules(Data("nao_existe.csv")));
        }

        // ── CheckSummaryRow ───────────────────────────────────────────────────

        [Fact]
        public void SummaryRow_PctOk_CalculatesCorrectly()
        {
            var row = new CheckSummaryRow { Total = 10, Ok = 7, Vazia = 2, Ausente = 1 };
            Assert.Equal("70%", row.PctOk);
        }

        [Fact]
        public void SummaryRow_PctOk_ZeroTotal_ReturnsDash()
        {
            var row = new CheckSummaryRow { Total = 0 };
            Assert.Equal("—", row.PctOk);
        }

        [Fact]
        public void SummaryRow_BarOk_HasCorrectLength()
        {
            var row = new CheckSummaryRow { Total = 10, Ok = 10 };
            // 20 blocos + 2 espaços + percentual
            Assert.Contains("100%", row.BarOk);
            Assert.Contains("████████████████████", row.BarOk);
        }
    }
}
