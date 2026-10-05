using System.Globalization;
using System.Text;
using OneWood.LaunchMonitor.OpenConnect;
using OneWood.LaunchMonitor.Shots;

namespace OneWood.LaunchMonitor.Diagnostics
{
    /// <summary>One-line human-readable descriptions of messages, for logs, overlays and reports.</summary>
    public static class ShotFormatter
    {
        public static string Summary(OpenConnectMessage message, ShotValidationResult result)
        {
            var sb = new StringBuilder();
            if (message.ShotNumber.HasValue)
                sb.Append('#').Append(message.ShotNumber.Value).Append(' ');

            sb.Append(result.Kind);
            if (result.IsShot)
                sb.Append(result.HasErrors ? " REJECTED" : " ok");

            switch (result.Kind)
            {
                case ShotKind.Heartbeat:
                case ShotKind.Status:
                    var o = message.ShotDataOptions;
                    if (o != null)
                        sb.Append(" ready=").Append(Flag(o.LaunchMonitorIsReady))
                          .Append(" ballDetected=").Append(Flag(o.LaunchMonitorBallDetected));
                    break;
                default:
                    var b = message.BallData;
                    if (b != null)
                    {
                        sb.Append(" | ball ").Append(N(b.Speed)).Append(" mph")
                          .Append(" VLA ").Append(N(b.VLA)).Append('°')
                          .Append(" HLA ").Append(N(b.HLA)).Append('°');
                        if (result.Launch.HasValue)
                        {
                            var l = result.Launch.Value;
                            sb.Append(" spin ").Append(N(l.SpinRpm, "0")).Append(" rpm")
                              .Append(" axis ").Append(N(UnitConversion.RadiansToDegrees(l.SpinAxisRadians))).Append('°');
                        }
                        if (b.CarryDistance.HasValue)
                            sb.Append(" carry ").Append(N(b.CarryDistance));
                    }
                    var c = message.ClubData;
                    if (c?.Speed != null)
                        sb.Append(" | club ").Append(N(c.Speed)).Append(" mph");
                    break;
            }
            return sb.ToString();
        }

        static string Flag(bool? value) => value.HasValue ? (value.Value ? "yes" : "no") : "?";

        static string N(double? value, string format = "0.#") =>
            value.HasValue ? value.Value.ToString(format, CultureInfo.InvariantCulture) : "—";
    }
}
