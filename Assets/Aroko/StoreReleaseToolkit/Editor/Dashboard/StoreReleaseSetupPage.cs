using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Aroko.StoreRelease.Editor.Configuration;
using UnityEditor;
using UnityEngine;

namespace Aroko.StoreRelease.Editor.Dashboard
{
    internal sealed class StoreReleaseSetupPage : IStoreReleaseDashboardPage, IDisposable
    {
        private const string ManagedIconDirectory = "Assets/StoreReleaseToolkit/Icons";

        private IReadOnlyList<StoreSdkStatus> sdkStatuses = Array.Empty<StoreSdkStatus>();
        private bool showEpicEnvironments = true;
        private bool[] showEpicAdvanced = new bool[3];
        private string epicConfigStatusMessage = string.Empty;
        private bool epicConfigOperationFailed;
        private StoreReleaseDashboardContext activeContext;

        public string Title => "Setup";

        public void OnActivated(StoreReleaseDashboardContext context)
        {
            activeContext = context;
            StoreSdkInstaller.Changed -= OnInstallerChanged;
            StoreSdkInstaller.Changed += OnInstallerChanged;
            if (context.ProjectSettings.EnsureDefaults())
            {
                context.ProjectSettings.SaveSettings();
            }

            sdkStatuses = StoreSdkDetector.DetectAll();
            context.RefreshSetupReadiness(sdkStatuses);
        }

        public void Draw(StoreReleaseDashboardContext context)
        {
            DrawSdkStatus();
            DrawProjectSettings(context);
            DrawNextStep(context);
        }

        public void Dispose()
        {
            StoreSdkInstaller.Changed -= OnInstallerChanged;
            activeContext = null;
        }

        private void DrawSdkStatus()
        {
            DashboardGui.DrawSectionHeader(
                "1. Vendor SDKs",
                "The toolkit detects vendor SDKs for store-specific packaging but never signs in to vendor accounts.");
            foreach (StoreSdkStatus status in sdkStatuses)
            {
                using (new EditorGUILayout.VerticalScope(DashboardGui.CompactCardStyle))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(status.DisplayName, EditorStyles.boldLabel);
                        GUILayout.FlexibleSpace();
                        DashboardGui.DrawBadge(
                            status.Compatibility.ToString().ToUpperInvariant(),
                            status.Compatibility == StoreSdkCompatibility.Supported
                                ? DashboardGui.BadgeTone.Success
                                : status.Compatibility == StoreSdkCompatibility.Missing
                                    ? DashboardGui.BadgeTone.Danger
                                    : DashboardGui.BadgeTone.Warning);
                    }

                    DashboardGui.DrawKeyValue("Package", status.PackageName);
                    DashboardGui.DrawKeyValue(
                        "Installed",
                        string.IsNullOrWhiteSpace(status.InstalledVersion)
                            ? "Not detected"
                            : status.InstalledVersion);
                    DashboardGui.DrawKeyValue("Tested", status.TestedVersion);

                    string installUrl = StoreSdkDetector.GetInstallUrl(
                        status.PackageName);
                    if (!string.IsNullOrWhiteSpace(installUrl))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.FlexibleSpace();
                            bool alreadyInstalled =
                                status.Compatibility == StoreSdkCompatibility.Supported;
                            using (new EditorGUI.DisabledScope(
                                       alreadyInstalled || StoreSdkInstaller.IsBusy))
                            {
                                if (GUILayout.Button(
                                        DashboardGui.Content(
                                            "Download",
                                            alreadyInstalled
                                                ? "The supported SDK version is already installed."
                                                : "Download and install with Unity Package Manager from: " +
                                                  installUrl),
                                        GUILayout.Width(105f)))
                                {
                                    StoreSdkInstaller.Install(status.PackageName);
                                }
                            }
                        }
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(StoreSdkInstaller.StatusMessage))
            {
                EditorGUILayout.HelpBox(
                    StoreSdkInstaller.StatusMessage,
                    StoreSdkInstaller.LastOperationFailed
                        ? MessageType.Error
                        : MessageType.Info);
            }

