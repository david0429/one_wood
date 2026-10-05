namespace OneWood.LaunchMonitor.Shots
{
    /// <summary>
    /// Thresholds used by <see cref="ShotValidator"/>, in launch-monitor units (mph, degrees, rpm, yards).
    /// "Max" limits produce errors (the shot is rejected); "Warn" limits flag unusual but playable shots.
    /// </summary>
    public sealed class ShotValidationLimits
    {
        // Ball
        public double MaxBallSpeedMph = 250;
        public double MinVerticalLaunchDegrees = -20;
        public double MaxVerticalLaunchDegrees = 80;
        public double MaxAbsHorizontalLaunchDegrees = 45;
        public double WarnAbsHorizontalLaunchDegrees = 20;
        public double MaxSpinRpm = 15000;
        public double WarnSpinRpm = 12000;
        public double MaxAbsSpinAxisDegrees = 90;
        public double WarnAbsSpinAxisDegrees = 45;

        // Spin consistency between TotalSpin/SpinAxis and BackSpin/SideSpin
        public double MinSpinForConsistencyCheckRpm = 300;
        public double SpinTotalToleranceRpm = 100;
        public double SpinTotalToleranceFraction = 0.05;
        public double SpinAxisToleranceDegrees = 3;
        public double SpinAxisSignDeadbandDegrees = 2;

        // Carry sanity: yards of carry per mph of ball speed for a full swing
        public double MinCarryPerMph = 0.2;
        public double MaxCarryPerMph = 2.3;

        // Putts
        public double PuttMaxBallSpeedMph = 25;
        public double PuttMaxVerticalLaunchDegrees = 8;

        // Club (warnings only; club data is displayed, not simulated)
        public double MaxClubSpeedMph = 160;
        public double MinSmashFactor = 0.7;
        public double MaxSmashFactor = 1.6;
        public double MaxAbsAngleOfAttackDegrees = 15;
        public double MaxAbsPathDegrees = 20;
        public double MaxAbsFaceToTargetDegrees = 20;
        public double MinLoftDegrees = -5;
        public double MaxLoftDegrees = 80;
    }
}
