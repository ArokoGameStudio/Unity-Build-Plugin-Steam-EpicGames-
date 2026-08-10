using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Aroko.StoreRelease.Editor.Configuration;
using UnityEditor;
using UnityEngine;

namespace Aroko.StoreRelease.Editor.Dashboard
{
    internal sealed class StoreReleaseBuildPage : IStoreReleaseDashboardPage
    {
        private string outputOverride = string.Empty;
        private string outputPathError = string.Empty;

        public string Title => "Build";

        public void OnActivated(StoreReleaseDashboardContext context)
        {
            if (string.IsNullOrWhiteSpace(context.Version))
            {
                context.Version = context.ProjectSettings.DefaultVersion;
            }
        }

        public void Draw(StoreReleaseDashboardContext context)
        {
            StoreBuildProfile profile = DrawRequest(context);
            string outputPath = ResolveOutputPath(context, profile);
            if (DrawOutputSettings(profile, outputPath))
            {
                outputPath = ResolveOutputPath(context, profile);
            }

            DrawResolvedOutput(outputPath);
            string operationScope = CreateOperationScope(context, profile, outputPath);
            bool editorBusy = EditorApplication.isCompiling ||
                              EditorApplication.isPlayingOrWillChangePlaymode ||
                              BuildPipeline.isBuildingPlayer;

            if (editorBusy)
            {
                EditorGUILayout.HelpBox(
                    "Build actions are paused while Unity is compiling, changing Play Mode, or building a player.",
                    MessageType.Warning);
            }
            else if (profile != null && string.IsNullOrWhiteSpace(outputPathError))
            {
                DrawReadiness(context, profile, outputPath);
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(outputPath)))
                {
                    DrawBuildActions(context, profile, outputPath, operationScope);
                }
            }

