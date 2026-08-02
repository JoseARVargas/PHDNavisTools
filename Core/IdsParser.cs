using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace PHDNavisTools.Core
{
    internal static class IdsParser
    {
        public static IdsDocument ParseFile(string path)
        {
            XDocument xml;
            try { xml = XDocument.Load(path); }
            catch (Exception ex)
            {
                throw new InvalidDataException($"Erro ao ler o arquivo IDS: {ex.Message}", ex);
            }

            var root = xml.Root
                ?? throw new InvalidDataException("Arquivo IDS inválido: elemento raiz não encontrado.");

            var doc = new IdsDocument();

            var info = Child(root, "info");
            if (info != null)
            {
                doc.Info.Title       = ChildText(info, "title");
                doc.Info.Description = ChildText(info, "description");
                doc.Info.Author      = ChildText(info, "author");
                doc.Info.Date        = ChildText(info, "date");
                doc.Info.Milestone   = ChildText(info, "milestone");
            }

            var specs = Child(root, "specifications");
            if (specs != null)
                foreach (var s in specs.Elements().Where(e => e.Name.LocalName == "specification"))
                    doc.Specifications.Add(ParseSpec(s));

            return doc;
        }

        // ── specification ────────────────────────────────────────────────────

        private static IdsSpecification ParseSpec(XElement el)
        {
            var spec = new IdsSpecification
            {
                Name         = Attr(el, "name"),
                Identifier   = Attr(el, "identifier"),
                Description  = Attr(el, "description"),
                Instructions = Attr(el, "instructions"),
                IfcVersion   = Attr(el, "ifcVersion"),
            };

            var app = Child(el, "applicability");
            if (app != null)
                foreach (var f in app.Elements())
                    TryAdd(spec.Applicability, f, "required");

            var req = Child(el, "requirements");
            if (req != null)
                foreach (var f in req.Elements())
                    TryAdd(spec.Requirements, f, Attr(f, "cardinality", "required"));

            return spec;
        }

        // ── facets ───────────────────────────────────────────────────────────

        private static void TryAdd(List<IdsFacet> list, XElement el, string cardinality)
        {
            IdsFacet? facet = el.Name.LocalName switch
            {
                "entity"         => ParseEntity(el),
                "property"       => ParseProperty(el),
                "attribute"      => ParseAttribute(el),
                "classification" => ParseClassification(el),
                _                => null,
            };
            if (facet == null) return;
            facet.Cardinality = cardinality;
            list.Add(facet);
        }

        private static IdsEntityFacet ParseEntity(XElement el) => new IdsEntityFacet
        {
            Name           = ParseValue(Child(el, "name")),
            PredefinedType = Child(el, "predefinedType") is XElement pt ? ParseValue(pt) : null,
        };

        private static IdsPropertyFacet ParseProperty(XElement el) => new IdsPropertyFacet
        {
            DataType    = Attr(el, "datatype"),
            PropertySet = ParseValue(Child(el, "propertySet")),
            BaseName    = ParseValue(Child(el, "baseName")),
            Value       = Child(el, "value") is XElement v ? ParseValue(v) : null,
        };

        private static IdsAttributeFacet ParseAttribute(XElement el) => new IdsAttributeFacet
        {
            Name  = ParseValue(Child(el, "name")),
            Value = Child(el, "value") is XElement v ? ParseValue(v) : null,
        };

        private static IdsClassificationFacet ParseClassification(XElement el) => new IdsClassificationFacet
        {
            Value  = Child(el, "value")  is XElement v ? ParseValue(v)  : null,
            System = Child(el, "system") is XElement s ? ParseValue(s) : null,
        };

        // ── IdsValue ─────────────────────────────────────────────────────────

        private static IdsValue ParseValue(XElement? container)
        {
            if (container == null) return IdsValue.Any;

            var simple = Child(container, "simpleValue");
            if (simple != null)
                return new IdsValue { SimpleValue = simple.Value?.Trim() };

            // xs:restriction — namespace-agnostic search
            var restriction = container.Elements()
                .FirstOrDefault(e => e.Name.LocalName == "restriction");
            if (restriction != null)
                return new IdsValue { Restriction = ParseRestriction(restriction) };

            return IdsValue.Any;
        }

        private static IdsRestriction ParseRestriction(XElement el)
        {
            var r = new IdsRestriction { Base = Attr(el, "base") };
            foreach (var child in el.Elements())
            {
                switch (child.Name.LocalName)
                {
                    case "enumeration":  r.Enumeration.Add(Attr(child, "value")); break;
                    case "pattern":      r.Pattern      = Attr(child, "value");   break;
                    case "minInclusive": r.MinInclusive = Attr(child, "value");   break;
                    case "maxInclusive": r.MaxInclusive = Attr(child, "value");   break;
                }
            }
            return r;
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static XElement? Child(XElement el, string localName)
            => el.Elements().FirstOrDefault(e => e.Name.LocalName == localName);

        private static string ChildText(XElement el, string localName)
            => Child(el, localName)?.Value?.Trim() ?? "";

        private static string Attr(XElement el, string name, string fallback = "")
            => el.Attribute(name)?.Value?.Trim() ?? fallback;
    }
}
