using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OneWood.LaunchMonitor.OpenConnect;

namespace OneWood.LaunchMonitor.Shots
{
    public enum ShotKind
    {
        Heartbeat,
        Status,
        FullSwing,
        Putt,
    }

    /// <summary>Game state the validator needs. Update it between messages.</summary>
    public sealed class ShotValidationContext
    {
        /// <summary>True when the ball is on the green (or the player chose the putter), so the next shot is a putt.</summary>
        public bool ExpectingPutt;

        /// <summary>ShotNumber of the last accepted shot, for duplicate/reset detection.</summary>
        public int? PreviousShotNumber;
    }

    public sealed class ShotValidationResult
    {
        public ShotKind Kind { get; }
        public IReadOnlyList<ValidationIssue> Issues { get; }

        /// <summary>The normalized shot, set only for shots without errors.</summary>
        public LaunchData? Launch { get; }

        public ShotValidationResult(ShotKind kind, IReadOnlyList<ValidationIssue> issues, LaunchData? launch)
        {
            Kind = kind;
            Issues = issues;
            Launch = launch;
        }

        public bool IsShot => Kind == ShotKind.FullSwing || Kind == ShotKind.Putt;
        public bool HasErrors => Issues.Any(i => i.Severity == IssueSeverity.Error);
        public bool HasWarnings => Issues.Any(i => i.Severity == IssueSeverity.Warning);
        public bool IsValidShot => IsShot && !HasErrors;
        public bool HasIssue(string code) => Issues.Any(i => i.Code == code);
    }

    /// <summary>
    /// Checks launch-monitor feedback for completeness, physical plausibility and internal
    /// consistency, then converts accepted shots to <see cref="LaunchData"/>. Errors reject
    /// the shot (the player re-hits without penalty); warnings are logged but the shot plays.
    /// </summary>
    public sealed class ShotValidator
    {
        enum DistanceUnit
        {
            Yards,
            Meters,
        }

        readonly ShotValidationLimits _limits;

        public ShotValidator(ShotValidationLimits limits = null)
        {
            _limits = limits ?? new ShotValidationLimits();
        }

        public ShotValidationResult Validate(OpenConnectMessage message, ShotValidationContext context = null)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            context = context ?? new ShotValidationContext();
            var issues = new List<ValidationIssue>();

            var distanceUnit = ValidateEnvelope(message, issues);

            if (message.IsHeartbeat())
                return new ShotValidationResult(ShotKind.Heartbeat, issues, null);

            if (!message.ClaimsBallData())
            {
                if (HasAnyValue(message.BallData))
                    Warn(issues, IssueCodes.BallDataUnexpected, "BallData has values but ContainsBallData is false; treating as a status message.");
                return new ShotValidationResult(ShotKind.Status, issues, null);
            }

            var options = message.ShotDataOptions;
            if (options == null)
                Warn(issues, IssueCodes.OptionsMissing, "ShotDataOptions missing; inferring a shot from BallData.");
            else if (options.LaunchMonitorIsReady == false)
                Warn(issues, IssueCodes.DeviceNotReady, "Shot received while LaunchMonitorIsReady is false.");

            ValidateShotNumber(message.ShotNumber, context, issues);

            var ball = message.BallData;
            if (ball == null)
            {
                Error(issues, IssueCodes.BallDataMissing, "ContainsBallData is true but BallData is missing.");
                return new ShotValidationResult(context.ExpectingPutt ? ShotKind.Putt : ShotKind.FullSwing, issues, null);
            }

            if (!CheckFinite(BallFields(ball), IssueCodes.BallNonFinite, IssueSeverity.Error, issues))
                return new ShotValidationResult(context.ExpectingPutt ? ShotKind.Putt : ShotKind.FullSwing, issues, null);

            var kind = ClassifyShot(ball, context, issues);
            ValidateLaunch(ball, kind, issues);
            var spin = ResolveSpin(ball, kind, issues);
            ValidateCarry(ball, kind, distanceUnit, issues);
            ValidateClub(message, kind, issues);

            LaunchData? launch = null;
            if (!issues.Any(i => i.Severity == IssueSeverity.Error) && spin.HasValue)
                launch = ToLaunchData(message, kind, spin.Value, distanceUnit);

