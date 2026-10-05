using System;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using OneWood.LaunchMonitor.OpenConnect;

namespace OneWood.Tests
{
    public class OpenConnectResponseTests
    {
        [Test]
        public void ShotReceived_Has200AndNullPlayer()
        {
            var json = JObject.Parse(OpenConnectResponse.ShotReceived());
            Assert.That((int)json["Code"], Is.EqualTo(200));
            Assert.That((string)json["Message"], Is.Not.Empty);
            Assert.That(json["Player"].Type, Is.EqualTo(JTokenType.Null));
        }

        [Test]
        public void Failure_Has501()
        {
            var json = JObject.Parse(OpenConnectResponse.Failure("bad"));
            Assert.That((int)json["Code"], Is.EqualTo(501));
            Assert.That((string)json["Message"], Is.EqualTo("bad"));
        }

        [Test]
        public void PlayerInfo_Has201HandednessAndClub()
        {
            var json = JObject.Parse(OpenConnectResponse.Player(new PlayerInfo(Handedness.Left, ClubCodes.Putter, 12.5)));
            Assert.That((int)json["Code"], Is.EqualTo(201));
            Assert.That((string)json["Player"]["Handed"], Is.EqualTo("LH"));
            Assert.That((string)json["Player"]["Club"], Is.EqualTo("PT"));
            Assert.That((double)json["Player"]["DistanceToTarget"], Is.EqualTo(12.5));
        }

        [Test]
        public void PlayerInfo_OmitsUnknownDistance()
        {
            var json = JObject.Parse(OpenConnectResponse.Player(new PlayerInfo(Handedness.Right, ClubCodes.Driver)));
            Assert.That((string)json["Player"]["Handed"], Is.EqualTo("RH"));
            Assert.That(json["Player"]["DistanceToTarget"], Is.Null);
        }

        [TestCase("DR", true)]
        [TestCase("I7", true)]
        [TestCase("PT", true)]
        [TestCase("dr", false)]
        [TestCase("I10", false)]
        [TestCase(null, false)]
        public void ClubCodes_Validate(string code, bool valid)
        {
            Assert.That(ClubCodes.IsValid(code), Is.EqualTo(valid));
        }

        [Test]
        public void PlayerInfo_RejectsInvalidClub()
        {
            Assert.Throws<ArgumentException>(() => new PlayerInfo(Handedness.Right, "Mithril"));
        }
    }
}
