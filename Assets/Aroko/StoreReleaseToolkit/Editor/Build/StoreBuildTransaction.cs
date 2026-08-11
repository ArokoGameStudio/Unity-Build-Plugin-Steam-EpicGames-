using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Aroko.StoreRelease.Editor.Configuration;
using UnityEditor;
using UnityEngine;
using EditorStorePlatform = Aroko.StoreRelease.Editor.Configuration.StorePlatform;

namespace Aroko.StoreRelease.Editor.Build
{
    /// <summary>
    /// Temporarily applies the requested player version and filters inactive-store
    /// plugins from the build. Persistent mutations are journaled under Library so
    /// an interrupted build can restore the project on the next editor update.
    /// </summary>
    [InitializeOnLoad]
    internal sealed class StoreBuildTransaction : IDisposable
    {
        private sealed class PluginFilterState
        {
            public string AssetPath = string.Empty;
            public bool Excluded;
        }

        [Serializable]
        private sealed class TransactionState
        {
            public string OriginalBundleVersion = string.Empty;
            public bool CreatedTemporaryEosWindowsConfig;
            public bool RestorationCompleted;
        }

        private const string EosWindowsConfigAssetPath =
            "Assets/StreamingAssets/EOS/eos_windows_config.json";

        private const string TemporaryEosWindowsConfig =
            "{\n" +
            "  \"deployment\": {\n" +
            "    \"SandboxId\": { \"Value\": \"00000000-0000-0000-0000-000000000002\" },\n" +
            "    \"DeploymentId\": \"00000000-0000-0000-0000-000000000001\"\n" +
            "  },\n" +
            "  \"clientCredentials\": {\n" +
            "    \"ClientId\": \"steam-build-placeholder\",\n" +
            "    \"ClientSecret\": \"steam-build-placeholder\",\n" +
            "    \"EncryptionKey\": \"1111111111111111111111111111111111111111111111111111111111111111\"\n" +
            "  },\n" +
            "  \"isServer\": false,\n" +
            "  \"platformOptionsFlags\": \"DisableOverlay\",\n" +
            "  \"authScopeOptionsFlags\": \"BasicProfile\",\n" +
            "  \"integratedPlatformManagementFlags\": \"Disabled\",\n" +
            "  \"tickBudgetInMilliseconds\": 0,\n" +
            "  \"taskNetworkTimeoutSeconds\": 0.0,\n" +
            "  \"threadAffinity\": null,\n" +
            "  \"alwaysSendInputToOverlay\": false,\n" +
            "  \"initialButtonDelayForOverlay\": 0.0,\n" +
            "  \"repeatButtonDelayForOverlay\": 0.0,\n" +
            "  \"toggleFriendsButtonCombination\": \"SpecialLeft\",\n" +
            "  \"schemaVersion\": \"1.0\"\n" +
            "}\n";

        private static readonly string[] SteamPluginTokens =
        {
            "steam_api",
            "steamworks",
            "com.rlabrecque.steamworks"
        };

        private static readonly string[] EpicPluginTokens =
        {
            "eossdk",
            "epic.onlineservices",
            "com.playeveryware.eos"
        };

        private readonly TransactionState state;
        private readonly List<PluginFilterState> pluginFilters =
            new List<PluginFilterState>();
        private bool disposed;

        static StoreBuildTransaction()
        {
            EditorApplication.delayCall += RecoverIfNeeded;
        }

        private StoreBuildTransaction(TransactionState state)
        {
            this.state = state;
        }

        public static StoreBuildTransaction Begin(StoreBuildRequest request)
        {
            if (request == null || request.Profile == null)
            {
                throw new ArgumentNullException(nameof(request), "A build request and profile are required.");
            }

            RecoverIfNeeded();
            var transactionState = new TransactionState
            {
                OriginalBundleVersion = PlayerSettings.bundleVersion
            };
            WriteJournal(transactionState);

            var transaction = new StoreBuildTransaction(transactionState);
            try
            {
                PlayerSettings.bundleVersion = request.Version;
                if (request.Profile.Store == EditorStorePlatform.Steam)
                {
                    transaction.EnsureTemporaryEosWindowsConfig();
                }
                transaction.DisableInactivePlugins(request.Profile.Store);
                WriteJournal(transactionState);
                return transaction;
            }
            catch
            {
                transaction.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            Restore(state, pluginFilters);
            state.RestorationCompleted = true;
            WriteJournal(state);
            DeleteJournalFiles();
        }

        private void DisableInactivePlugins(EditorStorePlatform activeStore)
        {
            string[] inactiveTokens = activeStore == EditorStorePlatform.Steam
                ? EpicPluginTokens
                : SteamPluginTokens;

            foreach (string assetPath in AssetDatabase.GetAllAssetPaths())
            {
                string normalized = assetPath.Replace('\\', '/').ToLowerInvariant();
                if (!inactiveTokens.Any(token => normalized.Contains(token)))
                {
                    continue;
                }

                var importer = AssetImporter.GetAtPath(assetPath) as PluginImporter;
                if (importer == null ||
                    !importer.GetCompatibleWithPlatform(BuildTarget.StandaloneWindows64) ||
                    !importer.ShouldIncludeInBuild())
                {
                    continue;
                }

                var filter = new PluginFilterState
                {
                    AssetPath = assetPath,
                    Excluded = true
                };
                pluginFilters.Add(filter);
                importer.SetIncludeInBuildDelegate(_ => !filter.Excluded);

                if (importer.ShouldIncludeInBuild())
                {
                    throw new InvalidOperationException(
                        "Could not isolate inactive store plugin: " + assetPath);
                }
            }
        }

        private void EnsureTemporaryEosWindowsConfig()
        {
            string absolutePath = GetEosWindowsConfigAbsolutePath();
            if (File.Exists(absolutePath))
            {
                return;
            }

            state.CreatedTemporaryEosWindowsConfig = true;
            WriteJournal(state);
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath));
            File.WriteAllText(
                absolutePath,
                TemporaryEosWindowsConfig,
                new UTF8Encoding(false));
        }

