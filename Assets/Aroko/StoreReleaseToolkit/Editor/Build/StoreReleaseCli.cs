using System;
using System.Collections.Generic;
using System.IO;
using Aroko.StoreRelease.Editor.Configuration;
using UnityEditor;
using UnityEngine;

namespace Aroko.StoreRelease.Editor
{
    public static class StoreReleaseCli
    {
        public static void Run()
        {
            int exitCode = 1;
            try
            {
                Dictionary<string, string> arguments = ParseArguments(
                    Environment.GetCommandLineArgs());
                string action = Get(arguments, "-srtAction", "validate").ToLowerInvariant();
                string profileId = Get(arguments, "-srtProfile", string.Empty);
                string version = Get(
                    arguments,
                    "-srtVersion",
                    StoreReleaseProjectSettings.instance.DefaultVersion);
                string reportOutput = Get(arguments, "-srtReport", string.Empty);
                StoreReleaseProjectSettings settings = StoreReleaseProjectSettings.instance;
                settings.EnsureDefaults();
                StoreBuildProfile profile = settings.GetProfile(profileId);
                if (profile == null)
                {
                    throw new ArgumentException(
                        "Unknown or missing -srtProfile value: " + profileId);
                }

                var request = new StoreBuildRequest(profile.Clone(), version, string.Empty);
                StoreOperationReport report;
                switch (action)
                {
                    case "validate":
                        report = Build.StoreBuildCoordinator.Validate(request);
                        break;
                    case "build":
                        report = Build.StoreBuildCoordinator.Build(request);
                        break;
                    default:
                        throw new ArgumentException(
                            "Unsupported -srtAction. Use validate or build.");
                }

                if (!string.IsNullOrWhiteSpace(reportOutput))
                {
                    string fullReport = Path.GetFullPath(reportOutput);
                    Directory.CreateDirectory(Path.GetDirectoryName(fullReport) ??
                                              throw new InvalidOperationException(
                                                  "Report output has no directory."));
                    File.WriteAllText(fullReport, JsonUtility.ToJson(report, true));
                }

                exitCode = report.Succeeded ? 0 : 1;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                exitCode = 1;
            }
            finally
            {
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(exitCode);
                }
            }
        }

        private static Dictionary<string, string> ParseArguments(string[] args)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < args.Length; index++)
            {
                if (!args[index].StartsWith("-srt", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                result[args[index]] = index + 1 < args.Length ? args[index + 1] : string.Empty;
                index++;
            }

            return result;
        }

        private static string Get(
            IReadOnlyDictionary<string, string> arguments,
            string key,
            string fallback)
        {
            return arguments.TryGetValue(key, out string value) &&
                   !string.IsNullOrWhiteSpace(value)
                ? value
                : fallback;
        }
    }
}