            if (GUILayout.Button("Refresh SDK Detection", GUILayout.Width(165f)))
            {
                sdkStatuses = StoreSdkDetector.DetectAll();
            }
        }

        private void OnInstallerChanged()
        {
            if (!StoreSdkInstaller.IsBusy)
            {
                sdkStatuses = StoreSdkDetector.DetectAll();
                activeContext?.RefreshSetupReadiness(sdkStatuses);
            }

            activeContext?.Repaint();
        }

        private void DrawProjectSettings(StoreReleaseDashboardContext context)
        {
            StoreReleaseProjectSettings settings = context.ProjectSettings;
            DashboardGui.DrawSectionHeader(
                "2. Shared build settings",
                "These source-controlled values define local validation and store-specific build output.");

            using (new EditorGUILayout.VerticalScope(DashboardGui.CardStyle))
            {
                string previousDefaultVersion = settings.DefaultVersion;
                EditorGUI.BeginChangeCheck();
                using (new EditorGUILayout.VerticalScope(DashboardGui.CompactCardStyle))
                {
                    DrawSettingsCardHeader(
                        "Build identity",
                        "Version, executable, and Windows presentation used by every store build.",
                        "SHARED",
                        DashboardGui.BadgeTone.Info);
                    settings.DefaultVersion = EditorGUILayout.TextField(
                        "Default Version",
                        settings.DefaultVersion);
                    settings.ExecutableName = EditorGUILayout.TextField(
                        "Executable Name",
                        settings.ExecutableName);
                    settings.WindowsIconAssetPath = DrawFilePath(
                        "Windows Icon",
                        settings.WindowsIconAssetPath,
                        "ico");
                }

                GUILayout.Space(4f);
                using (new EditorGUILayout.VerticalScope(DashboardGui.CompactCardStyle))
                {
                    bool steamConfigured = long.TryParse(
                                               settings.SteamAppId,
                                               out long parsedSteamAppId) &&
                                           parsedSteamAppId > 0;
                    DrawSettingsCardHeader(
                        "Steam",
                        "The public App ID included in the Steam release build.",
                        steamConfigured ? "READY" : "SETUP REQUIRED",
                        steamConfigured
                            ? DashboardGui.BadgeTone.Success
                            : DashboardGui.BadgeTone.Warning);
                    Color previousBackground = GUI.backgroundColor;
                    if (!steamConfigured)
                    {
                        GUI.backgroundColor = new Color(1f, 0.68f, 0.52f);
                    }

                    settings.SteamAppId = EditorGUILayout.TextField(
                        new GUIContent(
                            "Steam App ID *",
                            "Public numeric Steam application ID used by Steam builds."),
                        settings.SteamAppId);
                    GUI.backgroundColor = previousBackground;
                }

                if (EditorGUI.EndChangeCheck())
                {
                    settings.SaveSettings();
                    if (string.IsNullOrWhiteSpace(context.Version) ||
                        string.Equals(context.Version, previousDefaultVersion, StringComparison.Ordinal))
                    {
                        context.Version = settings.DefaultVersion;
                    }
                }

                var serializedSettings = new SerializedObject(settings);
                serializedSettings.Update();
                GUILayout.Space(8f);
                int readyEnvironmentCount = CountReadyEpicEnvironments(
                    serializedSettings.FindProperty("epicEnvironments"));
                using (new EditorGUILayout.HorizontalScope())
                {
                    showEpicEnvironments = EditorGUILayout.Foldout(
                        showEpicEnvironments,
                        new GUIContent(
                            "Epic environments",
                            "EOS packaging values for each Epic build channel."),
                        true);
                    GUILayout.FlexibleSpace();
                    DashboardGui.DrawBadge(
                        readyEnvironmentCount + " / 3 READY",
                        readyEnvironmentCount > 0
                            ? DashboardGui.BadgeTone.Success
                            : DashboardGui.BadgeTone.Neutral);
                }

                if (showEpicEnvironments)
                {
                    EditorGUILayout.HelpBox(
                        "Only complete the required fields for Epic environments you plan to build. " +
                        "Development, Stage, or Live environments that you do not use can remain empty.",
                        MessageType.Info);
                    DrawEpicEnvironments(
                        serializedSettings.FindProperty("epicEnvironments"));
                }

                if (serializedSettings.ApplyModifiedProperties())
                {
                    settings.SaveSettings();
                }

                DrawEpicConfigurationFiles(context, settings);

                context.RefreshSetupReadiness(sdkStatuses);
            }
        }

        private void DrawEpicEnvironments(SerializedProperty environments)
        {
            if (environments == null || !environments.isArray)
            {
                return;
            }

            if (showEpicAdvanced.Length != environments.arraySize)
            {
                Array.Resize(ref showEpicAdvanced, environments.arraySize);
            }

            for (int index = 0; index < environments.arraySize; index++)
            {
                SerializedProperty environment = environments.GetArrayElementAtIndex(index);
                SerializedProperty name = environment.FindPropertyRelative("name");
                string title = name == null || string.IsNullOrWhiteSpace(name.stringValue)
                    ? "Epic Environment"
                    : name.stringValue;

                using (new EditorGUILayout.VerticalScope(DashboardGui.CompactCardStyle))
                {
                    int missingFieldCount = CountMissingRequiredEpicFields(environment);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        environment.isExpanded = EditorGUILayout.Foldout(
                            environment.isExpanded,
                            title,
                            true);
                        GUILayout.FlexibleSpace();
                        DrawEnvironmentChannelBadge(environment);
                        DashboardGui.DrawBadge(
                            missingFieldCount == 0
                                ? "READY"
                                : missingFieldCount == 5
                                    ? "NOT SET"
                                    : "MISSING " + missingFieldCount,
                            missingFieldCount == 0
                                ? DashboardGui.BadgeTone.Success
                                : missingFieldCount == 5
                                    ? DashboardGui.BadgeTone.Neutral
                                    : DashboardGui.BadgeTone.Warning);
                    }

                    if (!environment.isExpanded)
                    {
                        continue;
                    }

                    EditorGUI.indentLevel++;
                    EditorGUILayout.LabelField(
                        "EOS connection",
                        EditorStyles.miniBoldLabel);
                    EditorGUILayout.LabelField(
                        "Complete these five values for this environment.",
                        EditorStyles.wordWrappedMiniLabel);
                    GUILayout.Space(2f);
                    DrawRequiredEpicField(
                        environment,
                        "productId",
                        "Product ID",
                        "Epic product identifier.");
                    DrawRequiredEpicField(
                        environment,
                        "sandboxId",
                        "Sandbox ID",
                        "Sandbox containing this deployment.");
                    DrawRequiredEpicField(
                        environment,
                        "deploymentId",
                        "Deployment ID",
                        "Deployment used by this build channel.");
                    DrawRequiredEpicField(
                        environment,
                        "eosClientId",
                        "EOS Client ID",
                        "Public runtime client ID configured for the product.");
                    DrawRequiredEpicField(
                        environment,
                        "clientSecret",
                        "Client Secret",
                        "Runtime EOS client secret packaged with the build; this is not a developer-account login.");

                    showEpicAdvanced[index] = EditorGUILayout.Foldout(
                        showEpicAdvanced[index],
                        "Advanced & optional",
                        true);
                    if (showEpicAdvanced[index])
                    {
                        EditorGUI.indentLevel++;
                        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                        {
                            EditorGUILayout.PropertyField(
                                environment.FindPropertyRelative("encryptionKey"),
                                new GUIContent(
                                    "Encryption Key",
                                    "Generated automatically and kept stable in project settings. Override only when necessary."));
                            EditorGUILayout.PropertyField(
                                environment.FindPropertyRelative("appArgs"),
                                new GUIContent(
                                    "App Arguments",
                                    "Optional arguments passed through the Epic launcher bootstrapper."));
                            EditorGUILayout.PropertyField(
                                environment.FindPropertyRelative("useBootstrapper"),
                                new GUIContent(
                                    "Use Bootstrapper",
                                    "Enabled by default. Disable only when the build should launch directly."));
                        }

                        EditorGUI.indentLevel--;
                    }

                    EditorGUI.indentLevel--;
                }
            }
        }

        private static void DrawRequiredEpicField(
            SerializedProperty environment,
            string propertyName,
            string label,
            string tooltip)
        {
            SerializedProperty property = environment.FindPropertyRelative(propertyName);
            if (property == null)
            {
                return;
            }

            bool missing = string.IsNullOrWhiteSpace(property.stringValue);
            Color originalBackground = GUI.backgroundColor;
            if (missing)
            {
                GUI.backgroundColor = new Color(1f, 0.68f, 0.52f);
            }

            EditorGUILayout.PropertyField(
                property,
                new GUIContent(label + " *", tooltip));
            GUI.backgroundColor = originalBackground;
        }

        private static void DrawSettingsCardHeader(
            string title,
            string description,
            string badge,
            DashboardGui.BadgeTone tone)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                DashboardGui.DrawBadge(badge, tone);
            }

            EditorGUILayout.LabelField(description, EditorStyles.wordWrappedMiniLabel);
            GUILayout.Space(3f);
        }

        private static int CountReadyEpicEnvironments(SerializedProperty environments)
        {
            if (environments == null || !environments.isArray)
            {
                return 0;
            }

            int ready = 0;
            for (int index = 0; index < environments.arraySize; index++)
            {
                if (CountMissingRequiredEpicFields(
                        environments.GetArrayElementAtIndex(index)) == 0)
                {
                    ready++;
                }
            }

            return ready;
        }

        private static int CountMissingRequiredEpicFields(SerializedProperty environment)
        {
            string[] requiredFields =
            {
                "productId",
                "sandboxId",
                "deploymentId",
                "eosClientId",
                "clientSecret"
            };
            int missing = 0;
            foreach (string fieldName in requiredFields)
            {
                SerializedProperty field = environment.FindPropertyRelative(fieldName);
                if (field == null || !IsMeaningfulValue(field.stringValue))
                {
                    missing++;
                }
            }

            return missing;
        }

        private static void DrawEnvironmentChannelBadge(SerializedProperty environment)
        {
            SerializedProperty channel = environment.FindPropertyRelative("channel");
            StoreReleaseChannel value = channel == null
                ? StoreReleaseChannel.Development
                : (StoreReleaseChannel)channel.intValue;
            switch (value)
            {
                case StoreReleaseChannel.Live:
                    DashboardGui.DrawBadge("LIVE", DashboardGui.BadgeTone.Success);
                    break;
                case StoreReleaseChannel.Stage:
                    DashboardGui.DrawBadge("STAGE", DashboardGui.BadgeTone.Warning);
                    break;
                default:
                    DashboardGui.DrawBadge("DEV", DashboardGui.BadgeTone.Info);
                    break;
            }
        }

        private static bool IsMeaningfulValue(string value)
        {
            string normalized = value == null ? string.Empty : value.Trim();
            return !string.IsNullOrWhiteSpace(normalized) &&
                   !normalized.StartsWith("REQUIRED_", StringComparison.OrdinalIgnoreCase) &&
                   !normalized.StartsWith("replace-", StringComparison.OrdinalIgnoreCase) &&
                   normalized.Any(character => character != '0');
        }

        private void DrawEpicConfigurationFiles(
            StoreReleaseDashboardContext context,
            StoreReleaseProjectSettings settings)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Epic Configuration Files", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "The toolkit creates both EOS JSON files from the Epic environment values above. " +
                "No file browsing or EOS plugin configuration window is required.",
                MessageType.Info);

            string productPath = StoreReleaseProjectSettings.DefaultEpicProductConfigTemplatePath;
            string windowsPath = StoreReleaseProjectSettings.DefaultEpicWindowsConfigTemplatePath;
            bool productExists = File.Exists(ToAbsoluteProjectPath(productPath));
            bool windowsExists = File.Exists(ToAbsoluteProjectPath(windowsPath));
            DashboardGui.DrawKeyValue(
                "Product Config",
                productPath + (productExists ? "  READY" : "  MISSING"));
            DashboardGui.DrawKeyValue(
                "Windows Config",
                windowsPath + (windowsExists ? "  READY" : "  MISSING"));

            bool eosInstalled = sdkStatuses.Any(status =>
                string.Equals(
                    status.PackageName,
                    StoreSdkDetector.EosPackageName,
                    StringComparison.OrdinalIgnoreCase) &&
                status.Compatibility == StoreSdkCompatibility.Supported);
            bool canGenerate = EosProjectConfigurationGenerator.CanGenerate(
                settings,
                out string generationBlockingReason);
            string buttonLabel = productExists && windowsExists
                ? "Refresh EOS Config Files"
                : "Create EOS Config Files";

            using (new EditorGUI.DisabledScope(!eosInstalled || !canGenerate))
            {
                if (GUILayout.Button(buttonLabel, GUILayout.Height(26f)))
                {
                    try
                    {
                        EosProjectConfigurationGenerator.CreateForProject(settings);
                        epicConfigStatusMessage =
                            "EOS product and Windows configuration files were created successfully.";
                        epicConfigOperationFailed = false;
                        context.RefreshSetupReadiness(sdkStatuses);
                    }
                    catch (Exception exception)
                    {
                        epicConfigStatusMessage = exception.Message;
                        epicConfigOperationFailed = true;
                    }
                }
            }

            if (!eosInstalled)
            {
                EditorGUILayout.HelpBox(
                    "Download the supported EOS Unity Plugin before creating its configuration files.",
                    MessageType.Warning);
            }
            else if (!canGenerate)
            {
                EditorGUILayout.HelpBox(generationBlockingReason, MessageType.Warning);
            }

            if (!string.IsNullOrWhiteSpace(epicConfigStatusMessage))
            {
                EditorGUILayout.HelpBox(
                    epicConfigStatusMessage,
                    epicConfigOperationFailed ? MessageType.Error : MessageType.Info);
            }
        }

        private static string ToAbsoluteProjectPath(string projectRelativePath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ??
                                 Application.dataPath;
            return Path.GetFullPath(Path.Combine(projectRoot, projectRelativePath));
        }

        private static string DrawFilePath(string label, string value, string extension)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                string next = EditorGUILayout.TextField(label, value);
                if (GUILayout.Button("Browse...", GUILayout.Width(72f)))
                {
                    string selected = DashboardGui.BrowseForFile("Choose " + label, next, extension);
                    if (!string.IsNullOrWhiteSpace(selected))
                    {
                        next = ImportIconIntoProject(selected);
                    }
                }

                return next;
            }
        }

        private static string ImportIconIntoProject(string selectedPath)
        {
            string projectRelative = DashboardGui.MakeProjectRelative(selectedPath);
            if (projectRelative.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                return projectRelative;
            }

            string sourcePath = Path.GetFullPath(selectedPath);
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ??
                                 throw new InvalidOperationException(
                                     "Unity project root is unavailable.");
            string destinationDirectory = Path.Combine(
                projectRoot,
                ManagedIconDirectory.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(destinationDirectory);

            string fileName = Path.GetFileName(sourcePath);
            string destinationPath = Path.Combine(destinationDirectory, fileName);
            if (!string.Equals(
                    sourcePath,
                    destinationPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(sourcePath, destinationPath, true);
            }

            string assetPath = ManagedIconDirectory + "/" + fileName;
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            return assetPath;
        }

        private static void DrawNextStep(StoreReleaseDashboardContext context)
        {
            DashboardGui.DrawSectionHeader("Next step");
            using (new EditorGUILayout.VerticalScope(DashboardGui.CardStyle))
            {
                if (!context.IsSetupReady)
                {
                    EditorGUILayout.HelpBox(
                        "Complete Setup to unlock Build and Diagnostics. " +
                        context.SetupBlockingReason,
                        MessageType.Warning);
                }

                EditorGUILayout.LabelField(
                    "Complete setup, then validate or create the selected store build.",
                    EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUI.DisabledScope(!context.IsSetupReady))
                {
                    if (GUILayout.Button("Continue to Build", GUILayout.Width(150f)))
                    {
                        context.NavigateTo(1);
                    }
                }
            }
        }
    }
}
