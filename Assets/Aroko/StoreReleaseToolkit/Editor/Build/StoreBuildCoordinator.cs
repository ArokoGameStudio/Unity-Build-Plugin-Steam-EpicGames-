using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Aroko.StoreRelease.Editor.Configuration;
using Aroko.StoreRelease.Editor.Dashboard;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using EditorStorePlatform = Aroko.StoreRelease.Editor.Configuration.StorePlatform;

namespace Aroko.StoreRelease.Editor.Build
{
    [InitializeOnLoad]
    public static class StoreBuildCoordinator
    {
        [Serializable]
        private sealed class ScriptingAssembliesManifest
        {
            public string[] names = Array.Empty<string>();
            public int[] types = Array.Empty<int>();
        }

        public const string DisableSteamworksDefine = "DISABLESTEAMWORKS";
        public const string DisableEosDefine = "EOS_DISABLE";
        public const string SteamBuildDefine = "AROKO_SRT_STEAM_BUILD";
        public const string EpicBuildDefine = "AROKO_SRT_EPIC_BUILD";
        public const string SelectSteamDefine = "AROKO_SRT_SELECT_STEAM";
        public const string SelectEpicDefine = "AROKO_SRT_SELECT_EPIC";
        private const string BuildInProgressSessionKey =
            "Aroko.StoreReleaseToolkit.BuildInProgress";
        internal const string ActiveBuildStoreSessionKey =
            "Aroko.StoreReleaseToolkit.ActiveBuildStore";

        private static readonly Regex VersionPattern =
            new Regex("^[A-Za-z0-9][A-Za-z0-9._+-]{0,99}$", RegexOptions.Compiled);

        private static readonly IReadOnlyDictionary<EditorStorePlatform, IStoreEditorAdapter> Adapters =
            new Dictionary<EditorStorePlatform, IStoreEditorAdapter>
            {
                { EditorStorePlatform.Steam, new SteamBuildAdapter() },
                { EditorStorePlatform.Epic, new EpicBuildAdapter() }
            };

        static StoreBuildCoordinator()
        {
            StoreReleaseEditorHooks.ValidateHandler = Validate;
            StoreReleaseEditorHooks.BuildHandler = Build;
        }

        public static event Action<StoreOperationReport> OperationCompleted;

        /// <summary>
        /// True only while this toolkit owns a Unity player build. Project-specific
        /// build processors can use this to avoid applying a second store workflow.
        /// </summary>
        public static bool IsBuildInProgress
        {
            get => SessionState.GetBool(BuildInProgressSessionKey, false);
            private set => SessionState.SetBool(BuildInProgressSessionKey, value);
        }

