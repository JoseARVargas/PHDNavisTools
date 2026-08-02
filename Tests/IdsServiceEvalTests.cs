using System.Collections.Generic;
using System.Linq;
using Xunit;
using PHDNavisTools.Core;

namespace PHDNavisTools.Tests
{
    /// <summary>
    /// Testa o motor de avaliação do IdsService usando caches de propriedades
    /// construídos manualmente — sem instanciar nenhum tipo do Navisworks.
    /// </summary>
    public class IdsServiceEvalTests
    {
        // ── helper ────────────────────────────────────────────────────────────

        private static Dictionary<string, Dictionary<string, string>> Cache(
            params (string cat, string prop, string val)[] entries)
        {
            var cache = new Dictionary<string, Dictionary<string, string>>(
                System.StringComparer.OrdinalIgnoreCase);

            foreach (var entry in entries)
            {
                if (!cache.TryGetValue(entry.cat, out var props))
                    cache[entry.cat] = props = new Dictionary<string, string>(
                        System.StringComparer.OrdinalIgnoreCase);
                props[entry.prop] = entry.val;
            }
            return cache;
        }

        // ── GetEntityTypeFromCache ────────────────────────────────────────────

        [Theory]
        [InlineData("Entity")]
        [InlineData("IFC Type")]
        [InlineData("IfcType")]
        [InlineData("IFC Entity")]
        [InlineData("EntityType")]
        public void GetEntityType_FindsAllKnownPropertyNames(string propName)
        {
            var cache = Cache(("IFC", propName, "IfcColumn"));
            Assert.Equal("IfcColumn", IdsService.GetEntityTypeFromCache(cache));
        }

        [Fact]
        public void GetEntityType_ReturnsNullWhenPropertyAbsent()
        {
            var cache = Cache(("Pset_ColumnCommon", "LoadBearing", "YES"));
            Assert.Null(IdsService.GetEntityTypeFromCache(cache));
        }

        [Fact]
        public void GetEntityType_ReturnsNullWhenPropertyEmpty()
        {
            var cache = Cache(("IFC", "Entity", ""));
            Assert.Null(IdsService.GetEntityTypeFromCache(cache));
        }

        [Fact]
        public void GetEntityType_CaseInsensitiveCategoryLookup()
        {
            var cache = Cache(("ifc", "entity", "IfcBeam"));
            Assert.Equal("IfcBeam", IdsService.GetEntityTypeFromCache(cache));
        }

        // ── EvalFacet — entity ────────────────────────────────────────────────

        [Fact]
        public void EvalEntity_MatchingName_Passes()
        {
            var cache = Cache(("IFC", "Entity", "IfcColumn"));
            var facet = new IdsEntityFacet { Name = new IdsValue { SimpleValue = "IfcColumn" } };

            var (pass, reason) = IdsService.EvalFacet(cache, "IfcColumn", facet);

            Assert.True(pass);
            Assert.Equal("", reason);
        }

        [Fact]
        public void EvalEntity_MismatchedName_Fails()
        {
            var cache = Cache(("IFC", "Entity", "IfcBeam"));
            var facet = new IdsEntityFacet { Name = new IdsValue { SimpleValue = "IfcColumn" } };

            var (pass, _) = IdsService.EvalFacet(cache, "IfcBeam", facet);
            Assert.False(pass);
        }

        [Fact]
        public void EvalEntity_NullEntity_Fails()
        {
            var cache = Cache(("Pset_X", "Y", "Z"));
            var facet = new IdsEntityFacet { Name = new IdsValue { SimpleValue = "IfcColumn" } };

            var (pass, _) = IdsService.EvalFacet(cache, null, facet);
            Assert.False(pass);
        }

        [Fact]
        public void EvalEntity_AnyName_PassesAnyEntity()
        {
            var cache = Cache();
            var facet = new IdsEntityFacet { Name = IdsValue.Any };

            var (pass, _) = IdsService.EvalFacet(cache, "IfcWhatever", facet);
            Assert.True(pass);
        }

        // ── EvalFacet — property ──────────────────────────────────────────────

        [Fact]
        public void EvalProperty_PropertyExistsNoValueConstraint_Passes()
        {
            var cache = Cache(("Pset_ColumnCommon", "LoadBearing", "YES"));
            var facet = new IdsPropertyFacet
            {
                PropertySet = new IdsValue { SimpleValue = "Pset_ColumnCommon" },
                BaseName    = new IdsValue { SimpleValue = "LoadBearing" },
            };

            var (pass, _) = IdsService.EvalFacet(cache, null, facet);
            Assert.True(pass);
        }

