using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using NavisworksIfcExporter.Models;
using static NavisworksIfcExporter.Core.StreamingStepWriter;

namespace NavisworksIfcExporter.Core
{
    /// <summary>
    /// IFC4 / IFC2x3 writer built on StreamingStepWriter (no xBIM dependency).
    /// Features: streaming output (no in-memory model), Pset content dedup via FNV-1a 64-bit hash,
    /// 50+ IFC class catalog via IfcTypeCatalog, configurable coordinate precision.
    /// Inspired by / ported from BIMCamel (MIT).
    /// </summary>
    public class IfcWriter
    {
        private readonly string _authorName;
        private readonly string _organizationName;

        public IfcWriter(string authorName = "Exportador", string organizationName = "PHD")
        {
            _authorName = authorName;
            _organizationName = organizationName;
        }

        public void Write(IEnumerable<ElementData> elements, string outputPath,
                          IfcSchema schema = IfcSchema.Ifc4, int coordDecimals = 4)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
            using var w = new StreamingStepWriter(outputPath, coordDecimals);
            w.WriteHeader(schema, Path.GetFileName(outputPath), _authorName);

            var skel    = WriteSkeleton(w, schema);
            var psets   = new PsetDedup();
            var contained = new List<int>();
            int index   = 0;

            foreach (var el in elements)
            {
                index++;
                int objId = WriteElement(w, schema, skel, el, psets, index);
                contained.Add(objId);
            }

            if (contained.Count > 0)
                w.Write($"IFCRELCONTAINEDINSPATIALSTRUCTURE({G()},{Ref(skel.Owner)},$,$,({Join(contained)}),{Ref(skel.Storey)})");

            WriteDeferredPsets(w, skel.Owner, psets);
            w.WriteFooter();
        }

        // ── spatial skeleton ─────────────────────────────────────────────────────

        private struct Skel { public int Ctx, Owner, Axis, Storey, StoreyPlace; }

        private Skel WriteSkeleton(StreamingStepWriter w, IfcSchema schema)
        {
            int len   = w.Write("IFCSIUNIT(*,.LENGTHUNIT.,$,.METRE.)");
            int area  = w.Write("IFCSIUNIT(*,.AREAUNIT.,$,.SQUARE_METRE.)");
            int vol   = w.Write("IFCSIUNIT(*,.VOLUMEUNIT.,$,.CUBIC_METRE.)");
            int ang   = w.Write("IFCSIUNIT(*,.PLANEANGLEUNIT.,$,.RADIAN.)");
            int units = w.Write($"IFCUNITASSIGNMENT(({Ref(len)},{Ref(area)},{Ref(vol)},{Ref(ang)}))");

            int origin = w.Write("IFCCARTESIANPOINT((0.,0.,0.))");
            int axis   = w.Write($"IFCAXIS2PLACEMENT3D({Ref(origin)},$,$)");
            int ctx    = w.Write($"IFCGEOMETRICREPRESENTATIONCONTEXT($,'Model',3,{R(1e-5)},{Ref(axis)},$)");

            int person = w.Write($"IFCPERSON($,$,{Str(_authorName)},$,$,$,$,$)");
            int org    = w.Write($"IFCORGANIZATION($,{Str(_organizationName)},$,$,$)");
            int pao    = w.Write($"IFCPERSONANDORGANIZATION({Ref(person)},{Ref(org)},$)");
            int app    = w.Write($"IFCAPPLICATION({Ref(org)},'1.0','NavisworksIfcExporter','NavisIFC')");
            long ts    = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            int owner  = w.Write($"IFCOWNERHISTORY({Ref(pao)},{Ref(app)},$,.ADDED.,$,$,$,{ts})");

            int proj   = w.Write($"IFCPROJECT({G()},{Ref(owner)},'Projeto Navisworks',$,$,$,$,({Ref(ctx)}),{Ref(units)})");
            int siteP  = w.Write("IFCCARTESIANPOINT((0.,0.,0.))");
            int siteA  = w.Write($"IFCAXIS2PLACEMENT3D({Ref(siteP)},$,$)");
            int sitePl = w.Write($"IFCLOCALPLACEMENT($,{Ref(siteA)})");
            int site   = w.Write($"IFCSITE({G()},{Ref(owner)},'Site',$,$,{Ref(sitePl)},$,$,.ELEMENT.,$,$,$,$,$)");
            int bldgPl = w.Write($"IFCLOCALPLACEMENT({Ref(sitePl)},{Ref(axis)})");
            int bldg   = w.Write($"IFCBUILDING({G()},{Ref(owner)},'Edificio',$,$,{Ref(bldgPl)},$,$,.ELEMENT.,$,$,$)");
            int storeyPl = w.Write($"IFCLOCALPLACEMENT({Ref(bldgPl)},{Ref(axis)})");
            int storey = w.Write($"IFCBUILDINGSTOREY({G()},{Ref(owner)},'Pavimento 1',$,$,{Ref(storeyPl)},$,$,.ELEMENT.,0.)");

            w.Write($"IFCRELAGGREGATES({G()},{Ref(owner)},$,$,{Ref(proj)},({Ref(site)}))");
            w.Write($"IFCRELAGGREGATES({G()},{Ref(owner)},$,$,{Ref(site)},({Ref(bldg)}))");
            w.Write($"IFCRELAGGREGATES({G()},{Ref(owner)},$,$,{Ref(bldg)},({Ref(storey)}))");

            return new Skel { Ctx = ctx, Owner = owner, Axis = axis, Storey = storey, StoreyPlace = storeyPl };
        }