        public static StoreOperationReport Validate(StoreBuildRequest request)
        {
            StoreReleaseProjectSettings settings = StoreReleaseProjectSettings.instance;
            settings.EnsureDefaults();
            var report = StoreOperationReport.Success();

            if (request == null || request.Profile == null)
            {
                report.AddIssue(Error("REQUEST_MISSING", "A build profile is required."));
                return report;
            }

            if (!Adapters.TryGetValue(request.Profile.Store, out IStoreEditorAdapter adapter))
            {
                report.AddIssue(Error(
                    "STORE_UNSUPPORTED",
                    "The selected build profile does not identify a supported store."));
                return report;
            }

            ValidateReleaseInvariants(request.Profile, settings, report.Issues);
            ValidateGlobalStoreDefines(request.Profile.Store, report.Issues);

            if (string.IsNullOrWhiteSpace(request.Version) ||
                !VersionPattern.IsMatch(request.Version))
            {
                report.AddIssue(Error(
                    "VERSION_INVALID",
                    "Version must be 1-100 characters and use letters, numbers, '.', '_', '+', or '-'."));
            }

            if (!StoreConfigurationValidator.IsSafeExecutableName(
                    settings.ExecutableName))
            {
                report.AddIssue(Error(
                    "EXECUTABLE_INVALID",
                    "Configure a safe Windows executable filename ending in .exe, not a path."));
            }

            string[] scenes = ResolveScenes();
            if (scenes.Length == 0)
            {
                report.AddIssue(Error(
                    "SCENES_MISSING",
                    "Build Settings has no enabled scenes."));
            }
            else
            {
                foreach (string scene in scenes.Where(scene => !File.Exists(scene)))
                {
                    report.AddIssue(Error(
                        "SCENE_MISSING",
                        "A configured build scene does not exist.",
                        scene));
                }
            }

            string resolvedOutput = ResolveOutputDirectory(request, settings);
            try
            {
                ValidateOutputLocation(resolvedOutput);
                report.OutputPath = Path.Combine(resolvedOutput, settings.ExecutableName);
            }
            catch (Exception exception)
            {
                report.AddIssue(Error("OUTPUT_INVALID", exception.Message, resolvedOutput));
            }

            IReadOnlyList<StoreSdkStatus> sdkStatuses = StoreSdkDetector.DetectAll();
            string expectedPackage = request.Profile.Store == EditorStorePlatform.Steam
                ? StoreSdkDetector.SteamworksPackageName
                : StoreSdkDetector.EosPackageName;
            StoreSdkStatus sdk = sdkStatuses.FirstOrDefault(
                status => string.Equals(
                    status.PackageName, expectedPackage, StringComparison.OrdinalIgnoreCase));
            if (sdk == null || sdk.Compatibility == StoreSdkCompatibility.Missing)
            {
                report.AddIssue(Error(
                    "SDK_MISSING",
                    "Install the required vendor SDK with the Setup page Download button before building.",
                    expectedPackage));
            }
            else if (sdk.Compatibility == StoreSdkCompatibility.UnsupportedOlder)
            {
                report.AddIssue(Error(
                    "SDK_TOO_OLD",
                    "The installed SDK is older than the certified version.",
                    sdk.InstalledVersion));
            }
            else if (sdk.Compatibility != StoreSdkCompatibility.Supported)
            {
                report.AddIssue(new StoreValidationIssue(
                    StoreValidationSeverity.Warning,
                    "SDK_UNVERIFIED",
                    "The installed SDK version is not the certified version.",
                    sdk.InstalledVersion));
            }

            adapter.Validate(request, settings, report.Issues);
            report.Succeeded = !report.Issues.Any(
                issue => issue.Severity == StoreValidationSeverity.Error);
            return report;
        }

        public static StoreOperationReport Build(StoreBuildRequest request)
        {
            StoreOperationReport report = Validate(request);
            if (!report.Succeeded)
            {
                Complete(report);
                return report;
            }

            StoreReleaseProjectSettings settings = StoreReleaseProjectSettings.instance;
            string outputDirectory = Path.GetDirectoryName(report.OutputPath);
            SessionState.SetInt(
                ActiveBuildStoreSessionKey,
                (int)request.Profile.Store);
            IsBuildInProgress = true;
            try
            {
                if (request.Profile.Store == EditorStorePlatform.Epic)
                {
                    EosProjectConfigurationGenerator.CreateForBuild(
                        settings,
                        request.Profile,
                        request.Version);
                }

                PrepareOutputDirectory(outputDirectory);
                string[] defines = request.Profile.Store == EditorStorePlatform.Steam
                    ? new[]
                    {
                        StoreSdkDetector.SteamworksAvailableDefine,
                        SteamBuildDefine,
                        SelectSteamDefine,
                        DisableEosDefine
                    }
                    : new[]
                    {
                        StoreSdkDetector.EosAvailableDefine,
                        EpicBuildDefine,
                        SelectEpicDefine,
                        DisableSteamworksDefine
                    };
                var buildOptions = new BuildPlayerOptions
                {
                    scenes = ResolveScenes(),
                    locationPathName = report.OutputPath,
                    target = BuildTarget.StandaloneWindows64,
                    options = request.Profile.DevelopmentBuild
                        ? BuildOptions.Development
                        : BuildOptions.None,
                    extraScriptingDefines = defines
                };

                using (StoreBuildTransaction.Begin(request))
                {
                    BuildReport unityReport = BuildPipeline.BuildPlayer(buildOptions);
                    if (unityReport.summary.result != BuildResult.Succeeded)
                    {
                        report.Succeeded = false;
                        report.AddIssue(Error(
                            "UNITY_BUILD_FAILED",
                            "Unity build failed with " + unityReport.summary.totalErrors +
                            " errors and result " + unityReport.summary.result + "."));
                    }
                    else
                    {
                        RemoveNonShippingArtifacts(outputDirectory, settings.ExecutableName);
                        Adapters[request.Profile.Store].Postprocess(
                            request, settings, report.OutputPath);
                        RemoveInactiveManagedAssemblies(
                            outputDirectory,
                            settings.ExecutableName,
                            request.Profile.Store);
                        IReadOnlyList<string> contamination =
                            StoreOutputValidator.FindInactiveStoreArtifacts(
                                outputDirectory, request.Profile.Store.ToString());
                        foreach (string issue in contamination)
                        {
                            report.AddIssue(Error("INACTIVE_SDK", issue));
                        }

                        report.Succeeded = contamination.Count == 0;
                    }
                }

            }
            catch (Exception exception)
            {
                report.Succeeded = false;
                report.AddIssue(Error("BUILD_EXCEPTION", exception.Message));
                Debug.LogException(exception);
            }
            finally
            {
                IsBuildInProgress = false;
                SessionState.EraseInt(ActiveBuildStoreSessionKey);
            }

            try
            {
                report.LogPath = WriteReport(request, report);
            }
            catch (Exception exception)
            {
                report.AddIssue(new StoreValidationIssue(
                    StoreValidationSeverity.Warning,
                    "REPORT_WRITE_FAILED",
                    "The build result could not be written to Library: " +
                    exception.Message));
            }

            Complete(report);
            return report;
        }

