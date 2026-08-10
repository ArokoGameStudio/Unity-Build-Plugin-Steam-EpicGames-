using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Aroko.StoreRelease.Editor.Configuration;

namespace Aroko.StoreRelease.Editor.Build
{
    internal static class EosConfigurationUtility
    {
        public const string ProductConfigFileName = "eos_product_config.json";
        public const string WindowsConfigFileName = "eos_windows_config.json";

        public static string GetOutputDirectory(string buildDirectory, string executableName)
        {
            return Path.Combine(
                buildDirectory,
                Path.GetFileNameWithoutExtension(executableName) + "_Data",
                "StreamingAssets",
                "EOS");
        }

        public static void EnsureOutputFiles(
            string outputDirectory,
            string productConfigSource,
            string windowsConfigSource)
        {
            if (!File.Exists(productConfigSource))
            {
                throw new FileNotFoundException(
                    "EOS product configuration template is missing.", productConfigSource);
            }

            if (!File.Exists(windowsConfigSource))
            {
                throw new FileNotFoundException(
                    "EOS Windows configuration template is missing.", windowsConfigSource);
            }

            Directory.CreateDirectory(outputDirectory);
            File.Copy(
                productConfigSource,
                Path.Combine(outputDirectory, ProductConfigFileName),
                true);
            File.Copy(
                windowsConfigSource,
                Path.Combine(outputDirectory, WindowsConfigFileName),
                true);
            RemoveIntegratedPlatformFiles(outputDirectory);
        }

        public static void ConfigureOutput(
            string eosOutputDirectory,
            EpicEnvironmentDefinition environment,
            string version)
        {
            if (environment == null)
            {
                throw new ArgumentNullException(nameof(environment));
            }

            string productPath = Path.Combine(eosOutputDirectory, ProductConfigFileName);
            string windowsPath = Path.Combine(eosOutputDirectory, WindowsConfigFileName);
            if (!File.Exists(productPath) || !File.Exists(windowsPath))
            {
                throw new InvalidOperationException(
                    "The Epic build is missing EOS product/Windows configuration under '" +
                    eosOutputDirectory + "'. Configure the EOS Unity plugin before building.");
            }

            string productJson = File.ReadAllText(productPath);
            string windowsJson = File.ReadAllText(windowsPath);
            File.WriteAllText(
                productPath,
                RewriteProductConfig(productJson, environment, version),
                new UTF8Encoding(false));
            File.WriteAllText(
                windowsPath,
                RewriteWindowsConfig(windowsJson, environment),
                new UTF8Encoding(false));
            RemoveIntegratedPlatformFiles(eosOutputDirectory);
        }

        public static void ValidateOutput(
            string eosOutputDirectory,
            EpicEnvironmentDefinition environment,
            string version)
        {
            if (environment == null)
            {
                throw new ArgumentNullException(nameof(environment));
            }

            string productPath = Path.Combine(eosOutputDirectory, ProductConfigFileName);
            string windowsPath = Path.Combine(eosOutputDirectory, WindowsConfigFileName);
            if (!File.Exists(productPath) || !File.Exists(windowsPath))
            {
                throw new InvalidOperationException(
                    "EOS product/Windows configuration is missing from the Epic build.");
            }

            string productJson = File.ReadAllText(productPath);
            string windowsJson = File.ReadAllText(windowsPath);
            string expectedProduct =
                RewriteProductConfig(productJson, environment, version);
            string expectedWindows = RewriteWindowsConfig(windowsJson, environment);
            if (!string.Equals(productJson, expectedProduct, StringComparison.Ordinal) ||
                !string.Equals(windowsJson, expectedWindows, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The Epic build does not match the selected product, client, " +
                    "sandbox, deployment, or version.");
            }

            foreach (string file in Directory.EnumerateFiles(
                         eosOutputDirectory, "*steam*", SearchOption.AllDirectories))
            {
                throw new InvalidOperationException(
                    "The Epic build contains an integrated Steam configuration: " + file);
            }
        }

