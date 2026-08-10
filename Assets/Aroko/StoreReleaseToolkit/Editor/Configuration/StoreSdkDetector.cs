using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using PackageManagerInfo = UnityEditor.PackageManager.PackageInfo;

namespace Aroko.StoreRelease.Editor.Configuration
{
    public enum StoreSdkCompatibility
    {
        Missing = 0,
        Supported = 1,
        UnverifiedNewer = 2,
        UnsupportedOlder = 3,
        UnknownVersion = 4
    }

    public sealed class StoreSdkStatus
    {
        public StoreSdkStatus(
            string displayName,
            string packageName,
            string testedVersion,
            string installedVersion,
            StoreSdkCompatibility compatibility)
        {
            DisplayName = displayName;
            PackageName = packageName;
            TestedVersion = testedVersion;
            InstalledVersion = installedVersion;
            Compatibility = compatibility;
        }

        public string DisplayName { get; }
        public string PackageName { get; }
        public string TestedVersion { get; }
        public string InstalledVersion { get; }
        public StoreSdkCompatibility Compatibility { get; }
        public bool IsInstalled => Compatibility != StoreSdkCompatibility.Missing;
    }

    public static class StoreSdkDetector
    {
        public const string SteamworksPackageName = "com.rlabrecque.steamworks.net";
        public const string SteamworksTestedVersion = "2025.162.1";
        public const string SteamworksDownloadUrl =
            "https://github.com/rlabrecque/Steamworks.NET/releases";
        public const string SteamworksInstallUrl =
            "https://github.com/rlabrecque/Steamworks.NET.git?path=/com.rlabrecque.steamworks.net#2025.162.1";
        public const string SteamworksAvailableDefine =
            "AROKO_SRT_STEAMWORKS_AVAILABLE";
        public const string EosPackageName = "com.playeveryware.eos";
        public const string EosTestedVersion = "6.1.0";
        public const string EosDownloadUrl =
            "https://github.com/EOS-Contrib/eos_plugin_for_unity/releases";
        public const string EosInstallUrl =
            "https://github.com/EOS-Contrib/eos_plugin_for_unity_upm.git";
        public const string EosAvailableDefine = "AROKO_SRT_EOS_AVAILABLE";

        private static readonly string[] SteamworksAssetMarkers =
        {
            "com.rlabrecque.steamworks.net.asmdef",
            "steam_api64.dll"
        };

        private static readonly string[] EosAssetMarkers =
        {
            "com.playeveryware.eos.asmdef",
            "com.playeveryware.eos.core.asmdef",
            "com.Epic.OnlineServices.asmdef",
            "EOSSDK-Win64-Shipping.dll"
        };

        [Serializable]
        private sealed class PackageManifest
        {
            public string name = string.Empty;
            public string version = string.Empty;
        }

        public static IReadOnlyList<StoreSdkStatus> DetectAll()
        {
            PackageManagerInfo[] packages;
            try
            {
                packages = PackageManagerInfo.GetAllRegisteredPackages() ??
                           Array.Empty<PackageManagerInfo>();
            }
            catch
            {
                packages = Array.Empty<PackageManagerInfo>();
            }

            string[] assetPaths;
            try
            {
                assetPaths = AssetDatabase.GetAllAssetPaths()
                    .Where(path => path.StartsWith(
                        "Assets/",
                        StringComparison.OrdinalIgnoreCase))
                    .ToArray();
            }
            catch
            {
                assetPaths = Array.Empty<string>();
            }

            StoreSdkStatus steamworks = Detect(
                packages,
                "Steamworks.NET",
                SteamworksPackageName,
                SteamworksTestedVersion);
            if (!steamworks.IsInstalled)
            {
                steamworks = DetectAssetsInstall(
                    assetPaths,
                    "Steamworks.NET",
                    SteamworksPackageName,
                    SteamworksTestedVersion,
                    SteamworksAssetMarkers,
                    string.Empty);
            }

            StoreSdkStatus eos = Detect(
                packages,
                "EOS Unity Plugin",
                EosPackageName,
                EosTestedVersion);
            if (!eos.IsInstalled)
            {
                eos = DetectAssetsInstall(
                    assetPaths,
                    "EOS Unity Plugin",
                    EosPackageName,
                    EosTestedVersion,
                    EosAssetMarkers,
                    "eosPluginVersion.asset");
            }

            return new[]
            {
                steamworks,
                eos
            };
        }

