using System.Collections.Generic;
using System.Linq;
using System.Text;
using OneWood.LaunchMonitor.OpenConnect;
using OneWood.LaunchMonitor.Shots;

namespace OneWood.LaunchMonitor.Diagnostics
{
    /// <summary>
    /// Replays a shot log through the parser and validator and summarizes what the
    /// launch monitor actually sent: per-message results, issue counts, which schema
    /// fields were populated, and any fields outside the Open Connect v1 schema.
    /// </summary>
    public sealed class ShotLogReport
    {
        public int MessageCount { get; private set; }
        public int ParseFailures { get; private set; }
        public int Heartbeats { get; private set; }
        public int StatusMessages { get; private set; }
        public int FullSwings { get; private set; }
        public int Putts { get; private set; }
        public int RejectedShots { get; private set; }
        public int ShotsWithWarnings { get; private set; }

        /// <summary>Issue code → occurrences.</summary>
        public SortedDictionary<string, int> IssueCounts { get; } = new SortedDictionary<string, int>();

        /// <summary>Non-schema field path → occurrences.</summary>
        public SortedDictionary<string, int> UnknownFields { get; } = new SortedDictionary<string, int>();

        /// <summary>Schema field path → number of shots (full swings and putts) where it had a value.</summary>
        public Dictionary<string, int> ShotFieldCoverage { get; } = new Dictionary<string, int>();

        public List<string> Lines { get; } = new List<string>();

        public int ShotCount => FullSwings + Putts;

        /// <summary>True when every message parsed and no shot was rejected.</summary>
        public bool Passed => ParseFailures == 0 && RejectedShots == 0;

        /// <param name="expectingPutt">Treat every shot as a putt, for logs captured in putting mode.</param>
        public static ShotLogReport Build(IEnumerable<ShotLogEntry> entries, ShotValidator validator = null, bool expectingPutt = false)
        {
            validator = validator ?? new ShotValidator();
            var context = new ShotValidationContext { ExpectingPutt = expectingPutt };
            var report = new ShotLogReport();
            foreach (var field in OpenConnectParser.AllFieldPaths)
                report.ShotFieldCoverage[field] = 0;

            foreach (var entry in entries)
                report.Add(entry, validator, context);
            return report;
        }

        void Add(ShotLogEntry entry, ShotValidator validator, ShotValidationContext context)
        {
            MessageCount++;
            string prefix = $"line {entry.LineNumber}:";

            var parse = OpenConnectParser.Parse(entry.RawJson);
            if (!parse.Success)
            {
                ParseFailures++;
                Lines.Add($"{prefix} PARSE FAILURE {parse.Error}");
                return;
            }

            foreach (var field in parse.UnknownFields)
                Increment(UnknownFields, field);

            var result = validator.Validate(parse.Message, context);
            switch (result.Kind)
            {
                case ShotKind.Heartbeat: Heartbeats++; break;
                case ShotKind.Status: StatusMessages++; break;
                case ShotKind.FullSwing: FullSwings++; break;
                case ShotKind.Putt: Putts++; break;
            }

            if (result.IsShot)
            {
                if (result.HasErrors) RejectedShots++;
                else if (result.HasWarnings) ShotsWithWarnings++;
                foreach (var field in parse.PresentFields)
                    if (ShotFieldCoverage.ContainsKey(field))
                        ShotFieldCoverage[field]++;
                if (parse.Message.ShotNumber.HasValue && !result.HasErrors)
                    context.PreviousShotNumber = parse.Message.ShotNumber;
            }

            foreach (var issue in result.Issues)
                Increment(IssueCounts, issue.Code);

            // Heartbeats and clean status messages are noise in the per-line listing.
            bool quiet = !result.IsShot && result.Issues.All(i => i.Severity == IssueSeverity.Info);
            if (quiet) return;

            Lines.Add($"{prefix} {ShotFormatter.Summary(parse.Message, result)}");
            foreach (var issue in result.Issues)
                Lines.Add($"    {issue}");
            if (parse.UnknownFields.Count > 0)
                Lines.Add($"    unknown fields: {string.Join(", ", parse.UnknownFields)}");
        }

        public string ToText()
        {
            var sb = new StringBuilder();
            sb.AppendLine(Passed ? "RESULT: PASS" : "RESULT: FAIL");
            sb.AppendLine($"Messages: {MessageCount} (parse failures {ParseFailures}, heartbeats {Heartbeats}, status {StatusMessages})");
            sb.AppendLine($"Shots: {ShotCount} (full swings {FullSwings}, putts {Putts}) — rejected {RejectedShots}, with warnings {ShotsWithWarnings}");

            if (IssueCounts.Count > 0)
            {
                sb.AppendLine().AppendLine("Issues:");
                foreach (var pair in IssueCounts)
                    sb.AppendLine($"  {pair.Key,-42} {pair.Value}");
            }

            if (UnknownFields.Count > 0)
            {
                sb.AppendLine().AppendLine("Fields outside the Open Connect v1 schema:");
                foreach (var pair in UnknownFields)
                    sb.AppendLine($"  {pair.Key,-42} {pair.Value}");
            }

            if (ShotCount > 0)
            {
                sb.AppendLine().AppendLine($"Field coverage across {ShotCount} shot(s):");
                foreach (var field in OpenConnectParser.AllFieldPaths)
                    sb.AppendLine($"  {field,-42} {ShotFieldCoverage[field]}/{ShotCount}");
            }

            if (Lines.Count > 0)
            {
                sb.AppendLine().AppendLine("Messages:");
                foreach (var line in Lines)
                    sb.AppendLine(line);
            }
            return sb.ToString();
        }

        static void Increment(IDictionary<string, int> counts, string key)
        {
            counts.TryGetValue(key, out int count);
            counts[key] = count + 1;
        }
    }
}