        // ── element ──────────────────────────────────────────────────────────────

        private int WriteElement(StreamingStepWriter w, IfcSchema schema, Skel skel,
                                  ElementData el, PsetDedup psets, int index)
        {
            bool hasGeom = el.Geometry != null
                && el.Geometry.Vertices.Count > 0
                && el.Geometry.Triangles.Count > 0;

            string shapeRef = "$";
            if (hasGeom)
            {
                int item = schema == IfcSchema.Ifc4
                    ? WriteIfc4Mesh(w, el.Geometry!)
                    : WriteIfc2x3Mesh(w, el.Geometry!);

                string repType = schema == IfcSchema.Ifc4 ? "Tessellation" : "SurfaceModel";
                int rep = w.Write($"IFCSHAPEREPRESENTATION({Ref(skel.Ctx)},'Body','{repType}',({Ref(item)}))");
                int ps  = w.Write($"IFCPRODUCTDEFINITIONSHAPE($,$,({Ref(rep)}))");
                shapeRef = Ref(ps);
            }

            int place = w.Write($"IFCLOCALPLACEMENT({Ref(skel.StoreyPlace)},{Ref(skel.Axis)})");

            string guid = GetGuid(el.Id, el.Name, index);
            string objType = string.IsNullOrEmpty(el.Category) ? "$" : Str(el.Category);

            ResolveClass(schema, el.IfcType, out string ent, out int argCount, out string ninth);

            int objId = argCount == 9
                ? w.Write($"{ent}('{guid}',{Ref(skel.Owner)},{Str(el.Name)},$,{objType},{Ref(place)},{shapeRef},$,{ninth})")
                : w.Write($"{ent}('{guid}',{Ref(skel.Owner)},{Str(el.Name)},$,{objType},{Ref(place)},{shapeRef},$)");

            if (el.PropertySets != null && el.PropertySets.Count > 0)
                RegisterPsets(w, skel.Owner, objId, el.PropertySets, psets);

            return objId;
        }

        // ── geometry ─────────────────────────────────────────────────────────────

        private static int WriteIfc4Mesh(StreamingStepWriter w, GeometryData geom)
        {
            int pl = w.Begin("IFCCARTESIANPOINTLIST3D");
            w.Tok('(');
            for (int i = 0; i < geom.Vertices.Count; i++)
            {
                var v = geom.Vertices[i];
                if (i > 0) w.Sep();
                w.Tok('('); w.WriteReal(v[0]); w.Sep(); w.WriteReal(v[1]); w.Sep(); w.WriteReal(v[2]); w.Tok(')');
            }
            w.Tok(')');
            w.End();

            int fs = w.Begin("IFCTRIANGULATEDFACESET");
            w.RefTok(pl); w.Sep(); w.Tok("$"); w.Sep(); w.Tok("$"); w.Sep();
            w.Tok('(');
            for (int i = 0; i < geom.Triangles.Count; i++)
            {
                var t = geom.Triangles[i];
                if (i > 0) w.Sep();
                w.Tok('('); w.WriteIntRaw(t[0] + 1); w.Sep(); w.WriteIntRaw(t[1] + 1); w.Sep(); w.WriteIntRaw(t[2] + 1); w.Tok(')');
            }
            w.Tok(')'); w.Sep(); w.Tok("$");
            w.End();
            return fs;
        }

        private static int WriteIfc2x3Mesh(StreamingStepWriter w, GeometryData geom)
        {
            var ptIds = new int[geom.Vertices.Count];
            for (int i = 0; i < geom.Vertices.Count; i++)
            {
                var v = geom.Vertices[i];
                int id = w.Begin("IFCCARTESIANPOINT");
                w.Tok('('); w.WriteReal(v[0]); w.Sep(); w.WriteReal(v[1]); w.Sep(); w.WriteReal(v[2]); w.Tok(')');
                w.End();
                ptIds[i] = id;
            }

            var faceIds = new int[geom.Triangles.Count];
            for (int i = 0; i < geom.Triangles.Count; i++)
            {
                var t = geom.Triangles[i];
                int loop = w.Begin("IFCPOLYLOOP");
                w.Tok('('); w.RefTok(ptIds[t[0]]); w.Sep(); w.RefTok(ptIds[t[1]]); w.Sep(); w.RefTok(ptIds[t[2]]); w.Tok(')');
                w.End();
                int bound = w.Begin("IFCFACEOUTERBOUND"); w.RefTok(loop); w.Sep(); w.Tok(".T."); w.End();
                int face  = w.Begin("IFCFACE"); w.Tok('('); w.RefTok(bound); w.Tok(')'); w.End();
                faceIds[i] = face;
            }

            int cfs = w.Begin("IFCCONNECTEDFACESET");
            w.Tok('(');
            for (int i = 0; i < faceIds.Length; i++) { if (i > 0) w.Sep(); w.RefTok(faceIds[i]); }
            w.Tok(')');
            w.End();

            int model = w.Begin("IFCFACEBASEDSURFACEMODEL"); w.Tok('('); w.RefTok(cfs); w.Tok(')'); w.End();
            return model;
        }

