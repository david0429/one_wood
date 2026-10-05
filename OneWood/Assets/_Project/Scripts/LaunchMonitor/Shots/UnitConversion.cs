using System;

namespace OneWood.LaunchMonitor.Shots
{
    public static class UnitConversion
    {
        public const double MetersPerSecondPerMph = 0.44704;
        public const double MetersPerYard = 0.9144;
        public const double RadiansPerDegree = Math.PI / 180.0;
        public const double RadiansPerSecondPerRpm = 2.0 * Math.PI / 60.0;

        public static double MphToMetersPerSecond(double mph) => mph * MetersPerSecondPerMph;
        public static double MetersPerSecondToMph(double mps) => mps / MetersPerSecondPerMph;
        public static double YardsToMeters(double yards) => yards * MetersPerYard;
        public static double MetersToYards(double meters) => meters / MetersPerYard;
        public static double DegreesToRadians(double degrees) => degrees * RadiansPerDegree;
        public static double RadiansToDegrees(double radians) => radians / RadiansPerDegree;
        public static double RpmToRadiansPerSecond(double rpm) => rpm * RadiansPerSecondPerRpm;
    }
}