            return new ShotValidationResult(kind, issues, launch);
        }

        DistanceUnit ValidateEnvelope(OpenConnectMessage message, List<ValidationIssue> issues)
        {
            if (string.IsNullOrEmpty(message.APIversion))
                Warn(issues, IssueCodes.ApiVersionMissing, "APIversion missing; assuming \"1\".");
            else if (message.APIversion != "1")
                Warn(issues, IssueCodes.ApiVersionUnsupported, $"APIversion \"{message.APIversion}\" is not \"1\"; fields may differ.");

            var units = message.Units;
            if (string.IsNullOrEmpty(units))
            {
                Warn(issues, IssueCodes.UnitsMissing, "Units missing; assuming Yards.");
                return DistanceUnit.Yards;
            }
            if (units.Equals("Yards", StringComparison.OrdinalIgnoreCase))
                return DistanceUnit.Yards;
            if (units.Equals("Metres", StringComparison.OrdinalIgnoreCase) || units.Equals("Meters", StringComparison.OrdinalIgnoreCase))
            {
                Warn(issues, IssueCodes.UnitsMetric, "Metric units: distances read as metres, speeds still assumed mph (unverified).");
                return DistanceUnit.Meters;
            }
            Error(issues, IssueCodes.UnitsUnknown, $"Unknown Units \"{units}\".");
            return DistanceUnit.Yards;
        }

        void ValidateShotNumber(int? shotNumber, ShotValidationContext context, List<ValidationIssue> issues)
        {
            if (!shotNumber.HasValue)
            {
                Warn(issues, IssueCodes.ShotNumberMissing, "ShotNumber missing; duplicate shots cannot be detected.");
                return;
            }
            if (!context.PreviousShotNumber.HasValue) return;

            int previous = context.PreviousShotNumber.Value;
            if (shotNumber.Value == previous)
                Warn(issues, IssueCodes.ShotNumberDuplicate, $"ShotNumber {shotNumber} repeats the previous shot (possible resend).");
            else if (shotNumber.Value < previous)
                Info(issues, IssueCodes.ShotNumberReset, $"ShotNumber went from {previous} to {shotNumber} (client restarted?).");
        }

        ShotKind ClassifyShot(OpenConnectBallData ball, ShotValidationContext context, List<ValidationIssue> issues)
        {
            if (context.ExpectingPutt) return ShotKind.Putt;
            if (ball.Speed.HasValue && ball.VLA.HasValue &&
                ball.Speed.Value > 0 &&
                ball.Speed.Value <= _limits.PuttMaxBallSpeedMph &&
                ball.VLA.Value <= _limits.PuttMaxVerticalLaunchDegrees)
            {
                Info(issues, IssueCodes.KindHeuristic, "Classified as a putt from low ball speed and launch angle.");
                return ShotKind.Putt;
            }
            return ShotKind.FullSwing;
        }

        void ValidateLaunch(OpenConnectBallData ball, ShotKind kind, List<ValidationIssue> issues)
        {
            if (!ball.Speed.HasValue)
                Error(issues, IssueCodes.BallSpeedMissing, "BallData.Speed missing.");
            else if (ball.Speed.Value <= 0 || ball.Speed.Value > _limits.MaxBallSpeedMph)
                Error(issues, IssueCodes.BallSpeedRange, $"Ball speed {F(ball.Speed)} mph outside (0, {F(_limits.MaxBallSpeedMph)}].");

            if (!ball.VLA.HasValue)
                Error(issues, IssueCodes.VlaMissing, "BallData.VLA missing.");
            else if (ball.VLA.Value < _limits.MinVerticalLaunchDegrees || ball.VLA.Value > _limits.MaxVerticalLaunchDegrees)
                Error(issues, IssueCodes.VlaRange, $"Vertical launch {F(ball.VLA)}° outside [{F(_limits.MinVerticalLaunchDegrees)}, {F(_limits.MaxVerticalLaunchDegrees)}].");
            else if (kind == ShotKind.Putt && ball.VLA.Value > _limits.PuttMaxVerticalLaunchDegrees)
                Warn(issues, IssueCodes.PuttVlaHigh, $"Putt launched at {F(ball.VLA)}°; was it chipped?");

            if (!ball.HLA.HasValue)
                Error(issues, IssueCodes.HlaMissing, "BallData.HLA missing.");
            else if (Math.Abs(ball.HLA.Value) > _limits.MaxAbsHorizontalLaunchDegrees)
                Error(issues, IssueCodes.HlaRange, $"Horizontal launch {F(ball.HLA)}° beyond ±{F(_limits.MaxAbsHorizontalLaunchDegrees)}.");
            else if (Math.Abs(ball.HLA.Value) > _limits.WarnAbsHorizontalLaunchDegrees)
                Warn(issues, IssueCodes.HlaWide, $"Horizontal launch {F(ball.HLA)}° is very wide.");
        }

