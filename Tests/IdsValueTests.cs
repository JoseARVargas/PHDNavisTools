using System.Collections.Generic;
using Xunit;
using PHDNavisTools.Core;

namespace PHDNavisTools.Tests
{
    public class IdsValueTests
    {
        // ── IdsValue.Any ─────────────────────────────────────────────────────

        [Fact]
        public void Any_MatchesAnyNonNullString()
        {
            Assert.True(IdsValue.Any.Matches("IfcColumn"));
            Assert.True(IdsValue.Any.Matches(""));
            Assert.True(IdsValue.Any.Matches("qualquer coisa 123"));
        }

        [Fact]
        public void Any_ReturnsFalseForNull()
        {
            Assert.False(IdsValue.Any.Matches(null));
        }

        [Fact]
        public void Any_IsNotConstrained()
        {
            Assert.False(IdsValue.Any.IsConstrained);
        }

        // ── SimpleValue ───────────────────────────────────────────────────────

        [Theory]
        [InlineData("IfcColumn", "IfcColumn")]
        [InlineData("IfcColumn", "IFCCOLUMN")]
        [InlineData("IfcColumn", "ifccolumn")]
        [InlineData("IfcColumn", "ifCCOLUMN")]
        public void SimpleValue_CaseInsensitiveMatch(string simple, string actual)
        {
            var v = new IdsValue { SimpleValue = simple };
            Assert.True(v.Matches(actual));
        }

        [Fact]
        public void SimpleValue_NoMatchForDifferentValue()
        {
            var v = new IdsValue { SimpleValue = "IfcColumn" };
            Assert.False(v.Matches("IfcBeam"));
        }

        [Fact]
        public void SimpleValue_NoMatchForNull()
        {
            var v = new IdsValue { SimpleValue = "IfcColumn" };
            Assert.False(v.Matches(null));
        }

        [Fact]
        public void SimpleValue_IsConstrained()
        {
            var v = new IdsValue { SimpleValue = "X" };
            Assert.True(v.IsConstrained);
        }

        // ── Restriction — enumeration ─────────────────────────────────────────

        [Theory]
        [InlineData("IfcColumn")]
        [InlineData("IFCBEAM")]
        [InlineData("ifcslab")]
        public void Restriction_Enumeration_CaseInsensitiveMatch(string actual)
        {
            var v = new IdsValue
            {
                Restriction = new IdsRestriction
                {
                    Enumeration = { "IfcColumn", "IfcBeam", "IfcSlab" }
                }
            };
            Assert.True(v.Matches(actual));
        }

        [Fact]
        public void Restriction_Enumeration_NoMatchOutsideList()
        {
            var v = new IdsValue
            {
                Restriction = new IdsRestriction
                {
                    Enumeration = { "IfcColumn", "IfcBeam" }
                }
            };
            Assert.False(v.Matches("IfcWall"));
            Assert.False(v.Matches(""));
        }

        [Fact]
        public void Restriction_Enumeration_IsConstrained()
        {
            var v = new IdsValue
            {
                Restriction = new IdsRestriction { Enumeration = { "A" } }
            };
            Assert.True(v.IsConstrained);
        }

        // ── Restriction — pattern ─────────────────────────────────────────────

        [Theory]
        [InlineData("IfcColumn")]
        [InlineData("IfcBeam")]
        [InlineData("IfcSlab")]
        public void Restriction_Pattern_MatchesIfcTypes(string actual)
        {
            var v = new IdsValue
            {
                Restriction = new IdsRestriction { Pattern = "^Ifc[A-Z].*" }
            };
            Assert.True(v.Matches(actual));
        }

        [Fact]
        public void Restriction_Pattern_NoMatchForNonIfc()
        {
            var v = new IdsValue
            {
                Restriction = new IdsRestriction { Pattern = "^Ifc[A-Z].*" }
            };
            Assert.False(v.Matches("NotIfc"));
            Assert.False(v.Matches("ifc_lower"));
        }

        [Fact]
        public void Restriction_InvalidPattern_ReturnsFalse()
        {
            var v = new IdsValue
            {
                Restriction = new IdsRestriction { Pattern = "[invalid regex" }
            };
            Assert.False(v.Matches("anything"));
        }

        // ── IdsRestriction.ToString ───────────────────────────────────────────

        [Fact]
        public void ToString_Enumeration_JoinsWithPipe()
        {
            var r = new IdsRestriction { Enumeration = { "A", "B", "C" } };
            Assert.Equal("A | B | C", r.ToString());
        }

        [Fact]
        public void ToString_Pattern_WrapsInSlashes()
        {
            var r = new IdsRestriction { Pattern = "^Ifc.*" };
            Assert.Equal("/^Ifc.*/", r.ToString());
        }
    }
}
