using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Aroko.StoreRelease.Editor.Dashboard
{
    internal static class StoreReleaseDocumentation
    {
        internal enum Page
        {
            SetupGuide,
            ApiReference,
            HelpCenter
        }

        private const string DocumentationRoot =
            "Assets/Aroko/StoreReleaseToolkit/Documentation/Web";

        private static readonly string[] WorkflowAnchors =
        {
            "setup-workflow",
            "build-workflow",
            "diagnostics",
            "quickstart"
        };

        [MenuItem("Help/Store Release Toolkit/Setup Guide", false, 5000)]
        private static void OpenSetupGuideMenu()
        {
            Open(Page.SetupGuide);
        }

        [MenuItem("Help/Store Release Toolkit/API Reference", false, 5001)]
        private static void OpenApiReferenceMenu()
        {
            Open(Page.ApiReference);
        }

        [MenuItem("Help/Store Release Toolkit/Help Center", false, 5002)]
        private static void OpenHelpCenterMenu()
        {
            Open(Page.HelpCenter);
        }

        [MenuItem("Help/Store Release Toolkit/Build Checklist", false, 5010)]
        private static void OpenReleaseChecklistMenu()
        {
            Open(Page.SetupGuide, "build-checklist");
        }

        [MenuItem("Help/Store Release Toolkit/CI Command Builder", false, 5011)]
        private static void OpenCiCommandBuilderMenu()
        {
            Open(Page.HelpCenter, "command-builder");
        }

        internal static void Open(Page page, string anchor = "")
        {
            string assetPath = GetAssetPath(page);
            string absolutePath = ToAbsoluteProjectPath(assetPath);
            if (!File.Exists(absolutePath))
            {
                OpenMarkdownFallback(page, assetPath);
                return;
            }

            var uriBuilder = new UriBuilder(
                new Uri(absolutePath, UriKind.Absolute));
            if (!string.IsNullOrWhiteSpace(anchor))
            {
                uriBuilder.Fragment = anchor.Trim().TrimStart('#');
            }

            Application.OpenURL(uriBuilder.Uri.AbsoluteUri);
        }

        internal static void OpenForWorkflowStep(int pageIndex)
        {
            int safeIndex = Mathf.Clamp(pageIndex, 0, WorkflowAnchors.Length - 1);
            if (safeIndex == 3)
            {
                Open(Page.ApiReference, WorkflowAnchors[safeIndex]);
                return;
            }

            if (safeIndex == 2)
            {
                Open(Page.HelpCenter, WorkflowAnchors[safeIndex]);
                return;
            }

            Open(Page.SetupGuide, WorkflowAnchors[safeIndex]);
        }

        internal static string GetAssetPath(Page page)
        {
            switch (page)
            {
                case Page.ApiReference:
                    return DocumentationRoot + "/api-reference.html";
                case Page.HelpCenter:
                    return DocumentationRoot + "/help-center.html";
                default:
                    return DocumentationRoot + "/setup-guide.html";
            }
        }

        internal static string GetWorkflowAnchor(int pageIndex)
        {
            return WorkflowAnchors[
                Mathf.Clamp(pageIndex, 0, WorkflowAnchors.Length - 1)];
        }

        private static string ToAbsoluteProjectPath(string assetPath)
        {
            string projectRoot =
                Directory.GetParent(Application.dataPath)?.FullName ??
                throw new InvalidOperationException(
                    "The Unity project root could not be resolved.");
            return Path.GetFullPath(
                Path.Combine(
                    projectRoot,
                    assetPath.Replace(
                        '/',
                        Path.DirectorySeparatorChar)));
        }

        private static void OpenMarkdownFallback(Page page, string missingAssetPath)
        {
            string fallbackPath = page == Page.ApiReference
                ? "Assets/Aroko/StoreReleaseToolkit/Documentation/API.md"
                : "Assets/Aroko/StoreReleaseToolkit/Documentation/Setup.md";
            UnityEngine.Object fallback =
                AssetDatabase.LoadMainAssetAtPath(fallbackPath);
            if (fallback != null)
            {
                Debug.LogWarning(
                    "Store Release Toolkit could not find the interactive offline " +
                    "documentation at " + missingAssetPath +
                    ". Opening the Markdown fallback.");
                AssetDatabase.OpenAsset(fallback);
                return;
            }

            Debug.LogWarning(
                "Store Release Toolkit documentation is missing: " +
                missingAssetPath);
        }
    }
}
