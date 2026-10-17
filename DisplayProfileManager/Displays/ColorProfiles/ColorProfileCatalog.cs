using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

using NLog;

using DisplayProfileManager.Common;

namespace DisplayProfileManager.Displays.ColorProfiles
{
    public static class ColorProfileCatalog
    {
        private static readonly Logger _logger = LoggerHelper.GetLogger();

        [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetColorDirectory(
            string pMachineName,
            System.Text.StringBuilder pBuffer,
            ref uint pdwSize);

        private static uint ReadBigEndianUInt32(BinaryReader br)
        {
            var bytes = br.ReadBytes(4);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);

            return BitConverter.ToUInt32(bytes, 0);
        }

        public static string GetSystemColorDirectory()
        {
            uint size = 0;
            GetColorDirectory(null, null, ref size);

            var sb = new System.Text.StringBuilder((int)size + 2);
            if (GetColorDirectory(null, sb, ref size))
            {
                return sb.ToString();
            }

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"spool\drivers\color");
        }

        public static IReadOnlyList<string> GetInstalledColorProfilesFiltered(bool hdrOnly)
        {
            try
            {
                string dir = GetSystemColorDirectory();
                if (!Directory.Exists(dir))
                {
                    return [];
                }

                var files = Directory.GetFiles(dir, "*.icc").Concat(Directory.GetFiles(dir, "*.icm")).OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
                return [.. files.Where(f => !hdrOnly || IccProfileIsHdr(f)).Select(Path.GetFileName)];
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to enumerate system color profiles");
                return [];
            }
        }

        public static bool IccProfileIsHdr(string fullPath)
        {
            try
            {
                using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 512, useAsync: false);
                return IccProfileIsHdr(fs);
            }
            catch
            {
                return false;
            }
        }

        internal static bool IccProfileIsHdr(Stream stream)
        {
            if (stream == null || !stream.CanRead || !stream.CanSeek)
            {
                return false;
            }

            try
            {
                using var br = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
                if (stream.Length < 132)
                {
                    return false;
                }

                stream.Seek(128, SeekOrigin.Begin);
                uint tagCount = ReadBigEndianUInt32(br);
                if (tagCount > 1000 || stream.Length < 132 + (tagCount * 12L))
                {
                    return false;
                }

                const uint SIG_CICP = 0x63696370;
                const uint SIG_MHC2 = 0x4D484332;
                uint? cicpOffset = null, cicpSize = null;
                for (uint i = 0; i < tagCount; i++)
                {
                    uint sig = ReadBigEndianUInt32(br);
                    uint offset = ReadBigEndianUInt32(br);
                    uint size = ReadBigEndianUInt32(br);

                    if (sig == SIG_MHC2)
                    {
                        return true;
                    }

                    if (sig == SIG_CICP)
                    {
                        cicpOffset = offset;
                        cicpSize = size;
                    }
                }

                if (cicpOffset.HasValue && cicpSize >= 12)
                {
                    stream.Seek(cicpOffset.Value + 8, SeekOrigin.Begin);
                    br.ReadByte();
                    byte transferFunction = br.ReadByte();

                    return transferFunction == 16 || transferFunction == 18;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
