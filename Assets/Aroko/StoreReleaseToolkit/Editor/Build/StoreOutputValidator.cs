using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Aroko.StoreRelease.Editor.Build
{
    internal static class StoreOutputValidator
    {
        private const int BufferSize = 64 * 1024;

        private static readonly HashSet<string> ContentScanExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".dll", ".exe", ".json", ".txt", ".config", ".xml"
            };

        private static readonly string[] AllStoreFileTokens =
        {
            "steam_api", "steamworks", "steam_appid",
            "aroko.storerelease.steam", "eossdk",
            "epic.onlineservices", "com.playeveryware.eos",
            "aroko.storerelease.epic", "eosbootstrapper",
            "eos_steam_config.json",
            EosConfigurationUtility.ProductConfigFileName,
            EosConfigurationUtility.WindowsConfigFileName
        };

        public static IReadOnlyList<string> FindInactiveStoreArtifacts(
            string outputDirectory, string activeStore)
        {
            bool steam = string.Equals(activeStore, "Steam", StringComparison.OrdinalIgnoreCase);
            string[] forbiddenFileTokens = steam
                ? new[] { "eossdk", "epic.onlineservices", "eosbootstrapper" }
                : new[] { "steam_api", "steamworks", "steam_appid" };
            string[] forbiddenContentTokens = steam
                ? new[]
                {
                    "Epic.OnlineServices", "PlayEveryWare.EpicOnlineServices",
                    "EOSSDK-Win64-Shipping"
                }
                : new[]
                {
                    "Steamworks", "SteamAPI_Init", "steam_api64"
                };

            return FindArtifacts(
                outputDirectory,
                forbiddenFileTokens,
                forbiddenContentTokens,
                allowSteamReferencesInEosOverlay: !steam);
        }

        public static IReadOnlyList<string> FindAnyStoreArtifacts(
            string outputDirectory)
        {
            return FindArtifacts(
                outputDirectory,
                AllStoreFileTokens,
                new[]
                {
                    "Steamworks", "SteamAPI_Init", "steam_api64",
                    "Epic.OnlineServices", "PlayEveryWare.EpicOnlineServices",
                    "EOSSDK-Win64-Shipping"
                },
                allowSteamReferencesInEosOverlay: false);
        }

        internal static bool IsAnyStoreArtifactPath(string path)
        {
            return AllStoreFileTokens.Any(token =>
                (path ?? string.Empty).IndexOf(
                    token,
                    StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static IReadOnlyList<string> FindArtifacts(
            string outputDirectory,
            IReadOnlyList<string> forbiddenFileTokens,
            IReadOnlyList<string> forbiddenContentTokens,
            bool allowSteamReferencesInEosOverlay)
        {
            if (!Directory.Exists(outputDirectory))
            {
                return new[] { "Build output directory does not exist: " + outputDirectory };
            }

            var issues = new List<string>();
            IEnumerable<string> files;
            try
            {
                files = EnumerateRegularFiles(outputDirectory).ToArray();
            }
            catch (Exception exception)
            {
                return new[]
                {
                    "Build output could not be safely inspected: " +
                    exception.Message
                };
            }

            foreach (string file in files)
            {
                string relative = MakeRelativePath(outputDirectory, file);
                foreach (string token in forbiddenFileTokens)
                {
                    if (relative.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        issues.Add("Inactive-store filename: " + relative);
                        break;
                    }
                }

                if (!ContentScanExtensions.Contains(Path.GetExtension(file)))
                {
                    continue;
                }

                bool officialEosOverlayBridge = allowSteamReferencesInEosOverlay &&
                    Path.GetFileName(file).StartsWith(
                        "GfxPluginNativeRender-", StringComparison.OrdinalIgnoreCase);
                foreach (string token in forbiddenContentTokens)
                {
                    if (officialEosOverlayBridge &&
                        (string.Equals(token, "SteamAPI_Init", StringComparison.Ordinal) ||
                         string.Equals(token, "steam_api64", StringComparison.Ordinal)))
                    {
                        continue;
                    }

                    if (ContainsAsciiToken(file, token))
                    {
                        issues.Add("Inactive-store reference '" + token + "' in " + relative);
                    }
                }
            }

            return issues.Distinct(StringComparer.OrdinalIgnoreCase).Take(100).ToArray();
        }

        private static IEnumerable<string> EnumerateRegularFiles(
            string rootDirectory)
        {
            string root = Path.GetFullPath(rootDirectory);
            for (var current = new DirectoryInfo(root);
                 current != null;
                 current = current.Parent)
            {
                if (current.Exists &&
                    (current.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException(
                        "the output path passes through a symbolic link or junction: " +
                        current.FullName);
                }
            }

            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                foreach (string entry in Directory.EnumerateFileSystemEntries(
                             directory,
                             "*",
                             SearchOption.TopDirectoryOnly))
                {
                    FileAttributes attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new InvalidDataException(
                            "a symbolic link or junction was found at " +
                            MakeRelativePath(root, entry) + ".");
                    }

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        pending.Push(entry);
                    }
                    else
                    {
                        yield return entry;
                    }
                }
            }
        }

        private static bool ContainsAsciiToken(string filePath, string token)
        {
            byte[] tokenBytes = Encoding.ASCII.GetBytes(token.ToLowerInvariant());
            if (tokenBytes.Length == 0)
            {
                return false;
            }

            byte[] buffer = new byte[BufferSize + tokenBytes.Length];
            int carry = 0;
            using (var stream = new FileStream(
                       filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                while (true)
                {
                    int read = stream.Read(buffer, carry, BufferSize);
                    if (read <= 0)
                    {
                        return false;
                    }

                    int length = carry + read;
                    for (int index = 0; index <= length - tokenBytes.Length; index++)
                    {
                        bool match = true;
                        for (int tokenIndex = 0; tokenIndex < tokenBytes.Length; tokenIndex++)
                        {
                            byte value = buffer[index + tokenIndex];
                            if (value >= (byte)'A' && value <= (byte)'Z')
                            {
                                value = (byte)(value + 32);
                            }

                            if (value != tokenBytes[tokenIndex])
                            {
                                match = false;
                                break;
                            }
                        }

                        if (match)
                        {
                            return true;
                        }
                    }

                    carry = Math.Min(tokenBytes.Length - 1, length);
                    Buffer.BlockCopy(buffer, length - carry, buffer, 0, carry);
                }
            }
        }

        private static string MakeRelativePath(string root, string path)
        {
            Uri rootUri = new Uri(Path.GetFullPath(root).TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                                  Path.DirectorySeparatorChar);
            Uri fileUri = new Uri(Path.GetFullPath(path));
            return Uri.UnescapeDataString(rootUri.MakeRelativeUri(fileUri).ToString())
                .Replace('/', Path.DirectorySeparatorChar);
        }
    }
}
