using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OneWood.LaunchMonitor.OpenConnect
{
    public sealed class OpenConnectParseResult
    {
        public bool Success { get; }
        public OpenConnectMessage Message { get; }
        public string Error { get; }

        /// <summary>Fields in the JSON that are not part of the Open Connect v1 schema, e.g. "BallData.Foo".</summary>
        public IReadOnlyList<string> UnknownFields { get; }

        /// <summary>Schema fields that were present with a non-null value, using canonical names, e.g. "ClubData.Path".</summary>
        public IReadOnlyList<string> PresentFields { get; }

        OpenConnectParseResult(bool success, OpenConnectMessage message, string error,
            IReadOnlyList<string> unknown, IReadOnlyList<string> present)
        {
            Success = success;
            Message = message;
            Error = error;
            UnknownFields = unknown;
            PresentFields = present;
        }

        internal static OpenConnectParseResult Ok(OpenConnectMessage message, List<string> unknown, List<string> present) =>
            new OpenConnectParseResult(true, message, null, unknown, present);

        internal static OpenConnectParseResult Fail(string error) =>
            new OpenConnectParseResult(false, null, error, Array.Empty<string>(), Array.Empty<string>());
    }

    public static class OpenConnectParser
    {
        static readonly JsonSerializer Serializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Ignore,
            FloatParseHandling = FloatParseHandling.Double,
        });

        static readonly Dictionary<string, string> TopLevelFields = CanonicalFieldNames(typeof(OpenConnectMessage));

        static readonly Dictionary<string, Dictionary<string, string>> NestedFields =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                { nameof(OpenConnectMessage.BallData), CanonicalFieldNames(typeof(OpenConnectBallData)) },
                { nameof(OpenConnectMessage.ClubData), CanonicalFieldNames(typeof(OpenConnectClubData)) },
                { nameof(OpenConnectMessage.ShotDataOptions), CanonicalFieldNames(typeof(OpenConnectShotDataOptions)) },
            };

        /// <summary>All schema field paths, e.g. "BallData.Speed", in declaration order.</summary>
        public static IReadOnlyList<string> AllFieldPaths { get; } = BuildAllFieldPaths();

        public static OpenConnectParseResult Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return OpenConnectParseResult.Fail("Empty message.");

            JObject root;
            try
            {
                using (var reader = new JsonTextReader(new System.IO.StringReader(json)) { FloatParseHandling = FloatParseHandling.Double })
                {
                    var token = JToken.ReadFrom(reader);
                    root = token as JObject;
                    if (root == null)
                        return OpenConnectParseResult.Fail($"Expected a JSON object but got {token.Type}.");
                }
            }
            catch (JsonException e)
            {
                return OpenConnectParseResult.Fail($"Invalid JSON: {e.Message}");
            }

            var unknown = new List<string>();
            var present = new List<string>();
            CollectFields(root, unknown, present);

            try
            {
                var message = root.ToObject<OpenConnectMessage>(Serializer);
                return OpenConnectParseResult.Ok(message, unknown, present);
            }
            catch (Exception e) when (e is JsonException || e is FormatException || e is InvalidCastException || e is ArgumentException)
            {
                return OpenConnectParseResult.Fail($"Field has the wrong type: {e.Message}");
            }
        }

        static void CollectFields(JObject root, List<string> unknown, List<string> present)
        {
            foreach (var property in root.Properties())
            {
                if (!TopLevelFields.TryGetValue(property.Name, out var canonical))
                {
                    unknown.Add(property.Name);
                    continue;
                }

                if (NestedFields.TryGetValue(canonical, out var nested) && property.Value is JObject child)
                {
                    foreach (var sub in child.Properties())
                    {
                        if (!nested.TryGetValue(sub.Name, out var subCanonical))
                            unknown.Add($"{canonical}.{sub.Name}");
                        else if (sub.Value.Type != JTokenType.Null)
                            present.Add($"{canonical}.{subCanonical}");
                    }
                }
                else if (property.Value.Type != JTokenType.Null && !NestedFields.ContainsKey(canonical))
                {
                    present.Add(canonical);
                }
            }
        }

        static Dictionary<string, string> CanonicalFieldNames(Type type) =>
            type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                .ToDictionary(f => f.Name, f => f.Name, StringComparer.OrdinalIgnoreCase);

        static IReadOnlyList<string> BuildAllFieldPaths()
        {
            var paths = new List<string>();
            foreach (var field in typeof(OpenConnectMessage).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (NestedFields.ContainsKey(field.Name))
                    paths.AddRange(field.FieldType.GetFields(BindingFlags.Public | BindingFlags.Instance)
                        .Select(f => $"{field.Name}.{f.Name}"));
                else
                    paths.Add(field.Name);
            }
            return paths;
        }
    }
}
