using System;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using OneWood.LaunchMonitor.OpenConnect;
using OneWood.LaunchMonitor.Shots;

namespace OneWood.Tests
{
    public class ShotValidatorTests
    {
        const double Tolerance = 1e-6;

        readonly ShotValidator _validator = new ShotValidator();

        ShotValidationResult Validate(string json, ShotValidationContext context = null)
        {
            var parse = OpenConnectParser.Parse(json);
            Assert.That(parse.Success, Is.True, parse.Error);
            return _validator.Validate(parse.Message, context);
        }

        static void AssertIssue(ShotValidationResult result, string code, IssueSeverity severity)
        {
            Assert.That(result.Issues, Has.Some.Matches<ValidationIssue>(i => i.Code == code && i.Severity == severity),
                $"Expected {severity} {code}; got:\n{string.Join("\n", result.Issues)}");
        }

        [Test]
        public void ValidDriver_IsAcceptedWithoutWarnings()
        {
            var result = Validate(TestShots.Driver);

            Assert.That(result.Kind, Is.EqualTo(ShotKind.FullSwing));
            Assert.That(result.IsValidShot, Is.True);
            Assert.That(result.HasWarnings, Is.False, string.Join("\n", result.Issues));
            Assert.That(result.Launch.HasValue, Is.True);
        }

        [Test]
        public void ValidDriver_IsConvertedToSI()
        {
            var launch = Validate(TestShots.Driver).Launch.Value;

            Assert.That(launch.ShotNumber, Is.EqualTo(1));
            Assert.That(launch.IsPutt, Is.False);
            Assert.That(launch.BallSpeedMetersPerSecond, Is.EqualTo(148.2 * 0.44704).Within(Tolerance));
            Assert.That(launch.VerticalLaunchRadians, Is.EqualTo(12.4 * Math.PI / 180).Within(Tolerance));
            Assert.That(launch.HorizontalLaunchRadians, Is.EqualTo(1.2 * Math.PI / 180).Within(Tolerance));
            Assert.That(launch.SpinRpm, Is.EqualTo(2850).Within(Tolerance));
            Assert.That(launch.SpinAxisRadians, Is.EqualTo(-4.1 * Math.PI / 180).Within(Tolerance));
            Assert.That(launch.SpinRadiansPerSecond, Is.EqualTo(2850 * 2 * Math.PI / 60).Within(Tolerance));
            Assert.That(launch.ReportedCarryMeters, Is.EqualTo(245 * 0.9144).Within(Tolerance));
            Assert.That(launch.Club.HasValue, Is.True);
            Assert.That(launch.Club.Value.SpeedMetersPerSecond, Is.EqualTo(101.3 * 0.44704).Within(Tolerance));
            Assert.That(launch.Club.Value.PathRadians, Is.EqualTo(2.0 * Math.PI / 180).Within(Tolerance));
        }

        [Test]
        public void Heartbeat_IsNotAShot()
        {
            var result = Validate(TestShots.Heartbeat);
            Assert.That(result.Kind, Is.EqualTo(ShotKind.Heartbeat));
            Assert.That(result.IsShot, Is.False);
            Assert.That(result.Launch.HasValue, Is.False);
        }

        [Test]
        public void StatusWithStrayBallData_Warns()
        {
            var json = TestShots.DriverWith(o => o["ShotDataOptions"]["ContainsBallData"] = false);
            var result = Validate(json);
            Assert.That(result.Kind, Is.EqualTo(ShotKind.Status));
            AssertIssue(result, IssueCodes.BallDataUnexpected, IssueSeverity.Warning);
        }

        [Test]
        public void SlowLowShot_IsClassifiedAsPuttByHeuristic()
        {
            var result = Validate(TestShots.Putt);
            Assert.That(result.Kind, Is.EqualTo(ShotKind.Putt));
            Assert.That(result.IsValidShot, Is.True);
            Assert.That(result.Launch.Value.IsPutt, Is.True);
            AssertIssue(result, IssueCodes.KindHeuristic, IssueSeverity.Info);
        }

        [Test]
        public void ExpectingPutt_ForcesPuttAndAllowsMissingSpin()
        {
            var json = TestShots.DriverWith(o =>
            {
                o["BallData"] = new JObject { ["Speed"] = 9.0, ["HLA"] = 0.0, ["VLA"] = 1.0 };
                o["ClubData"] = null;
                o["ShotDataOptions"]["ContainsClubData"] = false;
            });
            var result = Validate(json, new ShotValidationContext { ExpectingPutt = true });
            Assert.That(result.Kind, Is.EqualTo(ShotKind.Putt));
            Assert.That(result.IsValidShot, Is.True, string.Join("\n", result.Issues));
            Assert.That(result.Launch.Value.SpinRpm, Is.EqualTo(0));
            Assert.That(result.HasIssue(IssueCodes.KindHeuristic), Is.False);
        }

