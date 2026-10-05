using System;
using System.IO;
using System.Linq;
using OneWood.Dev;
using OneWood.LaunchMonitor.Diagnostics;
using UnityEditor;
using UnityEngine;

namespace OneWood.Editor
{
    /// <summary>
    /// Validates recorded shot logs (from the Shot Feedback Monitor or Tools/open_connect.py capture).
    ///
    /// Batch mode:
    ///   Unity -batchmode -projectPath OneWood -executeMethod OneWood.Editor.ShotLogValidation.RunFromCommandLine
    ///         -shotLog path/to/log.jsonl [-shotLog another.jsonl] [-expectPutts] [-report out.txt]
    /// Exits 0 when every log passes, 1 when any fails, 2 on usage errors.
    /// </summary>
    public static class ShotLogValidation
    {
        [MenuItem("One Wood/Validate Shot Log...")]
        static void ValidateFromMenu()
        {
            var directory = ShotFeedbackMonitor.DefaultLogDirectory;
            Directory.CreateDirectory(directory);
            var path = EditorUtility.OpenFilePanel("Validate shot log", directory, "jsonl");
            if (string.IsNullOrEmpty(path)) return;

            var report = ShotLogReport.Build(ShotLogFile.Read(path));
            var text = report.ToText();
            if (report.Passed) Debug.Log($"Shot log {path}\n{text}");
            else Debug.LogWarning($"Shot log {path}\n{text}");

            EditorUtility.DisplayDialog("Shot log validation",
                $"{(report.Passed ? "PASS" : "FAIL")}\n\n{report.MessageCount} messages, {report.ShotCount} shots, " +
                $"{report.RejectedShots} rejected, {report.ParseFailures} parse failures.\n\nFull report in the Console.",
                "OK");
        }

        public static void RunFromCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            var logs = args.Select((a, i) => (a, i)).Where(p => p.a == "-shotLog" && p.i + 1 < args.Length)
                .Select(p => args[p.i + 1]).ToList();
            bool expectPutts = args.Contains("-expectPutts");
            int reportIndex = Array.IndexOf(args, "-report");
            string reportPath = reportIndex >= 0 && reportIndex + 1 < args.Length ? args[reportIndex + 1] : null;

            if (logs.Count == 0)
            {
                Console.Error.WriteLine("Usage: -executeMethod OneWood.Editor.ShotLogValidation.RunFromCommandLine -shotLog <file.jsonl> [-expectPutts] [-report <out.txt>]");
                EditorApplication.Exit(2);
                return;
            }

            bool allPassed = true;
            var output = new System.Text.StringBuilder();
            foreach (var argument in logs)
            {
                var log = ResolvePath(argument);
                if (!File.Exists(log))
                {
                    output.AppendLine($"=== {log}\nRESULT: FAIL (file not found)\n");
                    allPassed = false;
                    continue;
                }
                var report = ShotLogReport.Build(ShotLogFile.Read(log), expectingPutt: expectPutts);
                allPassed &= report.Passed;
                output.AppendLine($"=== {log}").AppendLine(report.ToText());
            }

            var text = output.ToString();
            Console.WriteLine(text);
            Debug.Log(text);
            if (reportPath != null)
            {
                if (!Path.IsPathRooted(reportPath))
                    reportPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", reportPath));
                File.WriteAllText(reportPath, text);
            }
            EditorApplication.Exit(allPassed ? 0 : 1);
        }

        /// <summary>Unity runs with the project folder as its working directory; also accept paths relative to the repo root.</summary>
        static string ResolvePath(string path)
        {
            if (Path.IsPathRooted(path) || File.Exists(path)) return path;
            var repoRelative = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", path));
            return File.Exists(repoRelative) ? repoRelative : path;
        }
    }
}
