using System;
using NUnit.Framework;
using OneWood.LaunchMonitor.Shots;

namespace OneWood.Tests
{
    public class UnitConversionTests
    {
        const double Tolerance = 1e-9;

        [Test]
        public void Mph_RoundTrips()
        {
            Assert.That(UnitConversion.MphToMetersPerSecond(100), Is.EqualTo(44.704).Within(Tolerance));
            Assert.That(UnitConversion.MetersPerSecondToMph(44.704), Is.EqualTo(100).Within(Tolerance));
        }

        [Test]
        public void Yards_RoundTrip()
        {
            Assert.That(UnitConversion.YardsToMeters(100), Is.EqualTo(91.44).Within(Tolerance));
            Assert.That(UnitConversion.MetersToYards(91.44), Is.EqualTo(100).Within(Tolerance));
        }

        [Test]
        public void Angles_AndSpin()
        {
            Assert.That(UnitConversion.DegreesToRadians(180), Is.EqualTo(Math.PI).Within(Tolerance));
            Assert.That(UnitConversion.RadiansToDegrees(Math.PI / 2), Is.EqualTo(90).Within(Tolerance));
            Assert.That(UnitConversion.RpmToRadiansPerSecond(60), Is.EqualTo(2 * Math.PI).Within(Tolerance));
        }

        [TestCase(3000, 0, 3000, 0)]
        [TestCase(3000, 10, 2954.423259, 520.944533)]
        [TestCase(3000, -10, 2954.423259, -520.944533)]
        public void SpinComponents_SplitIntoBackAndSide(double total, double axis, double back, double side)
        {
            var spin = new SpinComponents(total, axis);
            Assert.That(spin.BackspinRpm, Is.EqualTo(back).Within(1e-5));
            Assert.That(spin.SidespinRpm, Is.EqualTo(side).Within(1e-5));
        }

        [Test]
        public void SpinComponents_RoundTripThroughBackAndSide()
        {
            var original = new SpinComponents(6800, 2.5);
            var rebuilt = SpinComponents.FromBackAndSide(original.BackspinRpm, original.SidespinRpm);
            Assert.That(rebuilt.TotalRpm, Is.EqualTo(6800).Within(1e-6));
            Assert.That(rebuilt.AxisDegrees, Is.EqualTo(2.5).Within(1e-9));
        }

        [Test]
        public void PositiveSideSpin_MeansPositiveAxis()
        {
            Assert.That(SpinComponents.FromBackAndSide(3000, 200).AxisDegrees, Is.GreaterThan(0));
            Assert.That(SpinComponents.FromBackAndSide(3000, -200).AxisDegrees, Is.LessThan(0));
        }
    }
}
