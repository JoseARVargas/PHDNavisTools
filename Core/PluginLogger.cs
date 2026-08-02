using System;
using System.Diagnostics;
using System.IO;

namespace PHDNavisTools.Core
{
    internal static class PluginLogger
    {
        private static readonly string _logPath;
        private static readonly object _lock = new object();

        static PluginLogger()
        {
            const string devRoot = @"c:\dev\navisworks-ifc-exporter";
            string dir = Directory.Exists(devRoot)
                ? Path.Combine(devRoot, "logs")
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Autodesk", "Navisworks 2026", "Plugins", "PHDNavisTools", "logs");

            try { Directory.CreateDirectory(dir); } catch { }
            _logPath = Path.Combine(dir, "plugin.log");
        }

        public static string LogPath => _logPath;

        public static void Info(string msg)  => Write("INFO ", msg);
        public static void Warn(string msg)  => Write("WARN ", msg);
        public static void Error(string msg, Exception? ex = null)
        {
            string full = ex == null ? msg
                : $"{msg} | {ex.GetType().Name}: {ex.Message}\n    {ex.StackTrace?.Replace("\n", "\n    ")}";
            Write("ERROR", full);
        }

        // Logs elapsed time + optional throughput when disposed.
        // Usage: using (PluginLogger.Perf("fase", itemCount)) { ... }
        public static PerfScope Perf(string label, int? itemCount = null)
            => new PerfScope(label, itemCount);

        public static void Clear()
        {
            try { lock (_lock) File.Delete(_logPath); } catch { }
        }

        private static void Write(string level, string msg)
        {
            try
            {
                lock (_lock)
                    File.AppendAllText(_logPath,
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {msg}{Environment.NewLine}");
            }
            catch { }
        }

        // -----------------------------------------------------------------------

        internal sealed class PerfScope : IDisposable
        {
            private readonly string _label;
            private readonly int? _itemCount;
            private readonly Stopwatch _sw = Stopwatch.StartNew();
            private bool _disposed;

            internal PerfScope(string label, int? itemCount)
            {
                _label     = label;
                _itemCount = itemCount;
                Write("PERF ", $">> {label} — início");
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _sw.Stop();

                long ms = _sw.ElapsedMilliseconds;
                string throughput = (_itemCount.HasValue && ms > 0)
                    ? $"  ({_itemCount.Value / (ms / 1000.0):N0} itens/s)"
                    : "";

                Write("PERF ", $"<< {_label} — {ms} ms{throughput}");
            }

            // Mid-scope checkpoint without stopping the timer
            public void Mark(string note)
                => Write("PERF ", $"   {_label} [{_sw.ElapsedMilliseconds} ms] {note}");
        }
    }
}
