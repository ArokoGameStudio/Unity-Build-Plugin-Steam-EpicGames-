using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Aroko.StoreRelease.Editor.Configuration
{
    internal static class EosProjectConfigurationGenerator
    {
        private const string SchemaVersion = "1.0";

        public static bool CanGenerate(
            StoreReleaseProjectSettings settings,
            out string blockingReason)
        {
            if (settings == null)
            {
                blockingReason = "Project settings are unavailable.";
                return false;
            }

            List<EpicEnvironmentDefinition> configured = GetConfiguredEnvironments(settings);
            if (configured.Count == 0)
            {
                blockingReason =
                    "Configure Product ID, Sandbox ID, Deployment ID, EOS Client ID, " +
                    "and Client Secret for at least one Epic environment.";
                return false;
            }

            string productId = configured[0].ProductId.Trim();
            EpicEnvironmentDefinition invalidIdentifiers = configured.FirstOrDefault(
                environment =>
                    !StoreConfigurationValidator.IsValidEpicSandboxId(environment.SandboxId) ||
                    !StoreConfigurationValidator.IsValidEpicDeploymentId(environment.DeploymentId));
            if (invalidIdentifiers != null)
            {
                blockingReason =
                    "Epic environment '" + invalidIdentifiers.Name +
                    "' contains an invalid Sandbox ID or Deployment ID.";
                return false;
            }

            if (configured.Any(environment => !string.Equals(
                    productId,
                    environment.ProductId.Trim(),
                    StringComparison.OrdinalIgnoreCase)))
            {
                blockingReason =
                    "Configured Epic environments must belong to the same Product ID.";
                return false;
            }

            blockingReason = string.Empty;
            return true;
        }

        public static void CreateForProject(StoreReleaseProjectSettings settings)
        {
            if (!CanGenerate(settings, out string blockingReason))
            {
                throw new InvalidOperationException(blockingReason);
            }

            settings.EpicProductConfigTemplatePath =
                StoreReleaseProjectSettings.DefaultEpicProductConfigTemplatePath;
            settings.EpicWindowsConfigTemplatePath =
                StoreReleaseProjectSettings.DefaultEpicWindowsConfigTemplatePath;

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                throw new InvalidOperationException("Unity project root could not be resolved.");
            }

            StoreBuildProfile activeProfile = settings.GetProfile(settings.ActiveProfileId);
            string preferredEnvironmentName = activeProfile != null &&
                                              activeProfile.Store == StorePlatform.Epic
                ? activeProfile.EpicEnvironmentName
                : string.Empty;
            WriteFiles(
                Path.GetFullPath(Path.Combine(
                    projectRoot,
                    settings.EpicProductConfigTemplatePath)),
                Path.GetFullPath(Path.Combine(
                    projectRoot,
                    settings.EpicWindowsConfigTemplatePath)),
                Application.productName,
                settings.DefaultVersion,
                settings.EpicEnvironments,
                preferredEnvironmentName);

            settings.SaveSettings();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        internal static void CreateForBuild(
            StoreReleaseProjectSettings settings,
            StoreBuildProfile profile,
            string version)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (profile == null || profile.Store != StorePlatform.Epic)
            {
                throw new ArgumentException(
                    "An Epic build profile is required.",
                    nameof(profile));
            }

            EpicEnvironmentDefinition environment =
                settings.GetEpicEnvironment(profile.EpicEnvironmentName);
            if (environment == null)
            {
                throw new InvalidOperationException(
                    "The selected Epic environment does not exist.");
            }

            if (!IsConfigured(environment) ||
                !StoreConfigurationValidator.IsValidEpicSandboxId(environment.SandboxId) ||
                !StoreConfigurationValidator.IsValidEpicDeploymentId(environment.DeploymentId))
            {
                throw new InvalidOperationException(
                    "The selected Epic environment is incomplete or contains an invalid Sandbox/Deployment ID.");
            }

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                throw new InvalidOperationException("Unity project root could not be resolved.");
            }

            settings.EpicProductConfigTemplatePath =
                StoreReleaseProjectSettings.DefaultEpicProductConfigTemplatePath;
            settings.EpicWindowsConfigTemplatePath =
                StoreReleaseProjectSettings.DefaultEpicWindowsConfigTemplatePath;
            WriteFiles(
                Path.GetFullPath(Path.Combine(
                    projectRoot,
                    settings.EpicProductConfigTemplatePath)),
                Path.GetFullPath(Path.Combine(
                    projectRoot,
                    settings.EpicWindowsConfigTemplatePath)),
                Application.productName,
                version,
                new[] { environment },
                profile.EpicEnvironmentName);

            settings.SaveSettings();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        internal static void WriteFiles(
            string productConfigPath,
            string windowsConfigPath,
            string productName,
            string version,
            IReadOnlyList<EpicEnvironmentDefinition> environments,
            string preferredEnvironmentName)
        {
            if (string.IsNullOrWhiteSpace(productConfigPath))
            {
                throw new ArgumentException(
                    "Product configuration output path is required.",
                    nameof(productConfigPath));
            }

            if (string.IsNullOrWhiteSpace(windowsConfigPath))
            {
                throw new ArgumentException(
                    "Windows configuration output path is required.",
                    nameof(windowsConfigPath));
            }

            List<EpicEnvironmentDefinition> configured = environments == null
                ? new List<EpicEnvironmentDefinition>()
                : environments.Where(IsConfigured).ToList();
            if (configured.Count == 0)
            {
                throw new InvalidOperationException(
                    "At least one fully configured Epic environment is required.");
            }

            string productId = configured[0].ProductId.Trim();
            if (configured.Any(environment => !string.Equals(
                    productId,
                    environment.ProductId.Trim(),
                    StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    "Configured Epic environments must belong to the same Product ID.");
            }

            EpicEnvironmentDefinition preferred = configured.FirstOrDefault(
                environment => string.Equals(
                    environment.Name,
                    preferredEnvironmentName,
                    StringComparison.OrdinalIgnoreCase)) ?? configured[0];

            string productDirectory = Path.GetDirectoryName(productConfigPath);
            string windowsDirectory = Path.GetDirectoryName(windowsConfigPath);
            if (!string.IsNullOrWhiteSpace(productDirectory))
            {
                Directory.CreateDirectory(productDirectory);
            }

            if (!string.IsNullOrWhiteSpace(windowsDirectory))
            {
                Directory.CreateDirectory(windowsDirectory);
            }

            var utf8 = new UTF8Encoding(false);
            File.WriteAllText(
                productConfigPath,
                BuildProductJson(productName, version, configured, preferred),
                utf8);
            File.WriteAllText(
                windowsConfigPath,
                BuildWindowsJson(preferred),
                utf8);
        }

        private static List<EpicEnvironmentDefinition> GetConfiguredEnvironments(
            StoreReleaseProjectSettings settings)
        {
            return settings.EpicEnvironments.Where(IsConfigured).ToList();
        }

        private static bool IsConfigured(EpicEnvironmentDefinition environment)
        {
            return environment != null &&
                   IsValueConfigured(environment.ProductId) &&
                   IsValueConfigured(environment.SandboxId) &&
                   IsValueConfigured(environment.DeploymentId) &&
                   IsValueConfigured(environment.EosClientId) &&
                   IsValueConfigured(environment.ClientSecret) &&
                   IsValueConfigured(environment.EncryptionKey);
        }

        private static bool IsValueConfigured(string value)
        {
            string normalized = value == null ? string.Empty : value.Trim();
            return !string.IsNullOrWhiteSpace(normalized) &&
                   !normalized.StartsWith("REQUIRED_", StringComparison.OrdinalIgnoreCase) &&
                   !normalized.StartsWith("replace-", StringComparison.OrdinalIgnoreCase) &&
                   normalized.Any(character => character != '0');
        }

        private static string BuildProductJson(
            string productName,
            string version,
            IReadOnlyList<EpicEnvironmentDefinition> environments,
            EpicEnvironmentDefinition preferred)
        {
            var json = new StringBuilder(2048);
            json.AppendLine("{");
            AppendProperty(json, 1, "ProductName", productName, true);
            AppendProperty(json, 1, "ProductId", preferred.ProductId, true);
            AppendProperty(json, 1, "ProductVersion", version, true);
            json.AppendLine("  \"imported\": true,");
            json.AppendLine("  \"Clients\": [");
            json.AppendLine("    {");
            json.AppendLine("      \"Value\": {");
            AppendProperty(json, 4, "ClientId", preferred.EosClientId, true);
            AppendProperty(json, 4, "ClientSecret", preferred.ClientSecret, true);
            AppendProperty(json, 4, "EncryptionKey", preferred.EncryptionKey, false);
            json.AppendLine("      },");
            AppendProperty(
                json,
                3,
                "Name",
                CreateClientName(productName),
                false);
            json.AppendLine("    }");
            json.AppendLine("  ],");
            json.AppendLine("  \"Environments\": {");
            json.AppendLine("    \"Deployments\": [");
            for (int index = 0; index < environments.Count; index++)
            {
                EpicEnvironmentDefinition environment = environments[index];
                json.AppendLine("      {");
                json.AppendLine("        \"Value\": {");
                json.AppendLine("          \"SandboxId\": {");
                AppendProperty(json, 6, "Value", environment.SandboxId, false);
                json.AppendLine("          },");
                AppendProperty(json, 5, "DeploymentId", environment.DeploymentId, false);
                json.AppendLine("        },");
                AppendProperty(json, 4, "Name", GetEnvironmentLabel(environment), false);
                json.Append("      }");
                json.AppendLine(index + 1 < environments.Count ? "," : string.Empty);
            }

            json.AppendLine("    ],");
            json.AppendLine("    \"Sandboxes\": [");
            for (int index = 0; index < environments.Count; index++)
            {
                EpicEnvironmentDefinition environment = environments[index];
                json.AppendLine("      {");
                json.AppendLine("        \"Value\": {");
                AppendProperty(json, 5, "Value", environment.SandboxId, false);
                json.AppendLine("        },");
                AppendProperty(json, 4, "Name", GetEnvironmentLabel(environment), false);
                json.Append("      }");
                json.AppendLine(index + 1 < environments.Count ? "," : string.Empty);
            }

            json.AppendLine("    ]");
            json.AppendLine("  },");
            AppendProperty(json, 1, "schemaVersion", SchemaVersion, false);
            json.AppendLine("}");
            return json.ToString();
        }

        private static string BuildWindowsJson(EpicEnvironmentDefinition environment)
        {
            var json = new StringBuilder(1024);
            json.AppendLine("{");
            json.AppendLine("  \"deployment\": {");
            json.AppendLine("    \"SandboxId\": {");
            AppendProperty(json, 3, "Value", environment.SandboxId, false);
            json.AppendLine("    },");
            AppendProperty(json, 2, "DeploymentId", environment.DeploymentId, false);
            json.AppendLine("  },");
            json.AppendLine("  \"clientCredentials\": {");
            AppendProperty(json, 2, "ClientId", environment.EosClientId, true);
            AppendProperty(json, 2, "ClientSecret", environment.ClientSecret, true);
            AppendProperty(json, 2, "EncryptionKey", environment.EncryptionKey, false);
            json.AppendLine("  },");
            json.AppendLine("  \"isServer\": false,");
            json.AppendLine("  \"platformOptionsFlags\": \"None\",");
            json.AppendLine("  \"authScopeOptionsFlags\": \"BasicProfile\",");
            json.AppendLine("  \"integratedPlatformManagementFlags\": \"Disabled\",");
            json.AppendLine("  \"tickBudgetInMilliseconds\": 0,");
            json.AppendLine("  \"taskNetworkTimeoutSeconds\": 0.0,");
            json.AppendLine("  \"threadAffinity\": null,");
            json.AppendLine("  \"alwaysSendInputToOverlay\": false,");
            json.AppendLine("  \"initialButtonDelayForOverlay\": 0.0,");
            json.AppendLine("  \"repeatButtonDelayForOverlay\": 0.0,");
            json.AppendLine("  \"toggleFriendsButtonCombination\": \"SpecialLeft\",");
            AppendProperty(json, 1, "schemaVersion", SchemaVersion, false);
            json.AppendLine("}");
            return json.ToString();
        }

        private static void AppendProperty(
            StringBuilder json,
            int indentation,
            string name,
            string value,
            bool trailingComma)
        {
            json.Append(' ', indentation * 2);
            json.Append('"').Append(EscapeJson(name)).Append("\": \"");
            json.Append(EscapeJson(value == null ? string.Empty : value.Trim()));
            json.Append('"');
            if (trailingComma)
            {
                json.Append(',');
            }

            json.AppendLine();
        }

        private static string GetEnvironmentLabel(EpicEnvironmentDefinition environment)
        {
            switch (environment.Channel)
            {
                case StoreReleaseChannel.Development:
                    return "Dev";
                case StoreReleaseChannel.Stage:
                    return "Stage";
                case StoreReleaseChannel.Live:
                    return "Live";
                default:
                    return string.IsNullOrWhiteSpace(environment.Name)
                        ? "Environment"
                        : environment.Name.Trim();
            }
        }

        private static string CreateClientName(string productName)
        {
            string stem = new string((productName ?? string.Empty)
                .Where(char.IsLetterOrDigit)
                .ToArray());
            return (string.IsNullOrWhiteSpace(stem) ? "Game" : stem) +
                   "_ClientPolicy";
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var escaped = new StringBuilder(value.Length + 8);
            foreach (char character in value)
            {
                switch (character)
                {
                    case '\\':
                        escaped.Append("\\\\");
                        break;
                    case '"':
                        escaped.Append("\\\"");
                        break;
                    case '\b':
                        escaped.Append("\\b");
                        break;
                    case '\f':
                        escaped.Append("\\f");
                        break;
                    case '\n':
                        escaped.Append("\\n");
                        break;
                    case '\r':
                        escaped.Append("\\r");
                        break;
                    case '\t':
                        escaped.Append("\\t");
                        break;
                    default:
                        if (character < 32)
                        {
                            escaped.Append("\\u").Append(((int)character).ToString("x4"));
                        }
                        else
                        {
                            escaped.Append(character);
                        }

                        break;
                }
            }

            return escaped.ToString();
        }
    }
}