        private static void RecoverIfNeeded()
        {
            if (!File.Exists(JournalPath))
            {
                return;
            }

            TransactionState recovered = ReadJournal(JournalPath);
            if (recovered == null && File.Exists(JournalBackupPath))
            {
                recovered = ReadJournal(JournalBackupPath);
            }

            if (recovered == null)
            {
                Debug.LogError(
                    "Store Release Toolkit could not read its interrupted build transaction journal.");
                return;
            }

            try
            {
                if (!recovered.RestorationCompleted)
                {
                    Restore(recovered, null);
                    recovered.RestorationCompleted = true;
                    WriteJournal(recovered);
                }

                DeleteJournalFiles();
                Debug.LogWarning(
                    "Store Release Toolkit recovered an interrupted build transaction.");
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "Store Release Toolkit could not recover a previous build transaction: " +
                    exception.Message);
            }
        }

        private static void Restore(
            TransactionState transactionState,
            IReadOnlyList<PluginFilterState> pluginFilters)
        {
            if (transactionState == null)
            {
                return;
            }

            var failures = new List<Exception>();
            try
            {
                PlayerSettings.bundleVersion = transactionState.OriginalBundleVersion ?? string.Empty;
            }
            catch (Exception exception)
            {
                failures.Add(new InvalidOperationException(
                    "Could not restore the original bundle version.",
                    exception));
            }

            if (transactionState.CreatedTemporaryEosWindowsConfig)
            {
                try
                {
                    DeleteTemporaryEosWindowsConfig();
                }
                catch (Exception exception)
                {
                    failures.Add(new InvalidOperationException(
                        "Could not remove the temporary EOS Windows configuration.",
                        exception));
                }
            }

            if (pluginFilters == null)
            {
                pluginFilters = Array.Empty<PluginFilterState>();
            }

            for (int index = pluginFilters.Count - 1; index >= 0; index--)
            {
                PluginFilterState filter = pluginFilters[index];
                try
                {
                    filter.Excluded = false;
                    var importer = AssetImporter.GetAtPath(filter.AssetPath) as PluginImporter;
                    if (importer == null)
                    {
                        throw new InvalidOperationException(
                            "Could not load the filtered plugin importer: " + filter.AssetPath);
                    }

                    if (!importer.ShouldIncludeInBuild())
                    {
                        throw new InvalidOperationException(
                            "Could not restore the filtered plugin: " + filter.AssetPath);
                    }
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            if (failures.Count > 0)
            {
                throw new AggregateException(
                    "One or more build transaction values could not be restored.",
                    failures);
            }
        }

        private static void WriteJournal(TransactionState transactionState)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(JournalPath));
            string tempPath = JournalPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                byte[] content = new UTF8Encoding(false).GetBytes(
                    JsonUtility.ToJson(transactionState, true));
                using (var stream = new FileStream(
                           tempPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None))
                {
                    stream.Write(content, 0, content.Length);
                    stream.Flush(true);
                }

                if (!File.Exists(JournalPath))
                {
                    File.Move(tempPath, JournalPath);
                    return;
                }

                try
                {
                    File.Replace(tempPath, JournalPath, JournalBackupPath, true);
                }
                catch (PlatformNotSupportedException)
                {
                    ReplaceJournalWithMove(tempPath);
                }
                catch (IOException)
                {
                    ReplaceJournalWithMove(tempPath);
                }

                TryDelete(JournalBackupPath);
            }
            finally
            {
                TryDelete(tempPath);
            }
        }

        private static TransactionState ReadJournal(string path)
        {
            try
            {
                return JsonUtility.FromJson<TransactionState>(File.ReadAllText(path));
            }
            catch
            {
                return null;
            }
        }

        private static void ReplaceJournalWithMove(string tempPath)
        {
            TryDelete(JournalBackupPath);
            File.Move(JournalPath, JournalBackupPath);
            try
            {
                File.Move(tempPath, JournalPath);
            }
            catch
            {
                if (!File.Exists(JournalPath) && File.Exists(JournalBackupPath))
                {
                    File.Move(JournalBackupPath, JournalPath);
                }

                throw;
            }
        }

        private static void DeleteJournalFiles()
        {
            TryDelete(JournalPath);
            TryDelete(JournalBackupPath);
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Cleanup is retried on the next editor update.
            }
        }

        private static string GetEosWindowsConfigAbsolutePath()
        {
            return Path.GetFullPath(Path.Combine(
                StoreBuildCoordinator.ProjectRoot,
                EosWindowsConfigAssetPath.Replace(
                    '/', Path.DirectorySeparatorChar)));
        }

        private static void DeleteTemporaryEosWindowsConfig()
        {
            string path = GetEosWindowsConfigAbsolutePath();
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            string metaPath = path + ".meta";
            if (File.Exists(metaPath))
            {
                File.Delete(metaPath);
            }

            if (File.Exists(path) || File.Exists(metaPath))
            {
                throw new IOException(
                    "The temporary EOS configuration or its metadata still exists.");
            }
        }

        private static string JournalPath => Path.Combine(
            StoreBuildCoordinator.ProjectRoot,
            "Library",
            "StoreReleaseToolkit",
            "active-build-transaction.json");

        private static string JournalBackupPath => JournalPath + ".bak";
    }
}
