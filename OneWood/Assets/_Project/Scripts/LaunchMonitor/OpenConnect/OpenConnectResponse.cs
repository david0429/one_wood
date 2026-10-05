using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace OneWood.LaunchMonitor.OpenConnect
{
    public enum Handedness
    {
        Right,
        Left,
    }

    /// <summary>GSPro Open Connect club codes. The launch monitor uses "PT" to switch to putting mode.</summary>
    public static class ClubCodes
    {
        public const string Driver = "DR";
        public const string Putter = "PT";

        static readonly HashSet<string> Valid = new HashSet<string>(StringComparer.Ordinal)
        {
            "DR", "W2", "W3", "W4", "W5", "W6", "W7",
            "H2", "H3", "H4", "H5", "H6", "H7",
            "I1", "I2", "I3", "I4", "I5", "I6", "I7", "I8", "I9",
            "PW", "GW", "SW", "LW", "PT",
        };

        public static bool IsValid(string code) => code != null && Valid.Contains(code);
    }

    public sealed class PlayerInfo
    {
        public Handedness Handed { get; }
        public string Club { get; }

        /// <summary>Distance to the target in the session's units (yards by default).</summary>
        public double? DistanceToTarget { get; }

        public PlayerInfo(Handedness handed, string club, double? distanceToTarget = null)
        {
            if (!ClubCodes.IsValid(club))
                throw new ArgumentException($"Unknown Open Connect club code '{club}'.", nameof(club));
            Handed = handed;
            Club = club;
            DistanceToTarget = distanceToTarget;
        }
    }

    /// <summary>Builds the JSON responses the game (acting as the GSPro server) sends back to the client.</summary>
    public static class OpenConnectResponse
    {
        public const int ShotReceivedCode = 200;
        public const int PlayerInfoCode = 201;
        public const int FailureCode = 501;

        public static string ShotReceived(string message = "Shot received successfully") =>
            Serialize(ShotReceivedCode, message, null);

        public static string Failure(string message) =>
            Serialize(FailureCode, message, null);

        public static string Player(PlayerInfo player)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));
            var payload = new Dictionary<string, object>
            {
                { "Handed", player.Handed == Handedness.Left ? "LH" : "RH" },
                { "Club", player.Club },
            };
            if (player.DistanceToTarget.HasValue)
                payload.Add("DistanceToTarget", player.DistanceToTarget.Value);
            return Serialize(PlayerInfoCode, "One Wood Player Information", payload);
        }

        static string Serialize(int code, string message, object player) =>
            JsonConvert.SerializeObject(new Dictionary<string, object>
            {
                { "Code", code },
                { "Message", message },
                { "Player", player },
            });
    }
}
