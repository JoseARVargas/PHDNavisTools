using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NavisworksIfcExporter.Models;

namespace NavisworksIfcExporter.Core
{
    internal static class FbxWriter
    {
        // Writes a single-mesh FBX 7.4 ASCII file from pre-combined geometry.
        // Coordinate system matches Navisworks: Z-up, right-handed, units in metres.
        public static void WriteAscii(string path, string meshName,
            List<double[]> vertices, List<int[]> triangles)
        {
            if (vertices == null || vertices.Count == 0)
                throw new ArgumentException("Nenhum vértice para exportar.", nameof(vertices));
            if (triangles == null || triangles.Count == 0)
                throw new ArgumentException("Nenhum triângulo para exportar.", nameof(triangles));

            long geoId   = NewId();
            long modelId = NewId();
            string name  = SanitizeName(meshName);

            using var w = new StreamWriter(path, false, new UTF8Encoding(false), 65536);
            WriteHeader(w);
            WriteGlobalSettings(w);
            WriteDefinitions(w);
            WriteObjects(w, geoId, modelId, name, vertices, triangles);
            WriteConnections(w, geoId, modelId);
        }

        // -----------------------------------------------------------------------

        private static void WriteHeader(TextWriter w)
        {
            w.WriteLine("; FBX 7.4.0 project file");
            w.WriteLine("; Gerado por PHD NavisPlugin");
            w.WriteLine();
            w.WriteLine("FBXHeaderExtension:  {");
            w.WriteLine("\tFBXHeaderVersion: 1003");
            w.WriteLine("\tFBXVersion: 7400");
            w.WriteLine("\tCreator: \"PHD NavisPlugin\"");
            w.WriteLine("}");
            w.WriteLine();
        }

        private static void WriteGlobalSettings(TextWriter w)
        {
            // Z-up right-handed coordinate system (Navisworks default)
            w.WriteLine("GlobalSettings:  {");
            w.WriteLine("\tVersion: 1000");
            w.WriteLine("\tProperties70:  {");
            w.WriteLine("\t\tP: \"UpAxis\", \"int\", \"Integer\", \"\",2");
            w.WriteLine("\t\tP: \"UpAxisSign\", \"int\", \"Integer\", \"\",1");
            w.WriteLine("\t\tP: \"FrontAxis\", \"int\", \"Integer\", \"\",1");
            w.WriteLine("\t\tP: \"FrontAxisSign\", \"int\", \"Integer\", \"\",1");
            w.WriteLine("\t\tP: \"CoordAxis\", \"int\", \"Integer\", \"\",0");
            w.WriteLine("\t\tP: \"CoordAxisSign\", \"int\", \"Integer\", \"\",1");
            w.WriteLine("\t\tP: \"UnitScaleFactor\", \"double\", \"Number\", \"\",1");
            w.WriteLine("\t}");
            w.WriteLine("}");
            w.WriteLine();
        }

        private static void WriteDefinitions(TextWriter w)
        {
            w.WriteLine("Definitions:  {");
            w.WriteLine("\tVersion: 100");
            w.WriteLine("\tCount: 2");
            w.WriteLine("\tObjectType: \"Geometry\" {");
            w.WriteLine("\t\tCount: 1");
            w.WriteLine("\t}");
            w.WriteLine("\tObjectType: \"Model\" {");
            w.WriteLine("\t\tCount: 1");
            w.WriteLine("\t}");
            w.WriteLine("}");
            w.WriteLine();
        }

        private static void WriteObjects(TextWriter w, long geoId, long modelId, string name,
            List<double[]> vertices, List<int[]> triangles)
        {
            w.WriteLine("Objects:  {");

            // Geometry node
            w.WriteLine($"\tGeometry: {geoId}, \"Geometry::{name}\", \"Mesh\" {{");

            // Vertex array — flat [x,y,z, x,y,z, ...]
            w.WriteLine($"\t\tVertices: *{vertices.Count * 3} {{");
            w.Write("\t\t\ta: ");
            for (int i = 0; i < vertices.Count; i++)
            {
                if (i > 0) w.Write(',');
                var v = vertices[i];
                w.Write(v[0].ToString("G9", CultureInfo.InvariantCulture));
                w.Write(',');
                w.Write(v[1].ToString("G9", CultureInfo.InvariantCulture));
                w.Write(',');
                w.Write(v[2].ToString("G9", CultureInfo.InvariantCulture));
            }
            w.WriteLine();
            w.WriteLine("\t\t}");

            // Polygon vertex index — FBX convention: last index of each polygon is ~i (bitwise NOT)
            w.WriteLine($"\t\tPolygonVertexIndex: *{triangles.Count * 3} {{");
            w.Write("\t\t\ta: ");
            for (int i = 0; i < triangles.Count; i++)
            {
                if (i > 0) w.Write(',');
                var t = triangles[i];
                w.Write(t[0]);
                w.Write(',');
                w.Write(t[1]);
                w.Write(',');
                w.Write(~t[2]);  // ~x == -(x+1): the FBX end-of-polygon marker
            }
            w.WriteLine();
            w.WriteLine("\t\t}");

            w.WriteLine("\t\tGeometryVersion: 124");
            w.WriteLine("\t}");

            // Model node (the scene node that references the geometry)
            w.WriteLine($"\tModel: {modelId}, \"Model::{name}\", \"Mesh\" {{");
            w.WriteLine("\t\tVersion: 232");
            w.WriteLine("\t\tProperties70:  {");
            w.WriteLine("\t\t\tP: \"DefaultAttributeIndex\", \"int\", \"Integer\", \"\",0");
            w.WriteLine("\t\t}");
            w.WriteLine("\t\tShading: T");
            w.WriteLine("\t\tCulling: \"CullingOff\"");
            w.WriteLine("\t}");

            w.WriteLine("}");
            w.WriteLine();
        }

        private static void WriteConnections(TextWriter w, long geoId, long modelId)
        {
            w.WriteLine("Connections:  {");
            w.WriteLine($"\tC: \"OO\",{modelId},0");          // model → root
            w.WriteLine($"\tC: \"OO\",{geoId},{modelId}");    // geometry → model
            w.WriteLine("}");
        }

        // -----------------------------------------------------------------------

        private static readonly Random _rng = new Random();

        private static long NewId()
        {
            var buf = new byte[8];
            _rng.NextBytes(buf);
            return (BitConverter.ToInt64(buf, 0) & 0x7FFF_FFFF_FFFF_FFFF) | 1L;
        }

        private static string SanitizeName(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
                sb.Append(c == '"' || c == ':' || c == '{' || c == '}' ? '_' : c);
            return sb.ToString();
        }
    }
}
