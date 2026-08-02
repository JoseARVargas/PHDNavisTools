// Geometry extraction adapted from BIMCamel IFC Exporter (MIT License)
// https://github.com/mrshoma99-rgb/bimcamel-ifc-exporter
using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.ComApi;
using Autodesk.Navisworks.Api.Interop.ComApi;
using PHDNavisTools.Models;

namespace PHDNavisTools.Core
{
    public class GeometryExtractor
    {
        // Set when tessellation fails — lets the caller log the reason once.
        public string LastComError { get; private set; } = string.Empty;

        public GeometryData? Extract(ModelItem item)
        {
            if (!item.HasGeometry) return null;

            var tessellated = TryComTessellation(item);
            if (tessellated != null) return tessellated;

            return ExtractBoundingBox(item);
        }

        // -----------------------------------------------------------------------
        // COM tessellation — BIMCamel approach:
        //   ModelItem → InwOpSelection → Paths → Fragments → GenerateSimplePrimitives
        // The local→world matrix from each fragment is applied so the resulting mesh
        // is in world-space coordinates.
        // -----------------------------------------------------------------------

        private GeometryData? TryComTessellation(ModelItem item)
        {
            var sink = new PrimitiveSink();
            int fragCount = 0;
            try
            {
                var coll   = new ModelItemCollection { item };
                var comSel = ComApiBridge.ToInwOpSelection(coll);

                foreach (InwOaPath3 path in comSel.Paths())
                {
                    foreach (InwOaFragment3 frag in path.Fragments())
                    {
                        fragCount++;
                        sink.CurrentTransform = ReadMatrix(frag);
                        frag.GenerateSimplePrimitives(nwEVertexProperty.eNORMAL, sink);
                    }
                }
            }
            catch (Exception ex)
            {
                LastComError = ex.GetType().Name + ": " + ex.Message;
                return null;
            }

            if (sink.TriangleCount == 0)
            {
                int managedFrags = item.Geometry?.FragmentCount ?? 0;
                LastComError = $"0 triângulos (frags COM={fragCount}, frags managed={managedFrags})";
                return null;
            }

            // Convert flat list (x,y,z triples) to our models
            var vertices = new List<double[]>(sink.Vertices.Count / 3);
            for (int i = 0; i < sink.Vertices.Count; i += 3)
                vertices.Add(new[] { sink.Vertices[i], sink.Vertices[i + 1], sink.Vertices[i + 2] });

            var triangles = new List<int[]>(sink.Indices.Count / 3);
            for (int i = 0; i < sink.Indices.Count; i += 3)
                triangles.Add(new[] { sink.Indices[i], sink.Indices[i + 1], sink.Indices[i + 2] });

            return new GeometryData { Vertices = vertices, Triangles = triangles };
        }

        // Reads the local→world 4×4 matrix from a fragment (column-major, 16 doubles).
        private static double[]? ReadMatrix(InwOaFragment3 frag)
        {
            try
            {
                var t   = (InwLTransform3f3)frag.GetLocalToWorldMatrix();
                var arr = (Array)t.Matrix;
                int lb  = arr.GetLowerBound(0);
                var m   = new double[16];
                for (int i = 0; i < 16; i++)
                    m[i] = Convert.ToDouble(arr.GetValue(lb + i));
                return m;
            }
            catch { return null; }
        }

        // -----------------------------------------------------------------------
        // Bounding-box fallback — always available, gives approximate geometry
        // -----------------------------------------------------------------------

