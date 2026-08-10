using System;
using System.Collections.Generic;
using System.Linq;
using Aroko.StoreRelease.Editor.Configuration;
using UnityEditor;
using UnityEngine;

namespace Aroko.StoreRelease.Editor.Dashboard
{
    internal sealed class StoreReleaseDiagnosticsPage : IStoreReleaseDashboardPage
    {
        private List<StoreValidationIssue> issues = new List<StoreValidationIssue>();
        private string lastRefreshTime = string.Empty;

        public string Title => "Diagnostics";

        public void OnActivated(StoreReleaseDashboardContext context)
        {
            Refresh(context);
        }

        public void Draw(StoreReleaseDashboardContext context)
        {
            DrawSummary(context);
            DrawIssues();
        }

        private void DrawSummary(StoreReleaseDashboardContext context)
        {
            DashboardGui.DrawSectionHeader(
                "Build readiness",
                "Diagnostics cover shared settings, profiles, Epic packaging values, and installed vendor SDKs.");

            int errors = issues.Count(issue => issue.Severity == StoreValidationSeverity.Error);
            int warnings = issues.Count(issue => issue.Severity == StoreValidationSeverity.Warning);
            using (new EditorGUILayout.VerticalScope(DashboardGui.CardStyle))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        errors > 0 ? "Build blockers found" : "Configuration scanned",
                        EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    DashboardGui.DrawBadge(
                        errors > 0 ? errors + " ERROR(S)" : "READY",
                        errors > 0 ? DashboardGui.BadgeTone.Danger : DashboardGui.BadgeTone.Success);
                    if (warnings > 0)
                    {
                        DashboardGui.DrawBadge(
                            warnings + " WARNING(S)",
                            DashboardGui.BadgeTone.Warning);
                    }
                }

                StoreBuildProfile profile = context.ActiveProfile;
                DashboardGui.DrawKeyValue(
                    "Active Profile",
                    profile == null ? "None" : profile.DisplayName);
                DashboardGui.DrawKeyValue("Last Scan", lastRefreshTime);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Refresh Diagnostics", GUILayout.Width(145f)))
                    {
                        Refresh(context);
                    }

                    if (GUILayout.Button("Open Setup", GUILayout.Width(100f)))
                    {
                        context.NavigateTo(0);
                    }

                    if (GUILayout.Button("Open Build", GUILayout.Width(100f)))
                    {
                        context.NavigateTo(1);
                    }
                }
            }
        }

        private void DrawIssues()
        {
            DashboardGui.DrawSectionHeader("Diagnostic results");
            if (issues.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No configuration or SDK issues were found.",
                    MessageType.Info);
                return;
            }

            foreach (StoreValidationIssue issue in issues
                         .OrderByDescending(item => item.Severity)
                         .ThenBy(item => item.Code, StringComparer.Ordinal))
            {
                using (new EditorGUILayout.VerticalScope(DashboardGui.CompactCardStyle))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(issue.Code, EditorStyles.boldLabel);
                        GUILayout.FlexibleSpace();
                        DashboardGui.DrawBadge(
                            issue.Severity.ToString().ToUpperInvariant(),
                            DashboardGui.ToBadgeTone(issue.Severity));
                    }

                    EditorGUILayout.LabelField(issue.Message, EditorStyles.wordWrappedMiniLabel);
                    if (!string.IsNullOrWhiteSpace(issue.Context))
                    {
                        EditorGUILayout.LabelField(
                            "Context: " + issue.Context,
                            EditorStyles.wordWrappedMiniLabel);
                    }
                }
            }
        }

        private void Refresh(StoreReleaseDashboardContext context)
        {
            try
            {
                issues = StoreConfigurationValidator.ValidateAll(context.ProjectSettings);
                foreach (StoreSdkStatus status in StoreSdkDetector.DetectAll())
                {
                    bool required = context.ActiveProfile != null &&
                                    ((context.ActiveProfile.Store == StorePlatform.Steam &&
                                      string.Equals(
                                          status.PackageName,
                                          StoreSdkDetector.SteamworksPackageName,
                                          StringComparison.OrdinalIgnoreCase)) ||
                                     (context.ActiveProfile.Store == StorePlatform.Epic &&
                                      string.Equals(
                                          status.PackageName,
                                          StoreSdkDetector.EosPackageName,
                                          StringComparison.OrdinalIgnoreCase)));
                    StoreValidationSeverity severity = status.Compatibility == StoreSdkCompatibility.Supported
                        ? StoreValidationSeverity.Info
                        : status.Compatibility == StoreSdkCompatibility.Missing && required
                            ? StoreValidationSeverity.Error
                            : StoreValidationSeverity.Warning;
                    issues.Add(new StoreValidationIssue(
                        severity,
                        "SRT-SDK",
                        status.DisplayName + ": " +
                        (string.IsNullOrWhiteSpace(status.InstalledVersion)
                            ? "not installed"
                            : status.InstalledVersion) +
                        " (" + status.Compatibility + ")."));
                }
            }
            catch (Exception exception)
            {
                issues = new List<StoreValidationIssue>
                {
                    new StoreValidationIssue(
                        StoreValidationSeverity.Error,
                        "SRT-DIAG-001",
                        "Diagnostics could not complete.",
                        exception.Message)
                };
            }

            lastRefreshTime = DateTime.Now.ToString("HH:mm:ss");
        }
    }
}
