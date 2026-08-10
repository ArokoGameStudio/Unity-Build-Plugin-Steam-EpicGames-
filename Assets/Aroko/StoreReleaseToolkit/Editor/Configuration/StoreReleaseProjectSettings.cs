using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Aroko.StoreRelease.Editor.Configuration
{
    [FilePath(
        "ProjectSettings/StoreReleaseToolkitSettings.asset",
        FilePathAttribute.Location.ProjectFolder)]
    public sealed class StoreReleaseProjectSettings : ScriptableSingleton<StoreReleaseProjectSettings>
    {
        public const int CurrentSchemaVersion = 2;
        public const string DefaultEpicProductConfigTemplatePath =
            "Assets/StreamingAssets/EOS/eos_product_config.json";
        public const string DefaultEpicWindowsConfigTemplatePath =
            "Assets/StreamingAssets/EOS/eos_windows_config.json";
        public const string DefaultOutputTemplate =
            "Builds/StoreReleaseToolkit/{Store}/{Profile}/{Version}";

        [SerializeField] private int schemaVersion = CurrentSchemaVersion;
        [SerializeField] private string defaultVersion = "1.0.0";
        [SerializeField] private string executableName = string.Empty;
        [SerializeField] private string windowsIconAssetPath = string.Empty;
        [SerializeField] private string epicProductConfigTemplatePath =
            DefaultEpicProductConfigTemplatePath;
        [SerializeField] private string epicWindowsConfigTemplatePath =
            DefaultEpicWindowsConfigTemplatePath;
        [SerializeField] private string activeProfileId = "steam-release";
        [SerializeField] private string steamAppId = string.Empty;
        [SerializeField] private List<StoreBuildProfile> profiles = new List<StoreBuildProfile>();
        [SerializeField] private List<EpicEnvironmentDefinition> epicEnvironments =
            new List<EpicEnvironmentDefinition>();

        public int SchemaVersion => schemaVersion;

        public string DefaultVersion
        {
            get => defaultVersion;
            set => defaultVersion = string.IsNullOrWhiteSpace(value) ? "1.0.0" : value.Trim();
        }

        public string ExecutableName
        {
            get => executableName;
            set => executableName = EnsureExecutableExtension(value);
        }

        public string WindowsIconAssetPath
        {
            get => windowsIconAssetPath;
            set => windowsIconAssetPath = NormalizeOptionalAssetPath(value);
        }

        public string EpicProductConfigTemplatePath
        {
            get => epicProductConfigTemplatePath;
            set => epicProductConfigTemplatePath = NormalizeProjectPath(
                value,
                DefaultEpicProductConfigTemplatePath);
        }

        public string EpicWindowsConfigTemplatePath
        {
            get => epicWindowsConfigTemplatePath;
            set => epicWindowsConfigTemplatePath = NormalizeProjectPath(
                value,
                DefaultEpicWindowsConfigTemplatePath);
        }

        public string ActiveProfileId
        {
            get => activeProfileId;
            set => activeProfileId = value ?? string.Empty;
        }

        public string SteamAppId
        {
            get => steamAppId;
            set => steamAppId = value == null ? string.Empty : value.Trim();
        }

        public List<StoreBuildProfile> Profiles
        {
            get
            {
                if (profiles == null)
                {
                    profiles = new List<StoreBuildProfile>();
                }

                return profiles;
            }
        }

        public List<EpicEnvironmentDefinition> EpicEnvironments
        {
            get
            {
                if (epicEnvironments == null)
                {
                    epicEnvironments = new List<EpicEnvironmentDefinition>();
                }

                return epicEnvironments;
            }
        }

        public bool EnsureDefaults()
        {
            bool changed = false;

            if (schemaVersion != CurrentSchemaVersion)
            {
                schemaVersion = CurrentSchemaVersion;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(defaultVersion))
            {
                defaultVersion = "1.0.0";
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(executableName))
            {
                executableName = EnsureExecutableExtension(Application.productName);
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(epicProductConfigTemplatePath))
            {
                epicProductConfigTemplatePath = DefaultEpicProductConfigTemplatePath;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(epicWindowsConfigTemplatePath))
            {
                epicWindowsConfigTemplatePath = DefaultEpicWindowsConfigTemplatePath;
                changed = true;
            }

            var defaultProfiles = new List<StoreBuildProfile>
            {
                CreateProfile(
                    "steam-release",
                    "Steam Release",
                    StorePlatform.Steam,
                    StoreReleaseChannel.Live,
                    false),
                CreateProfile(
                    "epic-development",
                    "Epic Development",
                    StorePlatform.Epic,
                    StoreReleaseChannel.Development,
                    true,
                    "Epic Development"),
                CreateProfile(
                    "epic-stage",
                    "Epic Stage",
                    StorePlatform.Epic,
                    StoreReleaseChannel.Stage,
                    false,
                    "Epic Stage"),
                CreateProfile(
                    "epic-live",
                    "Epic Live",
                    StorePlatform.Epic,
                    StoreReleaseChannel.Live,
                    false,
                    "Epic Live")
            };
            if (!ProfilesMatchDefaults(profiles, defaultProfiles))
            {
                profiles = defaultProfiles;
                changed = true;
            }

            changed |= EnsureEpicEnvironment("Epic Development", StoreReleaseChannel.Development);
            changed |= EnsureEpicEnvironment("Epic Stage", StoreReleaseChannel.Stage);
            changed |= EnsureEpicEnvironment("Epic Live", StoreReleaseChannel.Live);

            string sharedEncryptionKey = EpicEnvironments
                .Where(environment => environment != null)
                .Select(environment => environment.EncryptionKey)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (string.IsNullOrWhiteSpace(sharedEncryptionKey))
            {
                sharedEncryptionKey = GenerateEncryptionKey();
            }

            foreach (EpicEnvironmentDefinition environment in EpicEnvironments)
            {
                if (environment != null &&
                    string.IsNullOrWhiteSpace(environment.EncryptionKey))
                {
                    environment.EncryptionKey = sharedEncryptionKey;
                    changed = true;
                }
            }

            if (string.IsNullOrWhiteSpace(activeProfileId) || GetProfile(activeProfileId) == null)
            {
                activeProfileId = "steam-release";
                changed = true;
            }

            return changed;
        }

        public StoreBuildProfile GetProfile(string id)
        {
            return Profiles.FirstOrDefault(
                profile => profile != null &&
                           string.Equals(profile.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        public EpicEnvironmentDefinition GetEpicEnvironment(string name)
        {
            return EpicEnvironments.FirstOrDefault(
                environment => environment != null &&
                               string.Equals(
                                   environment.Name,
                                   name,
                                   StringComparison.OrdinalIgnoreCase));
        }

        public void SaveSettings()
        {
            EnsureDefaults();
            Save(true);
        }

        private static bool ProfilesMatchDefaults(
            IReadOnlyList<StoreBuildProfile> current,
            IReadOnlyList<StoreBuildProfile> defaults)
        {
            if (current == null || current.Count != defaults.Count)
            {
                return false;
            }

            for (int index = 0; index < defaults.Count; index++)
            {
                StoreBuildProfile actual = current[index];
                StoreBuildProfile expected = defaults[index];
                if (actual == null ||
                    !string.Equals(actual.Id, expected.Id, StringComparison.Ordinal) ||
                    !string.Equals(
                        actual.DisplayName,
                        expected.DisplayName,
                        StringComparison.Ordinal) ||
                    actual.Store != expected.Store ||
                    actual.Channel != expected.Channel ||
                    actual.DevelopmentBuild != expected.DevelopmentBuild ||
                    !string.Equals(
                        actual.OutputTemplate,
                        expected.OutputTemplate,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        actual.EpicEnvironmentName,
                        expected.EpicEnvironmentName,
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private bool EnsureEpicEnvironment(string name, StoreReleaseChannel channel)
        {
            if (GetEpicEnvironment(name) != null)
            {
                return false;
            }

            EpicEnvironments.Add(new EpicEnvironmentDefinition
            {
                Name = name,
                Channel = channel
            });
            return true;
        }

        private static StoreBuildProfile CreateProfile(
            string id,
            string displayName,
            StorePlatform store,
            StoreReleaseChannel channel,
            bool developmentBuild,
            string epicEnvironmentName = "")
        {
            return new StoreBuildProfile
            {
                Id = id,
                DisplayName = displayName,
                Store = store,
                Channel = channel,
                DevelopmentBuild = developmentBuild,
                OutputTemplate = DefaultOutputTemplate,
                EpicEnvironmentName = epicEnvironmentName
            };
        }

        private static string GenerateEncryptionKey()
        {
            var bytes = new byte[32];
            using (RandomNumberGenerator generator = RandomNumberGenerator.Create())
            {
                generator.GetBytes(bytes);
            }

            var key = new StringBuilder(64);
            foreach (byte value in bytes)
            {
                key.Append(value.ToString("x2"));
            }

            return key.ToString();
        }

        private static string EnsureExecutableExtension(string value)
        {
            string fileName = string.IsNullOrWhiteSpace(value) ? "Game" : value.Trim();
            foreach (char invalidCharacter in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(invalidCharacter, '_');
            }

            return fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? fileName
                : fileName + ".exe";
        }

        private static string NormalizeOptionalAssetPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string path = value.Trim().Replace('\\', '/');
            if (Path.IsPathRooted(path))
            {
                string projectRoot = Directory.GetParent(Application.dataPath).FullName
                    .Replace('\\', '/')
                    .TrimEnd('/');
                if (path.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase))
                {
                    return path.Substring(projectRoot.Length + 1);
                }
            }

            return path;
        }

        private static string NormalizeProjectPath(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            string path = value.Trim().Replace('\\', '/');
            if (Path.IsPathRooted(path))
            {
                string projectRoot = Directory.GetParent(Application.dataPath).FullName
                    .Replace('\\', '/')
                    .TrimEnd('/');
                if (path.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase))
                {
                    return path.Substring(projectRoot.Length + 1);
                }
            }

            return path;
        }
    }
}
