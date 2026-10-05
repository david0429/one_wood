using System;

namespace OneWood.LaunchMonitor.Shots
{
    /// <summary>
    /// Spin as total rate plus tilt of the spin axis. Axis sign convention (Open Connect):
    /// positive = axis tilted right = ball curves right (fade/slice for a right-hander).
    /// </summary>
    public readonly struct SpinComponents
    {
        public double TotalRpm { get; }
        public double AxisDegrees { get; }

        public SpinComponents(double totalRpm, double axisDegrees)
        {
            TotalRpm = totalRpm;
            AxisDegrees = axisDegrees;
        }

        public double BackspinRpm => TotalRpm * Math.Cos(UnitConversion.DegreesToRadians(AxisDegrees));
        public double SidespinRpm => TotalRpm * Math.Sin(UnitConversion.DegreesToRadians(AxisDegrees));

        public static SpinComponents FromBackAndSide(double backspinRpm, double sidespinRpm) =>
            new SpinComponents(
                Math.Sqrt(backspinRpm * backspinRpm + sidespinRpm * sidespinRpm),
                UnitConversion.RadiansToDegrees(Math.Atan2(sidespinRpm, backspinRpm)));
    }

    /// <summary>Club delivery in SI units. Every value is optional because the Mevo+ does not always report them.</summary>
    public readonly struct ClubDelivery
    {
        public double? SpeedMetersPerSecond { get; }
        public double? AngleOfAttackRadians { get; }
        public double? FaceToTargetRadians { get; }
        public double? PathRadians { get; }
        public double? DynamicLoftRadians { get; }

        public ClubDelivery(double? speedMetersPerSecond, double? angleOfAttackRadians, double? faceToTargetRadians,
            double? pathRadians, double? dynamicLoftRadians)
        {
            SpeedMetersPerSecond = speedMetersPerSecond;
            AngleOfAttackRadians = angleOfAttackRadians;
            FaceToTargetRadians = faceToTargetRadians;
            PathRadians = pathRadians;
            DynamicLoftRadians = dynamicLoftRadians;
        }
    }

    /// <summary>
    /// A validated shot normalized to SI units: the input to the ball-flight model.
    /// Horizontal angles and spin axis are positive to the right of the target line.
    /// </summary>
    public readonly struct LaunchData
    {
        public int? ShotNumber { get; }
        public bool IsPutt { get; }
        public double BallSpeedMetersPerSecond { get; }
        public double VerticalLaunchRadians { get; }
        public double HorizontalLaunchRadians { get; }
        public double SpinRpm { get; }
        public double SpinAxisRadians { get; }

        /// <summary>Carry reported by the launch monitor, for calibration only. Never used to fly the ball.</summary>
        public double? ReportedCarryMeters { get; }

        public ClubDelivery? Club { get; }

        public LaunchData(int? shotNumber, bool isPutt, double ballSpeedMetersPerSecond, double verticalLaunchRadians,
            double horizontalLaunchRadians, double spinRpm, double spinAxisRadians, double? reportedCarryMeters,
            ClubDelivery? club)
        {
            ShotNumber = shotNumber;
            IsPutt = isPutt;
            BallSpeedMetersPerSecond = ballSpeedMetersPerSecond;
            VerticalLaunchRadians = verticalLaunchRadians;
            HorizontalLaunchRadians = horizontalLaunchRadians;
            SpinRpm = spinRpm;
            SpinAxisRadians = spinAxisRadians;
            ReportedCarryMeters = reportedCarryMeters;
            Club = club;
        }

        public double SpinRadiansPerSecond => UnitConversion.RpmToRadiansPerSecond(SpinRpm);
    }
}