        [Test]
        public void ExpectingPutt_WithHighLaunch_Warns()
        {
            var json = TestShots.DriverWithBall("VLA", 15.0);
            var result = Validate(json, new ShotValidationContext { ExpectingPutt = true });
            AssertIssue(result, IssueCodes.PuttVlaHigh, IssueSeverity.Warning);
        }

        [Test]
        public void SpinFromBackAndSide_IsUsedWhenTotalMissing()
        {
            var json = TestShots.DriverWith(o =>
            {
                var ball = (JObject)o["BallData"];
                ball.Remove("TotalSpin");
                ball.Remove("SpinAxis");
                ball["BackSpin"] = 3000.0;
                ball["SideSpin"] = 300.0;
            });
            var result = Validate(json);
            Assert.That(result.IsValidShot, Is.True, string.Join("\n", result.Issues));
            Assert.That(result.Launch.Value.SpinRpm, Is.EqualTo(Math.Sqrt(3000 * 3000 + 300 * 300)).Within(Tolerance));
            Assert.That(result.Launch.Value.SpinAxisRadians, Is.EqualTo(Math.Atan2(300, 3000)).Within(Tolerance));
        }

        [Test]
        public void SpinSignMismatch_Warns()
        {
            var json = TestShots.DriverWithBall("SideSpin", 203.8);
            var result = Validate(json);
            AssertIssue(result, IssueCodes.SpinSignMismatch, IssueSeverity.Warning);
            Assert.That(result.IsValidShot, Is.True, "Sign mismatch is a warning, the shot still plays from TotalSpin/SpinAxis");
        }

        [Test]
        public void SpinConsistency_IsSkippedForLowSpin()
        {
            var json = TestShots.DriverWith(o =>
            {
                var ball = (JObject)o["BallData"];
                ball["TotalSpin"] = 150.0;
                ball["SpinAxis"] = 10.0;
                ball["BackSpin"] = 100.0;
                ball["SideSpin"] = -50.0;
            });
            var result = Validate(json);
            Assert.That(result.HasIssue(IssueCodes.SpinSignMismatch), Is.False);
            Assert.That(result.HasIssue(IssueCodes.SpinInconsistent), Is.False);
        }

        [TestCase("Speed", 0.0, IssueCodes.BallSpeedRange)]
        [TestCase("Speed", 260.0, IssueCodes.BallSpeedRange)]
        [TestCase("VLA", -25.0, IssueCodes.VlaRange)]
        [TestCase("VLA", 85.0, IssueCodes.VlaRange)]
        [TestCase("HLA", -50.0, IssueCodes.HlaRange)]
        [TestCase("TotalSpin", -10.0, IssueCodes.SpinRange)]
        [TestCase("TotalSpin", 16000.0, IssueCodes.SpinRange)]
        [TestCase("SpinAxis", 95.0, IssueCodes.SpinAxisRange)]
        [TestCase("CarryDistance", -1.0, IssueCodes.CarryRange)]
        public void OutOfRangeBallValue_IsRejected(string field, double value, string code)
        {
            var result = Validate(TestShots.DriverWithBall(field, value));
            AssertIssue(result, code, IssueSeverity.Error);
            Assert.That(result.IsValidShot, Is.False);
            Assert.That(result.Launch.HasValue, Is.False);
        }

        [TestCase("HLA", 25.0, IssueCodes.HlaWide)]
        [TestCase("TotalSpin", 13000.0, IssueCodes.SpinHigh)]
        [TestCase("SpinAxis", 50.0, IssueCodes.SpinAxisWide)]
        [TestCase("CarryDistance", 400.0, IssueCodes.CarryImplausible)]
        public void UnusualBallValue_WarnsButPlays(string field, double value, string code)
        {
            // Drop BackSpin/SideSpin so the edited value isn't also flagged as inconsistent.
            var json = TestShots.DriverWith(o =>
            {
                var ball = (JObject)o["BallData"];
                ball.Remove("BackSpin");
                ball.Remove("SideSpin");
                ball[field] = value;
            });
            var result = Validate(json);
            AssertIssue(result, code, IssueSeverity.Warning);
            Assert.That(result.IsValidShot, Is.True, string.Join("\n", result.Issues));
        }

