using System.Collections.Generic;

namespace PHDNavisTools.Models
{
    public class ElementData
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string IfcType { get; set; } = "IfcBuildingElementProxy";

        /// <summary>PropertySet name → (PropertyName → Value)</summary>
        public Dictionary<string, Dictionary<string, string>> PropertySets { get; set; } = new();

        public GeometryData? Geometry { get; set; }
    }

    public class GeometryData
    {
        public List<double[]> Vertices { get; set; } = new();  // each item: [x, y, z]
        public List<int[]>   Triangles { get; set; } = new();  // each item: [i0, i1, i2] (0-based)
    }
}