        public static string RewriteWindowsConfig(
            string windowsJson,
            string sandboxId,
            string deploymentId)
        {
            RequireConfigured("Epic sandbox ID", sandboxId);
            RequireConfigured("Epic deployment ID", deploymentId);
            if (string.IsNullOrWhiteSpace(windowsJson))
            {
                throw new ArgumentException("EOS Windows configuration is empty.", nameof(windowsJson));
            }

            var sandboxPattern = new Regex(
                "(?s)(?<prefix>\\\"deployment\\\"\\s*:\\s*\\{\\s*" +
                "\\\"SandboxId\\\"\\s*:\\s*\\{\\s*\\\"Value\\\"\\s*:\\s*\\\")" +
                "[^\\\"]*(?<suffix>\\\")");
            var deploymentPattern = new Regex(
                "(?<prefix>\\\"DeploymentId\\\"\\s*:\\s*\\\")[^\\\"]*" +
                "(?<suffix>\\\")");

            if (!sandboxPattern.IsMatch(windowsJson) ||
                !deploymentPattern.IsMatch(windowsJson))
            {
                throw new InvalidDataException(
                    "EOS Windows configuration does not contain a supported deployment block.");
            }

            string rewritten = sandboxPattern.Replace(
                windowsJson,
                match => match.Groups["prefix"].Value +
                         EscapeJson(sandboxId.Trim()) +
                         match.Groups["suffix"].Value,
                1);
            return deploymentPattern.Replace(
                rewritten,
                match => match.Groups["prefix"].Value +
                         EscapeJson(deploymentId.Trim()) +
                         match.Groups["suffix"].Value,
                1);
        }

        public static string RewriteWindowsConfig(
            string windowsJson,
            EpicEnvironmentDefinition environment)
        {
            if (environment == null)
            {
                throw new ArgumentNullException(nameof(environment));
            }

            string rewritten = RewriteWindowsConfig(
                windowsJson,
                environment.SandboxId,
                environment.DeploymentId);
            rewritten = RewriteJsonString(
                rewritten,
                "ClientId",
                environment.EosClientId,
                "EOS Client ID");
            rewritten = RewriteJsonString(
                rewritten,
                "ClientSecret",
                environment.ClientSecret,
                "EOS Client Secret");
            return RewriteJsonString(
                rewritten,
                "EncryptionKey",
                environment.EncryptionKey,
                "EOS Encryption Key");
        }

        public static string RewriteProductConfig(
            string productJson,
            EpicEnvironmentDefinition environment,
            string version)
        {
            if (environment == null)
            {
                throw new ArgumentNullException(nameof(environment));
            }

            if (string.IsNullOrWhiteSpace(productJson))
            {
                throw new ArgumentException(
                    "EOS product configuration is empty.",
                    nameof(productJson));
            }

            string rewritten = RewriteJsonString(
                productJson,
                "ProductId",
                environment.ProductId,
                "Epic Product ID");
            rewritten = RewriteJsonString(
                rewritten,
                "ProductVersion",
                version,
                "Build version");
            rewritten = RewriteJsonString(
                rewritten,
                "ClientId",
                environment.EosClientId,
                "EOS Client ID");
            rewritten = RewriteJsonString(
                rewritten,
                "ClientSecret",
                environment.ClientSecret,
                "EOS Client Secret");
            return RewriteJsonString(
                rewritten,
                "EncryptionKey",
                environment.EncryptionKey,
                "EOS Encryption Key");
        }

        public static void RemoveIntegratedPlatformFiles(string eosDirectory)
        {
            if (!Directory.Exists(eosDirectory))
            {
                return;
            }

            foreach (string file in Directory.EnumerateFiles(
                         eosDirectory, "*steam*", SearchOption.AllDirectories))
            {
                File.Delete(file);
            }
        }

        private static void RequireConfigured(string label, string value)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                Regex.IsMatch(value, "^(REQUIRED_|replace-|0{16,})",
                    RegexOptions.IgnoreCase))
            {
                throw new InvalidOperationException(label + " is not configured.");
            }
        }

        private static string EscapeJson(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string RewriteJsonString(
            string json,
            string propertyName,
            string value,
            string label)
        {
            RequireConfigured(label, value);
            var pattern = new Regex(
                "(?<prefix>\\\"" + Regex.Escape(propertyName) +
                "\\\"\\s*:\\s*\\\")[^\\\"]*(?<suffix>\\\")",
                RegexOptions.CultureInvariant);
            if (!pattern.IsMatch(json))
            {
                throw new InvalidDataException(
                    "EOS configuration does not contain " + propertyName + ".");
            }

            return pattern.Replace(
                json,
                match => match.Groups["prefix"].Value +
                         EscapeJson(value.Trim()) +
                         match.Groups["suffix"].Value,
                1);
        }
    }
}
