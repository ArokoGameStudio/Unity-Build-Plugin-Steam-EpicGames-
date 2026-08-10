using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Aroko.StoreRelease.Editor.Build
{
    internal static class WindowsIconWriter
    {
        private static readonly int[] DefaultSizes = { 16, 24, 32, 48, 64, 128, 256 };

        public static void WriteFromAsset(string sourceAssetPath, string outputPath)
        {
            if (string.IsNullOrWhiteSpace(sourceAssetPath))
            {
                throw new InvalidOperationException("A Windows icon source asset is required.");
            }

            string absoluteSource = ToAbsolutePath(sourceAssetPath);
            if (!File.Exists(absoluteSource))
            {
                throw new FileNotFoundException("Windows icon source was not found.", absoluteSource);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)) ??
                                      throw new InvalidOperationException("Icon output has no directory."));

            if (string.Equals(Path.GetExtension(absoluteSource), ".ico",
                    StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(absoluteSource, outputPath, true);
                Validate(outputPath);
                return;
            }

            Texture2D source = AssetDatabase.LoadAssetAtPath<Texture2D>(
                ToProjectRelativePath(absoluteSource));
            if (source == null)
            {
                byte[] bytes = File.ReadAllBytes(absoluteSource);
                source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!source.LoadImage(bytes, false))
                {
                    UnityEngine.Object.DestroyImmediate(source);
                    throw new InvalidOperationException(
                        "Icon source must be a Unity Texture2D, PNG, or ICO file.");
                }

                try
                {
                    Write(source, outputPath, DefaultSizes);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(source);
                }

                return;
            }

            Write(source, outputPath, DefaultSizes);
        }

        public static void Write(Texture2D source, string outputPath, IReadOnlyList<int> sizes)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (sizes == null || sizes.Count == 0)
            {
                throw new ArgumentException("At least one icon size is required.", nameof(sizes));
            }

            var images = new List<IconImage>(sizes.Count);
            foreach (int size in sizes)
            {
                if (size <= 0 || size > 256)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(sizes), "ICO image sizes must be between 1 and 256.");
                }

                images.Add(new IconImage(size, RenderPng(source, size)));
            }

            string fullOutput = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullOutput) ??
                                      throw new InvalidOperationException("Icon output has no directory."));
            using (var stream = new FileStream(fullOutput, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write((ushort)0);
                writer.Write((ushort)1);
                writer.Write((ushort)images.Count);

                int offset = 6 + images.Count * 16;
                foreach (IconImage image in images)
                {
                    writer.Write((byte)(image.Size == 256 ? 0 : image.Size));
                    writer.Write((byte)(image.Size == 256 ? 0 : image.Size));
                    writer.Write((byte)0);
                    writer.Write((byte)0);
                    writer.Write((ushort)1);
                    writer.Write((ushort)32);
                    writer.Write((uint)image.Png.Length);
                    writer.Write((uint)offset);
                    offset += image.Png.Length;
                }

                foreach (IconImage image in images)
                {
                    writer.Write(image.Png);
                }
            }

            Validate(fullOutput);
        }

        public static void Validate(string iconPath)
        {
            string fullPath = Path.GetFullPath(iconPath);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("Windows icon file is missing.", fullPath);
            }

            using (var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new BinaryReader(stream))
            {
                if (stream.Length < 22 || reader.ReadUInt16() != 0 || reader.ReadUInt16() != 1)
                {
                    throw new InvalidDataException("The file is not a valid Windows ICO container.");
                }

                ushort count = reader.ReadUInt16();
                if (count == 0 || stream.Length < 6 + count * 16L)
                {
                    throw new InvalidDataException("The Windows ICO file has no valid image entries.");
                }

                bool contains256 = false;
                for (int index = 0; index < count; index++)
                {
                    byte width = reader.ReadByte();
                    byte height = reader.ReadByte();
                    reader.ReadBytes(6);
                    uint size = reader.ReadUInt32();
                    uint offset = reader.ReadUInt32();
                    long directoryEnd = 6L + count * 16L;
                    long imageEnd = (long)offset + size;
                    if (size == 0 ||
                        offset < directoryEnd ||
                        offset >= stream.Length ||
                        imageEnd > stream.Length)
                    {
                        throw new InvalidDataException(
                            "The Windows ICO file has an invalid or truncated image entry.");
                    }

                    long nextDirectoryEntry = stream.Position;
                    ValidateImagePayload(
                        reader,
                        offset,
                        size,
                        width == 0 ? 256 : width,
                        height == 0 ? 256 : height);
                    stream.Position = nextDirectoryEntry;
                    contains256 |= width == 0 && height == 0;
                }

                if (!contains256)
                {
                    throw new InvalidDataException(
                        "The Windows ICO file must contain a 256 x 256 image.");
                }

                if (count < 2)
                {
                    throw new InvalidDataException(
                        "The Windows ICO file must contain multiple resolutions.");
                }
            }
        }

        private static void ValidateImagePayload(
            BinaryReader reader,
            uint offset,
            uint size,
            int declaredWidth,
            int declaredHeight)
        {
            Stream stream = reader.BaseStream;
            stream.Position = offset;
            byte[] prefix = reader.ReadBytes((int)Math.Min(size, 8u));
            bool png =
                prefix.Length == 8 &&
                prefix[0] == 0x89 &&
                prefix[1] == 0x50 &&
                prefix[2] == 0x4e &&
                prefix[3] == 0x47 &&
                prefix[4] == 0x0d &&
                prefix[5] == 0x0a &&
                prefix[6] == 0x1a &&
                prefix[7] == 0x0a;

            int payloadWidth;
            int payloadHeight;
            if (png)
            {
                if (size < 24 ||
                    ReadUInt32BigEndian(reader) != 13 ||
                    reader.ReadByte() != (byte)'I' ||
                    reader.ReadByte() != (byte)'H' ||
                    reader.ReadByte() != (byte)'D' ||
                    reader.ReadByte() != (byte)'R')
                {
                    throw new InvalidDataException(
                        "The Windows ICO file contains an invalid PNG image entry.");
                }

                uint width = ReadUInt32BigEndian(reader);
                uint height = ReadUInt32BigEndian(reader);
                if (width == 0 ||
                    height == 0 ||
                    width > int.MaxValue ||
                    height > int.MaxValue)
                {
                    throw new InvalidDataException(
                        "The Windows ICO PNG entry has invalid dimensions.");
                }

                payloadWidth = (int)width;
                payloadHeight = (int)height;
            }
            else
            {
                stream.Position = offset;
                uint headerSize = reader.ReadUInt32();
                if (headerSize == 12 && size >= 12)
                {
                    payloadWidth = reader.ReadUInt16();
                    int combinedHeight = reader.ReadUInt16();
                    if (combinedHeight == 0 ||
                        (combinedHeight & 1) != 0)
                    {
                        throw new InvalidDataException(
                            "The Windows ICO bitmap entry has an invalid mask height.");
                    }

                    payloadHeight = combinedHeight / 2;
                }
                else if (headerSize >= 40 && size >= 16)
                {
                    int width = reader.ReadInt32();
                    int combinedHeight = reader.ReadInt32();
                    long normalizedWidth = Math.Abs((long)width);
                    long fullHeight = Math.Abs((long)combinedHeight);
                    if (fullHeight == 0 || (fullHeight & 1L) != 0)
                    {
                        throw new InvalidDataException(
                            "The Windows ICO bitmap entry has an invalid mask height.");
                    }

                    long normalizedHeight = fullHeight / 2L;
                    if (normalizedWidth > int.MaxValue ||
                        normalizedHeight > int.MaxValue)
                    {
                        throw new InvalidDataException(
                            "The Windows ICO bitmap entry has invalid dimensions.");
                    }

                    payloadWidth = (int)normalizedWidth;
                    payloadHeight = (int)normalizedHeight;
                }
                else
                {
                    throw new InvalidDataException(
                        "The Windows ICO image entry is neither PNG nor a supported bitmap.");
                }
            }

            if (payloadWidth != declaredWidth ||
                payloadHeight != declaredHeight)
            {
                throw new InvalidDataException(
                    "The Windows ICO image payload dimensions do not match its directory entry.");
            }
        }

        private static uint ReadUInt32BigEndian(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(4);
            if (bytes.Length != 4)
            {
                throw new EndOfStreamException(
                    "The Windows ICO PNG entry is truncated.");
            }

            return ((uint)bytes[0] << 24) |
                   ((uint)bytes[1] << 16) |
                   ((uint)bytes[2] << 8) |
                   bytes[3];
        }

        private static byte[] RenderPng(Texture2D source, int size)
        {
            var renderTexture = RenderTexture.GetTemporary(
                size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previous = RenderTexture.active;
            var target = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
            try
            {
                Graphics.Blit(source, renderTexture);
                RenderTexture.active = renderTexture;
                target.ReadPixels(new Rect(0, 0, size, size), 0, 0, false);
                target.Apply(false, false);
                return target.EncodeToPNG();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(renderTexture);
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        private static string ToAbsolutePath(string path)
        {
            if (Path.IsPathRooted(path))
            {
                return Path.GetFullPath(path);
            }

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ??
                                 throw new InvalidOperationException("Unity project root is unavailable.");
            return Path.GetFullPath(Path.Combine(projectRoot, path));
        }

        private static string ToProjectRelativePath(string absolutePath)
        {
            string normalized = Path.GetFullPath(absolutePath).Replace('\\', '/');
            string projectRoot = (Directory.GetParent(Application.dataPath)?.FullName ??
                                  string.Empty).Replace('\\', '/').TrimEnd('/');
            if (!normalized.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase))
            {
                return absolutePath;
            }

            return normalized.Substring(projectRoot.Length + 1);
        }

        private readonly struct IconImage
        {
            public IconImage(int size, byte[] png)
            {
                Size = size;
                Png = png;
            }

            public int Size { get; }
            public byte[] Png { get; }
        }
    }
}