        [Fact]
        public void EvalProperty_ValueMatchesEnumeration_Passes()
        {
            var cache = Cache(("Pset_ColumnCommon", "LoadBearing", "YES"));
            var facet = new IdsPropertyFacet
            {
                PropertySet = new IdsValue { SimpleValue = "Pset_ColumnCommon" },
                BaseName    = new IdsValue { SimpleValue = "LoadBearing" },
                Value       = new IdsValue
                {
                    Restriction = new IdsRestriction { Enumeration = { "YES", "TRUE" } }
                },
            };

            var (pass, _) = IdsService.EvalFacet(cache, null, facet);
            Assert.True(pass);
        }

        [Fact]
        public void EvalProperty_ValueMismatch_Fails()
        {
            var cache = Cache(("Pset_ColumnCommon", "LoadBearing", "NO"));
            var facet = new IdsPropertyFacet
            {
                PropertySet = new IdsValue { SimpleValue = "Pset_ColumnCommon" },
                BaseName    = new IdsValue { SimpleValue = "LoadBearing" },
                Value       = new IdsValue { SimpleValue = "YES" },
            };

            var (pass, _) = IdsService.EvalFacet(cache, null, facet);
            Assert.False(pass);
        }

        [Fact]
        public void EvalProperty_PropertySetNotFound_Fails()
        {
            var cache = Cache(("OutroSet", "Qualquer", "X"));
            var facet = new IdsPropertyFacet
            {
                PropertySet = new IdsValue { SimpleValue = "Pset_ColumnCommon" },
                BaseName    = new IdsValue { SimpleValue = "LoadBearing" },
            };

            var (pass, reason) = IdsService.EvalFacet(cache, null, facet);
            Assert.False(pass);
            Assert.Contains("Pset_ColumnCommon", reason);
        }

        [Fact]
        public void EvalProperty_PropertyNameNotFoundInSet_Fails()
        {
            var cache = Cache(("Pset_ColumnCommon", "OutraPropriedade", "X"));
            var facet = new IdsPropertyFacet
            {
                PropertySet = new IdsValue { SimpleValue = "Pset_ColumnCommon" },
                BaseName    = new IdsValue { SimpleValue = "LoadBearing" },
            };

            var (pass, _) = IdsService.EvalFacet(cache, null, facet);
            Assert.False(pass);
        }

        [Fact]
        public void EvalProperty_EmptyValueWithConstraint_Fails()
        {
            var cache = Cache(("Pset_ColumnCommon", "LoadBearing", ""));
            var facet = new IdsPropertyFacet
            {
                PropertySet = new IdsValue { SimpleValue = "Pset_ColumnCommon" },
                BaseName    = new IdsValue { SimpleValue = "LoadBearing" },
                Value       = new IdsValue { SimpleValue = "YES" },
            };

            var (pass, _) = IdsService.EvalFacet(cache, null, facet);
            Assert.False(pass);
        }

        // ── EvalFacet — attribute ─────────────────────────────────────────────

        [Fact]
        public void EvalAttribute_PropertyExists_Passes()
        {
            var cache = Cache(("Element", "Name", "COL-01"));
            var facet = new IdsAttributeFacet
            {
                Name = new IdsValue { SimpleValue = "Name" }
            };

            var (pass, _) = IdsService.EvalFacet(cache, null, facet);
            Assert.True(pass);
        }

        [Fact]
        public void EvalAttribute_NotFound_Fails()
        {
            var cache = Cache(("Element", "Tag", "T01"));
            var facet = new IdsAttributeFacet
            {
                Name = new IdsValue { SimpleValue = "Name" }
            };

            var (pass, reason) = IdsService.EvalFacet(cache, null, facet);
            Assert.False(pass);
            Assert.Contains("Name", reason);
        }

        [Fact]
        public void EvalAttribute_EmptyValue_Fails()
        {
            var cache = Cache(("Element", "Name", ""));
            var facet = new IdsAttributeFacet
            {
                Name = new IdsValue { SimpleValue = "Name" }
            };

            var (pass, _) = IdsService.EvalFacet(cache, null, facet);
            Assert.False(pass);
        }

        // ── MatchesApplicability ──────────────────────────────────────────────

