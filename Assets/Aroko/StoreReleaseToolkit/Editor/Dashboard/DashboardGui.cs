using System;
using System.IO;
using Aroko.StoreRelease.Editor.Configuration;
using UnityEditor;
using UnityEngine;

namespace Aroko.StoreRelease.Editor.Dashboard
{
    internal static class DashboardGui
    {
        internal enum BadgeTone
        {
            Neutral,
            Info,
            Success,
            Warning,
            Danger
        }

        private static GUIStyle cardStyle;
        private static GUIStyle compactCardStyle;
        private static GUIStyle pageTitleStyle;
        private static GUIStyle pageDescriptionStyle;
        private static GUIStyle sectionTitleStyle;
        private static GUIStyle sectionDescriptionStyle;
        private static GUIStyle badgeStyle;
        private static GUIStyle smallButtonStyle;
        private static bool stylesInitialized;
        private static bool styleSkin;

        public static GUIStyle CardStyle
        {
            get
            {
                EnsureStyles();
                return cardStyle;
            }
        }

        public static GUIStyle CompactCardStyle
        {
            get
            {
                EnsureStyles();
                return compactCardStyle;
            }
        }

        public static GUIStyle PageTitleStyle
        {
            get
            {
                EnsureStyles();
                return pageTitleStyle;
            }
        }

        public static GUIStyle PageDescriptionStyle
        {
            get
            {
                EnsureStyles();
                return pageDescriptionStyle;
            }
        }

        public static GUIContent Content(string text, string tooltip)
        {
            return new GUIContent(text, tooltip);
        }

        public static void DrawPageHeader(
            string eyebrow,
            string title,
            string description)
        {
            GUILayout.Space(2f);
            EditorGUILayout.LabelField(
                eyebrow == null ? string.Empty : eyebrow.ToUpperInvariant(),
                EditorStyles.miniLabel);
            EditorGUILayout.LabelField(title, PageTitleStyle);
            EditorGUILayout.LabelField(description, PageDescriptionStyle);
            GUILayout.Space(10f);
        }

        public static void DrawSectionHeader(string title)
        {
            DrawSectionHeader(title, string.Empty);
        }

        public static void DrawSectionHeader(string title, string description)
        {
            EnsureStyles();
            GUILayout.Space(10f);
            EditorGUILayout.LabelField(title, sectionTitleStyle);
            if (!string.IsNullOrWhiteSpace(description))
            {
                EditorGUILayout.LabelField(description, sectionDescriptionStyle);
                GUILayout.Space(2f);
            }
        }

        public static void DrawHelpCard(
            string title,
            string message,
            MessageType messageType = MessageType.Info)
        {
            Color accent = GetMessageColor(messageType);
            using (new EditorGUILayout.VerticalScope(CardStyle))
            {
                Rect titleRect = EditorGUILayout.GetControlRect(false, 20f);
                EditorGUI.DrawRect(
                    new Rect(titleRect.x, titleRect.y, 3f, titleRect.height),
                    accent);
                GUI.Label(
                    new Rect(
                        titleRect.x + 10f,
                        titleRect.y,
                        titleRect.width - 10f,
                        titleRect.height),
                    title,
                    sectionTitleStyle);
                EditorGUILayout.LabelField(message, sectionDescriptionStyle);
            }
        }

        public static void DrawEmptyState(
            string title,
            string message,
            string actionLabel = null,
            Action action = null)
        {
            using (new EditorGUILayout.VerticalScope(CardStyle))
            {
                GUILayout.Space(6f);
                EditorGUILayout.LabelField(title, sectionTitleStyle);
                EditorGUILayout.LabelField(message, sectionDescriptionStyle);
                if (!string.IsNullOrWhiteSpace(actionLabel) && action != null)
                {
                    GUILayout.Space(4f);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button(actionLabel, GUILayout.Width(150f)))
                        {
                            action();
                        }

                        GUILayout.FlexibleSpace();
                    }
                }