        public static string ResolveOutputDirectory(
            StoreBuildRequest request,
            StoreReleaseProjectSettings settings)
        {
            if (!string.IsNullOrWhiteSpace(request.OutputPath))
            {
                string explicitPath = Path.GetFullPath(request.OutputPath);
                return string.Equals(
                    Path.GetExtension(explicitPath), ".exe", StringComparison.OrdinalIgnoreCase)
                    ? Path.GetDirectoryName(explicitPath)
                    : explicitPath;
            }

            string template = string.IsNullOrWhiteSpace(request.Profile.OutputTemplate)
                ? StoreReleaseProjectSettings.DefaultOutputTemplate
                : request.Profile.OutputTemplate;
            string path = template
                .Replace("{Store}", request.Profile.Store.ToString())
                .Replace("{Profile}", SanitizePathSegment(request.Profile.Id))
                .Replace("{Version}", SanitizePathSegment(request.Version));
            return Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(ProjectRoot, path));
        }

        private static void PrepareOutputDirectory(string outputDirectory)
        {
            ValidateOutputLocation(outputDirectory);
            string managedBuildRoot = Path.GetFullPath(
                Path.Combine(ProjectRoot, "Builds", "StoreReleaseToolkit"))
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string fullOutput = Path.GetFullPath(outputDirectory);
            EnsureNoReparsePointsInExistingPath(fullOutput);

            if (Directory.Exists(fullOutput) &&
                Directory.EnumerateFileSystemEntries(fullOutput).Any())
            {
                if (!fullOutput.StartsWith(managedBuildRoot, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "A custom output directory is not empty. The toolkit will only replace " +
                        "versioned outputs under Builds/StoreReleaseToolkit.");
                }

                EnsureNoReparsePointsForDeletion(
                    fullOutput,
                    managedBuildRoot.TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar));
                Directory.Delete(fullOutput, true);
            }

