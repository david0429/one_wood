namespace OneWood.LaunchMonitor.Shots
{
    public enum IssueSeverity
    {
        Info,
        Warning,
        Error,
    }

    public readonly struct ValidationIssue
    {
        public IssueSeverity Severity { get; }
        public string Code { get; }
        public string Message { get; }

        public ValidationIssue(IssueSeverity severity, string code, string message)
        {
            Severity = severity;
            Code = code;
            Message = message;
        }

        public override string ToString() => $"{Severity.ToString().ToUpperInvariant()} {Code}: {Message}";
    }

    /// <summary>Stable identifiers for validation issues, used by tests, logs and reports.</summary>
    public static class IssueCodes
    {
        public const string ApiVersionMissing = "envelope.api_version.missing";
        public const string ApiVersionUnsupported = "envelope.api_version.unsupported";
        public const string UnitsMissing = "envelope.units.missing";
        public const string UnitsMetric = "envelope.units.metric";
        public const string UnitsUnknown = "envelope.units.unknown";
        public const string ShotNumberMissing = "envelope.shot_number.missing";
        public const string ShotNumberDuplicate = "envelope.shot_number.duplicate";
        public const string ShotNumberReset = "envelope.shot_number.reset";
        public const string OptionsMissing = "envelope.options.missing";
        public const string BallDataMissing = "envelope.ball_data.missing";
        public const string BallDataUnexpected = "envelope.ball_data.unexpected";
        public const string ClubDataMissing = "envelope.club_data.missing";
        public const string DeviceNotReady = "device.not_ready";
        public const string KindHeuristic = "kind.heuristic";

        public const string BallNonFinite = "ball.non_finite";
        public const string BallSpeedMissing = "ball.speed.missing";
        public const string BallSpeedRange = "ball.speed.range";
        public const string VlaMissing = "ball.vla.missing";
        public const string VlaRange = "ball.vla.range";
        public const string HlaMissing = "ball.hla.missing";
        public const string HlaRange = "ball.hla.range";
        public const string HlaWide = "ball.hla.wide";
        public const string SpinMissing = "ball.spin.missing";
        public const string SpinPartial = "ball.spin.partial";
        public const string SpinRange = "ball.spin.range";
        public const string SpinHigh = "ball.spin.high";
        public const string SpinAxisRange = "ball.spin_axis.range";
        public const string SpinAxisWide = "ball.spin_axis.wide";
        public const string SpinInconsistent = "ball.spin.inconsistent";
        public const string SpinSignMismatch = "ball.spin.sign_mismatch";
        public const string CarryRange = "ball.carry.range";
        public const string CarryImplausible = "ball.carry.implausible";
        public const string PuttVlaHigh = "putt.vla.high";

        public const string ClubNonFinite = "club.non_finite";
        public const string ClubSpeedRange = "club.speed.range";
        public const string SmashRange = "club.smash.range";
        public const string ClubAngleRange = "club.angle.range";
    }
}
