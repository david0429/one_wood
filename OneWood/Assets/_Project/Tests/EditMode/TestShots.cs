using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace OneWood.Tests
{
    /// <summary>Shared fixtures: canned Open Connect messages and paths to Tools/ShotLogs.</summary>
    static class TestShots
    {
        public const string Driver =
            "{\"DeviceID\":\"FlightScope Mevo+\",\"Units\":\"Yards\",\"ShotNumber\":1,\"APIversion\":\"1\"," +
            "\"BallData\":{\"Speed\":148.2,\"SpinAxis\":-4.1,\"TotalSpin\":2850,\"BackSpin\":2842.7,\"SideSpin\":-203.8,\"HLA\":1.2,\"VLA\":12.4,\"CarryDistance\":245.0}," +
            "\"ClubData\":{\"Speed\":101.3,\"AngleOfAttack\":-1.0,\"FaceToTarget\":0.8,\"Loft\":13.1,\"Path\":2.0}," +
            "\"ShotDataOptions\":{\"ContainsBallData\":true,\"ContainsClubData\":true,\"LaunchMonitorIsReady\":true,\"LaunchMonitorBallDetected\":true,\"IsHeartBeat\":false}}";

        public const string Putt =
            "{\"DeviceID\":\"FlightScope Mevo+\",\"Units\":\"Yards\",\"ShotNumber\":2,\"APIversion\":\"1\"," +
            "\"BallData\":{\"Speed\":6.2,\"SpinAxis\":0,\"TotalSpin\":0,\"HLA\":0.4,\"VLA\":2.1}," +
            "\"ShotDataOptions\":{\"ContainsBallData\":true,\"ContainsClubData\":false,\"LaunchMonitorIsReady\":true,\"LaunchMonitorBallDetected\":true,\"IsHeartBeat\":false}}";

        public const string Heartbeat =
            "{\"DeviceID\":\"FlightScope Mevo+\",\"Units\":\"Yards\",\"ShotNumber\":0,\"APIversion\":\"1\"," +
            "\"ShotDataOptions\":{\"ContainsBallData\":false,\"ContainsClubData\":false,\"LaunchMonitorIsReady\":true,\"LaunchMonitorBallDetected\":false,\"IsHeartBeat\":true}}";

        /// <summary>The driver message with one ball field replaced (or removed when <paramref name="value"/> is null).</summary>
        public static string DriverWithBall(string field, JToken value)
        {
            var obj = JObject.Parse(Driver);
            var ball = (JObject)obj["BallData"];
            if (value == null) ball.Remove(field);
            else ball[field] = value;
            return obj.ToString();
        }

        public static string DriverWith(System.Action<JObject> edit)
        {
            var obj = JObject.Parse(Driver);
            edit(obj);
            return obj.ToString();
        }

        public static string ShotLogsDirectory =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Tools", "ShotLogs"));
    }
}