            Directory.CreateDirectory(fullOutput);
        }

        private static void EnsureNoReparsePointsInExistingPath(string path)
        {
            var current = new DirectoryInfo(path);
            while (current != null)
            {
                if (current.Exists &&
                    (current.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException(
                        "Refusing to build through a symbolic link or junction: " +
                        current.FullName);
                }

                current = current.Parent;
            }
        }

        private static void EnsureNoReparsePointsForDeletion(
            string outputDirectory,
            string managedBuildRoot)
        {
            var current = new DirectoryInfo(outputDirectory);
            while (current != null)
            {
                string currentPath = Path.GetFullPath(current.FullName)
                    .TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar);
                if (current.Exists &&
                    (current.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException(
                        "Refusing to replace a build output through a symbolic link or junction: " +
                        currentPath);
                }

                if (string.Equals(
                        currentPath,
                        managedBuildRoot,
                        StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                current = current.Parent;
            }

            var pending = new Stack<string>();
            pending.Push(outputDirectory);
            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                foreach (string entry in
                         Directory.EnumerateFileSystemEntries(
                             directory,
                             "*",
                             SearchOption.TopDirectoryOnly))
                {
                    FileAttributes attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new InvalidOperationException(
                            "Refusing to replace a build output that contains a symbolic link or junction: " +
                            entry);
                    }

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        pending.Push(entry);
                    }
                }
            }
        }

        private static void ValidateOutputLocation(string outputDirectory)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                throw new InvalidOperationException("Build output directory is empty.");
            }

            string full = Path.GetFullPath(outputDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string root = Path.GetPathRoot(full)?.TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(full, ProjectRoot.TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Refusing to use a drive or project root as build output.");
            }

            string[] protectedDirectories =
            {
                Application.dataPath,
                Path.Combine(ProjectRoot, "Packages"),
                Path.Combine(ProjectRoot, "ProjectSettings"),
                Path.Combine(ProjectRoot, "UserSettings"),
                Path.Combine(ProjectRoot, "Library")
            };
            foreach (string protectedDirectory in protectedDirectories)
            {
                string normalized = Path.GetFullPath(protectedDirectory)
                    .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if ((full + Path.DirectorySeparatorChar).StartsWith(
                        normalized, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "Build output cannot be inside " + protectedDirectory + ".");
                }
            }
        }

        private static string[] ResolveScenes()
        {
            return EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
        }

        private static void ValidateReleaseInvariants(
            StoreBuildProfile profile,
            StoreReleaseProjectSettings settings,
            ICollection<StoreValidationIssue> issues)
        {
            bool expectedDevelopment =
                profile.Channel == StoreReleaseChannel.Development;
            if (profile.DevelopmentBuild != expectedDevelopment)
            {
                issues.Add(Error(
                    "RELEASE_CHANNEL_MISMATCH",
                    "Development profiles must use Development Build; " +
                    "Stage and Live profiles must use a release build.",
                    profile.DisplayName));
            }

            if (profile.Store == EditorStorePlatform.Steam &&
                profile.Channel == StoreReleaseChannel.Stage)
            {
                issues.Add(Error(
                    "STEAM_CHANNEL_UNSUPPORTED",
                    "Steam profiles support Development or Live.",
                    profile.DisplayName));
            }

            if (profile.Store == EditorStorePlatform.Epic)
            {
                EpicEnvironmentDefinition environment =
                    settings.GetEpicEnvironment(profile.EpicEnvironmentName);
                if (environment != null && environment.Channel != profile.Channel)
                {
                    issues.Add(Error(
                        "EPIC_CHANNEL_MISMATCH",
                        "The selected Epic environment channel does not match the build profile.",
                        profile.DisplayName));
                }
            }
        }

        private static void ValidateGlobalStoreDefines(
            EditorStorePlatform selectedStore,
            ICollection<StoreValidationIssue> issues)
        {
#pragma warning disable 618
            string configured =
                PlayerSettings.GetScriptingDefineSymbolsForGroup(
                    BuildTargetGroup.Standalone);
#pragma warning restore 618
            var symbols = new HashSet<string>(
                (configured ?? string.Empty)
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(symbol => symbol.Trim()),
                StringComparer.Ordinal);

            string activeSdkDisable =
                selectedStore == EditorStorePlatform.Steam
                    ? DisableSteamworksDefine
                    : DisableEosDefine;
            if (symbols.Contains(activeSdkDisable))
            {
                issues.Add(Error(
                    "ACTIVE_SDK_DISABLED",
                    "Remove " + activeSdkDisable +
                    " from Player Settings before building this store. " +
                    "The toolkit applies inactive-SDK disable symbols per build."));
            }
        }

        private static void RemoveNonShippingArtifacts(
            string outputDirectory, string executableName)
        {
            string baseName = Path.GetFileNameWithoutExtension(executableName);
            string[] names =
            {
                baseName + "_BurstDebugInformation_DoNotShip",
                baseName + "_BackUpThisFolder_ButDontShipItWithYourGame"
            };
            foreach (string name in names)
            {
                string path = Path.Combine(outputDirectory, name);
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
        }

        private static void RemoveInactiveManagedAssemblies(
            string outputDirectory,
            string executableName,
            EditorStorePlatform activeStore)
        {
            string[] inactiveTokens = activeStore == EditorStorePlatform.Steam
                ? new[]
                {
                    "com.epic.onlineservices",
                    "com.playeveryware.eos",
                    "aroko.storerelease.epic"
                }
                : new[]
                {
                    "com.rlabrecque.steamworks.net",
                    "com.playeveryware.eos-editor.steam.utility",
                    "aroko.storerelease.steam"
                };
            string dataDirectory = Path.Combine(
                outputDirectory,
                Path.GetFileNameWithoutExtension(executableName) + "_Data");
            string manifestPath = Path.Combine(dataDirectory, "ScriptingAssemblies.json");
            if (File.Exists(manifestPath))
            {
                ScriptingAssembliesManifest manifest =
                    JsonUtility.FromJson<ScriptingAssembliesManifest>(
                        File.ReadAllText(manifestPath));
                if (manifest == null || manifest.names == null || manifest.types == null ||
                    manifest.names.Length != manifest.types.Length)
                {
                    throw new InvalidOperationException(
                        "Unity's ScriptingAssemblies.json could not be filtered safely.");
                }

                var retainedNames = new List<string>(manifest.names.Length);
                var retainedTypes = new List<int>(manifest.types.Length);
                for (int index = 0; index < manifest.names.Length; index++)
                {
                    if (ContainsInactiveToken(manifest.names[index], inactiveTokens))
                    {
                        continue;
                    }

                    retainedNames.Add(manifest.names[index]);
                    retainedTypes.Add(manifest.types[index]);
                }

                manifest.names = retainedNames.ToArray();
                manifest.types = retainedTypes.ToArray();
                File.WriteAllText(
                    manifestPath,
                    JsonUtility.ToJson(manifest),
                    new System.Text.UTF8Encoding(false));
            }

            string managedDirectory = Path.Combine(dataDirectory, "Managed");
            if (!Directory.Exists(managedDirectory))
            {
                return;
            }

            foreach (string path in Directory.EnumerateFiles(
                         managedDirectory,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                if (ContainsInactiveToken(Path.GetFileName(path), inactiveTokens))
                {
                    File.Delete(path);
                }
            }
        }

        private static bool ContainsInactiveToken(string value, string[] tokens)
        {
            return tokens.Any(token =>
                (value ?? string.Empty).IndexOf(
                    token,
                    StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string WriteReport(
            StoreBuildRequest request, StoreOperationReport report)
        {
            string reportDirectory = Path.Combine(
                ProjectRoot, "Library", "StoreReleaseToolkit", "Reports");
            Directory.CreateDirectory(reportDirectory);
            string fileName = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" +
                              SanitizePathSegment(request.Profile.Id) + "-" +
                              Guid.NewGuid().ToString("N").Substring(0, 8) +
                              ".json";
            string path = Path.Combine(reportDirectory, fileName);
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            return path;
        }

        private static void Complete(StoreOperationReport report)
        {
            if (report.Succeeded)
            {
                Debug.Log("Store Release Toolkit operation succeeded: " + report.OutputPath);
            }
            else
            {
                Debug.LogError(
                    "Store Release Toolkit operation failed:\n" +
                    string.Join("\n", report.Issues.Select(issue => issue.ToString())));
            }

            Action<StoreOperationReport> handlers = OperationCompleted;
            if (handlers == null)
            {
                return;
            }

            foreach (Action<StoreOperationReport> handler in
                     handlers.GetInvocationList())
            {
                try
                {
                    handler(report);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        private static StoreValidationIssue Error(
            string code, string message, string context = "")
        {
            return new StoreValidationIssue(
                StoreValidationSeverity.Error, code, message, context);
        }

        private static string SanitizePathSegment(string value)
        {
            string sanitized = string.IsNullOrWhiteSpace(value) ? "unnamed" : value.Trim();
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                sanitized = sanitized.Replace(invalid, '_');
            }

            return sanitized.Replace(' ', '-');
        }

        internal static string ProjectRoot =>
            Directory.GetParent(Application.dataPath)?.FullName ??
            throw new InvalidOperationException("Unity project root is unavailable.");
    }

    internal sealed class SteamBuildAdapter : IStoreEditorAdapter
    {
        public EditorStorePlatform Store => EditorStorePlatform.Steam;

        public void Validate(
            StoreBuildRequest request,
            StoreReleaseProjectSettings settings,
            IList<StoreValidationIssue> issues)
        {
            if (!Regex.IsMatch(settings.SteamAppId ?? string.Empty, "^[1-9][0-9]{1,19}$"))
            {
                issues.Add(new StoreValidationIssue(
                    StoreValidationSeverity.Error,
                    "STEAM_APP_ID",
                    "Steam App ID must be a positive numeric identifier."));
            }

        }

        public void Postprocess(
            StoreBuildRequest request,
            StoreReleaseProjectSettings settings,
            string executablePath)
        {
            string outputDirectory = Path.GetDirectoryName(executablePath);
            string eosStreamingAssets = Path.Combine(
                outputDirectory,
                Path.GetFileNameWithoutExtension(settings.ExecutableName) + "_Data",
                "StreamingAssets",
                "EOS");
            if (Directory.Exists(eosStreamingAssets))
            {
                Directory.Delete(eosStreamingAssets, true);
            }

            foreach (string eosBootstrapperName in new[]
                     {
                         "EOSBootstrapper.exe",
                         "EOSBootstrapper.ini",
                         "EOSBootstrapper.ico"
                     })
            {
                string eosBootstrapperPath =
                    Path.Combine(outputDirectory, eosBootstrapperName);
                if (File.Exists(eosBootstrapperPath))
                {
                    File.Delete(eosBootstrapperPath);
                }
            }

            string appIdPath = Path.Combine(outputDirectory, "steam_appid.txt");
            File.WriteAllText(appIdPath, settings.SteamAppId.Trim());
        }
    }

    internal sealed class EpicBuildAdapter : IStoreEditorAdapter
    {
        public EditorStorePlatform Store => EditorStorePlatform.Epic;

        public void Validate(
            StoreBuildRequest request,
            StoreReleaseProjectSettings settings,
            IList<StoreValidationIssue> issues)
        {
            EpicEnvironmentDefinition environment =
                settings.GetEpicEnvironment(request.Profile.EpicEnvironmentName);
            if (environment == null)
            {
                issues.Add(new StoreValidationIssue(
                    StoreValidationSeverity.Error,
                    "EPIC_ENVIRONMENT",
                    "The selected Epic environment is missing.",
                    request.Profile.EpicEnvironmentName));
                return;
            }

            Require(environment.ProductId, "EPIC_PRODUCT", "Epic product ID", issues);
            Require(environment.SandboxId, "EPIC_SANDBOX", "Epic sandbox ID", issues);
            Require(environment.DeploymentId, "EPIC_DEPLOYMENT", "Epic deployment ID", issues);
            Require(environment.EosClientId, "EPIC_EOS_CLIENT", "EOS client ID", issues);
            Require(
                environment.ClientSecret,
                "EPIC_EOS_CLIENT_SECRET",
                "EOS launcher client secret",
                issues);
            Require(
                environment.EncryptionKey,
                "EPIC_ENCRYPTION_KEY",
                "EOS encryption key",
                issues);

            if (!string.IsNullOrWhiteSpace(environment.SandboxId) &&
                !StoreConfigurationValidator.IsValidEpicSandboxId(environment.SandboxId))
            {
                issues.Add(new StoreValidationIssue(
                    StoreValidationSeverity.Error,
                    "EPIC_SANDBOX_FORMAT",
                    "Epic Sandbox ID must be a GUID or 'p-' followed by exactly 30 letters or numbers.",
                    environment.SandboxId));
            }

            if (!string.IsNullOrWhiteSpace(environment.DeploymentId) &&
                !StoreConfigurationValidator.IsValidEpicDeploymentId(environment.DeploymentId))
            {
                issues.Add(new StoreValidationIssue(
                    StoreValidationSeverity.Error,
                    "EPIC_DEPLOYMENT_FORMAT",
                    "Epic Deployment ID must be a non-empty GUID.",
                    environment.DeploymentId));
            }

            if (string.IsNullOrWhiteSpace(settings.WindowsIconAssetPath) ||
                !File.Exists(Path.GetFullPath(settings.WindowsIconAssetPath)))
            {
                issues.Add(new StoreValidationIssue(
                    StoreValidationSeverity.Error,
                    "EPIC_ICON",
                    "Configure an ICO or square texture for the Epic desktop shortcut.",
                    settings.WindowsIconAssetPath));
            }

            ValidateTemplate(
                settings.EpicProductConfigTemplatePath,
                EosConfigurationUtility.ProductConfigFileName,
                issues);
            ValidateTemplate(
                settings.EpicWindowsConfigTemplatePath,
                EosConfigurationUtility.WindowsConfigFileName,
                issues);
        }

        public void Postprocess(
            StoreBuildRequest request,
            StoreReleaseProjectSettings settings,
            string executablePath)
        {
            string outputDirectory = Path.GetDirectoryName(executablePath);
            EpicEnvironmentDefinition environment =
                settings.GetEpicEnvironment(request.Profile.EpicEnvironmentName) ??
                throw new InvalidOperationException(
                    "Epic environment disappeared during the build.");

            string eosOutput = EosConfigurationUtility.GetOutputDirectory(
                outputDirectory, settings.ExecutableName);
            string productTemplate = ToAbsolute(settings.EpicProductConfigTemplatePath);
            string windowsTemplate = ToAbsolute(settings.EpicWindowsConfigTemplatePath);
            if (!File.Exists(Path.Combine(
                    eosOutput, EosConfigurationUtility.ProductConfigFileName)) ||
                !File.Exists(Path.Combine(
                    eosOutput, EosConfigurationUtility.WindowsConfigFileName)))
            {
                EosConfigurationUtility.EnsureOutputFiles(
                    eosOutput, productTemplate, windowsTemplate);
            }

            EosConfigurationUtility.ConfigureOutput(
                eosOutput, environment, request.Version);
            EosConfigurationUtility.ValidateOutput(
                eosOutput, environment, request.Version);

            string bootstrapper = Directory.EnumerateFiles(
                    outputDirectory, "EOSBootstrapper.exe", SearchOption.TopDirectoryOnly)
                .FirstOrDefault();
            if (environment.UseBootstrapper && string.IsNullOrWhiteSpace(bootstrapper))
            {
                throw new InvalidOperationException(
                    "The selected Epic profile requires EOSBootstrapper.exe, " +
                    "but the build did not contain it.");
            }

            string launchExecutable = environment.UseBootstrapper
                ? bootstrapper
                : executablePath;
            string iconPath = Path.ChangeExtension(launchExecutable, ".ico");
            WindowsIconWriter.WriteFromAsset(settings.WindowsIconAssetPath, iconPath);
            WindowsIconWriter.Validate(iconPath);

            string steamAppId = Path.Combine(outputDirectory, "steam_appid.txt");
            if (File.Exists(steamAppId))
            {
                File.Delete(steamAppId);
            }

            string steamworksNotice = Path.Combine(
                outputDirectory,
                Path.GetFileNameWithoutExtension(settings.ExecutableName) + "_Data",
                "Plugins",
                "Steamworks.NET.txt");
            if (File.Exists(steamworksNotice))
            {
                File.Delete(steamworksNotice);
            }
        }

        private static void Require(
            string value,
            string code,
            string label,
            IList<StoreValidationIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                Regex.IsMatch(value, "^(REQUIRED_|replace-|0{16,})",
                    RegexOptions.IgnoreCase))
            {
                issues.Add(new StoreValidationIssue(
                    StoreValidationSeverity.Error,
                    code,
                    label + " is not configured."));
            }
        }

        private static void ValidateTemplate(
            string configuredPath,
            string expectedName,
            IList<StoreValidationIssue> issues)
        {
            string path = ToAbsolute(configuredPath);
            if (!File.Exists(path))
            {
                issues.Add(new StoreValidationIssue(
                    StoreValidationSeverity.Error,
                    "EPIC_CONFIG_TEMPLATE",
                    "EOS configuration template is missing.",
                    configuredPath));
            }
            else if (!string.Equals(
                         Path.GetFileName(path), expectedName,
                         StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new StoreValidationIssue(
                    StoreValidationSeverity.Warning,
                    "EPIC_CONFIG_NAME",
                    "EOS template has a non-standard filename.",
                    configuredPath));
            }
        }

        private static string ToAbsolute(string path)
        {
            return Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(StoreBuildCoordinator.ProjectRoot, path ?? string.Empty));
        }
    }
}
