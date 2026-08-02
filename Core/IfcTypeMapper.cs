using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;

namespace PHDNavisTools.Core
{
    public static class IfcTypeMapper
    {
        // Maps any Navisworks item to an IFC type using multiple property sources.
        // Works for Revit, AutoCAD, Civil 3D, DGN, IFC native imports, etc.
        public static string Map(Dictionary<string, Dictionary<string, string>> props, ModelItem item)
        {
            // Build a list of candidate strings to match against, in priority order.
            var candidates = new List<string>();

            foreach (var pset in props.Values)
            {
                TryAdd(pset, "Category",     candidates);
                TryAdd(pset, "Type",         candidates);
                TryAdd(pset, "Layer",        candidates);
                TryAdd(pset, "Element Class", candidates);
                TryAdd(pset, "IFC Class",    candidates);
            }

            // Navisworks class names (e.g. "Wall", "IfcWall", "LINE")
            if (!string.IsNullOrWhiteSpace(item.ClassDisplayName)) candidates.Add(item.ClassDisplayName);
            if (!string.IsNullOrWhiteSpace(item.ClassName))        candidates.Add(item.ClassName);
            if (!string.IsNullOrWhiteSpace(item.DisplayName))      candidates.Add(item.DisplayName);

            foreach (var c in candidates)
            {
                var mapped = MatchKeyword(c);
                if (mapped != null) return mapped;
            }

            return "IfcBuildingElementProxy";
        }

        private static void TryAdd(Dictionary<string, string> pset, string key, List<string> list)
        {
            if (pset.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v))
                list.Add(v);
        }

        // Keyword matching — intentionally broad to cover Revit (English), AutoCAD layers,
        // Civil 3D categories, DGN levels, and IFC class names.
        private static string? MatchKeyword(string s)
        {
            var c = s.ToLowerInvariant();

            if (c.Contains("ifcwall")    || c.Contains("wall"))        return "IfcWall";
            if (c.Contains("ifcslab")    || c.Contains("floor")
                                         || c.Contains("slab")
                                         || c.Contains("laje"))        return "IfcSlab";
            if (c.Contains("ifcroof")    || c.Contains("roof")
                                         || c.Contains("cobertura"))   return "IfcRoof";
            if (c.Contains("ifcdoor")    || c.Contains("door")
                                         || c.Contains("porta"))       return "IfcDoor";
            if (c.Contains("ifcwindow")  || c.Contains("window")
                                         || c.Contains("janela"))      return "IfcWindow";
            if (c.Contains("ifcstair")   || c.Contains("stair")
                                         || c.Contains("escada"))      return "IfcStair";
            if (c.Contains("ifccolumn")  || c.Contains("column")
                                         || c.Contains("pilar"))       return "IfcColumn";
            if (c.Contains("ifcbeam")    || c.Contains("beam")
                                         || c.Contains("viga"))        return "IfcBeam";
            if (c.Contains("ifcramp")    || c.Contains("ramp")
                                         || c.Contains("rampa"))       return "IfcRamp";
            if (c.Contains("ifcpipe")    || c.Contains("pipe")
                                         || c.Contains("tubo"))        return "IfcPipeSegment";
            if (c.Contains("ifcduct")    || c.Contains("duct")
                                         || c.Contains("duto"))        return "IfcDuctSegment";
            if (c.Contains("equipment") || c.Contains("equipamento"))  return "IfcFlowTerminal";
            if (c.Contains("furniture") || c.Contains("movel")
                                         || c.Contains("móvel"))       return "IfcFurnishingElement";
            if (c.Contains("site")      || c.Contains("terrain")
                                         || c.Contains("terreno"))     return "IfcSite";
            if (c.Contains("road")      || c.Contains("estrada")
                                         || c.Contains("highway"))     return "IfcRoad";

            return null;
        }
    }
}