        [TestCase("Speed", IssueCodes.BallSpeedMissing)]
        [TestCase("VLA", IssueCodes.VlaMissing)]
        [TestCase("HLA", IssueCodes.HlaMissing)]
        public void MissingRequiredBallValue_IsRejected(string field, string code)
        {
            var result = Validate(TestShots.DriverWithBall(field, null));
            AssertIssue(result, code, IssueSeverity.Error);
            Assert.That(result.IsValidShot, Is.False);
        }

        [Test]
        public void NonFiniteBallValue_IsRejected()
        {
            var message = OpenConnectParser.Parse(TestShots.Driver).Message;
            message.BallData.VLA = double.PositiveInfinity;
            var result = _validator.Validate(message);
            AssertIssue(result, IssueCodes.BallNonFinite, IssueSeverity.Error);
        }

        [Test]
        public void DuplicateShotNumber_Warns()
        {
            var result = Validate(TestShots.Driver, new ShotValidationContext { PreviousShotNumber = 1 });
            AssertIssue(result, IssueCodes.ShotNumberDuplicate, IssueSeverity.Warning);
        }

        [Test]
        public void ShotNumberGoingBackwards_IsInfo()
        {
            var result = Validate(TestShots.Driver, new ShotValidationContext { PreviousShotNumber = 40 });
            AssertIssue(result, IssueCodes.ShotNumberReset, IssueSeverity.Info);
            Assert.That(result.HasWarnings, Is.False);
        }

        [Test]
        public void MissingEnvelopeFields_Warn()
        {
            var json = TestShots.DriverWith(o =>
            {
                o.Remove("APIversion");
                o.Remove("Units");
                o.Remove("ShotNumber");
                o.Remove("ShotDataOptions");
            });
            var result = Validate(json);
            AssertIssue(result, IssueCodes.ApiVersionMissing, IssueSeverity.Warning);
            AssertIssue(result, IssueCodes.UnitsMissing, IssueSeverity.Warning);
            AssertIssue(result, IssueCodes.ShotNumberMissing, IssueSeverity.Warning);
            AssertIssue(result, IssueCodes.OptionsMissing, IssueSeverity.Warning);
            Assert.That(result.IsValidShot, Is.True);
        }

        [Test]
        public void UnsupportedApiVersion_Warns()
        {
            var result = Validate(TestShots.DriverWith(o => o["APIversion"] = "2"));
            AssertIssue(result, IssueCodes.ApiVersionUnsupported, IssueSeverity.Warning);
        }

        [Test]
        public void MetricUnits_ReadCarryAsMeters()
        {
            var result = Validate(TestShots.DriverWith(o => o["Units"] = "Metres"));
            AssertIssue(result, IssueCodes.UnitsMetric, IssueSeverity.Warning);
            Assert.That(result.Launch.Value.ReportedCarryMeters, Is.EqualTo(245).Within(Tolerance));
        }

        [Test]
        public void ClubDataProblems_AreWarningsOnly()
        {
            var json = TestShots.DriverWith(o =>
            {
                o["ClubData"]["Speed"] = 60.0;           // smash 2.47
                o["ClubData"]["AngleOfAttack"] = 30.0;
                o["ClubData"]["Path"] = double.NaN;
            });
            var result = Validate(json);
            AssertIssue(result, IssueCodes.ClubNonFinite, IssueSeverity.Warning);
            Assert.That(result.IsValidShot, Is.True);

            json = TestShots.DriverWith(o =>
            {
                o["ClubData"]["Speed"] = 60.0;
                o["ClubData"]["AngleOfAttack"] = 30.0;
            });
            result = Validate(json);
            AssertIssue(result, IssueCodes.SmashRange, IssueSeverity.Warning);
            AssertIssue(result, IssueCodes.ClubAngleRange, IssueSeverity.Warning);
            Assert.That(result.IsValidShot, Is.True);
        }

        [Test]
        public void ClubDataClaimedButMissing_Warns()
        {
            var result = Validate(TestShots.DriverWith(o => o.Remove("ClubData")));
            AssertIssue(result, IssueCodes.ClubDataMissing, IssueSeverity.Warning);
            Assert.That(result.Launch.Value.Club.HasValue, Is.False);
        }

        [Test]
        public void CustomLimits_AreRespected()
        {
            var strict = new ShotValidator(new ShotValidationLimits { MaxBallSpeedMph = 120 });
            var result = strict.Validate(OpenConnectParser.Parse(TestShots.Driver).Message);
            Assert.That(result.HasIssue(IssueCodes.BallSpeedRange), Is.True);
        }

        [Test]
        public void NullMessage_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => _validator.Validate(null));
        }
    }
}