        SpinComponents? ResolveSpin(OpenConnectBallData ball, ShotKind kind, List<ValidationIssue> issues)
        {
            SpinComponents? fromTotal = null;
            if (ball.TotalSpin.HasValue)
            {
                if (!ball.SpinAxis.HasValue)
                    Warn(issues, IssueCodes.SpinPartial, "TotalSpin without SpinAxis; assuming axis 0°.");
                fromTotal = new SpinComponents(ball.TotalSpin.Value, ball.SpinAxis ?? 0);
            }

            SpinComponents? fromParts = null;
            if (ball.BackSpin.HasValue)
            {
                if (!ball.SideSpin.HasValue)
                    Warn(issues, IssueCodes.SpinPartial, "BackSpin without SideSpin; assuming no side spin.");
                fromParts = SpinComponents.FromBackAndSide(ball.BackSpin.Value, ball.SideSpin ?? 0);
            }

            if (fromTotal.HasValue && fromParts.HasValue)
                CheckSpinConsistency(fromTotal.Value, fromParts.Value, issues);

            var spin = fromTotal ?? fromParts;
            if (!spin.HasValue)
            {
                if (kind == ShotKind.Putt)
                    return new SpinComponents(0, 0);
                Error(issues, IssueCodes.SpinMissing, "No spin data (TotalSpin or BackSpin) for a full swing.");
                return null;
            }

            double total = spin.Value.TotalRpm;
            if (total < 0 || total > _limits.MaxSpinRpm)
                Error(issues, IssueCodes.SpinRange, $"Total spin {F(total)} rpm outside [0, {F(_limits.MaxSpinRpm)}].");
            else if (total > _limits.WarnSpinRpm)
                Warn(issues, IssueCodes.SpinHigh, $"Total spin {F(total)} rpm is unusually high.");

            if (kind == ShotKind.FullSwing)
            {
                double axis = Math.Abs(spin.Value.AxisDegrees);
                if (axis > _limits.MaxAbsSpinAxisDegrees)
                    Error(issues, IssueCodes.SpinAxisRange, $"Spin axis {F(spin.Value.AxisDegrees)}° beyond ±{F(_limits.MaxAbsSpinAxisDegrees)}.");
                else if (axis > _limits.WarnAbsSpinAxisDegrees)
                    Warn(issues, IssueCodes.SpinAxisWide, $"Spin axis {F(spin.Value.AxisDegrees)}° is extreme.");
            }

            return spin;
        }

        void CheckSpinConsistency(SpinComponents fromTotal, SpinComponents fromParts, List<ValidationIssue> issues)
        {
            if (fromTotal.TotalRpm < _limits.MinSpinForConsistencyCheckRpm) return;

            double totalTolerance = Math.Max(_limits.SpinTotalToleranceRpm, fromTotal.TotalRpm * _limits.SpinTotalToleranceFraction);
            if (Math.Abs(fromTotal.TotalRpm - fromParts.TotalRpm) > totalTolerance)
                Warn(issues, IssueCodes.SpinInconsistent,
                    $"TotalSpin {F(fromTotal.TotalRpm)} rpm disagrees with BackSpin/SideSpin ({F(fromParts.TotalRpm)} rpm).");

            double deadband = _limits.SpinAxisSignDeadbandDegrees;
            bool opposite = (fromTotal.AxisDegrees > deadband && fromParts.AxisDegrees < -deadband) ||
                            (fromTotal.AxisDegrees < -deadband && fromParts.AxisDegrees > deadband);
            if (opposite)
                Warn(issues, IssueCodes.SpinSignMismatch,
                    $"SpinAxis is {F(fromTotal.AxisDegrees)}° but SideSpin implies {F(fromParts.AxisDegrees)}°; check the sign convention.");
            else if (Math.Abs(fromTotal.AxisDegrees - fromParts.AxisDegrees) > _limits.SpinAxisToleranceDegrees)
                Warn(issues, IssueCodes.SpinInconsistent,
                    $"SpinAxis {F(fromTotal.AxisDegrees)}° disagrees with BackSpin/SideSpin ({F(fromParts.AxisDegrees)}°).");
        }

