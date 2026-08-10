using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Aroko.StoreRelease.Editor.Configuration
{
    public static class StoreConfigurationValidator
    {
        public static List<StoreValidationIssue> ValidateAll(StoreReleaseProjectSettings settings)
        {
            var issues = new List<StoreValidationIssue>();
            if (settings == null)
            {
                issues.Add(Error("SRT-CONFIG-000", "Project settings could not be loaded."));
                return issues;
            }

            settings.EnsureDefaults();
            ValidateCommon(settings, issues);
            ValidateProfiles(settings, issues);
            ValidateEpicEnvironments(settings, issues);
            return issues;
        }

        public static List<StoreValidationIssue> ValidateProfile(
            StoreReleaseProjectSettings settings,
            StoreBuildProfile profile)
        {
            var issues = new List<StoreValidationIssue>();
            if (settings == null)
            {
                issues.Add(Error("SRT-CONFIG-000", "Project settings could not be loaded."));
                return issues;
            }

            if (profile == null)
            {
                issues.Add(Error("SRT-PROFILE-000", "Select a build profile."));
                return issues;
            }

            ValidateCommon(settings, issues);
            ValidateProfile(settings, profile, issues, true);
            return issues;
        }

        private static void ValidateCommon(
            StoreReleaseProjectSettings settings,
            ICollection<StoreValidationIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(settings.DefaultVersion))
            {
                issues.Add(Error("SRT-CONFIG-001", "A default build version is required."));
            }

            if (!IsSafeExecutableName(settings.ExecutableName))
            {
                issues.Add(Error(
                    "SRT-CONFIG-002",
                    "The Windows executable must be a safe filename ending in .exe, not a path."));
            }

            if (!string.IsNullOrWhiteSpace(settings.WindowsIconAssetPath))
            {
                string iconFullPath = ToFullProjectPath(settings.WindowsIconAssetPath);
                if (!File.Exists(iconFullPath))
                {
                    issues.Add(Error(
                        "SRT-ICON-001",
                        "The configured Windows icon file does not exist.",
                        settings.WindowsIconAssetPath));
                }
                else if (!settings.WindowsIconAssetPath.EndsWith(
                             ".ico",
                             StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(Warning(
                        "SRT-ICON-002",
                        "Use a multi-resolution .ico file for Epic desktop shortcuts.",
                        settings.WindowsIconAssetPath));
                }
            }

            ValidateOptionalFile(
                settings.EpicProductConfigTemplatePath,
                "SRT-EPIC-CONFIG-001",
                "The Epic product configuration template does not exist.",
                issues);
            ValidateOptionalFile(
                settings.EpicWindowsConfigTemplatePath,
                "SRT-EPIC-CONFIG-002",
                "The Epic Windows configuration template does not exist.",
                issues);
        }

        internal static bool IsSafeExecutableName(string value)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                value.Length > 255 ||
                !string.Equals(value, value.Trim(), StringComparison.Ordinal) ||
                !value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                Path.IsPathRooted(value))
            {
                return false;
            }

            const string invalidWindowsCharacters = "<>:\"/\\|?*";
            foreach (char character in value)
            {
                if (character < 32 ||
                    invalidWindowsCharacters.IndexOf(character) >= 0)
                {
                    return false;
                }
            }

            string stem = value.Substring(0, value.Length - 4);
            if (string.IsNullOrWhiteSpace(stem) ||
                stem.EndsWith(".", StringComparison.Ordinal) ||
                stem.EndsWith(" ", StringComparison.Ordinal))
            {
                return false;
            }

            string deviceName = stem.Split('.')[0];
            return !string.Equals(
                       deviceName,
                       "CON",
                       StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(
                       deviceName,
                       "PRN",
                       StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(
                       deviceName,
                       "AUX",
                       StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(
                       deviceName,
                       "NUL",
                       StringComparison.OrdinalIgnoreCase) &&
                   !(deviceName.Length == 4 &&
                     (deviceName.StartsWith(
                          "COM",
                          StringComparison.OrdinalIgnoreCase) ||
                      deviceName.StartsWith(
                          "LPT",
                          StringComparison.OrdinalIgnoreCase)) &&
                     deviceName[3] >= '1' &&
                     deviceName[3] <= '9');
        }

        private static void ValidateProfiles(
            StoreReleaseProjectSettings settings,
            ICollection<StoreValidationIssue> issues)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (StoreBuildProfile profile in settings.Profiles)
            {
                if (profile == null)
                {
                    issues.Add(Error("SRT-PROFILE-001", "A profile entry is null."));
                    continue;
                }

                if (!ids.Add(profile.Id ?? string.Empty))
                {
                    issues.Add(Error(
                        "SRT-PROFILE-002",
                        "Profile IDs must be unique.",
                        profile.Id));
                }

                ValidateProfile(settings, profile, issues, false);
            }
        }

        private static void ValidateProfile(
            StoreReleaseProjectSettings settings,
            StoreBuildProfile profile,
            ICollection<StoreValidationIssue> issues,
            bool validateSelectedEpicFields)
        {
            string context = string.IsNullOrWhiteSpace(profile.DisplayName)
                ? profile.Id
                : profile.DisplayName;

            if (string.IsNullOrWhiteSpace(profile.Id))
            {
                issues.Add(Error("SRT-PROFILE-003", "A profile ID is required.", context));
            }

            if (profile.Store == StorePlatform.None)
            {
                issues.Add(Error(
                    "SRT-PROFILE-004",
                    "A profile must target Steam or Epic.",
                    context));
            }

            bool expectedDevelopmentBuild =
                profile.Channel == StoreReleaseChannel.Development;
            if (profile.DevelopmentBuild != expectedDevelopmentBuild)
            {
                issues.Add(Error(
                    "SRT-PROFILE-008",
                    "Development channel profiles must use Development Build; " +
                    "Stage and Live profiles must use a release build.",
                    context));
            }

            if (profile.Store == StorePlatform.Steam &&
                profile.Channel == StoreReleaseChannel.Stage)
            {
                issues.Add(Error(
                    "SRT-STEAM-004",
                    "Steam build profiles support Development or Live channels.",
                    context));
            }

            if (string.IsNullOrWhiteSpace(profile.OutputTemplate))
            {
                issues.Add(Error(
                    "SRT-PROFILE-005",
                    "An output template is required.",
                    context));
            }
            else if (!profile.OutputTemplate.Contains("{Version}"))
            {
                issues.Add(Warning(
                    "SRT-PROFILE-006",
                    "The output template does not contain {Version}; consecutive builds may overwrite.",
                    context));
            }

            if (profile.Store == StorePlatform.Steam)
            {
                ValidateSteam(settings, profile, issues);
            }
            else if (profile.Store == StorePlatform.Epic)
            {
                EpicEnvironmentDefinition environment =
                    settings.GetEpicEnvironment(profile.EpicEnvironmentName);
                if (environment == null)
                {
                    issues.Add(Error(
                        "SRT-EPIC-001",
                        "The selected Epic environment does not exist.",
                        context));
                }
                else if (environment.Channel != profile.Channel)
                {
                    issues.Add(Error(
                        "SRT-EPIC-006",
                        "The Epic profile channel does not match its selected environment.",
                        context));
                }

                if (environment != null && validateSelectedEpicFields)
                {
                    ValidateEpicEnvironmentFields(
                        environment,
                        issues,
                        true);
                }
            }
        }

        private static void ValidateSteam(
            StoreReleaseProjectSettings settings,
            StoreBuildProfile profile,
            ICollection<StoreValidationIssue> issues)
        {
            if (!IsPositiveInteger(settings.SteamAppId))
            {
                issues.Add(Error(
                    "SRT-STEAM-001",
                    "Steam App ID must be a positive integer.",
                    profile.DisplayName));
            }

        }

        private static void ValidateEpicEnvironments(
            StoreReleaseProjectSettings settings,
            ICollection<StoreValidationIssue> issues)
        {
            StoreBuildProfile activeProfile =
                settings.GetProfile(settings.ActiveProfileId);
            string requiredEnvironmentName =
                activeProfile != null &&
                activeProfile.Store == StorePlatform.Epic
                    ? activeProfile.EpicEnvironmentName
                    : string.Empty;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (EpicEnvironmentDefinition environment in settings.EpicEnvironments)
            {
                if (environment == null)
                {
                    issues.Add(Error("SRT-EPIC-002", "An Epic environment entry is null."));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(environment.Name))
                {
                    issues.Add(Error("SRT-EPIC-003", "Every Epic environment needs a name."));
                }
                else if (!names.Add(environment.Name))
                {
                    issues.Add(Error(
                        "SRT-EPIC-004",
                        "Epic environment names must be unique.",
                        environment.Name));
                }

                ValidateEpicEnvironmentFields(
                    environment,
                    issues,
                    string.Equals(
                        environment.Name,
                        requiredEnvironmentName,
                        StringComparison.OrdinalIgnoreCase));
            }
        }

        private static void ValidateEpicEnvironmentFields(
            EpicEnvironmentDefinition environment,
            ICollection<StoreValidationIssue> issues,
            bool required)
        {
            ValidateRequired(
                environment.ProductId,
                "Product ID",
                environment.Name,
                issues,
                required);
            ValidateRequired(
                environment.SandboxId,
                "Sandbox ID",
                environment.Name,
                issues,
                required);
            ValidateRequired(
                environment.DeploymentId,
                "Deployment ID",
                environment.Name,
                issues,
                required);
            ValidateRequired(
                environment.EosClientId,
                "EOS Client ID",
                environment.Name,
                issues,
                required);
            ValidateRequired(
                environment.ClientSecret,
                "EOS launcher Client Secret",
                environment.Name,
                issues,
                required);
            ValidateRequired(
                environment.EncryptionKey,
                "EOS Encryption Key",
                environment.Name,
                issues,
                required);

            if (IsConfiguredValue(environment.SandboxId) &&
                !IsValidEpicSandboxId(environment.SandboxId))
            {
                issues.Add(Error(
                    "SRT-EPIC-007",
                    "Sandbox ID must be a GUID or 'p-' followed by exactly 30 letters or numbers.",
                    environment.Name));
            }

            if (IsConfiguredValue(environment.DeploymentId) &&
                !IsValidEpicDeploymentId(environment.DeploymentId))
            {
                issues.Add(Error(
                    "SRT-EPIC-008",
                    "Deployment ID must be a non-empty GUID (32 hexadecimal characters are accepted).",
                    environment.Name));
            }
        }

        private static void ValidateRequired(
            string value,
            string label,
            string context,
            ICollection<StoreValidationIssue> issues,
            bool required)
        {
            if (!IsConfiguredValue(value))
            {
                issues.Add(required
                    ? Error(
                        "SRT-EPIC-005",
                        $"{label} is required by the selected Epic profile.",
                        context)
                    : Warning(
                        "SRT-EPIC-005",
                        $"{label} is not configured.",
                        context));
            }
        }

        internal static bool IsValidEpicSandboxId(string value)
        {
            string normalized = value == null ? string.Empty : value.Trim();
            return Guid.TryParse(normalized, out Guid id) && id != Guid.Empty ||
                   Regex.IsMatch(normalized, "^p-[A-Za-z0-9]{30}$");
        }

        internal static bool IsValidEpicDeploymentId(string value)
        {
            string normalized = value == null ? string.Empty : value.Trim();
            return Guid.TryParse(normalized, out Guid id) && id != Guid.Empty;
        }

        private static bool IsConfiguredValue(string value)
        {
            string normalized = value == null ? string.Empty : value.Trim();
            return !string.IsNullOrWhiteSpace(normalized) &&
                   !normalized.StartsWith("REQUIRED_", StringComparison.OrdinalIgnoreCase) &&
                   !normalized.StartsWith("replace-", StringComparison.OrdinalIgnoreCase) &&
                   normalized.Any(character => character != '0');
        }

        private static bool IsPositiveInteger(string value)
        {
            return ulong.TryParse(value, out ulong parsed) && parsed > 0;
        }

        private static void ValidateOptionalFile(
            string path,
            string code,
            string message,
            ICollection<StoreValidationIssue> issues)
        {
            if (!string.IsNullOrWhiteSpace(path) && !File.Exists(ToFullProjectPath(path)))
            {
                issues.Add(Warning(code, message, path));
            }
        }

        private static string ToFullProjectPath(string path)
        {
            if (Path.IsPathRooted(path))
            {
                return Path.GetFullPath(path);
            }

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.GetFullPath(Path.Combine(projectRoot, path));
        }

        private static StoreValidationIssue Error(string code, string message, string context = "")
        {
            return new StoreValidationIssue(StoreValidationSeverity.Error, code, message, context);
        }

        private static StoreValidationIssue Warning(string code, string message, string context = "")
        {
            return new StoreValidationIssue(StoreValidationSeverity.Warning, code, message, context);
        }
    }
}
