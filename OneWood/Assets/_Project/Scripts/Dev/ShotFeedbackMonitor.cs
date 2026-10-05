using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using OneWood.LaunchMonitor.Diagnostics;
using OneWood.LaunchMonitor.OpenConnect;
using OneWood.LaunchMonitor.Shots;
using UnityEngine;

namespace OneWood.Dev
{
    /// <summary>
    /// Development tool: runs the Open Connect server, validates every message from the
    /// launch monitor, logs it to a JSON Lines shot log, and shows the results on screen.
    /// Use it to check what FS Golf actually sends before the real game consumes shots.
    /// </summary>
    [AddComponentMenu("One Wood/Dev/Shot Feedback Monitor")]
    public sealed class ShotFeedbackMonitor : MonoBehaviour
    {
        struct HistoryEntry
        {
            public string Summary;
            public List<ValidationIssue> Issues;
            public bool Failed;
        }

        [SerializeField] int port = OpenConnectServer.DefaultPort;
        [SerializeField] bool respondToStatusMessages;
        [SerializeField] bool writeShotLog = true;
        [SerializeField, Range(1, 20)] int historySize = 6;

        readonly ConcurrentQueue<ReceivedMessage> _incoming = new ConcurrentQueue<ReceivedMessage>();
        readonly ConcurrentQueue<string> _diagnostics = new ConcurrentQueue<string>();
        readonly ShotValidator _validator = new ShotValidator();
        readonly ShotValidationContext _context = new ShotValidationContext();
        readonly List<HistoryEntry> _history = new List<HistoryEntry>();

        OpenConnectServer _server;
        ShotLogWriter _log;
        string _startError;
        OpenConnectShotDataOptions _lastStatus;
        DateTime? _lastMessageUtc;
        int _messages, _heartbeats, _validShots, _rejectedShots, _parseFailures;
        GUIStyle _style;

