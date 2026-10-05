using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using OneWood.LaunchMonitor.Diagnostics;
using OneWood.LaunchMonitor.OpenConnect;
using OneWood.LaunchMonitor.Shots;

namespace OneWood.Tests
{
    /// <summary>Validates the shared shot logs in Tools/ShotLogs (also replayable with Tools/open_connect.py).</summary>
    public class SampleShotLogTests
    {
        static string SamplePath => Path.Combine(TestShots.ShotLogsDirectory, "sample_shots.jsonl");
        static string ProblemPath => Path.Combine(TestShots.ShotLogsDirectory, "problem_shots.jsonl");

        [Test]
        public void SampleLog_PassesWithoutWarnings()
        {
            Assert.That(File.Exists(SamplePath), Is.True, SamplePath);
            var report = ShotLogReport.Build(ShotLogFile.Read(SamplePath));
            var text = report.ToText();

            Assert.That(report.Passed, Is.True, text);
            Assert.That(report.ShotsWithWarnings, Is.EqualTo(0), text);
            Assert.That(report.FullSwings, Is.EqualTo(3), text);
            Assert.That(report.Putts, Is.EqualTo(1), text);
            Assert.That(report.Heartbeats, Is.EqualTo(1), text);
            Assert.That(report.StatusMessages, Is.EqualTo(1), text);
            Assert.That(report.UnknownFields, Is.Empty, text);
        }

        public static IEnumerable<TestCaseData> ProblemCases()
        {
            foreach (var line in File.ReadLines(ProblemPath))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#")) continue;
                var obj = JObject.Parse(trimmed);
                yield return new TestCaseData(
                        (string)obj["expect"], (string)obj["severity"], obj["raw"].ToString(Newtonsoft.Json.Formatting.None))
                    .SetName($"ProblemShot: {(string)obj["note"]}");
            }
        }

        [TestCaseSource(nameof(ProblemCases))]
        public void ProblemShot_RaisesExpectedIssue(string expectedCode, string severity, string raw)
        {
            var parse = OpenConnectParser.Parse(raw);
            Assert.That(parse.Success, Is.True, parse.Error);

            var result = new ShotValidator().Validate(parse.Message);
            var expectedSeverity = (IssueSeverity)System.Enum.Parse(typeof(IssueSeverity), severity);

            Assert.That(result.Issues.Any(i => i.Code == expectedCode && i.Severity == expectedSeverity), Is.True,
                $"Expected {severity} {expectedCode}; got:\n{string.Join("\n", result.Issues)}");
            Assert.That(result.IsValidShot, Is.EqualTo(expectedSeverity != IssueSeverity.Error),
                string.Join("\n", result.Issues));
        }
    }
}
