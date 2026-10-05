using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OneWood.LaunchMonitor.Diagnostics;
using OneWood.LaunchMonitor.Shots;

namespace OneWood.Tests
{
    public class ShotLogTests
    {
        string _directory;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "OneWoodTests_" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }

        [Test]
        public void Writer_RoundTripsThroughReader()
        {
            var time = new DateTime(2026, 10, 4, 12, 30, 0, DateTimeKind.Utc);
            string path;
            using (var writer = ShotLogWriter.CreateInDirectory(_directory))
            {
                path = writer.Path;
                writer.Write(time, "127.0.0.1:5000", TestShots.Driver);
                writer.Write(time, "127.0.0.1:5000", "{broken");
            }

            var entries = ShotLogFile.Read(path).ToList();
            Assert.That(entries.Count, Is.EqualTo(2));
            Assert.That(entries[0].ReceivedUtc, Is.EqualTo(time));
            Assert.That(entries[0].RemoteEndpoint, Is.EqualTo("127.0.0.1:5000"));
            Assert.That(Newtonsoft.Json.Linq.JToken.DeepEquals(
                Newtonsoft.Json.Linq.JToken.Parse(entries[0].RawJson),
                Newtonsoft.Json.Linq.JToken.Parse(TestShots.Driver)), Is.True);
            Assert.That(entries[1].RawJson, Is.EqualTo("{broken"), "Invalid JSON is preserved verbatim");
        }

        [Test]
        public void Reader_AcceptsBareMessagesAndSkipsComments()
        {
            var lines = new[] { "# comment", "", "// another", TestShots.Driver, "  " + TestShots.Heartbeat };
            var entries = ShotLogFile.ReadLines(lines).ToList();
            Assert.That(entries.Count, Is.EqualTo(2));
            Assert.That(entries[0].LineNumber, Is.EqualTo(4));
            Assert.That(entries[0].RawJson, Is.EqualTo(TestShots.Driver));
            Assert.That(entries[0].ReceivedUtc, Is.Null);
        }

        [Test]
        public void Report_SummarizesMixedLog()
        {
            var duplicate = TestShots.Driver;
            var lines = new[] { TestShots.Heartbeat, TestShots.Driver, duplicate, TestShots.Putt, "not json",
                TestShots.DriverWithBall("Speed", -1.0) };

            var report = ShotLogReport.Build(ShotLogFile.ReadLines(lines));

            Assert.That(report.MessageCount, Is.EqualTo(6));
            Assert.That(report.Heartbeats, Is.EqualTo(1));
            Assert.That(report.FullSwings, Is.EqualTo(3));
            Assert.That(report.Putts, Is.EqualTo(1));
            Assert.That(report.ParseFailures, Is.EqualTo(1));
            Assert.That(report.RejectedShots, Is.EqualTo(1));
            Assert.That(report.IssueCounts[IssueCodes.ShotNumberDuplicate], Is.EqualTo(1));
            Assert.That(report.Passed, Is.False);
            Assert.That(report.ShotFieldCoverage["BallData.Speed"], Is.EqualTo(4));
            Assert.That(report.ShotFieldCoverage["ClubData.Path"], Is.EqualTo(3));
            Assert.That(report.ShotFieldCoverage["ClubData.ClosureRate"], Is.EqualTo(0));

            var text = report.ToText();
            StringAssert.StartsWith("RESULT: FAIL", text);
            StringAssert.Contains("PARSE FAILURE", text);
            StringAssert.Contains("ClubData.ClosureRate", text);
        }

        [Test]
        public void Report_ListsUnknownFields()
        {
            var lines = new[] { TestShots.DriverWith(o => o["BallData"]["Smash"] = 1.46) };
            var report = ShotLogReport.Build(ShotLogFile.ReadLines(lines));
            Assert.That(report.UnknownFields["BallData.Smash"], Is.EqualTo(1));
            Assert.That(report.Passed, Is.True);
            StringAssert.Contains("BallData.Smash", report.ToText());
        }

        [Test]
        public void Report_ExpectingPutts_ClassifiesEveryShotAsPutt()
        {
            var report = ShotLogReport.Build(ShotLogFile.ReadLines(new[] { TestShots.Putt }), expectingPutt: true);
            Assert.That(report.Putts, Is.EqualTo(1));
            Assert.That(report.IssueCounts.ContainsKey(IssueCodes.KindHeuristic), Is.False);
        }
    }
}