        public static string DefaultLogDirectory =>
            Application.isEditor
                ? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "shots"))
                : Path.Combine(Application.persistentDataPath, "ShotLogs");

        void OnEnable()
        {
            _server = new OpenConnectServer(port) { RespondToStatusMessages = respondToStatusMessages };
            _server.MessageReceived += _incoming.Enqueue;
            _server.Diagnostic += _diagnostics.Enqueue;
            try
            {
                _server.Start();
                _startError = null;
            }
            catch (SocketException e)
            {
                _startError = $"Could not listen on port {port}: {e.Message}";
                Debug.LogError($"[ShotFeedback] {_startError}");
            }

            if (writeShotLog)
            {
                _log = ShotLogWriter.CreateInDirectory(DefaultLogDirectory);
                Debug.Log($"[ShotFeedback] Logging messages to {_log.Path}");
            }
        }

        void OnDisable()
        {
            _server?.Dispose();
            _server = null;
            _log?.Dispose();
            _log = null;
        }

        void Update()
        {
            while (_diagnostics.TryDequeue(out var diagnostic))
                Debug.Log($"[ShotFeedback] {diagnostic}");
            while (_incoming.TryDequeue(out var message))
                Process(message);
        }

        void Process(ReceivedMessage received)
        {
            _messages++;
            _lastMessageUtc = received.ReceivedUtc;
            _log?.Write(received.ReceivedUtc, received.RemoteEndpoint, received.RawJson);

            if (!received.Parse.Success)
            {
                _parseFailures++;
                AddHistory($"PARSE FAILURE: {received.Parse.Error}", new List<ValidationIssue>(), true);
                Debug.LogWarning($"[ShotFeedback] Parse failure: {received.Parse.Error}\n{received.RawJson}");
                return;
            }

            var message = received.Parse.Message;
            var result = _validator.Validate(message, _context);
            if (message.ShotDataOptions != null)
                _lastStatus = message.ShotDataOptions;

            if (!result.IsShot)
            {
                if (result.Kind == ShotKind.Heartbeat) _heartbeats++;
                if (result.HasWarnings || result.HasErrors)
                    AddHistory(ShotFormatter.Summary(message, result), new List<ValidationIssue>(result.Issues), result.HasErrors);
                return;
            }

            if (result.HasErrors) _rejectedShots++;
            else
            {
                _validShots++;
                if (message.ShotNumber.HasValue)
                    _context.PreviousShotNumber = message.ShotNumber;
            }

            var summary = ShotFormatter.Summary(message, result);
            AddHistory(summary, new List<ValidationIssue>(result.Issues), result.HasErrors);

            string details = string.Join("\n", result.Issues);
            if (received.Parse.UnknownFields.Count > 0)
                details += $"\nUnknown fields: {string.Join(", ", received.Parse.UnknownFields)}";
            if (result.HasErrors) Debug.LogWarning($"[ShotFeedback] {summary}\n{details}");
            else Debug.Log($"[ShotFeedback] {summary}\n{details}");
        }

        void AddHistory(string summary, List<ValidationIssue> issues, bool failed)
        {
            _history.Insert(0, new HistoryEntry { Summary = summary, Issues = issues, Failed = failed });
            if (_history.Count > historySize)
                _history.RemoveAt(_history.Count - 1);
        }

        void SetExpectingPutt(bool expectingPutt)
        {
            _context.ExpectingPutt = expectingPutt;
            var club = expectingPutt ? ClubCodes.Putter : ClubCodes.Driver;
            bool sent = _server != null && _server.SendPlayerInfo(new PlayerInfo(Handedness.Right, club));
            Debug.Log($"[ShotFeedback] Expecting {(expectingPutt ? "putt" : "full swing")}; player info (club {club}) {(sent ? "sent" : "not sent: no client")}.");
        }

        void OnGUI()
        {
            if (_style == null)
                _style = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
            _style.fontSize = Mathf.Max(14, Screen.height / 45);

            GUILayout.BeginArea(new Rect(20, 20, Screen.width - 40, Screen.height - 40), GUI.skin.box);

            if (_startError != null)
                GUILayout.Label($"<color=#ff6666>{_startError}</color>", _style);
            else if (_server != null)
            {
                string connection = _server.IsClientConnected
                    ? $"<color=#66ff66>Connected</color> ({_server.ClientEndpoint})"
                    : $"<color=#ffaa00>Waiting for FS Golf</color> on port {_server.Port}";
                GUILayout.Label($"<b>Open Connect</b>: {connection}", _style);
            }

            string ready = _lastStatus == null ? "?" : Yes(_lastStatus.LaunchMonitorIsReady);
            string detected = _lastStatus == null ? "?" : Yes(_lastStatus.LaunchMonitorBallDetected);
            string last = _lastMessageUtc.HasValue ? $"{(DateTime.UtcNow - _lastMessageUtc.Value).TotalSeconds:0}s ago" : "never";
            GUILayout.Label($"Device ready: {ready}   Ball detected: {detected}   Last message: {last}", _style);
            GUILayout.Label($"Messages {_messages}   Heartbeats {_heartbeats}   Shots ok {_validShots}   Rejected {_rejectedShots}   Parse failures {_parseFailures}", _style);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_context.ExpectingPutt ? "Mode: PUTT (switch to full swing)" : "Mode: FULL SWING (switch to putt)", GUILayout.Height(_style.fontSize * 2)))
                SetExpectingPutt(!_context.ExpectingPutt);
            GUILayout.EndHorizontal();

            if (_log != null)
                GUILayout.Label($"Log: {_log.Path}", _style);

            GUILayout.Space(_style.fontSize);
            foreach (var entry in _history)
            {
                string color = entry.Failed ? "#ff6666" : "#ffffff";
                GUILayout.Label($"<color={color}><b>{entry.Summary}</b></color>", _style);
                foreach (var issue in entry.Issues)
                    GUILayout.Label($"    <color={IssueColor(issue.Severity)}>{issue}</color>", _style);
            }

            GUILayout.EndArea();
        }

        static string Yes(bool? value) => value.HasValue ? (value.Value ? "yes" : "no") : "?";

        static string IssueColor(IssueSeverity severity) =>
            severity == IssueSeverity.Error ? "#ff6666" : severity == IssueSeverity.Warning ? "#ffcc44" : "#aaaaaa";
    }
}
