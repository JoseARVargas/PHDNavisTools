using System.Collections.Generic;
using System.Linq;

namespace NavisworksIfcExporter.Core
{
    /// <summary>
    /// Dual-schema IFC class catalog (50+ classes). Ported from BIMCamel (MIT).
    /// Keyed by friendly name ("Wall", "Pipe segment"); each entry carries the IFC4 entity name,
    /// the IFC2x3 entity name (empty = fall back to IfcBuildingElementProxy in 2x3), and the
    /// IFC2x3 attribute count (8 or 9).
    /// </summary>
    public static class IfcTypeCatalog
    {
        public readonly struct IfcClass
        {
            public readonly string Ifc4;
            public readonly string Ifc2x3;
            public readonly int Args2x3;
            public IfcClass(string ifc4, string ifc2x3 = "", int args2x3 = 0)
            { Ifc4 = ifc4; Ifc2x3 = ifc2x3 ?? ""; Args2x3 = args2x3; }
        }

        public static readonly Dictionary<string, IfcClass> Catalog = new(System.StringComparer.OrdinalIgnoreCase)
        {
            // Architectural / structural (IFC2x3 → proxy)
            { "Wall",                     new IfcClass("IFCWALL") },
            { "Wall (standard case)",     new IfcClass("IFCWALLSTANDARDCASE") },
            { "Slab",                     new IfcClass("IFCSLAB") },
            { "Beam",                     new IfcClass("IFCBEAM") },
            { "Column",                   new IfcClass("IFCCOLUMN") },
            { "Member",                   new IfcClass("IFCMEMBER") },
            { "Plate",                    new IfcClass("IFCPLATE") },
            { "Footing",                  new IfcClass("IFCFOOTING") },
            { "Railing",                  new IfcClass("IFCRAILING") },
            { "Stair",                    new IfcClass("IFCSTAIR") },
            { "Ramp",                     new IfcClass("IFCRAMP") },
            { "Roof",                     new IfcClass("IFCROOF") },
            { "Covering",                 new IfcClass("IFCCOVERING") },
            { "Curtain wall",             new IfcClass("IFCCURTAINWALL") },
            { "Chimney",                  new IfcClass("IFCCHIMNEY") },
            { "Shading device",           new IfcClass("IFCSHADINGDEVICE") },
            { "Furniture",                new IfcClass("IFCFURNITURE") },
            { "Door",                     new IfcClass("IFCDOOR") },
            { "Window",                   new IfcClass("IFCWINDOW") },
            { "Stair flight",             new IfcClass("IFCSTAIRFLIGHT") },
            { "Ramp flight",              new IfcClass("IFCRAMPFLIGHT") },
            { "Building element proxy",   new IfcClass("IFCBUILDINGELEMENTPROXY") },
            // Piping
            { "Pipe segment",             new IfcClass("IFCPIPESEGMENT",   "IFCFLOWSEGMENT",    8) },
            { "Pipe fitting",             new IfcClass("IFCPIPEFITTING",   "IFCFLOWFITTING",    8) },
            { "Valve",                    new IfcClass("IFCVALVE",         "IFCFLOWCONTROLLER", 8) },
            { "Pump",                     new IfcClass("IFCPUMP",          "IFCFLOWMOVINGDEVICE", 8) },
            { "Tank",                     new IfcClass("IFCTANK",          "IFCFLOWSTORAGEDEVICE", 8) },
            { "Flow meter",               new IfcClass("IFCFLOWMETER",     "IFCDISTRIBUTIONCONTROLELEMENT", 9) },
            { "Flow instrument",          new IfcClass("IFCFLOWINSTRUMENT","IFCDISTRIBUTIONCONTROLELEMENT", 9) },
            { "Filter",                   new IfcClass("IFCFILTER",        "IFCFLOWTREATMENTDEVICE", 8) },
            { "Strainer",                 new IfcClass("IFCFILTER",        "IFCFLOWTREATMENTDEVICE", 8) },
            // HVAC / ducting
            { "Duct segment",             new IfcClass("IFCDUCTSEGMENT",   "IFCFLOWSEGMENT",    8) },
            { "Duct fitting",             new IfcClass("IFCDUCTFITTING",   "IFCFLOWFITTING",    8) },
            { "Duct silencer",            new IfcClass("IFCDUCTSILENCER",  "IFCFLOWTREATMENTDEVICE", 8) },
            { "Air terminal",             new IfcClass("IFCAIRTERMINAL",   "IFCFLOWTERMINAL",   8) },
            { "Fan",                      new IfcClass("IFCFAN",           "IFCFLOWMOVINGDEVICE", 8) },
            { "Coil",                     new IfcClass("IFCCOIL",          "IFCENERGYCONVERSIONDEVICE", 8) },
            { "Boiler",                   new IfcClass("IFCBOILER",        "IFCENERGYCONVERSIONDEVICE", 8) },
            { "Chiller",                  new IfcClass("IFCCHILLER",       "IFCENERGYCONVERSIONDEVICE", 8) },
            { "Compressor",               new IfcClass("IFCCOMPRESSOR",    "IFCFLOWMOVINGDEVICE", 8) },
            { "Heat exchanger",           new IfcClass("IFCHEATEXCHANGER", "IFCENERGYCONVERSIONDEVICE", 8) },
            { "Space heater",             new IfcClass("IFCSPACEHEATER",   "IFCFLOWTERMINAL",   8) },
            { "Unitary equipment",        new IfcClass("IFCUNITARYEQUIPMENT","IFCENERGYCONVERSIONDEVICE", 8) },
            // Plumbing
            { "Sanitary terminal",        new IfcClass("IFCSANITARYTERMINAL","IFCFLOWTERMINAL", 8) },
            // Electrical
            { "Cable carrier segment",    new IfcClass("IFCCABLECARRIERSEGMENT","IFCFLOWSEGMENT", 8) },
            { "Cable carrier fitting",    new IfcClass("IFCCABLECARRIERFITTING","IFCFLOWFITTING", 8) },
            { "Cable segment",            new IfcClass("IFCCABLESEGMENT",  "IFCFLOWSEGMENT",    8) },
            { "Cable fitting",            new IfcClass("IFCCABLEFITTING",  "IFCFLOWFITTING",    8) },
            { "Light fixture",            new IfcClass("IFCLIGHTFIXTURE",  "IFCFLOWTERMINAL",   8) },
            { "Outlet",                   new IfcClass("IFCOUTLET",        "IFCFLOWTERMINAL",   8) },
            { "Switching device",         new IfcClass("IFCSWITCHINGDEVICE","IFCFLOWCONTROLLER",8) },
            { "Protective device",        new IfcClass("IFCPROTECTIVEDEVICE","IFCFLOWCONTROLLER",8) },
            { "Transformer",              new IfcClass("IFCTRANSFORMER",   "IFCENERGYCONVERSIONDEVICE", 8) },
            { "Distribution board",       new IfcClass("IFCDISTRIBUTIONBOARD","IFCFLOWCONTROLLER",8) },
            { "Electric appliance",       new IfcClass("IFCELECTRICAPPLIANCE","IFCFLOWTERMINAL",8) },
            { "Electric generator",       new IfcClass("IFCELECTRICGENERATOR","IFCENERGYCONVERSIONDEVICE",8) },
            { "Electric motor",           new IfcClass("IFCELECTRICMOTOR", "IFCENERGYCONVERSIONDEVICE", 8) },
            // Controls / instrumentation
            { "Sensor",                   new IfcClass("IFCSENSOR",    "IFCDISTRIBUTIONCONTROLELEMENT", 9) },
            { "Actuator",                 new IfcClass("IFCACTUATOR",  "IFCDISTRIBUTIONCONTROLELEMENT", 9) },
            { "Controller",               new IfcClass("IFCCONTROLLER","IFCDISTRIBUTIONCONTROLELEMENT", 9) },
            // Accessories
            { "Discrete accessory",       new IfcClass("IFCDISCRETEACCESSORY") },
            { "Fastener",                 new IfcClass("IFCFASTENER") },
            // Furnishing / misc
            { "Furnishing element",       new IfcClass("IFCFURNISHINGELEMENT") },
        };

        // Maps "IFCWALL" (uppercase) → friendly name "Wall" for reverse-lookup from our IfcTypeMapper output.
        private static Dictionary<string, string>? _reverse;
        public static Dictionary<string, string> Reverse
        {
            get
            {
                if (_reverse != null) return _reverse;
                var d = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
                foreach (var kv in Catalog)
                    if (!d.ContainsKey(kv.Value.Ifc4))
                        d[kv.Value.Ifc4] = kv.Key;
                return _reverse = d;
            }
        }

        /// <summary>All friendly names sorted, proxy first — for UI dropdowns.</summary>
        public static List<string> Keys()
        {
            var keys = Catalog.Keys
                .Where(k => !k.Equals("Building element proxy", System.StringComparison.OrdinalIgnoreCase))
                .OrderBy(k => k, System.StringComparer.OrdinalIgnoreCase)
                .ToList();
            keys.Insert(0, "Building element proxy");
            return keys;
        }
    }
}
