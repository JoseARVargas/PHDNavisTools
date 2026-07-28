using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace NavisworksIfcExporter.Core
{
    public enum IfcSchema { Ifc4, Ifc2x3 }

    /// <summary>
    /// Low-allocation STEP (ISO-10303-21) text emitter. Ported from BIMCamel (MIT).
    /// Two emission paths:
    ///   • Write(string) — convenience for skeleton/relationship entities (bounded count).
    ///   • Begin/Tok/Sep/WriteReal/RefTok/WriteIntRaw/End — hot path; zero string allocation per mesh.
    /// </summary>
    public sealed class StreamingStepWriter : IDisposable
    {
        private readonly StreamWriter _w;
        private int _id;
        private long _bytes;
        private readonly char[] _num = new char[32];

        public long BytesWritten => _bytes;

        private void Emit(char c) { _w.Write(c); _bytes++; }
        private void Emit(string s) { _w.Write(s); _bytes += s.Length; }
        private void Emit(char[] buf, int n) { _w.Write(buf, 0, n); _bytes += n; }

        private readonly int _frac;
        private readonly long _fracPowL;
        private readonly double _fracPowD;

        public StreamingStepWriter(string path, int coordDecimals = 6)
        {
            _frac = coordDecimals < 1 ? 1 : (coordDecimals > 9 ? 9 : coordDecimals);
            _fracPowL = 1; for (int i = 0; i < _frac; i++) _fracPowL *= 10;
            _fracPowD = _fracPowL;
            _w = new StreamWriter(path, false, new UTF8Encoding(false), 4 << 20);
        }

        public int Write(string typeAndArgs)
        {
            int id = ++_id;
            Emit('#'); WriteIntRaw(id); Emit('='); Emit(typeAndArgs); Emit(";\n");
            return id;
        }

        public int Begin(string typeName)
        {
            int id = ++_id;
            Emit('#'); WriteIntRaw(id); Emit('='); Emit(typeName); Emit('(');
            return id;
        }

        public void Tok(string s) => Emit(s);
        public void Tok(char c)   => Emit(c);
        public void Sep()         => Emit(',');
        public void RefTok(int id) { Emit('#'); WriteIntRaw(id); }

        public void WriteStr(string? s)
        {
            if (string.IsNullOrEmpty(s)) { Emit('$'); return; }
            Emit('\'');
            foreach (char ch in s!)
            {
                if (ch == '\'') Emit("''");
                else if (ch == '\r' || ch == '\n') Emit(' ');
                else Emit(ch);
            }
            Emit('\'');
        }

        public void End() => Emit(");\n");

        public void WriteIntRaw(long val)
        {
            if (val == 0) { Emit('0'); return; }
            int p = 0;
            bool neg = val < 0;
            ulong v = neg ? (ulong)(-val) : (ulong)val;
            while (v > 0) { _num[p++] = (char)('0' + (int)(v % 10)); v /= 10; }
            if (neg) Emit('-');
            for (int i = p - 1; i >= 0; i--) Emit(_num[i]);
        }

        public void WriteReal(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) { Emit("0.0"); return; }
            if (v < 1.0e7 && v > -1.0e7) { WriteRealFast(v); return; }
            Emit(R(v));
        }

        private void WriteRealFast(double v)
        {
            bool neg = v < 0;
            double av = neg ? -v : v;
            long intPart = (long)av;
            double f = av - intPart;
            long frac = (long)(f * _fracPowD + 0.5);
            if (frac >= _fracPowL) { frac -= _fracPowL; intPart++; }

            var buf = _num;
            int p = 0;
            if (neg && (intPart != 0 || frac != 0)) buf[p++] = '-';

            if (intPart == 0) buf[p++] = '0';
            else
            {
                int start = p;
                long t = intPart;
                while (t > 0) { buf[p++] = (char)('0' + (int)(t % 10)); t /= 10; }
                for (int a = start, b = p - 1; a < b; a++, b--) { var tmp = buf[a]; buf[a] = buf[b]; buf[b] = tmp; }
            }

            buf[p++] = '.';
            long div = _fracPowL / 10;
            int fracStart = p;
            for (int i = 0; i < _frac; i++) { buf[p++] = (char)('0' + (int)(frac / div)); frac %= div; div /= 10; }
            int end = p - 1;
            while (end > fracStart && buf[end] == '0') end--;
            p = end + 1;
            Emit(buf, p);
        }

        public static string Ref(int id) => "#" + id.ToString(CultureInfo.InvariantCulture);

        public static string R(double v) =>
            v.ToString("0.0##########", CultureInfo.InvariantCulture);

        public static string R6(double v) =>
            v.ToString("0.0#####", CultureInfo.InvariantCulture);

        public static string Str(string? s)
        {
            if (string.IsNullOrEmpty(s)) return "$";
            var sb = new StringBuilder(s!.Length + 2);
            sb.Append('\'');
            foreach (var ch in s!) { if (ch == '\'') sb.Append("''"); else if (ch == '\r' || ch == '\n') sb.Append(' '); else sb.Append(ch); }
            sb.Append('\'');
            return sb.ToString();
        }

        public void WriteHeader(IfcSchema schema, string fileName, string author)
        {
            string schemaId = schema == IfcSchema.Ifc4 ? "IFC4" : "IFC2X3";
            string ts = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
            Emit("ISO-10303-21;\n");
            Emit("HEADER;\n");
            Emit("FILE_DESCRIPTION(('ViewDefinition [CoordinationView]'),'2;1');\n");
            Emit($"FILE_NAME({Str(fileName)},'{ts}',({Str(author)}),(''),'NavisworksIfcExporter','NavisIFC','');\n");
            Emit($"FILE_SCHEMA(('{schemaId}'));\n");
            Emit("ENDSEC;\n");
            Emit("DATA;\n");
        }

        public void WriteFooter()
        {
            Emit("ENDSEC;\n");
            Emit("END-ISO-10303-21;\n");
            _w.Flush();
        }

        public void Dispose() => _w.Dispose();
    }
}