            if (context.LastReport != null &&
                string.Equals(context.LastOperationScope, operationScope, StringComparison.Ordinal))
            {
                DashboardGui.DrawReport(
                    context.LastReport,
                    context.LastOperationName,
                    context.LastOperationTarget,
                    context.LastOperationTime);
            }
        }

        private static StoreBuildProfile DrawRequest(StoreReleaseDashboardContext context)
        {
            DashboardGui.DrawSectionHeader("1. Choose a build target");
            List<StoreBuildProfile> profiles = context.ProjectSettings.Profiles
                .Where(profile => profile != null)
                .ToList();
            if (profiles.Count == 0)
            {
                DashboardGui.DrawEmptyState(
                    "No build profiles",
                    "The built-in Steam and Epic build profiles could not be loaded.",
                    "Open Setup",
                    () => context.NavigateTo(0));
                return null;
            }

            StoreBuildProfile current = context.ActiveProfile;
            if (current == null || !profiles.Contains(current))
            {
                current = profiles[0];
            }

            using (new EditorGUILayout.VerticalScope(DashboardGui.CardStyle))
            {
                int currentIndex = Mathf.Max(0, profiles.IndexOf(current));
                string[] labels = profiles
                    .Select(profile => string.IsNullOrWhiteSpace(profile.DisplayName)
                        ? profile.Id
                        : profile.DisplayName)
                    .ToArray();
                int selectedIndex = EditorGUILayout.Popup(
                    new GUIContent("Profile", "The store, channel, scenes, output, and packaging configuration."),
                    currentIndex,
                    labels);
                if (selectedIndex != currentIndex)
                {
                    current = profiles[selectedIndex];
                    context.ActiveProfile = current;
                }

                context.Version = EditorGUILayout.TextField(
                    new GUIContent("Version", "The player and output-folder version."),
                    context.Version).Trim();
                EditorGUILayout.LabelField("Store", current.Store.ToString());
                EditorGUILayout.LabelField("Channel", current.Channel.ToString());
                EditorGUILayout.LabelField(
                    "Build Type",
                    current.DevelopmentBuild ? "Development Player" : "Release Player");
            }

            return current;
        }

        private bool DrawOutputSettings(StoreBuildProfile profile, string outputPath)
        {
            DashboardGui.DrawSectionHeader("2. Confirm the build destination");
            EditorGUI.BeginChangeCheck();
            outputOverride = EditorGUILayout.TextField(
                new GUIContent(
                    "Output Override",
                    "Optional output folder. Relative paths start at the project root; {Store}, {Profile}, and {Version} are supported."),
                outputOverride);
            bool changed = EditorGUI.EndChangeCheck();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Browse...", GUILayout.Width(90f)))
                {
                    string selected = DashboardGui.BrowseForFolder("Choose Store Build Output", outputPath);
                    if (!string.IsNullOrWhiteSpace(selected))
                    {
                        outputOverride = DashboardGui.MakeProjectRelative(selected);
                        changed = true;
                        GUI.FocusControl(null);
                    }
                }

                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(outputOverride)))
                {
                    if (GUILayout.Button("Use Profile Template", GUILayout.Width(145f)))
                    {
                        outputOverride = string.Empty;
                        changed = true;
                        GUI.FocusControl(null);
                    }
                }

                GUILayout.FlexibleSpace();
                if (profile != null)
                {
                    EditorGUILayout.LabelField(
                        string.IsNullOrWhiteSpace(profile.OutputTemplate)
                            ? "Profile template missing"
                            : "Profile template configured",
                        EditorStyles.miniLabel,
                        GUILayout.Width(185f));
                }
            }

            return changed;
        }

        private void DrawResolvedOutput(string outputPath)
        {
            if (!string.IsNullOrWhiteSpace(outputPathError))
            {
                EditorGUILayout.HelpBox(outputPathError, MessageType.Error);
                return;
            }

            using (new EditorGUILayout.VerticalScope(DashboardGui.CardStyle))
            {
                EditorGUILayout.LabelField("Resolved Output", EditorStyles.boldLabel);
                if (string.IsNullOrWhiteSpace(outputPath))
                {
                    EditorGUILayout.LabelField("No output path is available.");
                    return;
                }

                DashboardGui.DrawPath("Folder", outputPath);
                EditorGUILayout.LabelField(
                    Directory.Exists(outputPath)
                        ? "The existing folder will be replaced by the next successful build."
                        : "The folder will be created by Build.",
                    EditorStyles.wordWrappedMiniLabel);
            }
        }

        private static void DrawReadiness(
            StoreReleaseDashboardContext context,
            StoreBuildProfile profile,
            string outputPath)
        {
            StoreOperationReport validationReport = ValidateLocally(context, profile, outputPath);
            int errors = validationReport.Issues.Count(issue =>
                issue != null && issue.Severity == StoreValidationSeverity.Error);
            int warnings = validationReport.Issues.Count(issue =>
                issue != null && issue.Severity == StoreValidationSeverity.Warning);
            EditorGUILayout.HelpBox(
                errors > 0
                    ? $"{errors} blocking configuration error(s) found. Validate to review them."
                    : warnings > 0
                        ? $"No blocking configuration errors found. Review {warnings} warning(s) before building."
                        : "Ready to validate or create a store-specific Windows x64 build.",
                errors > 0 ? MessageType.Error : warnings > 0 ? MessageType.Warning : MessageType.Info);
        }

        private static void DrawBuildActions(
            StoreReleaseDashboardContext context,
            StoreBuildProfile profile,
            string outputPath,
            string operationScope)
        {
            DashboardGui.DrawSectionHeader("3. Validate and build");
            EditorGUILayout.LabelField(
                "Validate checks configuration without creating a player. Build validates first, isolates the inactive store SDK, and creates the Windows x64 output for manual delivery.",
                EditorStyles.wordWrappedMiniLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Validate Configuration", GUILayout.Height(28f)))
                {
                    StoreOperationReport local = ValidateLocally(context, profile, outputPath);
                    StoreOperationReport report = local.Succeeded &&
                                                  StoreReleaseEditorHooks.ValidateHandler != null
                        ? StoreReleaseEditorHooks.Validate(CreateRequest(context, profile, outputPath))
                        : local;
                    RecordReport(context, report, "Configuration validation", operationScope, profile, outputPath);
                }

                if (GUILayout.Button("Build Windows x64", GUILayout.Height(28f)))
                {
                    StoreOperationReport local = ValidateLocally(context, profile, outputPath);
                    StoreOperationReport report = local.Succeeded
                        ? StoreReleaseEditorHooks.Build(CreateRequest(context, profile, outputPath))
                        : local;
                    RecordReport(context, report, "Windows x64 build", operationScope, profile, outputPath);
                    if (report.Succeeded && Directory.Exists(outputPath))
                    {
                        DashboardGui.Reveal(outputPath);
                    }
                }
            }
        }

        private static StoreOperationReport ValidateLocally(
            StoreReleaseDashboardContext context,
            StoreBuildProfile profile,
            string outputPath)
        {
            StoreOperationReport report = StoreOperationReport.Success(outputPath);
            foreach (StoreValidationIssue issue in StoreConfigurationValidator.ValidateProfile(
                         context.ProjectSettings,
                         profile))
            {
                report.AddIssue(issue);
            }

            if (string.IsNullOrWhiteSpace(context.Version))
            {
                report.AddIssue(new StoreValidationIssue(
                    StoreValidationSeverity.Error,
                    "SRT-BUILD-001",
                    "Build version is required."));
            }

            return report;
        }

        private static StoreBuildRequest CreateRequest(
            StoreReleaseDashboardContext context,
            StoreBuildProfile profile,
            string outputPath)
        {
            return new StoreBuildRequest(profile.Clone(), context.Version, outputPath);
        }

        private static string CreateOperationScope(
            StoreReleaseDashboardContext context,
            StoreBuildProfile profile,
            string outputPath)
        {
            return profile == null
                ? string.Empty
                : string.Join(
                    "|",
                    "build",
                    profile.Id,
                    profile.Store,
                    profile.Channel,
                    context.Version ?? string.Empty,
                    outputPath ?? string.Empty);
        }

        private static void RecordReport(
            StoreReleaseDashboardContext context,
            StoreOperationReport report,
            string operationName,
            string operationScope,
            StoreBuildProfile profile,
            string outputPath)
        {
            string target = profile.Store + " / " + profile.Channel + " / " +
                            profile.DisplayName + " / version " + context.Version +
                            "\nOutput: " + outputPath;
            context.SetLastReport(report, operationName, operationScope, target);
        }

        private string ResolveOutputPath(
            StoreReleaseDashboardContext context,
            StoreBuildProfile profile)
        {
            outputPathError = string.Empty;
            string rawPath = string.IsNullOrWhiteSpace(outputOverride)
                ? profile?.OutputTemplate
                : outputOverride;
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                return string.Empty;
            }

            try
            {
                string path = rawPath
                    .Replace("{Store}", profile == null ? "None" : profile.Store.ToString())
                    .Replace("{Profile}", SanitizePathPart(profile?.Id))
                    .Replace("{Version}", SanitizePathPart(context.Version));
                if (!Path.IsPathRooted(path))
                {
                    DirectoryInfo projectDirectory = Directory.GetParent(Application.dataPath);
                    if (projectDirectory == null)
                    {
                        throw new InvalidOperationException("The Unity project root could not be resolved.");
                    }

                    path = Path.Combine(projectDirectory.FullName, path);
                }

                return Path.GetFullPath(path);
            }
            catch (Exception exception)
            {
                outputPathError = "The output path is invalid: " + exception.Message;
                return string.Empty;
            }
        }

        private static string SanitizePathPart(string value)
        {
            string sanitized = string.IsNullOrWhiteSpace(value) ? "unspecified" : value.Trim();
            foreach (char invalidCharacter in Path.GetInvalidFileNameChars())
            {
                sanitized = sanitized.Replace(invalidCharacter, '_');
            }

            return sanitized;
        }
    }
}
