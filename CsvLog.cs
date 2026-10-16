using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TcpLatencyGraph
{
    internal static class Csv
    {
        public const string Header = "Timestamp,Host,Port,IP,LatencyMs,Status,Detail";

        public static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
                return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }

    // Several graph windows can log to the same file (e.g. -log together with the
    // start window). They share one writer so lines don't overwrite each other.
    // Only used from the UI thread.
    internal sealed class SharedLog
    {
        private static readonly Dictionary<string, SharedLog> OpenLogs =
            new Dictionary<string, SharedLog>(StringComparer.OrdinalIgnoreCase);

        private readonly string _path;
        private readonly StreamWriter _writer;
        private int _refs;

        private SharedLog(string path, StreamWriter writer)
        {
            _path = path;
            _writer = writer;
        }

        public static SharedLog Acquire(string path)
        {
            string fullPath = Path.GetFullPath(path);
            SharedLog log;
            if (!OpenLogs.TryGetValue(fullPath, out log))
            {
                var stream = new FileStream(fullPath, FileMode.Append, FileAccess.Write, FileShare.Read);
                try
                {
                    // Check before creating the writer, it writes the BOM right away on a new file
                    bool isNew = stream.Length == 0;
                    var writer = new StreamWriter(stream, new UTF8Encoding(true)) { AutoFlush = true };
                    if (isNew)
                        writer.WriteLine(Csv.Header);
                    log = new SharedLog(fullPath, writer);
                }
                catch
                {
                    stream.Dispose();
                    throw;
                }
                OpenLogs[fullPath] = log;
            }
            log._refs++;
            return log;
        }

        public void WriteLine(string line)
        {
            _writer.WriteLine(line);
        }

        public void Release()
        {
            if (--_refs > 0)
                return;

            OpenLogs.Remove(_path);
            try
            {
                _writer.Dispose();
            }
            catch (IOException)
            {
                // Already failed while writing, nothing more to do
            }
        }
    }
}