        public static GeometryData? ExtractBoundingBox(ModelItem item)
        {
            var bb = item.Geometry?.BoundingBox;
            if (bb == null || bb.IsEmpty) return null;

            var mn = bb.Min;
            var mx = bb.Max;

            var verts = new List<double[]>
            {
                new[] { mn.X, mn.Y, mn.Z }, // 0
                new[] { mx.X, mn.Y, mn.Z }, // 1
                new[] { mx.X, mx.Y, mn.Z }, // 2
                new[] { mn.X, mx.Y, mn.Z }, // 3
                new[] { mn.X, mn.Y, mx.Z }, // 4
                new[] { mx.X, mn.Y, mx.Z }, // 5
                new[] { mx.X, mx.Y, mx.Z }, // 6
                new[] { mn.X, mx.Y, mx.Z }, // 7
            };

            var tris = new List<int[]>
            {
                new[] { 0, 2, 1 }, new[] { 0, 3, 2 }, // bottom
                new[] { 4, 5, 6 }, new[] { 4, 6, 7 }, // top
                new[] { 0, 1, 5 }, new[] { 0, 5, 4 }, // front
                new[] { 3, 6, 2 }, new[] { 3, 7, 6 }, // back
                new[] { 0, 4, 7 }, new[] { 0, 7, 3 }, // left
                new[] { 1, 2, 6 }, new[] { 1, 6, 5 }, // right
            };

            return new GeometryData { Vertices = verts, Triangles = tris };
        }
    }

    // -----------------------------------------------------------------------
    // COM callback — adapted from BIMCamel's PrimitiveSink (MIT License).
    // Applies the local→world matrix so collected vertices are in world-space.
    // -----------------------------------------------------------------------

    internal sealed class PrimitiveSink : InwSimplePrimitivesCB
    {
        public readonly List<double> Vertices = new List<double>();
        public readonly List<int>    Indices  = new List<int>();
        public int TriangleCount { get; private set; }

        // Column-major 4×4 local→world matrix for the fragment being walked; null = identity.
        public double[]? CurrentTransform { get; set; }

        // Reused scratch buffer — avoids boxing on the hot path (Array.Copy into float[3]).
        private readonly float[] _c3 = new float[3];
        private bool _coordIsFloat = true;

        public void Triangle(InwSimpleVertex v1, InwSimpleVertex v2, InwSimpleVertex v3)
        {
            Indices.Add(AddVertex(v1));
            Indices.Add(AddVertex(v2));
            Indices.Add(AddVertex(v3));
            TriangleCount++;
        }

        public void Line(InwSimpleVertex v1, InwSimpleVertex v2) { }
        public void Point(InwSimpleVertex v1) { }
        public void SnapPoint(InwSimpleVertex v1) { }

        private int AddVertex(InwSimpleVertex v)
        {
            var c  = (Array)v.coord;
            int lb = c.GetLowerBound(0);
            double lx, ly, lz;

            // Fast path: SAFEARRAYs in Navisworks are typically 1-based Single[*].
            // Array.Copy avoids per-element boxing. Falls back on first type mismatch.
            if (_coordIsFloat)
            {
                try
                {
                    Array.Copy(c, lb, _c3, 0, 3);
                    lx = _c3[0]; ly = _c3[1]; lz = _c3[2];
                }
                catch
                {
                    _coordIsFloat = false;
                    lx = Convert.ToDouble(c.GetValue(lb));
                    ly = Convert.ToDouble(c.GetValue(lb + 1));
                    lz = Convert.ToDouble(c.GetValue(lb + 2));
                }
            }
            else
            {
                lx = Convert.ToDouble(c.GetValue(lb));
                ly = Convert.ToDouble(c.GetValue(lb + 1));
                lz = Convert.ToDouble(c.GetValue(lb + 2));
            }

            // Apply local→world transform (column-major: world = M * [x y z 1]^T)
            double wx, wy, wz;
            var m = CurrentTransform;
            if (m == null)
            {
                wx = lx; wy = ly; wz = lz;
            }
            else
            {
                wx = m[0] * lx + m[4] * ly + m[8]  * lz + m[12];
                wy = m[1] * lx + m[5] * ly + m[9]  * lz + m[13];
                wz = m[2] * lx + m[6] * ly + m[10] * lz + m[14];
            }

            Vertices.Add(wx);
            Vertices.Add(wy);
            Vertices.Add(wz);
            return (Vertices.Count / 3) - 1;
        }
    }
}
