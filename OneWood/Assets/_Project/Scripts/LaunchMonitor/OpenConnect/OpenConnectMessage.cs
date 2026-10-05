namespace OneWood.LaunchMonitor.OpenConnect
{
    /// <summary>
    /// A message sent by a launch-monitor client (FS Golf for the Mevo+) using the
    /// GSPro Open Connect v1 protocol. Every field is optional on the wire, so all
    /// values are nullable; <see cref="Shots.ShotValidator"/> decides what is required.
    /// Field names match the protocol exactly (Newtonsoft binds public fields).
    /// </summary>
    public sealed class OpenConnectMessage
    {
        public string DeviceID;
        public string Units;
        public int? ShotNumber;
        public string APIversion;
        public OpenConnectBallData BallData;
        public OpenConnectClubData ClubData;
        public OpenConnectShotDataOptions ShotDataOptions;

        public bool IsHeartbeat() => ShotDataOptions?.IsHeartBeat == true;

        /// <summary>True when the message claims to carry a shot (ball data), heartbeat aside.</summary>
        public bool ClaimsBallData()
        {
            if (IsHeartbeat()) return false;
            return ShotDataOptions?.ContainsBallData ?? BallData != null;
        }
    }

    /// <summary>Ball launch data. Speeds in mph, angles in degrees, spin in rpm, distance in <see cref="OpenConnectMessage.Units"/>.</summary>
    public sealed class OpenConnectBallData
    {
        public double? Speed;
        public double? SpinAxis;
        public double? TotalSpin;
        public double? BackSpin;
        public double? SideSpin;
        public double? HLA;
        public double? VLA;
        public double? CarryDistance;
    }

    /// <summary>Club delivery data. Speeds in mph, angles in degrees.</summary>
    public sealed class OpenConnectClubData
    {
        public double? Speed;
        public double? AngleOfAttack;
        public double? FaceToTarget;
        public double? Lie;
        public double? Loft;
        public double? Path;
        public double? SpeedAtImpact;
        public double? VerticalFaceImpact;
        public double? HorizontalFaceImpact;
        public double? ClosureRate;
    }

    public sealed class OpenConnectShotDataOptions
    {
        public bool? ContainsBallData;
        public bool? ContainsClubData;
        public bool? LaunchMonitorIsReady;
        public bool? LaunchMonitorBallDetected;
        public bool? IsHeartBeat;
    }
}