        void ValidateCarry(OpenConnectBallData ball, ShotKind kind, DistanceUnit unit, List<ValidationIssue> issues)
        {
            if (!ball.CarryDistance.HasValue) return;
            double carry = ball.CarryDistance.Value;
            if (carry < 0)
            {
                Error(issues, IssueCodes.CarryRange, $"Carry distance {F(carry)} is negative.");
                return;
            }
            if (kind != ShotKind.FullSwing || carry == 0 || !ball.Speed.HasValue || ball.Speed.Value <= 0) return;

            double carryYards = unit == DistanceUnit.Meters ? UnitConversion.MetersToYards(carry) : carry;
            double ratio = carryYards / ball.Speed.Value;
            if (ratio < _limits.MinCarryPerMph || ratio > _limits.MaxCarryPerMph)
                Warn(issues, IssueCodes.CarryImplausible,
                    $"Reported carry {F(carryYards)} yd is implausible for {F(ball.Speed)} mph ball speed.");
        }

        void ValidateClub(OpenConnectMessage message, ShotKind kind, List<ValidationIssue> issues)
        {
            var club = message.ClubData;
            bool claimsClub = message.ShotDataOptions?.ContainsClubData ?? club != null;
            if (!claimsClub) return;
            if (club == null)
            {
                Warn(issues, IssueCodes.ClubDataMissing, "ContainsClubData is true but ClubData is missing.");
                return;
            }
            if (!CheckFinite(ClubFields(club), IssueCodes.ClubNonFinite, IssueSeverity.Warning, issues))
                return;

            if (club.Speed.HasValue)
            {
                if (club.Speed.Value <= 0 || club.Speed.Value > _limits.MaxClubSpeedMph)
                    Warn(issues, IssueCodes.ClubSpeedRange, $"Club speed {F(club.Speed)} mph outside (0, {F(_limits.MaxClubSpeedMph)}].");
                else if (kind == ShotKind.FullSwing && message.BallData?.Speed is double ballSpeed && ballSpeed > 0)
                {
                    double smash = ballSpeed / club.Speed.Value;
                    if (smash < _limits.MinSmashFactor || smash > _limits.MaxSmashFactor)
                        Warn(issues, IssueCodes.SmashRange, $"Smash factor {smash.ToString("0.00", CultureInfo.InvariantCulture)} outside [{F(_limits.MinSmashFactor)}, {F(_limits.MaxSmashFactor)}].");
                }
            }

            CheckClubAngle("AngleOfAttack", club.AngleOfAttack, -_limits.MaxAbsAngleOfAttackDegrees, _limits.MaxAbsAngleOfAttackDegrees, issues);
            CheckClubAngle("Path", club.Path, -_limits.MaxAbsPathDegrees, _limits.MaxAbsPathDegrees, issues);
            CheckClubAngle("FaceToTarget", club.FaceToTarget, -_limits.MaxAbsFaceToTargetDegrees, _limits.MaxAbsFaceToTargetDegrees, issues);
            CheckClubAngle("Loft", club.Loft, _limits.MinLoftDegrees, _limits.MaxLoftDegrees, issues);
        }

        static void CheckClubAngle(string name, double? value, double min, double max, List<ValidationIssue> issues)
        {
            if (value.HasValue && (value.Value < min || value.Value > max))
                Warn(issues, IssueCodes.ClubAngleRange, $"ClubData.{name} {F(value)}° outside [{F(min)}, {F(max)}].");
        }

