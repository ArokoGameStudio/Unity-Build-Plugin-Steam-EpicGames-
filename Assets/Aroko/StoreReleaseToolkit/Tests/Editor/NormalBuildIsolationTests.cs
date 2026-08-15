using System;
using System.IO;
using System.Linq;
using Aroko.StoreRelease.Editor.Build;
using NUnit.Framework;

namespace Aroko.StoreRelease.Tests.Editor
{
    public sealed class NormalBuildIsolationTests
    {
        [Test]
        public void AssemblyFilterRemovesBothProvidersAndVendorApis()
        {
            string[] assemblies =
            {
                "Library/ScriptAssemblies/Assembly-CSharp.dll",
                "Library/ScriptAssemblies/Aroko.StoreRelease.Runtime.dll",
                "Library/ScriptAssemblies/Aroko.StoreRelease.Steam.dll",
                "Library/ScriptAssemblies/Aroko.StoreRelease.Epic.dll",
                "Library/ScriptAssemblies/com.rlabrecque.steamworks.net.dll",
                "Library/ScriptAssemblies/com.playeveryware.eos.dll",
                "Library/ScriptAssemblies/com.playeveryware.eos.core.dll",
                "Library/ScriptAssemblies/com.Epic.OnlineServices.dll"
            };

            string[] filtered =
                NormalBuildAssemblyFilter.FilterStoreSpecificAssemblies(assemblies);

            CollectionAssert.AreEquivalent(
                new[]
                {
                    "Library/ScriptAssemblies/Assembly-CSharp.dll",
                    "Library/ScriptAssemblies/Aroko.StoreRelease.Runtime.dll"
                },
                filtered);
        }

        [Test]
        public void NormalArtifactScanKeepsNeutralRuntime()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                File.WriteAllText(
                    Path.Combine(directory, "Aroko.StoreRelease.Runtime.dll"),
                    "store-neutral facade");

                Assert.That(
                    StoreOutputValidator.FindAnyStoreArtifacts(directory),
                    Is.Empty);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public void NormalArtifactScanReportsSteamAndEpicFiles()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                File.WriteAllBytes(
                    Path.Combine(directory, "steam_api64.dll"),
                    Array.Empty<byte>());
                File.WriteAllBytes(
                    Path.Combine(directory, "EOSSDK-Win64-Shipping.dll"),
                    Array.Empty<byte>());

                string[] issues = StoreOutputValidator
                    .FindAnyStoreArtifacts(directory)
                    .ToArray();

                Assert.That(issues, Has.Length.EqualTo(2));
                Assert.That(issues.Any(issue => issue.Contains("steam_api64.dll")), Is.True);
                Assert.That(issues.Any(issue => issue.Contains("EOSSDK-Win64-Shipping.dll")), Is.True);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static string CreateTemporaryDirectory()
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "Aroko.StoreRelease.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