        public static string GetDownloadUrl(string packageName)
        {
            if (string.Equals(
                    packageName,
                    SteamworksPackageName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return SteamworksDownloadUrl;
            }

            return string.Equals(
                    packageName,
                    EosPackageName,
                    StringComparison.OrdinalIgnoreCase)
                ? EosDownloadUrl
                : string.Empty;
        }

        public static string GetInstallUrl(string packageName)
        {
            if (string.Equals(
                    packageName,
                    SteamworksPackageName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return SteamworksInstallUrl;
            }

            return string.Equals(
                    packageName,
                    EosPackageName,
                    StringComparison.OrdinalIgnoreCase)
                ? EosInstallUrl
                : string.Empty;
        }

        public static bool AreRequiredSdksReady(
            IEnumerable<StoreBuildProfile> profiles,
            IReadOnlyList<StoreSdkStatus> statuses,
            out string blockingReason)
        {
            blockingReason = string.Empty;
            foreach (StoreBuildProfile profile in profiles ??
                     Enumerable.Empty<StoreBuildProfile>())
            {
                if (profile == null)
                {
                    continue;
                }

                string packageName = profile.Store == StorePlatform.Steam
                    ? SteamworksPackageName
                    : profile.Store == StorePlatform.Epic
                        ? EosPackageName
                        : string.Empty;
                if (string.IsNullOrWhiteSpace(packageName))
                {
                    continue;
                }

                StoreSdkStatus status = statuses?.FirstOrDefault(
                    item => string.Equals(
                        item.PackageName,
                        packageName,
                        StringComparison.OrdinalIgnoreCase));
                if (status == null ||
                    status.Compatibility == StoreSdkCompatibility.Missing ||
                    status.Compatibility == StoreSdkCompatibility.UnsupportedOlder)
                {
                    blockingReason =
                        (status?.DisplayName ?? packageName) +
                        " must be installed at a supported version.";
                    return false;
                }
            }

            return true;
        }

        public static StoreSdkStatus Detect(
            IEnumerable<PackageManagerInfo> packages,
            string displayName,
            string packageName,
            string testedVersion)
        {
            PackageManagerInfo package = packages.FirstOrDefault(
                item => item != null &&
                        string.Equals(item.name, packageName, StringComparison.OrdinalIgnoreCase));

            if (package == null)
            {
                return new StoreSdkStatus(
                    displayName,
                    packageName,
                    testedVersion,
                    string.Empty,
                    StoreSdkCompatibility.Missing);
            }

            return CreateStatus(
                displayName,
                packageName,
                testedVersion,
                package.version ?? string.Empty);
        }

        private static StoreSdkStatus DetectAssetsInstall(
            IReadOnlyList<string> assetPaths,
            string displayName,
            string packageName,
            string testedVersion,
            IReadOnlyList<string> requiredMarkers,
            string versionMarker)
        {
            if (!HasAllAssetMarkers(assetPaths, requiredMarkers))
            {
                return new StoreSdkStatus(
                    displayName,
                    packageName,
                    testedVersion,
                    string.Empty,
                    StoreSdkCompatibility.Missing);
            }

            string installedVersion = FindManifestVersion(
                assetPaths,
                packageName);
            if (string.IsNullOrWhiteSpace(installedVersion) &&
                !string.IsNullOrWhiteSpace(versionMarker))
            {
                installedVersion = FindTextAssetVersion(
                    assetPaths,
                    versionMarker);
            }

            return CreateStatus(
                displayName,
                packageName,
                testedVersion,
                installedVersion);
        }

        private static StoreSdkStatus CreateStatus(
            string displayName,
            string packageName,
            string testedVersion,
            string installedVersion)
        {
            string normalizedInstalled = (installedVersion ?? string.Empty).Trim();
            string normalizedTested = (testedVersion ?? string.Empty).Trim();
            if (!TryParseVersion(installedVersion, out Version installed) ||
                !TryParseVersion(testedVersion, out Version tested))
            {
                return new StoreSdkStatus(
                    displayName,
                    packageName,
                    testedVersion,
                    installedVersion,
                    StoreSdkCompatibility.UnknownVersion);
            }

            int comparison = installed.CompareTo(tested);
            if (comparison == 0 &&
                !string.Equals(
                    normalizedInstalled,
                    normalizedTested,
                    StringComparison.OrdinalIgnoreCase))
            {
                return new StoreSdkStatus(
                    displayName,
                    packageName,
                    testedVersion,
                    installedVersion,
                    StoreSdkCompatibility.UnknownVersion);
            }

            return new StoreSdkStatus(
                displayName,
                packageName,
                testedVersion,
                installedVersion,
                comparison == 0
                    ? StoreSdkCompatibility.Supported
                    : comparison > 0
                        ? StoreSdkCompatibility.UnverifiedNewer
                        : StoreSdkCompatibility.UnsupportedOlder);
        }

        private static bool HasAllAssetMarkers(
            IReadOnlyList<string> assetPaths,
            IReadOnlyList<string> requiredMarkers)
        {
            foreach (string marker in requiredMarkers)
            {
                if (!assetPaths.Any(path => string.Equals(
                        Path.GetFileName(path),
                        marker,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    return false;
                }
            }

            return true;
        }

        private static string FindManifestVersion(
            IReadOnlyList<string> assetPaths,
            string packageName)
        {
            foreach (string assetPath in assetPaths)
            {
                if (!string.Equals(
                        Path.GetFileName(assetPath),
                        "package.json",
                        StringComparison.OrdinalIgnoreCase) ||
                    !TryReadAssetText(assetPath, out string json))
                {
                    continue;
                }

                try
                {
                    PackageManifest manifest =
                        JsonUtility.FromJson<PackageManifest>(json);
                    if (manifest != null &&
                        string.Equals(
                            manifest.name,
                            packageName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return manifest.version ?? string.Empty;
                    }
                }
                catch
                {
                    // A malformed unrelated package manifest is not an SDK match.
                }
            }

            return string.Empty;
        }

        private static string FindTextAssetVersion(
            IReadOnlyList<string> assetPaths,
            string markerName)
        {
            foreach (string assetPath in assetPaths)
            {
                if (!string.Equals(
                        Path.GetFileName(assetPath),
                        markerName,
                        StringComparison.OrdinalIgnoreCase) ||
                    !TryReadAssetText(assetPath, out string text))
                {
                    continue;
                }

                Match serializedText = Regex.Match(
                    text,
                    @"(?m)^\s*m_Script:\s*(?<version>[^\r\n]+)\s*$",
                    RegexOptions.CultureInvariant);
                string candidate = serializedText.Success
                    ? serializedText.Groups["version"].Value.Trim()
                    : text.Trim();
                if (TryParseVersion(candidate, out _))
                {
                    return candidate;
                }
            }

            return string.Empty;
        }

        private static bool TryReadAssetText(
            string assetPath,
            out string text)
        {
            text = string.Empty;
            try
            {
                string assetsRoot = Path.GetFullPath(Application.dataPath)
                    .TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar);
                string projectRoot = Directory.GetParent(assetsRoot)?.FullName;
                if (string.IsNullOrWhiteSpace(projectRoot))
                {
                    return false;
                }

                string fullPath = Path.GetFullPath(
                    Path.Combine(
                        projectRoot,
                        assetPath.Replace(
                            '/',
                            Path.DirectorySeparatorChar)));
                if (!fullPath.StartsWith(
                        assetsRoot + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(fullPath))
                {
                    return false;
                }

                text = File.ReadAllText(fullPath);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryParseVersion(string value, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string normalized = value.Trim();
            int suffixIndex = normalized.IndexOfAny(new[] { '-', '+' });
            if (suffixIndex >= 0)
            {
                normalized = normalized.Substring(0, suffixIndex);
            }

            return Version.TryParse(normalized, out version);
        }
    }
}
