using System;
using System.IO;
using System.Linq;
using Xunit;
using PHDNavisTools.Core;

namespace PHDNavisTools.Tests
{
    public class IdsParserTests
    {
        private static string Data(string name)
            => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData", name);

        // ── arquivo mínimo ────────────────────────────────────────────────────

        [Fact]
        public void ParseFile_Minimal_ReturnsDocumentWithOneSpec()
        {
            var doc = IdsParser.ParseFile(Data("sample_minimal.ids"));

            Assert.Equal("Minimal IDS", doc.Info.Title);
            Assert.Equal("PHD",         doc.Info.Author);
            Assert.Single(doc.Specifications);

            var spec = doc.Specifications[0];
            Assert.Equal("Test Spec",  spec.Name);
            Assert.Equal("SPEC-001",   spec.Identifier);
            Assert.Equal("IFC4",       spec.IfcVersion);
        }

        [Fact]
        public void ParseFile_Minimal_ApplicabilityHasEntityFacet()
        {
            var spec = IdsParser.ParseFile(Data("sample_minimal.ids")).Specifications[0];

            var entity = Assert.IsType<IdsEntityFacet>(spec.Applicability[0]);
            Assert.Equal("IfcColumn", entity.Name.SimpleValue);
            Assert.Null(entity.PredefinedType);
        }

        [Fact]
        public void ParseFile_Minimal_RequirementHasPropertyFacet()
        {
            var spec = IdsParser.ParseFile(Data("sample_minimal.ids")).Specifications[0];

            var prop = Assert.IsType<IdsPropertyFacet>(spec.Requirements[0]);
            Assert.Equal("Pset_ColumnCommon", prop.PropertySet.SimpleValue);
            Assert.Equal("LoadBearing",       prop.BaseName.SimpleValue);
            Assert.Null(prop.Value);
        }

        // ── namespace ids: ────────────────────────────────────────────────────

        [Fact]
        public void ParseFile_NsPrefix_ParsedIdenticallyToNoPrefix()
        {
            var noPrefix   = IdsParser.ParseFile(Data("sample_minimal.ids"));
            var withPrefix = IdsParser.ParseFile(Data("sample_ns_prefix.ids"));

            Assert.Equal(noPrefix.Info.Title,                    withPrefix.Info.Title);
            Assert.Equal(noPrefix.Specifications.Count,          withPrefix.Specifications.Count);
            Assert.Equal(noPrefix.Specifications[0].Name,        withPrefix.Specifications[0].Name);
            Assert.Equal(noPrefix.Specifications[0].Identifier,  withPrefix.Specifications[0].Identifier);

            var e1 = (IdsEntityFacet)noPrefix.Specifications[0].Applicability[0];
            var e2 = (IdsEntityFacet)withPrefix.Specifications[0].Applicability[0];
            Assert.Equal(e1.Name.SimpleValue, e2.Name.SimpleValue);
        }

        // ── arquivo completo ──────────────────────────────────────────────────

        [Fact]
        public void ParseFile_Full_TwoSpecifications()
        {
            var doc = IdsParser.ParseFile(Data("sample_full.ids"));
            Assert.Equal(2, doc.Specifications.Count);
        }

        [Fact]
        public void ParseFile_Full_FirstSpec_EntityFacetInApplicability()
        {
            var spec = IdsParser.ParseFile(Data("sample_full.ids")).Specifications[0];
            var entity = Assert.IsType<IdsEntityFacet>(spec.Applicability[0]);
            Assert.Equal("IfcColumn", entity.Name.SimpleValue);
        }

        [Fact]
        public void ParseFile_Full_PropertyWithEnumerationRestriction()
        {
            var spec = IdsParser.ParseFile(Data("sample_full.ids")).Specifications[0];

            var prop = spec.Requirements
                .OfType<IdsPropertyFacet>()
                .First(p => p.Value?.Restriction?.Enumeration.Count > 0);

            Assert.Equal("Pset_ColumnCommon", prop.PropertySet.SimpleValue);
            Assert.Equal("LoadBearing",       prop.BaseName.SimpleValue);
            Assert.Contains("YES",  prop.Value!.Restriction!.Enumeration);
            Assert.Contains("TRUE", prop.Value!.Restriction!.Enumeration);
        }

        [Fact]
        public void ParseFile_Full_AttributeFacetParsed()
        {
            var spec = IdsParser.ParseFile(Data("sample_full.ids")).Specifications[0];

            var attr = spec.Requirements.OfType<IdsAttributeFacet>().First();
            Assert.Equal("Name", attr.Name.SimpleValue);
            Assert.Equal("required", attr.Cardinality);
        }

        [Fact]
        public void ParseFile_Full_ClassificationFacetIsOptional()
        {
            var spec = IdsParser.ParseFile(Data("sample_full.ids")).Specifications[0];

            var cls = spec.Requirements.OfType<IdsClassificationFacet>().First();
            Assert.Equal("optional", cls.Cardinality);
            Assert.Equal("Uniclass", cls.System!.SimpleValue);
        }

        [Fact]
        public void ParseFile_Full_ProhibitedPropertyFacet()
        {
            var spec = IdsParser.ParseFile(Data("sample_full.ids")).Specifications[0];

            var prohibited = spec.Requirements
                .OfType<IdsPropertyFacet>()
                .First(p => p.Cardinality == "prohibited");

            Assert.Equal("Pset_Proibido", prohibited.PropertySet.SimpleValue);
        }

        [Fact]
        public void ParseFile_Full_SecondSpecUsesPatternRestriction()
        {
            var spec = IdsParser.ParseFile(Data("sample_full.ids")).Specifications[1];
            var entity = Assert.IsType<IdsEntityFacet>(spec.Applicability[0]);

            Assert.Null(entity.Name.SimpleValue);
            Assert.NotNull(entity.Name.Restriction);
            Assert.Equal("^IfcBeam.*", entity.Name.Restriction!.Pattern);
        }

        [Fact]
        public void ParseFile_Full_InfoFieldsParsed()
        {
            var info = IdsParser.ParseFile(Data("sample_full.ids")).Info;

            Assert.Equal("Full IDS",                    info.Title);
            Assert.Equal("Cobre todos os tipos de facet", info.Description);
            Assert.Equal("2026-06-25",                  info.Date);
            Assert.Equal("LOD 350",                     info.Milestone);
        }

        // ── erros ─────────────────────────────────────────────────────────────

        [Fact]
        public void ParseFile_InvalidXml_ThrowsInvalidDataException()
        {
            Assert.Throws<InvalidDataException>(
                () => IdsParser.ParseFile(Data("sample_invalid.xml")));
        }

        [Fact]
        public void ParseFile_NonExistentFile_ThrowsInvalidDataException()
        {
            Assert.Throws<InvalidDataException>(
                () => IdsParser.ParseFile(Data("does_not_exist.ids")));
        }
    }
}