                GUILayout.Space(4f);
            }
        }

        public static void DrawBadge(string text, BadgeTone tone)
        {
            EnsureStyles();
            Color previousBackground = GUI.backgroundColor;
            Color previousContent = GUI.contentColor;
            GUI.backgroundColor = GetBadgeColor(tone);
            GUI.contentColor = GetBadgeTextColor(tone);
            GUILayout.Label(text, badgeStyle);
            GUI.backgroundColor = previousBackground;
            GUI.contentColor = previousContent;
        }

        public static bool SmallButton(GUIContent content, float width = 70f)
        {
            EnsureStyles();
            return GUILayout.Button(content, smallButtonStyle, GUILayout.Width(width));
        }

        public static void DrawKeyValue(
            string label,
            string value,
            string tooltip = null,
            BadgeTone? badgeTone = null)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    Content(label, tooltip),
                    EditorStyles.miniLabel,
                    GUILayout.Width(110f));
                if (badgeTone.HasValue)
                {
                    DrawBadge(value, badgeTone.Value);
                    GUILayout.FlexibleSpace();
                }
                else
                {
                    EditorGUILayout.SelectableLabel(
                        value ?? string.Empty,
                        EditorStyles.label,
                        GUILayout.Height(EditorGUIUtility.singleLineHeight));
                }
            }
        }

        public static void DrawReport(
            StoreOperationReport report,
            string operationName = "",
            string operationTarget = "",
            DateTime? operationTime = null)
        {
            if (report == null)
            {
                return;
            }

            int errorCount = 0;
            int warningCount = 0;
            int infoCount = 0;
            foreach (StoreValidationIssue issue in report.Issues)
            {
                if (issue == null)
                {
                    continue;
                }

                switch (issue.Severity)
                {
                    case StoreValidationSeverity.Error:
                        errorCount++;
                        break;
                    case StoreValidationSeverity.Warning:
                        warningCount++;
                        break;
                    default:
                        infoCount++;
                        break;
                }
            }

            DrawSectionHeader(
                "Latest operation",
                "The newest validation or build result.");
            using (new EditorGUILayout.VerticalScope(CardStyle))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        string.IsNullOrWhiteSpace(operationName)
                            ? report.Succeeded
                                ? "Operation completed"
                                : "Action required"
                            : operationName,
                        sectionTitleStyle);
                    GUILayout.FlexibleSpace();
                    DrawBadge(
                        report.Succeeded ? "SUCCESS" : "FAILED",
                        report.Succeeded ? BadgeTone.Success : BadgeTone.Danger);
                }

                EditorGUILayout.LabelField(
                    ReportSummary(report.Succeeded, errorCount, warningCount, infoCount),
                    sectionDescriptionStyle);
                if (!string.IsNullOrWhiteSpace(operationTarget))
                {
                    GUILayout.Space(3f);
                    EditorGUILayout.LabelField(
                        "Target",
                        EditorStyles.miniBoldLabel);
                    EditorGUILayout.LabelField(
                        operationTarget,
                        sectionDescriptionStyle);
                }

                if (operationTime.HasValue)
                {
                    EditorGUILayout.LabelField(
                        "Recorded " + operationTime.Value.ToString("yyyy-MM-dd HH:mm:ss"),
                        EditorStyles.miniLabel);
                }

                if (errorCount + warningCount + infoCount > 0)
                {
                    GUILayout.Space(4f);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (errorCount > 0)
                        {
                            DrawBadge(
                                errorCount + (errorCount == 1 ? " error" : " errors"),
                                BadgeTone.Danger);
                        }

                        if (warningCount > 0)
                        {
                            DrawBadge(
                                warningCount + (warningCount == 1 ? " warning" : " warnings"),
                                BadgeTone.Warning);
                        }

                        if (infoCount > 0)
                        {
                            DrawBadge(
                                infoCount + (infoCount == 1 ? " note" : " notes"),
                                BadgeTone.Info);
                        }

                        GUILayout.FlexibleSpace();
                    }
                }
            }

            foreach (StoreValidationIssue issue in report.Issues)
            {
                if (issue == null)
                {
                    continue;
                }

                using (new EditorGUILayout.VerticalScope(CompactCardStyle))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(
                            string.IsNullOrWhiteSpace(issue.Code)
                                ? issue.Severity.ToString()
                                : issue.Code,
                            EditorStyles.boldLabel);
                        GUILayout.FlexibleSpace();
                        DrawBadge(
                            issue.Severity.ToString().ToUpperInvariant(),
                            ToBadgeTone(issue.Severity));
                    }

                    EditorGUILayout.LabelField(issue.Message, sectionDescriptionStyle);
                    if (!string.IsNullOrWhiteSpace(issue.Context))
                    {
                        GUILayout.Space(2f);
                        EditorGUILayout.LabelField(
                            "Context: " + issue.Context,
                            EditorStyles.wordWrappedMiniLabel);
                    }
                }
            }

            DrawPath("Output", report.OutputPath);
            DrawPath("Log", report.LogPath);
        }

        public static void DrawPath(string label, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    label,
                    EditorStyles.miniLabel,
                    GUILayout.Width(105f));
                EditorGUILayout.SelectableLabel(
                    path,
                    EditorStyles.textField,
                    GUILayout.Height(EditorGUIUtility.singleLineHeight));
                if (SmallButton(
                        Content("Copy", "Copy this path to the clipboard."),
                        48f))
                {
                    EditorGUIUtility.systemCopyBuffer = path;
                }

                bool exists = PathExists(path);
                bool isFolder = Directory.Exists(path);
                using (new EditorGUI.DisabledScope(!exists))
                {
                    if (SmallButton(
                            Content(
                                isFolder ? "Open Folder" : "Show",
                                exists
                                    ? "Reveal this file or folder."
                                    : "This path does not exist yet."),
                            isFolder ? 82f : 48f))
                    {
                        Reveal(path);
                    }
                }
            }
        }

        public static void Reveal(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                string fullPath = Path.GetFullPath(path);
                if (File.Exists(fullPath) || Directory.Exists(fullPath))
                {
                    EditorUtility.RevealInFinder(fullPath);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "Store Release Toolkit could not reveal the path: " +
                    exception.Message);
            }
        }

        public static string BrowseForFile(
            string title,
            string currentPath,
            string extension)
        {
            string directory = InitialDirectory(currentPath);
            return EditorUtility.OpenFilePanel(title, directory, extension);
        }

        public static string BrowseForFolder(string title, string currentPath)
        {
            string directory = InitialDirectory(currentPath);
            return EditorUtility.OpenFolderPanel(title, directory, string.Empty);
        }

        public static string MakeProjectRelative(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            string fullPath = Path.GetFullPath(path).Replace('\\', '/');
            string projectRoot = Directory.GetParent(Application.dataPath).FullName
                .Replace('\\', '/')
                .TrimEnd('/');
            return fullPath.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase)
                ? fullPath.Substring(projectRoot.Length + 1)
                : fullPath;
        }

        public static MessageType ToMessageType(StoreValidationSeverity severity)
        {
            switch (severity)
            {
                case StoreValidationSeverity.Error:
                    return MessageType.Error;
                case StoreValidationSeverity.Warning:
                    return MessageType.Warning;
                default:
                    return MessageType.Info;
            }
        }

        public static BadgeTone ToBadgeTone(StoreValidationSeverity severity)
        {
            switch (severity)
            {
                case StoreValidationSeverity.Error:
                    return BadgeTone.Danger;
                case StoreValidationSeverity.Warning:
                    return BadgeTone.Warning;
                default:
                    return BadgeTone.Info;
            }
        }

        private static void EnsureStyles()
        {
            bool currentSkin = EditorGUIUtility.isProSkin;
            if (stylesInitialized && styleSkin == currentSkin)
            {
                return;
            }

            stylesInitialized = true;
            styleSkin = currentSkin;
            cardStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(12, 12, 10, 10),
                margin = new RectOffset(0, 0, 4, 6)
            };
            compactCardStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(10, 10, 7, 7),
                margin = new RectOffset(0, 0, 3, 3)
            };
            pageTitleStyle = new GUIStyle(EditorStyles.largeLabel)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                fixedHeight = 27f
            };
            pageDescriptionStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                fontSize = 12,
                wordWrap = true,
                richText = false
            };
            sectionTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                wordWrap = true
            };
            sectionDescriptionStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel)
            {
                fontSize = 11,
                wordWrap = true
            };
            badgeStyle = new GUIStyle(EditorStyles.miniButton)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 9,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(8, 8, 2, 2),
                margin = new RectOffset(2, 2, 1, 1),
                fixedHeight = 19f
            };
            smallButtonStyle = new GUIStyle(EditorStyles.miniButton)
            {
                fixedHeight = EditorGUIUtility.singleLineHeight + 2f,
                padding = new RectOffset(6, 6, 1, 1)
            };
        }

        private static string ReportSummary(
            bool succeeded,
            int errorCount,
            int warningCount,
            int infoCount)
        {
            if (!succeeded)
            {
                return errorCount > 0
                    ? "Resolve the blocking errors below, then run the operation again."
                    : "The operation did not complete. Review the details below.";
            }

            if (warningCount > 0)
            {
                return "The operation completed, but review its warnings before release.";
            }

            return infoCount > 0
                ? "The operation completed with additional information."
                : "The operation completed without reported issues.";
        }

        private static bool PathExists(string path)
        {
            try
            {
                string fullPath = Path.GetFullPath(path);
                return File.Exists(fullPath) || Directory.Exists(fullPath);
            }
            catch
            {
                return false;
            }
        }

        private static Color GetMessageColor(MessageType messageType)
        {
            switch (messageType)
            {
                case MessageType.Error:
                    return GetBadgeColor(BadgeTone.Danger);
                case MessageType.Warning:
                    return GetBadgeColor(BadgeTone.Warning);
                default:
                    return GetBadgeColor(BadgeTone.Info);
            }
        }

        private static Color GetBadgeColor(BadgeTone tone)
        {
            bool dark = EditorGUIUtility.isProSkin;
            switch (tone)
            {
                case BadgeTone.Info:
                    return dark
                        ? new Color(0.22f, 0.52f, 0.84f, 1f)
                        : new Color(0.45f, 0.72f, 1f, 1f);
                case BadgeTone.Success:
                    return dark
                        ? new Color(0.24f, 0.62f, 0.39f, 1f)
                        : new Color(0.52f, 0.82f, 0.60f, 1f);
                case BadgeTone.Warning:
                    return dark
                        ? new Color(0.82f, 0.58f, 0.18f, 1f)
                        : new Color(1f, 0.76f, 0.35f, 1f);
                case BadgeTone.Danger:
                    return dark
                        ? new Color(0.77f, 0.30f, 0.30f, 1f)
                        : new Color(0.95f, 0.50f, 0.50f, 1f);
                default:
                    return dark
                        ? new Color(0.42f, 0.44f, 0.48f, 1f)
                        : new Color(0.72f, 0.74f, 0.78f, 1f);
            }
        }

        private static Color GetBadgeTextColor(BadgeTone tone)
        {
            if (EditorGUIUtility.isProSkin)
            {
                return Color.white;
            }

            return tone == BadgeTone.Neutral
                ? new Color(0.12f, 0.12f, 0.12f, 1f)
                : new Color(0.08f, 0.08f, 0.08f, 1f);
        }

        private static string InitialDirectory(string currentPath)
        {
            if (!string.IsNullOrWhiteSpace(currentPath))
            {
                try
                {
                    string fullPath = Path.GetFullPath(currentPath);
                    if (Directory.Exists(fullPath))
                    {
                        return fullPath;
                    }

                    string directory = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                    {
                        return directory;
                    }
                }
                catch
                {
                    // Fall through to project root.
                }
            }

            return Directory.GetParent(Application.dataPath).FullName;
        }
    }
}
