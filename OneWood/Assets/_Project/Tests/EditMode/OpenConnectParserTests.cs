using NUnit.Framework;
using OneWood.LaunchMonitor.OpenConnect;

namespace OneWood.Tests
{
    public class OpenConnectParserTests
    {
        [Test]
        public void FullShot_ParsesAllSections()
        {
            var result = OpenConnectParser.Parse(TestShots.Driver);

            Assert.That(result.Success, Is.True, result.Error);
            var m = result.Message;
            Assert.That(m.DeviceID, Is.EqualTo("FlightScope Mevo+"));
            Assert.That(m.Units, Is.EqualTo("Yards"));
            Assert.That(m.ShotNumber, Is.EqualTo(1));
            Assert.That(m.APIversion, Is.EqualTo("1"));
            Assert.That(m.BallData.Speed, Is.EqualTo(148.2));
            Assert.That(m.BallData.SpinAxis, Is.EqualTo(-4.1));
            Assert.That(m.BallData.VLA, Is.EqualTo(12.4));
            Assert.That(m.ClubData.Speed, Is.EqualTo(101.3));
            Assert.That(m.ShotDataOptions.ContainsBallData, Is.True);
            Assert.That(m.ClaimsBallData(), Is.True);
            Assert.That(m.IsHeartbeat(), Is.False);
            Assert.That(result.UnknownFields, Is.Empty);
        }

        [Test]
        public void Heartbeat_IsRecognized()
        {
            var result = OpenConnectParser.Parse(TestShots.Heartbeat);
            Assert.That(result.Success, Is.True);
            Assert.That(result.Message.IsHeartbeat(), Is.True);
            Assert.That(result.Message.ClaimsBallData(), Is.False);
            Assert.That(result.Message.BallData, Is.Null);
        }

        [Test]
        public void MissingFields_AreNull()
        {
            var result = OpenConnectParser.Parse("{\"BallData\":{\"Speed\":100}}");
            Assert.That(result.Success, Is.True);
            Assert.That(result.Message.BallData.VLA, Is.Null);
            Assert.That(result.Message.ShotDataOptions, Is.Null);
            Assert.That(result.Message.ClaimsBallData(), Is.True, "BallData without options is treated as a shot");
        }

        [Test]
        public void NumericStrings_AreAccepted()
        {
            var result = OpenConnectParser.Parse("{\"ShotNumber\":\"7\",\"BallData\":{\"Speed\":\"148.2\"}}");
            Assert.That(result.Success, Is.True, result.Error);
            Assert.That(result.Message.ShotNumber, Is.EqualTo(7));
            Assert.That(result.Message.BallData.Speed, Is.EqualTo(148.2));
        }

        [Test]
        public void FieldNames_AreCaseInsensitive()
        {
            var result = OpenConnectParser.Parse("{\"balldata\":{\"speed\":120}}");
            Assert.That(result.Success, Is.True);
            Assert.That(result.Message.BallData.Speed, Is.EqualTo(120));
            Assert.That(result.UnknownFields, Is.Empty);
        }

        [Test]
        public void UnknownFields_AreReported()
        {
            var result = OpenConnectParser.Parse("{\"Extra\":1,\"BallData\":{\"Speed\":120,\"Smash\":1.4}}");
            Assert.That(result.Success, Is.True);
            Assert.That(result.UnknownFields, Is.EquivalentTo(new[] { "Extra", "BallData.Smash" }));
        }

        [Test]
        public void PresentFields_UseCanonicalNamesAndSkipNulls()
        {
            var result = OpenConnectParser.Parse("{\"units\":\"Yards\",\"BallData\":{\"speed\":120,\"VLA\":null},\"ClubData\":{\"Path\":1}}");
            Assert.That(result.PresentFields, Is.EquivalentTo(new[] { "Units", "BallData.Speed", "ClubData.Path" }));
        }

        [Test]
        public void AllFieldPaths_CoversSchema()
        {
            Assert.That(OpenConnectParser.AllFieldPaths, Does.Contain("BallData.Speed"));
            Assert.That(OpenConnectParser.AllFieldPaths, Does.Contain("ClubData.ClosureRate"));
            Assert.That(OpenConnectParser.AllFieldPaths, Does.Contain("ShotDataOptions.IsHeartBeat"));
            Assert.That(OpenConnectParser.AllFieldPaths, Does.Contain("APIversion"));
            Assert.That(OpenConnectParser.AllFieldPaths, Does.Not.Contain("BallData"));
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("{\"BallData\":")]
        [TestCase("not json")]
        [TestCase("[1,2,3]")]
        [TestCase("42")]
        public void Malformed_Fails(string json)
        {
            var result = OpenConnectParser.Parse(json);
            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Is.Not.Empty);
        }

        [TestCase("{\"BallData\":{\"Speed\":\"fast\"}}")]
        [TestCase("{\"BallData\":5}")]
        [TestCase("{\"ShotNumber\":{\"a\":1}}")]
        public void WrongTypes_Fail(string json)
        {
            var result = OpenConnectParser.Parse(json);
            Assert.That(result.Success, Is.False);
            StringAssert.Contains("wrong type", result.Error);
        }
    }
}
