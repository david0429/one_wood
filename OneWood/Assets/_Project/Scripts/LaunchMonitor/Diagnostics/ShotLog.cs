using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OneWood.LaunchMonitor.Diagnostics
{
    public readonly struct ShotLogEntry
    {
        public int LineNumber { get; }
        public DateTime? ReceivedUtc { get; }
        public string RemoteEndpoint { get; }

        /// <summary>The message exactly as the launch monitor sent it (may be invalid JSON).</summary>
        public string RawJson { get; }

        public ShotLogEntry(int lineNumber, DateTime? receivedUtc, string remoteEndpoint, string rawJson)
        {
            LineNumber = lineNumber;
            ReceivedUtc = receivedUtc;
            RemoteEndpoint = remoteEndpoint;
            RawJson = rawJson;
        }
    }

    /// <summary>
    /// Shot logs are JSON Lines. Each line is either a bare Open Connect message, or an
    /// envelope <c>{"t": ISO-8601, "remote": "ip:port", "raw": {message}}</c> (or <c>"rawText"</c>
    /// when the message was not valid JSON). Blank lines and lines starting with # or // are ignored.
    /// </summary>
    public static class ShotLogFile
    {
        public static IEnumerable<ShotLogEntry> Read(string path) => ReadLines(File.ReadLines(path));

        public static IEnumerable<ShotLogEntry> ReadLines(IEnumerable<string> lines)
        {
            int lineNumber = 0;
            foreach (var line in lines)
            {
                lineNumber++;
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal) || trimmed.StartsWith("//", StringComparison.Ordinal))
                    continue;
                yield return ParseLine(lineNumber, trimmed);
            }
        }

        static ShotLogEntry ParseLine(int lineNumber, string line)
        {
            JObject obj;
            try
            {
                obj = JObject.Parse(line);
            }
            catch (JsonException)
            {
                return new ShotLogEntry(lineNumber, null, null, line);
            }

            var raw = obj["raw"];
            var rawText = obj["rawText"];
            if (raw == null && rawText == null)
                return new ShotLogEntry(lineNumber, null, null, line);

            DateTime? received = null;
            var t = obj["t"];
            if (t != null && t.Type == JTokenType.Date)
                received = t.Value<DateTime>().ToUniversalTime();
            else if (t != null && DateTime.TryParse(t.ToString(), CultureInfo.InvariantCulture,
                         DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
                received = parsed;

            string rawJson = raw != null ? raw.ToString(Formatting.None) : rawText.ToString();
            return new ShotLogEntry(lineNumber, received, obj["remote"]?.ToString(), rawJson);
        }
    }

    /// <summary>Appends received messages to a JSON Lines file. Thread-safe.</summary>
    public sealed class ShotLogWriter : IDisposable
    {
        readonly object _lock = new object();
        readonly StreamWriter _writer;

        public string Path { get; }

        public ShotLogWriter(string path)
        {
            Path = path;
            var directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            _writer = new StreamWriter(path, append: true, new UTF8Encoding(false)) { AutoFlush = true };
        }

        /// <summary>Creates a timestamped log file (shots_yyyyMMdd_HHmmss.jsonl) in <paramref name="directory"/>.</summary>
        public static ShotLogWriter CreateInDirectory(string directory) =>
            new ShotLogWriter(System.IO.Path.Combine(directory,
                $"shots_{DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)}.jsonl"));

        public void Write(DateTime receivedUtc, string remoteEndpoint, string rawJson)
        {
            var envelope = new JObject
            {
                ["t"] = receivedUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                ["remote"] = remoteEndpoint,
            };
            try
            {
                envelope["raw"] = JToken.Parse(rawJson);
            }
            catch (JsonException)
            {
                envelope["rawText"] = rawJson;
            }

            string line = envelope.ToString(Formatting.None);
            lock (_lock)
                _writer.WriteLine(line);
        }

        public void Dispose()
        {
            lock (_lock)
                _writer.Dispose();
        }
    }
}