        // ── class resolution ─────────────────────────────────────────────────────
        // IfcTypeMapper returns "IfcWall" (PascalCase). IfcTypeCatalog.Reverse maps "IFCWALL" → "Wall".

        private static void ResolveClass(IfcSchema schema, string ifcType,
                                          out string entity, out int argCount, out string ninth)
        {
            ninth = "$";
            string upper = (ifcType ?? "").ToUpperInvariant();

            if (IfcTypeCatalog.Reverse.TryGetValue(upper, out string? friendly)
                && IfcTypeCatalog.Catalog.TryGetValue(friendly, out var cls))
            {
                if (schema == IfcSchema.Ifc4)
                {
                    entity = cls.Ifc4;
                    argCount = 9;
                    return;
                }
                if (cls.Ifc2x3.Length > 0)
                {
                    entity   = cls.Ifc2x3;
                    argCount = cls.Args2x3 > 0 ? cls.Args2x3 : 8;
                    return;
                }
            }

            entity   = "IFCBUILDINGELEMENTPROXY";
            argCount = 9;
        }

        // ── Pset dedup (FNV-1a 64-bit, same as BIMCamel) ─────────────────────────

        private sealed class PsetDedup
        {
            public readonly Dictionary<long, int> ByHash  = new();
            public readonly Dictionary<int, List<int>> Members = new();
        }

        private static void RegisterPsets(StreamingStepWriter w, int owner, int objId,
            Dictionary<string, Dictionary<string, string>> propertySets, PsetDedup d)
        {
            foreach (var psetKv in propertySets)
            {
                var psetName = psetKv.Key;
                var props    = psetKv.Value;
                if (props.Count == 0) continue;

                long h = HashPset(psetName, props);
                if (!d.ByHash.TryGetValue(h, out int psetId))
                {
                    var propIds = new List<int>(props.Count);
                    foreach (var propKv in props)
                    {
                        int pv = w.Begin("IFCPROPERTYSINGLEVALUE");
                        w.WriteStr(propKv.Key); w.Sep(); w.Tok("$"); w.Sep();
                        w.Tok("IFCTEXT("); w.WriteStr(string.IsNullOrEmpty(propKv.Value) ? " " : propKv.Value); w.Tok(")");
                        w.Sep(); w.Tok("$");
                        w.End();
                        propIds.Add(pv);
                    }

                    psetId = w.Begin("IFCPROPERTYSET");
                    w.Tok(G()); w.Sep(); w.RefTok(owner); w.Sep(); w.WriteStr(psetName); w.Sep(); w.Tok("$"); w.Sep();
                    w.Tok("(");
                    for (int i = 0; i < propIds.Count; i++) { if (i > 0) w.Sep(); w.RefTok(propIds[i]); }
                    w.Tok(")");
                    w.End();
                    d.ByHash[h] = psetId;
                }

                if (!d.Members.TryGetValue(psetId, out var mem)) { mem = new List<int>(); d.Members[psetId] = mem; }
                mem.Add(objId);
            }
        }

        private static void WriteDeferredPsets(StreamingStepWriter w, int owner, PsetDedup d)
        {
            foreach (var kv in d.Members)
                w.Write($"IFCRELDEFINESBYPROPERTIES({G()},{Ref(owner)},$,$,({Join(kv.Value)}),{Ref(kv.Key)})");
        }

        private static long HashPset(string psetName, Dictionary<string, string> props)
        {
            unchecked
            {
                ulong h = 14695981039346656037UL;
                void Mix(string? s)
                {
                    if (s != null) foreach (char c in s) { h ^= c; h *= 1099511628211UL; }
                    h ^= 0x1FUL; h *= 1099511628211UL;
                }
                Mix(psetName);
                foreach (var kv in props) { Mix(kv.Key); Mix(kv.Value); }
                return (long)h;
            }
        }

        // ── helpers ──────────────────────────────────────────────────────────────

        private static string GetGuid(string? idStr, string name, int index)
        {
            if (!string.IsNullOrEmpty(idStr) && Guid.TryParse(idStr, out var g) && g != Guid.Empty)
                return IfcGuid.ToIfcGuid(g);
            using var md5 = MD5.Create();
            return IfcGuid.ToIfcGuid(new Guid(md5.ComputeHash(Encoding.UTF8.GetBytes($"{name}#{index}"))));
        }

        private static string G() => "'" + IfcGuid.ToIfcGuid(Guid.NewGuid()) + "'";
        private static string Ref(int id) => "#" + id.ToString(System.Globalization.CultureInfo.InvariantCulture);

        private static string Join(List<int> ids)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < ids.Count; i++) { if (i > 0) sb.Append(','); sb.Append(Ref(ids[i])); }
            return sb.ToString();
        }
    }
}
