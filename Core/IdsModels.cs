using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PHDNavisTools.Core
{
    // ── documento IDS ────────────────────────────────────────────────────────

    public class IdsDocument
    {
        public IdsInfo                Info           { get; set; } = new IdsInfo();
        public List<IdsSpecification> Specifications { get; set; } = new List<IdsSpecification>();
    }

    public class IdsInfo
    {
        public string Title       { get; set; } = "";
        public string Description { get; set; } = "";
        public string Author      { get; set; } = "";
        public string Date        { get; set; } = "";
        public string Milestone   { get; set; } = "";
    }

    // ── specification ────────────────────────────────────────────────────────

    public class IdsSpecification
    {
        public string         Name         { get; set; } = "";
        public string         Identifier   { get; set; } = "";
        public string         Description  { get; set; } = "";
        public string         Instructions { get; set; } = "";
        public string         IfcVersion   { get; set; } = "";
        public List<IdsFacet> Applicability { get; set; } = new List<IdsFacet>();
        public List<IdsFacet> Requirements  { get; set; } = new List<IdsFacet>();
    }

    // ── facets ───────────────────────────────────────────────────────────────

    public abstract class IdsFacet
    {
        /// <summary>"required" | "optional" | "prohibited" (contexto de requirements)</summary>
        public string Cardinality { get; set; } = "required";
    }

    public class IdsEntityFacet : IdsFacet
    {
        public IdsValue  Name           { get; set; } = IdsValue.Any;
        public IdsValue? PredefinedType { get; set; }
    }

    public class IdsPropertyFacet : IdsFacet
    {
        public IdsValue  PropertySet { get; set; } = IdsValue.Any;
        public IdsValue  BaseName    { get; set; } = IdsValue.Any;
        public IdsValue? Value       { get; set; }
        public string?   DataType    { get; set; }
    }

    public class IdsAttributeFacet : IdsFacet
    {
        public IdsValue  Name  { get; set; } = IdsValue.Any;
        public IdsValue? Value { get; set; }
    }

    public class IdsClassificationFacet : IdsFacet
    {
        public IdsValue? Value  { get; set; }
        public IdsValue? System { get; set; }
    }

    // ── IdsValue / restrições ────────────────────────────────────────────────

    public class IdsValue
    {
        public static readonly IdsValue Any = new IdsValue();

        public string?         SimpleValue { get; set; }
        public IdsRestriction? Restriction { get; set; }

        public bool IsConstrained => SimpleValue != null || Restriction != null;

        public bool Matches(string? actual)
        {
            if (actual == null) return false;
            if (SimpleValue != null)
                return string.Equals(SimpleValue, actual, StringComparison.OrdinalIgnoreCase);
            if (Restriction != null)
                return Restriction.Matches(actual);
            return true; // sem restrição → aceita qualquer valor
        }

        public override string ToString()
            => SimpleValue ?? Restriction?.ToString() ?? "*";
    }

    public class IdsRestriction
    {
        public string?      Base         { get; set; }
        public List<string> Enumeration  { get; set; } = new List<string>();
        public string?      Pattern      { get; set; }
        public string?      MinInclusive { get; set; }
        public string?      MaxInclusive { get; set; }

        public bool Matches(string actual)
        {
            if (Enumeration.Count > 0)
                return Enumeration.Any(e => string.Equals(e, actual, StringComparison.OrdinalIgnoreCase));
            if (Pattern != null)
            {
                try   { return Regex.IsMatch(actual, Pattern, RegexOptions.IgnoreCase); }
                catch { return false; }
            }
            return true;
        }

        public override string ToString()
        {
            if (Enumeration.Count > 0) return string.Join(" | ", Enumeration);
            if (Pattern      != null)  return $"/{Pattern}/";
            return "*";
        }
    }

    // ── resultados ───────────────────────────────────────────────────────────

    public enum IdsStatus { Pass, Fail, NotApplicable }

    public class IdsCheckResult
    {
        public string    SpecName      { get; set; } = "";
        public string    SpecId        { get; set; } = "";
        public IdsStatus Status        { get; set; }
        public string    StatusIcon    => Status == IdsStatus.Pass ? "✓ PASS"
                                       : Status == IdsStatus.Fail ? "✗ FAIL"
                                       : "— N/A";
        public string    Identifier    { get; set; } = "";
        public string    SourceFile    { get; set; } = "";
        public string    FailureReason { get; set; } = "";
    }
}