        static LaunchData ToLaunchData(OpenConnectMessage message, ShotKind kind, SpinComponents spin, DistanceUnit unit)
        {
            var ball = message.BallData;
            double? carryMeters = null;
            if (ball.CarryDistance.HasValue)
                carryMeters = unit == DistanceUnit.Meters ? ball.CarryDistance.Value : UnitConversion.YardsToMeters(ball.CarryDistance.Value);

            ClubDelivery? club = null;
            var c = message.ClubData;
            if (c != null && message.ShotDataOptions?.ContainsClubData != false)
            {
                club = new ClubDelivery(
                    c.Speed.HasValue ? UnitConversion.MphToMetersPerSecond(c.Speed.Value) : (double?)null,
                    Radians(c.AngleOfAttack), Radians(c.FaceToTarget), Radians(c.Path), Radians(c.Loft));
            }

            return new LaunchData(
                message.ShotNumber,
                kind == ShotKind.Putt,
                UnitConversion.MphToMetersPerSecond(ball.Speed.Value),
                UnitConversion.DegreesToRadians(ball.VLA.Value),
                UnitConversion.DegreesToRadians(ball.HLA.Value),
                spin.TotalRpm,
                UnitConversion.DegreesToRadians(spin.AxisDegrees),
                carryMeters,
                club);
        }

        static double? Radians(double? degrees) =>
            degrees.HasValue ? UnitConversion.DegreesToRadians(degrees.Value) : (double?)null;

        static bool CheckFinite(IEnumerable<(string name, double? value)> fields, string code, IssueSeverity severity, List<ValidationIssue> issues)
        {
            bool allFinite = true;
            foreach (var (name, value) in fields)
            {
                if (value.HasValue && (double.IsNaN(value.Value) || double.IsInfinity(value.Value)))
                {
                    issues.Add(new ValidationIssue(severity, code, $"{name} is {F(value)}."));
                    allFinite = false;
                }
            }
            return allFinite;
        }

        static IEnumerable<(string, double?)> BallFields(OpenConnectBallData b)
        {
            yield return ("BallData.Speed", b.Speed);
            yield return ("BallData.SpinAxis", b.SpinAxis);
            yield return ("BallData.TotalSpin", b.TotalSpin);
            yield return ("BallData.BackSpin", b.BackSpin);
            yield return ("BallData.SideSpin", b.SideSpin);
            yield return ("BallData.HLA", b.HLA);
            yield return ("BallData.VLA", b.VLA);
            yield return ("BallData.CarryDistance", b.CarryDistance);
        }

        static IEnumerable<(string, double?)> ClubFields(OpenConnectClubData c)
        {
            yield return ("ClubData.Speed", c.Speed);
            yield return ("ClubData.AngleOfAttack", c.AngleOfAttack);
            yield return ("ClubData.FaceToTarget", c.FaceToTarget);
            yield return ("ClubData.Lie", c.Lie);
            yield return ("ClubData.Loft", c.Loft);
            yield return ("ClubData.Path", c.Path);
            yield return ("ClubData.SpeedAtImpact", c.SpeedAtImpact);
            yield return ("ClubData.VerticalFaceImpact", c.VerticalFaceImpact);
            yield return ("ClubData.HorizontalFaceImpact", c.HorizontalFaceImpact);
            yield return ("ClubData.ClosureRate", c.ClosureRate);
        }

        static bool HasAnyValue(OpenConnectBallData b) =>
            b != null && BallFields(b).Any(f => f.Item2.HasValue);

        static string F(double? value) =>
            value.HasValue ? value.Value.ToString("0.##", CultureInfo.InvariantCulture) : "null";

        static void Info(List<ValidationIssue> issues, string code, string message) =>
            issues.Add(new ValidationIssue(IssueSeverity.Info, code, message));

        static void Warn(List<ValidationIssue> issues, string code, string message) =>
            issues.Add(new ValidationIssue(IssueSeverity.Warning, code, message));

        static void Error(List<ValidationIssue> issues, string code, string message) =>
            issues.Add(new ValidationIssue(IssueSeverity.Error, code, message));
    }
}
