using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Aroko.StoreRelease.Editor.Packaging
{
    /// <summary>
    /// Validates and exports only the Asset Store product root. Vendor SDKs,
    /// vendor binaries and project-specific settings are intentionally outside
    /// this root.
    /// </summary>
    public static class AssetStorePackageExporter
    {
        public const string ProductRoot = "Assets/Aroko/StoreReleaseToolkit";
        public const string PackageVersion = "1.0.1";
        private const string TestsRoot = ProductRoot + "/Tests";
        private static readonly string[] RequiredFiles =
        {
            ProductRoot + "/README.md",
            ProductRoot + "/CHANGELOG.md",
            ProductRoot + "/Third-Party Notices.txt",
            ProductRoot + "/Documentation/Setup.md",
            ProductRoot + "/Documentation/Setup Checklist.md",
            ProductRoot + "/Documentation/API.md",
            ProductRoot + "/Documentation/CI.md",
            ProductRoot + "/Documentation/Web/setup-guide.html",
            ProductRoot + "/Documentation/Web/api-reference.html",
            ProductRoot + "/Documentation/Web/help-center.html",
            ProductRoot + "/Documentation/Web/docs.css",
            ProductRoot + "/Documentation/Web/docs.js",
            ProductRoot + "/Runtime/Aroko.StoreRelease.Runtime.asmdef",
            ProductRoot + "/Runtime/StoreAchievements.cs",
            ProductRoot + "/Runtime/Providers/Steam/Aroko.StoreRelease.Steam.asmdef",
            ProductRoot + "/Runtime/Providers/Steam/SteamAchievementProvider.cs",
            ProductRoot + "/Runtime/Providers/Epic/Aroko.StoreRelease.Epic.asmdef",
            ProductRoot + "/Runtime/Providers/Epic/EpicAchievementProvider.cs"
        };

        private static readonly HashSet<string> ForbiddenExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".exe", ".dll", ".so", ".dylib", ".zip", ".tgz", ".7z", ".rar",
                ".unitypackage", ".ps1", ".bat", ".cmd"
            };

        private static readonly string[] ForbiddenPortableContent =
        {
            "Packed" + "Life",
            "Packing" + " Life",
            "com." + "packed" + "life",
            "PACKED" + "LIFE_",
            "Store" + "Integrations",
            "Assets/Scripts/" + "Platform",
            "Steam" + "CMD",
            "BuildPatch" + "Tool",
            "Upload-Epic" + "Build"
        };

        private static readonly HashSet<string> SecretScanExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".cs", ".asmdef", ".json", ".asset", ".yaml", ".yml", ".txt",
                ".xml", ".config", ".ini", ".md", ".html", ".css", ".js"
            };

        private static readonly Regex[] HighConfidenceSecretPatterns =
        {
            new Regex(
                "-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----",
                RegexOptions.Compiled | RegexOptions.CultureInvariant),
            new Regex(
                "\\bAKIA[0-9A-Z]{16}\\b",
                RegexOptions.Compiled | RegexOptions.CultureInvariant),
            new Regex(
                "\\bgh[pousr]_[A-Za-z0-9]{30,}\\b",
                RegexOptions.Compiled | RegexOptions.CultureInvariant),
            new Regex(
                "(?i)\\b(?:client[_ -]?secret|password|api[_ -]?key)\\b" +
                "\\s*[:=]\\s*[\\\"']?(?<value>[A-Za-z0-9+/=_-]{20,})",
                RegexOptions.Compiled | RegexOptions.CultureInvariant)
        };

        private static readonly string[] SafeSecretReferencePrefixes =
        {
            "REQUIRED_",
            "REPLACE_",
            "PLACEHOLDER_",
            "USE_"
        };

        private static readonly Regex MetaGuidPattern = new Regex(
            "(?m)^guid:\\s*(?<guid>[0-9a-fA-F]{32})\\s*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        [MenuItem("Tools/Store Release Toolkit/Validate Asset Store Package")]
        public static void ValidateMenu()
        {
            IReadOnlyList<string> issues = Validate();
            if (issues.Count == 0)
            {
                Debug.Log("Store Release Toolkit Asset Store package validation passed.");
                return;
            }

            throw new InvalidOperationException(
                "Store Release Toolkit package validation failed:\n" +
                string.Join("\n", issues.Select(issue => "- " + issue)));
        }

        [MenuItem("Tools/Store Release Toolkit/Export Asset Store Package")]
        public static void ExportMenu()
        {
            string output = EditorUtility.SaveFilePanel(
                "Export Store Release Toolkit",
                Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath,
                "Store Release Toolkit " + PackageVersion,
                "unitypackage");
            if (string.IsNullOrWhiteSpace(output))
            {
                return;
            }

            Export(output);
        }

        [MenuItem("Tools/Store Release Toolkit/Export Package To Builds")]
        public static void ExportDefaultPackage()
        {
            Export(Path.Combine(
                Directory.GetParent(Application.dataPath)?.FullName ??
                Application.dataPath,
                "Builds",
                "StoreReleaseToolkit",
                "Distribution",
                "Store Release Toolkit " + PackageVersion + ".unitypackage"));
        }

        public static void Export(string outputPath)
        {
            IReadOnlyList<string> issues = Validate();
            if (issues.Count > 0)
            {
                throw new InvalidOperationException(
                    "Package export blocked:\n" +
                    string.Join("\n", issues.Select(issue => "- " + issue)));
            }

            string fullOutputPath = Path.GetFullPath(outputPath);
            string outputDirectory = Path.GetDirectoryName(fullOutputPath);
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                throw new ArgumentException("A package output directory is required.", nameof(outputPath));
            }

            string absoluteRoot = ToAbsoluteProjectPath(ProductRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (IsSameOrChildPath(fullOutputPath, absoluteRoot))
            {
                throw new InvalidOperationException(
                    "Export the .unitypackage outside the product root so it cannot include itself.");
            }

            Directory.CreateDirectory(outputDirectory);
            IReadOnlyList<string> exportAssets = BuildExportAssetList();
            if (exportAssets.Count == 0)
            {
                throw new InvalidOperationException(
                    "The package export manifest is empty.");
            }

            AssetDatabase.ExportPackage(
                exportAssets.ToArray(),
                fullOutputPath,
                ExportPackageOptions.Default);
            Debug.Log("Exported Store Release Toolkit package: " + fullOutputPath);
        }

        public static IReadOnlyList<string> BuildExportAssetList()
        {
            if (!AssetDatabase.IsValidFolder(ProductRoot))
            {
                return Array.Empty<string>();
            }

            return AssetDatabase.FindAssets(string.Empty, new[] { ProductRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path =>
                    !string.IsNullOrWhiteSpace(path) &&
                    (string.Equals(
                         path,
                         ProductRoot,
                         StringComparison.OrdinalIgnoreCase) ||
                     path.StartsWith(
                         ProductRoot + "/",
                         StringComparison.OrdinalIgnoreCase)))
                .Where(path =>
                    !string.Equals(
                        path,
                        TestsRoot,
                        StringComparison.OrdinalIgnoreCase) &&
                    !path.StartsWith(
                        TestsRoot + "/",
                        StringComparison.OrdinalIgnoreCase))
                .Where(path => !path.EndsWith(
                    ".meta",
                    StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static IReadOnlyList<string> Validate()
        {
            var issues = new List<string>();
            if (!AssetDatabase.IsValidFolder(ProductRoot))
            {
                issues.Add("Product root is missing: " + ProductRoot);
                return issues;
            }

            foreach (string requiredFile in RequiredFiles)
            {
                if (!File.Exists(ToAbsoluteProjectPath(requiredFile)))
                {
                    issues.Add("Required documentation is missing: " + requiredFile);
                }
            }

            string absoluteRoot = ToAbsoluteProjectPath(ProductRoot);
            ValidateMetadata(absoluteRoot, issues);
            IReadOnlyList<string> exportAssets = BuildExportAssetList();
            if (exportAssets.Count == 0)
            {
                issues.Add("The package export manifest is empty.");
            }

            foreach (string assetPath in exportAssets)
            {
                string file = ToAbsoluteProjectPath(assetPath);
                if (!File.Exists(file))
                {
                    continue;
                }

                string extension = Path.GetExtension(file);
                if (ForbiddenExtensions.Contains(extension))
                {
                    issues.Add("Forbidden bundled binary/archive: " +
                               file.Substring(absoluteRoot.Length).TrimStart(
                                   Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                }

                string fileName = Path.GetFileName(file);
                bool isSourceOrMetadata =
                    extension.Equals(".cs", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".asmdef", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".meta", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".md", StringComparison.OrdinalIgnoreCase);
                if (!isSourceOrMetadata &&
                    (fileName.IndexOf("credential", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     fileName.IndexOf("secret", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    issues.Add("Potential credential file inside package: " + fileName);
                }

                if (SecretScanExtensions.Contains(extension) &&
                    ContainsHighConfidenceSecret(file))
                {
                    issues.Add(
                        "Potential hard-coded secret inside package: " +
                        file.Substring(absoluteRoot.Length).TrimStart(
                            Path.DirectorySeparatorChar,
                            Path.AltDirectorySeparatorChar));
                }

                if (SecretScanExtensions.Contains(extension) &&
                    ContainsForbiddenPortableContent(file))
                {
                    issues.Add(
                        "Project-specific or delivery-only content inside export: " +
                        assetPath.Substring(ProductRoot.Length).TrimStart('/'));
                }
            }

            return issues;
        }

        private static bool ContainsHighConfidenceSecret(string path)
        {
            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch
            {
                return true;
            }

            foreach (Regex pattern in HighConfidenceSecretPatterns)
            {
                foreach (Match match in pattern.Matches(text))
                {
                    Group valueGroup = match.Groups["value"];
                    if (valueGroup.Success &&
                        IsSafeSecretReference(valueGroup.Value))
                    {
                        continue;
                    }

                    return true;
                }
            }

            return false;
        }

        private static bool ContainsForbiddenPortableContent(string path)
        {
            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch
            {
                return true;
            }

            return ForbiddenPortableContent.Any(token =>
                text.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsSafeSecretReference(string value)
        {
            string normalized = (value ?? string.Empty).Trim();
            return SafeSecretReferencePrefixes.Any(
                prefix => normalized.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase));
        }

        private static void ValidateMetadata(
            string absoluteRoot,
            ICollection<string> issues)
        {
            var expectedTargets = Directory
                .EnumerateFileSystemEntries(
                    absoluteRoot,
                    "*",
                    SearchOption.AllDirectories)
                .Where(path => !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                .Concat(new[] { absoluteRoot });

            foreach (string target in expectedTargets)
            {
                if (!File.Exists(target + ".meta"))
                {
                    issues.Add(
                        "Missing Unity metadata: " +
                        ToProductRelativePath(absoluteRoot, target));
                }
            }

            var seenGuids = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            IEnumerable<string> metaFiles = Directory.EnumerateFiles(
                    absoluteRoot,
                    "*.meta",
                    SearchOption.AllDirectories)
                .Concat(new[] { absoluteRoot + ".meta" });
            foreach (string meta in metaFiles)
            {
                if (!File.Exists(meta))
                {
                    continue;
                }

                string target = meta.Substring(0, meta.Length - ".meta".Length);
                if (!File.Exists(target) && !Directory.Exists(target))
                {
                    issues.Add(
                        "Orphaned Unity metadata: " +
                        ToProductRelativePath(absoluteRoot, meta));
                }

                string metaText;
                try
                {
                    metaText = File.ReadAllText(meta);
                }
                catch (Exception exception)
                {
                    issues.Add(
                        "Could not read Unity metadata " +
                        ToProductRelativePath(absoluteRoot, meta) + ": " +
                        exception.Message);
                    continue;
                }

                Match guidMatch = MetaGuidPattern.Match(metaText);
                if (!guidMatch.Success)
                {
                    issues.Add(
                        "Unity metadata has no valid GUID: " +
                        ToProductRelativePath(absoluteRoot, meta));
                    continue;
                }

                string guid = guidMatch.Groups["guid"].Value;
                if (seenGuids.TryGetValue(guid, out string firstMeta))
                {
                    issues.Add(
                        "Duplicate Unity metadata GUID " + guid + ": " +
                        ToProductRelativePath(absoluteRoot, firstMeta) + " and " +
                        ToProductRelativePath(absoluteRoot, meta));
                }
                else
                {
                    seenGuids.Add(guid, meta);
                }
            }
        }

        private static string ToAbsoluteProjectPath(string projectRelativePath)
        {
            string projectRoot =
                Directory.GetParent(Application.dataPath)?.FullName ??
                throw new InvalidOperationException("Unity project root could not be resolved.");
            return Path.GetFullPath(
                Path.Combine(
                    projectRoot,
                    projectRelativePath.Replace(
                        '/',
                        Path.DirectorySeparatorChar)));
        }

        private static bool IsSameOrChildPath(string path, string parent)
        {
            string candidate = Path.GetFullPath(path);
            string normalizedParent = Path.GetFullPath(parent)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(
                       candidate,
                       normalizedParent,
                       StringComparison.OrdinalIgnoreCase) ||
                   candidate.StartsWith(
                       normalizedParent + Path.DirectorySeparatorChar,
                       StringComparison.OrdinalIgnoreCase);
        }

        private static string ToProductRelativePath(
            string absoluteRoot,
            string path)
        {
            string relative = path.Substring(absoluteRoot.Length)
                .TrimStart(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
            return string.IsNullOrWhiteSpace(relative)
                ? Path.GetFileName(absoluteRoot)
                : relative;
        }
    }
}
