using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Aroko.StoreRelease.Editor.Build
{
    /// <summary>
    /// Makes a direct Windows x64 Unity build store-neutral. Store builds started
    /// by StoreBuildCoordinator keep using their selected provider instead.
    /// </summary>
    internal sealed class NormalBuildIsolationPreprocessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => int.MinValue;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.StandaloneWindows64 ||
                StoreBuildCoordinator.IsBuildInProgress)
            {
                return;
            }

            NormalBuildIsolationState.Begin();
        }
    }

    internal sealed class NormalBuildAssemblyFilter : IFilterBuildAssemblies
    {
        private static readonly string[] StoreSpecificAssemblyTokens =
        {
            "Aroko.StoreRelease.Steam",
            "Aroko.StoreRelease.Epic",
            "com.rlabrecque.steamworks.net",
            "com.playeveryware.eos",
            "com.Epic.OnlineServices"
        };

        public int callbackOrder => int.MaxValue;

        public string[] OnFilterAssemblies(
            BuildOptions buildOptions,
            string[] assemblies)
        {
            return NormalBuildIsolationState.IsActive
                ? FilterStoreSpecificAssemblies(assemblies)
                : assemblies;
        }

        internal static string[] FilterStoreSpecificAssemblies(
            IEnumerable<string> assemblies)
        {
            return (assemblies ?? Array.Empty<string>())
                .Where(path => !IsStoreSpecificAssembly(path))
                .ToArray();
        }

        internal static bool IsStoreSpecificAssembly(string path)
        {
            return StoreSpecificAssemblyTokens.Any(token =>
                (path ?? string.Empty).IndexOf(
                    token,
                    StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }

    internal sealed class NormalBuildIsolationPostprocessor : IPostprocessBuildWithReport
    {
        public int callbackOrder => int.MaxValue;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (!NormalBuildIsolationState.IsActive)
            {
                return;
            }

            NormalBuildIsolationState.Complete(report);
        }
    }

    [InitializeOnLoad]
    internal static class NormalBuildIsolationState
    {
        [Serializable]
        private sealed class RuntimeInitializeManifest
        {
            public RuntimeInitializeEntry[] root = Array.Empty<RuntimeInitializeEntry>();
        }

        [Serializable]
        private sealed class RuntimeInitializeEntry
        {
            public string assemblyName = string.Empty;
            public string nameSpace = string.Empty;
            public string className = string.Empty;
            public string methodName = string.Empty;
            public int loadTypes;
            public bool isUnityClass;
        }

        private const string ActiveSessionKey =
            "Aroko.StoreReleaseToolkit.NormalBuildIsolationActive";

        private static StoreBuildTransaction transaction;

        static NormalBuildIsolationState()
        {
            EditorApplication.update += RecoverAfterStoppedBuild;
        }

        internal static bool IsActive =>
            SessionState.GetBool(ActiveSessionKey, false);

        internal static void Begin()
        {
            if (IsActive)
            {
                throw new BuildFailedException(
                    "Store Release Toolkit already has a normal-build isolation transaction in progress.");
            }

            transaction = StoreBuildTransaction.BeginNormalBuild();
            SessionState.SetBool(ActiveSessionKey, true);
        }

        internal static void Complete(BuildReport report)
        {
            try
            {
                string outputDirectory = ResolveOutputDirectory(report);
                RemoveStoreArtifacts(outputDirectory);

                IReadOnlyList<string> contamination =
                    StoreOutputValidator.FindAnyStoreArtifacts(outputDirectory);
                if (contamination.Count > 0)
                {
                    throw new BuildFailedException(
                        "Normal build still contains Steam or Epic artifacts:\n" +
                        string.Join("\n", contamination.Select(issue => "- " + issue)));
                }

                Debug.Log(
                    "[Store Release Toolkit] Normal Windows x64 build completed " +
                    "without Steam or Epic provider artifacts.");
            }
            finally
            {
                End();
            }
        }

        private static void RecoverAfterStoppedBuild()
        {
            if (!IsActive || BuildPipeline.isBuildingPlayer)
            {
                return;
            }

            try
            {
                End();
                Debug.LogWarning(
                    "[Store Release Toolkit] Restored normal-build isolation " +
                    "after a failed or cancelled build.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static void End()
        {
            try
            {
                transaction?.Dispose();
            }
            finally
            {
                transaction = null;
                SessionState.EraseBool(ActiveSessionKey);
            }
        }

        private static string ResolveOutputDirectory(BuildReport report)
        {
            string outputPath = report?.summary.outputPath;
            string outputDirectory = string.IsNullOrWhiteSpace(outputPath)
                ? null
                : Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (string.IsNullOrWhiteSpace(outputDirectory) ||
                !Directory.Exists(outputDirectory))
            {
                throw new BuildFailedException(
                    "Normal build output directory could not be inspected: " + outputPath);
            }

            return outputDirectory;
        }

        private static void RemoveStoreArtifacts(string outputDirectory)
        {
            foreach (string file in Directory.EnumerateFiles(
                         outputDirectory,
                         "*",
                         SearchOption.AllDirectories).ToArray())
            {
                string relative = file.Substring(outputDirectory.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (StoreOutputValidator.IsAnyStoreArtifactPath(relative))
                {
                    File.Delete(file);
                }
            }

            FilterRuntimeInitializeManifests(outputDirectory);

            foreach (string directory in Directory.EnumerateDirectories(
                         outputDirectory,
                         "*",
                         SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length)
                     .ToArray())
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }
        }

        private static void FilterRuntimeInitializeManifests(
            string outputDirectory)
        {
            foreach (string path in Directory.EnumerateFiles(
                         outputDirectory,
                         "RuntimeInitializeOnLoads.json",
                         SearchOption.AllDirectories))
            {
                RuntimeInitializeManifest manifest =
                    JsonUtility.FromJson<RuntimeInitializeManifest>(
                        File.ReadAllText(path));
                if (manifest == null || manifest.root == null)
                {
                    throw new InvalidOperationException(
                        "Unity's RuntimeInitializeOnLoads.json could not be filtered safely.");
                }

                RuntimeInitializeEntry[] retained = manifest.root
                    .Where(entry => entry != null &&
                                    !NormalBuildAssemblyFilter.IsStoreSpecificAssembly(
                                        entry.assemblyName))
                    .ToArray();
                if (retained.Length == manifest.root.Length)
                {
                    continue;
                }

                manifest.root = retained;
                File.WriteAllText(
                    path,
                    JsonUtility.ToJson(manifest),
                    new UTF8Encoding(false));
            }
        }
    }
}