        [Fact]
        public void MatchesApplicability_EmptyFacets_ReturnsTrue()
        {
            var cache = Cache();
            Assert.True(IdsService.MatchesApplicability(cache, null, new List<IdsFacet>()));
        }

        [Fact]
        public void MatchesApplicability_EntityMatches_ReturnsTrue()
        {
            var cache = Cache(("IFC", "Entity", "IfcColumn"));
            var facets = new List<IdsFacet>
            {
                new IdsEntityFacet { Name = new IdsValue { SimpleValue = "IfcColumn" } }
            };

            Assert.True(IdsService.MatchesApplicability(cache, "IfcColumn", facets));
        }

        [Fact]
        public void MatchesApplicability_EntityMismatches_ReturnsFalse()
        {
            var cache = Cache(("IFC", "Entity", "IfcBeam"));
            var facets = new List<IdsFacet>
            {
                new IdsEntityFacet { Name = new IdsValue { SimpleValue = "IfcColumn" } }
            };

            Assert.False(IdsService.MatchesApplicability(cache, "IfcBeam", facets));
        }

        // ── CheckRequirements ─────────────────────────────────────────────────

        [Fact]
        public void CheckRequirements_AllRequired_Pass()
        {
            var cache = Cache(
                ("Pset_ColumnCommon", "LoadBearing", "YES"),
                ("Element",          "Name",        "COL-01"));

            var facets = new List<IdsFacet>
            {
                new IdsPropertyFacet
                {
                    PropertySet = new IdsValue { SimpleValue = "Pset_ColumnCommon" },
                    BaseName    = new IdsValue { SimpleValue = "LoadBearing" },
                },
                new IdsAttributeFacet { Name = new IdsValue { SimpleValue = "Name" } },
            };

            var (pass, reason) = IdsService.CheckRequirements(cache, null, facets);
            Assert.True(pass);
            Assert.Equal("", reason);
        }

        [Fact]
        public void CheckRequirements_OptionalFacetIgnored()
        {
            var cache = Cache(("Pset_ColumnCommon", "LoadBearing", "YES"));

            var facets = new List<IdsFacet>
            {
                new IdsPropertyFacet
                {
                    PropertySet = new IdsValue { SimpleValue = "Pset_ColumnCommon" },
                    BaseName    = new IdsValue { SimpleValue = "LoadBearing" },
                },
                new IdsAttributeFacet
                {
                    Name        = new IdsValue { SimpleValue = "MissingAttr" },
                    Cardinality = "optional",
                },
            };

            var (pass, _) = IdsService.CheckRequirements(cache, null, facets);
            Assert.True(pass);
        }

        [Fact]
        public void CheckRequirements_RequiredFails_ReturnsFail()
        {
            var cache = Cache(("OutroSet", "Prop", "Val"));
            var facets = new List<IdsFacet>
            {
                new IdsPropertyFacet
                {
                    PropertySet = new IdsValue { SimpleValue = "Pset_ColumnCommon" },
                    BaseName    = new IdsValue { SimpleValue = "LoadBearing" },
                },
            };

            var (pass, reason) = IdsService.CheckRequirements(cache, null, facets);
            Assert.False(pass);
            Assert.NotEmpty(reason);
        }

        [Fact]
        public void CheckRequirements_ProhibitedPropertyPresent_Fails()
        {
            var cache = Cache(("Pset_Proibido", "Deletar", "algum valor"));
            var facets = new List<IdsFacet>
            {
                new IdsPropertyFacet
                {
                    PropertySet = new IdsValue { SimpleValue = "Pset_Proibido" },
                    BaseName    = new IdsValue { SimpleValue = "Deletar" },
                    Cardinality = "prohibited",
                },
            };

            var (pass, reason) = IdsService.CheckRequirements(cache, null, facets);
            Assert.False(pass);
            Assert.Contains("Proibido", reason);
        }

        [Fact]
        public void CheckRequirements_ProhibitedPropertyAbsent_Passes()
        {
            var cache = Cache(("OutroSet", "OutraProp", "val"));
            var facets = new List<IdsFacet>
            {
                new IdsPropertyFacet
                {
                    PropertySet = new IdsValue { SimpleValue = "Pset_Proibido" },
                    BaseName    = new IdsValue { SimpleValue = "Deletar" },
                    Cardinality = "prohibited",
                },
            };

            var (pass, _) = IdsService.CheckRequirements(cache, null, facets);
            Assert.True(pass);
        }
    }
}
