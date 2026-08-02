// Adapted from BIMCamel IFC Exporter (MIT License)
// https://github.com/mrshoma99-rgb/bimcamel-ifc-exporter
using System;
using System.Collections.Generic;
using System.Linq;
using PHDNavisTools.Models;

namespace PHDNavisTools.Core
{
    public static class PropertyMapper
    {
        /// <summary>
        /// Applies mapping rules to a collected property dict. Returns a new dict —
        /// the original is never modified. Passthrough if rules is null or empty.
        /// </summary>
        public static Dictionary<string, Dictionary<string, string>> Apply(
            Dictionary<string, Dictionary<string, string>> props,
            IList<MappingRule> rules)
        {
            if (rules == null || rules.Count == 0) return props;

            var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var psetKvp in props)
            {
                foreach (var propKvp in psetKvp.Value)
                {
                    var rule = FindRule(rules, psetKvp.Key, propKvp.Key);
                    if (rule?.Exclude == true) continue;

                    var targetPset = !string.IsNullOrEmpty(rule?.TargetPset)
                        ? rule!.TargetPset
                        : psetKvp.Key;
                    var targetProp = !string.IsNullOrEmpty(rule?.TargetProperty)
                        ? rule!.TargetProperty
                        : propKvp.Key;

                    if (!result.TryGetValue(targetPset, out var pset))
                        result[targetPset] = pset = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    pset[targetProp] = propKvp.Value;
                }
            }

            return result;
        }

        private static MappingRule? FindRule(IList<MappingRule> rules, string pset, string prop)
        {
            foreach (var r in rules)
            {
                var psetOk = string.IsNullOrEmpty(r.SourcePset)
                    || pset.Equals(r.SourcePset, StringComparison.OrdinalIgnoreCase);
                var propOk = string.IsNullOrEmpty(r.SourceProperty)
                    || prop.Equals(r.SourceProperty, StringComparison.OrdinalIgnoreCase);
                if (psetOk && propOk) return r;
            }
            return null;
        }
    }

    // Catalog of standard IFC Psets — ported from BIMCamel (MIT License).
    public static class PsetCatalog
    {
        public static readonly Dictionary<string, string[]> Common =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "Pset_WallCommon",        new[] { "Reference", "Status", "IsExternal", "LoadBearing", "FireRating", "AcousticRating", "ThermalTransmittance" } },
            { "Pset_SlabCommon",        new[] { "Reference", "Status", "IsExternal", "LoadBearing", "FireRating", "AcousticRating", "ThermalTransmittance", "PitchAngle" } },
            { "Pset_BeamCommon",        new[] { "Reference", "Status", "IsExternal", "LoadBearing", "FireRating", "Span", "Slope", "Roll" } },
            { "Pset_ColumnCommon",      new[] { "Reference", "Status", "IsExternal", "LoadBearing", "FireRating", "Slope", "Roll" } },
            { "Pset_MemberCommon",      new[] { "Reference", "Status", "IsExternal", "LoadBearing", "FireRating", "Span", "Slope", "Roll" } },
            { "Pset_PlateCommon",       new[] { "Reference", "Status", "IsExternal", "LoadBearing", "FireRating", "AcousticRating", "ThermalTransmittance" } },
            { "Pset_CoveringCommon",    new[] { "Reference", "Status", "IsExternal", "FireRating", "AcousticRating", "ThermalTransmittance", "Finish", "TotalThickness" } },
            { "Pset_RoofCommon",        new[] { "Reference", "Status", "IsExternal", "FireRating", "AcousticRating", "ThermalTransmittance", "ProjectedArea", "TotalArea" } },
            { "Pset_DoorCommon",        new[] { "Reference", "Status", "FireRating", "AcousticRating", "IsExternal", "ThermalTransmittance", "FireExit", "SelfClosing", "SmokeStop" } },
            { "Pset_WindowCommon",      new[] { "Reference", "Status", "FireRating", "AcousticRating", "IsExternal", "ThermalTransmittance", "GlazingAreaFraction" } },
            { "Pset_SpaceCommon",       new[] { "Reference", "IsExternal", "GrossPlannedArea", "NetPlannedArea", "PubliclyAccessible", "HandicapAccessible", "Category" } },
            { "Pset_BuildingElementProxyCommon", new[] { "Reference", "Status" } },
            { "Pset_PipeSegmentTypeCommon", new[] { "Reference", "Status", "NominalDiameter", "InnerDiameter", "OuterDiameter", "WallThickness" } },
            { "Pset_DuctSegmentTypeCommon", new[] { "Reference", "Status", "NominalDiameterOrWidth", "NominalHeight" } },
            { "Pset_ManufacturerTypeInformation", new[] { "ArticleNumber", "ModelReference", "ModelLabel", "Manufacturer", "ProductionYear" } },
            { "Pset_ManufacturerOccurrence",      new[] { "AcquisitionDate", "BarCode", "SerialNumber", "BatchReference" } },
        };

        public static List<string> PsetNames() =>
            Common.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();

        public static List<string> PropertiesFor(string psetName) =>
            Common.TryGetValue(psetName, out var props) ? props.ToList() : new List<string>();
    }
}
