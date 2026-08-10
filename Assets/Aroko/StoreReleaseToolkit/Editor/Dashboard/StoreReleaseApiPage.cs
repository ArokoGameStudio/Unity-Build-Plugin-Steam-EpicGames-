using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Aroko.StoreRelease.Editor.Dashboard
{
    internal sealed class StoreReleaseApiPage : IStoreReleaseDashboardPage
    {
        private const string ApiPagePath =
            "Assets/Aroko/StoreReleaseToolkit/Editor/Dashboard/StoreReleaseApiPage.cs";

        private static readonly ApiDefinition[] Definitions =
        {
            new ApiDefinition(
                "achievement-unlock",
                "Unlock an achievement",
                "GAMEPLAY",
                "Persists immediately and unlocks through the Steam or Epic provider selected by " +
                "the build profile. Calls made before the provider is ready are retried.",
                "StoreAchievements.Unlock(string achievementId)",
                "using Aroko.StoreRelease.Runtime;\n\n" +
                "public static class GameAchievementIds\n" +
                "{\n" +
                "    public const string FirstWin = \"first_win\";\n" +
                "}\n\n" +
                "StoreAchievements.Unlock(GameAchievementIds.FirstWin);",
                @"\bStoreAchievements\s*\.\s*Unlock\s*\(",
                "Assets/Aroko/StoreReleaseToolkit/Runtime/StoreAchievements.cs"),
            new ApiDefinition(
                "get-profile",
                "Get a build profile",
                "EDITOR",
                "Loads a canonical Steam or Epic profile from the source-controlled toolkit settings.",
                "StoreReleaseProjectSettings.instance.GetProfile(string id)",
                "var settings = StoreReleaseProjectSettings.instance;\n" +
                "StoreBuildProfile profile = settings.GetProfile(\"steam-release\");\n" +
                "var request = new StoreBuildRequest(\n" +
                "    profile.Clone(), \"1.0.0\", @\"D:\\Builds\\Steam\");",
                @"\b(?:StoreReleaseProjectSettings\s*\.\s*instance|[A-Za-z_][A-Za-z0-9_]*)\s*\.\s*GetProfile\s*\(",
                "Assets/Aroko/StoreReleaseToolkit/Editor/Configuration/StoreReleaseProjectSettings.cs"),
            new ApiDefinition(
                "validate-build",
                "Validate a build request",
                "EDITOR",
                "Runs the same profile, SDK, output, scene, and store checks used before a dashboard build.",
                "StoreBuildCoordinator.Validate(StoreBuildRequest request)",
                "StoreOperationReport report = StoreBuildCoordinator.Validate(request);\n" +
                "if (!report.Succeeded)\n" +
                "    Debug.LogError(string.Join(\"\\n\", report.Issues));",
                @"\bStoreBuildCoordinator\s*\.\s*Validate\s*\(",
                "Assets/Aroko/StoreReleaseToolkit/Editor/Build/StoreBuildCoordinator.cs"),
            new ApiDefinition(
                "build",
                "Create a store build",
                "EDITOR",
                "Creates the selected Windows x64 Steam or Epic build locally. It never uploads or signs in.",
                "StoreBuildCoordinator.Build(StoreBuildRequest request)",
                "StoreOperationReport report = StoreBuildCoordinator.Build(request);\n" +
                "if (report.Succeeded)\n" +
                "    Debug.Log(\"Build ready: \" + report.OutputPath);",
                @"\bStoreBuildCoordinator\s*\.\s*Build\s*\(",
                "Assets/Aroko/StoreReleaseToolkit/Editor/Build/StoreBuildCoordinator.cs"),
            new ApiDefinition(
                "build-completed",
                "Observe completed operations",
                "EDITOR",
                "Receives the final report whenever StoreBuildCoordinator completes a build operation.",
                "StoreBuildCoordinator.OperationCompleted",
                "StoreBuildCoordinator.OperationCompleted += report =>\n" +
                "    Debug.Log(report.Succeeded ? \"Ready\" : \"Blocked\");",
                @"\bStoreBuildCoordinator\s*\.\s*OperationCompleted\s*(?:\+=|-=)",
                "Assets/Aroko/StoreReleaseToolkit/Editor/Build/StoreBuildCoordinator.cs"),
            new ApiDefinition(
                "validate-profile",
                "Validate one profile",
                "DIAGNOSTICS",
                "Checks one Steam or Epic profile without starting a player build.",
                "StoreConfigurationValidator.ValidateProfile(settings, profile)",
                "List<StoreValidationIssue> issues =\n" +
                "    StoreConfigurationValidator.ValidateProfile(settings, profile);",
                @"\bStoreConfigurationValidator\s*\.\s*ValidateProfile\s*\(",
                "Assets/Aroko/StoreReleaseToolkit/Editor/Configuration/StoreConfigurationValidator.cs"),
            new ApiDefinition(
                "validate-all",
                "Validate all settings",
                "DIAGNOSTICS",
                "Checks shared settings and every canonical build profile in one call.",
                "StoreConfigurationValidator.ValidateAll(settings)",
                "List<StoreValidationIssue> issues =\n" +
                "    StoreConfigurationValidator.ValidateAll(settings);",
                @"\bStoreConfigurationValidator\s*\.\s*ValidateAll\s*\(",
                "Assets/Aroko/StoreReleaseToolkit/Editor/Configuration/StoreConfigurationValidator.cs"),
            new ApiDefinition(
                "detect-sdks",
                "Detect installed store SDKs",
                "DIAGNOSTICS",
                "Returns the installed and certified compatibility state for Steamworks.NET and EOS.",
                "StoreSdkDetector.DetectAll()",
                "IReadOnlyList<StoreSdkStatus> sdks = StoreSdkDetector.DetectAll();\n" +
                "foreach (StoreSdkStatus sdk in sdks)\n" +
                "    Debug.Log($\"{sdk.DisplayName}: {sdk.Compatibility}\");",
                @"\bStoreSdkDetector\s*\.\s*DetectAll\s*\(",
                "Assets/Aroko/StoreReleaseToolkit/Editor/Configuration/StoreSdkDetector.cs")
        };

        private readonly Dictionary<string, UsageResult> usages =
            new Dictionary<string, UsageResult>(StringComparer.Ordinal);
        private readonly HashSet<string> expandedExamples =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "achievement-unlock"
            };
        private readonly HashSet<string> expandedUsages =
            new HashSet<string>(StringComparer.Ordinal);

        private DateTime lastRefreshTime;
        private string scanMessage = string.Empty;
        private bool hasScanned;
        private GUIStyle codeStyle;
        private GUIStyle usagePathStyle;
        private bool styleSkin;

        public string Title => "API";

        public void OnActivated(StoreReleaseDashboardContext context)
        {
            if (!hasScanned)
            {
                RefreshUsage(context);
            }
        }

        public void Draw(StoreReleaseDashboardContext context)
        {
            EnsureStyles();
            DrawOverview(context);
            DrawGroup(
                "Gameplay achievements",
                "Platform-neutral calls that automatically use the Steam or Epic provider in the selected build.",
                "GAMEPLAY");
            DrawGroup(
                "Editor build automation",
                "High-level APIs for custom editor tools and CI entry points. These APIs are editor-only.",
                "EDITOR");
            DrawGroup(
                "Validation and diagnostics",
                "Read-only checks for profiles, project settings, and installed vendor SDKs.",
                "DIAGNOSTICS");
        }

        private void DrawOverview(StoreReleaseDashboardContext context)
        {
            int usedApis = usages.Values.Count(result => result.TotalCount > 0);
            int totalReferences = usages.Values.Sum(result => result.TotalCount);

            using (new EditorGUILayout.VerticalScope(DashboardGui.CardStyle))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUILayout.VerticalScope())
                    {
                        EditorGUILayout.LabelField(
                            "Recommended API surface",
                            EditorStyles.boldLabel);
                        EditorGUILayout.LabelField(
                            "Copy working examples, inspect source, and see where each API is already used.",
                            EditorStyles.wordWrappedMiniLabel);
                    }

                    GUILayout.FlexibleSpace();
                    if (DashboardGui.SmallButton(
                            DashboardGui.Content(
                                "Full Reference",
                                "Open the complete offline API reference."),
                            92f))
                    {
                        StoreReleaseDocumentation.Open(
                            StoreReleaseDocumentation.Page.ApiReference);
                    }

                    if (DashboardGui.SmallButton(
                            DashboardGui.Content(
                                "Refresh Usage",
                                "Rescan production C# files under Assets."),
                            96f))
                    {
                        RefreshUsage(context);
                    }
                }

                GUILayout.Space(6f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    DashboardGui.DrawBadge(
                        Definitions.Length + " RECOMMENDED",
                        DashboardGui.BadgeTone.Info);
                    DashboardGui.DrawBadge(
                        usedApis + " USED",
                        usedApis > 0
                            ? DashboardGui.BadgeTone.Success
                            : DashboardGui.BadgeTone.Neutral);
                    DashboardGui.DrawBadge(
                        totalReferences + " REFERENCES",
                        totalReferences > 0
                            ? DashboardGui.BadgeTone.Success
                            : DashboardGui.BadgeTone.Neutral);
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField(
                        hasScanned
                            ? "Scanned " + lastRefreshTime.ToString("HH:mm:ss")
                            : "Not scanned",
                        EditorStyles.miniLabel,
                        GUILayout.Width(112f));
                }

                if (!string.IsNullOrWhiteSpace(scanMessage))
                {
                    GUILayout.Space(4f);
                    EditorGUILayout.HelpBox(scanMessage, MessageType.Warning);
                }
                else
                {
                    GUILayout.Space(3f);
                    EditorGUILayout.LabelField(
                        "Counts are static C# references. Tests, generated files, declarations, and this API page are excluded.",
                        EditorStyles.wordWrappedMiniLabel);
                }
            }
        }

        private void DrawGroup(string title, string description, string category)
        {
            DashboardGui.DrawSectionHeader(title, description);
            foreach (ApiDefinition definition in Definitions.Where(
                         item => string.Equals(
                             item.Category,
                             category,
                             StringComparison.Ordinal)))
            {
                DrawApiCard(definition);
            }
        }

        private void DrawApiCard(ApiDefinition definition)
        {
            usages.TryGetValue(definition.Id, out UsageResult usage);
            usage = usage ?? UsageResult.Empty;

            using (new EditorGUILayout.VerticalScope(DashboardGui.CardStyle))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(definition.Title, EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    DashboardGui.DrawBadge(
                        definition.Category,
                        definition.Category == "GAMEPLAY"
                            ? DashboardGui.BadgeTone.Success
                            : definition.Category == "EDITOR"
                                ? DashboardGui.BadgeTone.Info
                                : DashboardGui.BadgeTone.Neutral);
                    DashboardGui.DrawBadge(
                        usage.TotalCount == 0
                            ? "UNUSED"
                            : usage.TotalCount + (usage.TotalCount == 1 ? " USE" : " USES"),
                        usage.TotalCount == 0
                            ? DashboardGui.BadgeTone.Warning
                            : DashboardGui.BadgeTone.Success);
                }

                EditorGUILayout.LabelField(
                    definition.Description,
                    EditorStyles.wordWrappedMiniLabel);
                GUILayout.Space(4f);
                DrawCopyRow("Signature", definition.Signature);

                using (new EditorGUILayout.HorizontalScope())
                {
                    bool showExample = expandedExamples.Contains(definition.Id);
                    bool nextShowExample = EditorGUILayout.Foldout(
                        showExample,
                        "Example",
                        true);
                    SetExpanded(expandedExamples, definition.Id, nextShowExample);
                    GUILayout.FlexibleSpace();
                    if (DashboardGui.SmallButton(
                            DashboardGui.Content(
                                "Copy Example",
                                "Copy this complete example to the clipboard."),
                            88f))
                    {
                        EditorGUIUtility.systemCopyBuffer = definition.Example;
                    }

                    if (DashboardGui.SmallButton(
                            DashboardGui.Content(
                                "Open Source",
                                "Open the API declaration in the code editor."),
                            82f))
                    {
                        OpenAsset(definition.SourcePath, 0);
                    }
                }

                if (expandedExamples.Contains(definition.Id))
                {
                    EditorGUILayout.SelectableLabel(
                        definition.Example,
                        codeStyle,
                        GUILayout.MinHeight(CodeHeight(definition.Example)));
                }

                DrawUsage(definition, usage);
            }
        }

        private void DrawCopyRow(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    label,
                    EditorStyles.miniLabel,
                    GUILayout.Width(64f));
                EditorGUILayout.SelectableLabel(
                    value,
                    codeStyle,
                    GUILayout.Height(EditorGUIUtility.singleLineHeight + 5f));
                if (DashboardGui.SmallButton(
                        DashboardGui.Content("Copy", "Copy the API signature."),
                        48f))
                {
                    EditorGUIUtility.systemCopyBuffer = value;
                }
            }
        }

        private void DrawUsage(ApiDefinition definition, UsageResult usage)
        {
            string summary = usage.TotalCount == 0
                ? "No production references found"
                : usage.TotalCount +
                  (usage.TotalCount == 1 ? " reference" : " references") +
                  " in " + usage.Locations.Count +
                  (usage.Locations.Count == 1 ? " file" : " files");

            using (new EditorGUILayout.HorizontalScope())
            {
                bool showUsage = expandedUsages.Contains(definition.Id);
                using (new EditorGUI.DisabledScope(usage.Locations.Count == 0))
                {
                    bool nextShowUsage = EditorGUILayout.Foldout(
                        showUsage,
                        "Usage  •  " + summary,
                        true);
                    SetExpanded(expandedUsages, definition.Id, nextShowUsage);
                }
            }

            if (!expandedUsages.Contains(definition.Id))
            {
                return;
            }

            foreach (UsageLocation location in usage.Locations)
            {
                using (new EditorGUILayout.HorizontalScope(DashboardGui.CompactCardStyle))
                {
                    EditorGUILayout.LabelField(
                        location.AssetPath + ":" + location.FirstLine,
                        usagePathStyle);
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField(
                        location.Count + (location.Count == 1 ? " match" : " matches"),
                        EditorStyles.miniLabel,
                        GUILayout.Width(72f));
                    if (DashboardGui.SmallButton(
                            DashboardGui.Content(
                                "Open",
                                "Open the first matching line in this source file."),
                            48f))
                    {
                        OpenAsset(location.AssetPath, location.FirstLine);
                    }
                }
            }
        }

        private void RefreshUsage(StoreReleaseDashboardContext context)
        {
            usages.Clear();
            scanMessage = string.Empty;
            try
            {
                ApiUsageScan scan = StoreReleaseApiUsageScanner.Scan(
                    Definitions,
                    ApiPagePath);
                foreach (KeyValuePair<string, UsageResult> pair in scan.Results)
                {
                    usages[pair.Key] = pair.Value;
                }

                if (scan.SkippedFiles > 0)
                {
                    scanMessage = "Usage scan skipped " + scan.SkippedFiles +
                                  " unreadable C# file(s).";
                }
            }
            catch (Exception exception)
            {
                scanMessage = "Usage scan could not complete: " + exception.Message;
            }

            hasScanned = true;
            lastRefreshTime = DateTime.Now;
            GUI.FocusControl(null);
            context?.Repaint();
        }

        private void EnsureStyles()
        {
            bool currentSkin = EditorGUIUtility.isProSkin;
            if (codeStyle != null && styleSkin == currentSkin)
            {
                return;
            }

            styleSkin = currentSkin;
            codeStyle = new GUIStyle(EditorStyles.textArea)
            {
                fontSize = 11,
                wordWrap = true,
                padding = new RectOffset(7, 7, 4, 4)
            };
            usagePathStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                clipping = TextClipping.Clip
            };
        }

        private static void SetExpanded(
            ISet<string> set,
            string id,
            bool expanded)
        {
            if (expanded)
            {
                set.Add(id);
            }
            else
            {
                set.Remove(id);
            }
        }

        private static float CodeHeight(string value)
        {
            int lines = string.IsNullOrEmpty(value)
                ? 1
                : value.Count(character => character == '\n') + 1;
            return Mathf.Clamp(lines * 16f + 10f, 42f, 150f);
        }

        private static void OpenAsset(string assetPath, int line)
        {
            Object asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (asset != null)
            {
                AssetDatabase.OpenAsset(asset, line > 0 ? line : -1);
                return;
            }

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (!string.IsNullOrWhiteSpace(projectRoot))
            {
                DashboardGui.Reveal(Path.Combine(projectRoot, assetPath));
            }
        }
    }

    internal sealed class ApiDefinition
    {
        public ApiDefinition(
            string id,
            string title,
            string category,
            string description,
            string signature,
            string example,
            string usagePattern,
            string sourcePath)
        {
            Id = id;
            Title = title;
            Category = category;
            Description = description;
            Signature = signature;
            Example = example;
            UsageRegex = new Regex(
                usagePattern,
                RegexOptions.Compiled | RegexOptions.CultureInvariant);
            SourcePath = sourcePath;
        }

        public string Id { get; }
        public string Title { get; }
        public string Category { get; }
        public string Description { get; }
        public string Signature { get; }
        public string Example { get; }
        public Regex UsageRegex { get; }
        public string SourcePath { get; }
    }

    internal sealed class ApiUsageScan
    {
        public ApiUsageScan(
            IReadOnlyDictionary<string, UsageResult> results,
            int skippedFiles)
        {
            Results = results;
            SkippedFiles = skippedFiles;
        }

        public IReadOnlyDictionary<string, UsageResult> Results { get; }
        public int SkippedFiles { get; }
    }

    internal sealed class UsageResult
    {
        public static readonly UsageResult Empty =
            new UsageResult(Array.Empty<UsageLocation>());

        public UsageResult(IReadOnlyList<UsageLocation> locations)
        {
            Locations = locations ?? Array.Empty<UsageLocation>();
            TotalCount = Locations.Sum(location => location.Count);
        }

        public int TotalCount { get; }
        public IReadOnlyList<UsageLocation> Locations { get; }
    }

    internal sealed class UsageLocation
    {
        public UsageLocation(
            string assetPath,
            int count,
            int firstLine)
        {
            AssetPath = assetPath;
            Count = count;
            FirstLine = firstLine;
        }

        public string AssetPath { get; }
        public int Count { get; }
        public int FirstLine { get; }
    }

    internal static class StoreReleaseApiUsageScanner
    {
        public static ApiUsageScan Scan(
            IReadOnlyList<ApiDefinition> definitions,
            string excludedAssetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ??
                                 throw new InvalidOperationException(
                                     "The Unity project root could not be resolved.");
            var locations = definitions.ToDictionary(
                definition => definition.Id,
                definition => new List<UsageLocation>(),
                StringComparer.Ordinal);
            int skippedFiles = 0;

            foreach (string absolutePath in EnumerateSourceFiles(projectRoot))
            {
                string assetPath = MakeProjectRelative(projectRoot, absolutePath);
                if (ShouldExclude(assetPath, excludedAssetPath))
                {
                    continue;
                }

                string source;
                try
                {
                    source = File.ReadAllText(absolutePath);
                }
                catch
                {
                    skippedFiles++;
                    continue;
                }

                foreach (ApiDefinition definition in definitions)
                {
                    MatchCollection matches = definition.UsageRegex.Matches(source);
                    if (matches.Count == 0)
                    {
                        continue;
                    }

                    locations[definition.Id].Add(
                        new UsageLocation(
                            assetPath,
                            matches.Count,
                            GetLineNumber(source, matches[0].Index)));
                }
            }

            var results = locations.ToDictionary(
                pair => pair.Key,
                pair => new UsageResult(
                    pair.Value
                        .OrderBy(location => location.AssetPath, StringComparer.OrdinalIgnoreCase)
                        .ToArray()),
                StringComparer.Ordinal);
            return new ApiUsageScan(results, skippedFiles);
        }

        internal static int CountMatches(string source, string usagePattern)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(usagePattern))
            {
                return 0;
            }

            return Regex.Matches(
                source,
                usagePattern,
                RegexOptions.CultureInvariant).Count;
        }

        private static IEnumerable<string> EnumerateSourceFiles(string projectRoot)
        {
            string[] roots =
            {
                Path.Combine(projectRoot, "Assets")
            };
            foreach (string root in roots)
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                foreach (string path in Directory.EnumerateFiles(
                             root,
                             "*.cs",
                             SearchOption.AllDirectories))
                {
                    yield return path;
                }
            }
        }

        private static bool ShouldExclude(
            string assetPath,
            string excludedAssetPath)
        {
            string normalized = assetPath.Replace('\\', '/');
            return string.Equals(
                       normalized,
                       excludedAssetPath,
                       StringComparison.OrdinalIgnoreCase) ||
                   normalized.IndexOf("/Tests/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   normalized.IndexOf("/Generated/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string MakeProjectRelative(
            string projectRoot,
            string absolutePath)
        {
            string root = Path.GetFullPath(projectRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                          Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(absolutePath);
            return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? fullPath.Substring(root.Length).Replace('\\', '/')
                : fullPath.Replace('\\', '/');
        }

        private static int GetLineNumber(string source, int characterIndex)
        {
            int line = 1;
            int safeLength = Mathf.Clamp(characterIndex, 0, source.Length);
            for (int index = 0; index < safeLength; index++)
            {
                if (source[index] == '\n')
                {
                    line++;
                }
            }

            return line;
        }
    }
}
