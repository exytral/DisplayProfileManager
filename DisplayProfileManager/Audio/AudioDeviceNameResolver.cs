using System;
using System.Collections.Generic;
using System.Management;
using System.Threading;

using NLog;

using DisplayProfileManager.Common;

namespace DisplayProfileManager.Audio
{
    public static class AudioDeviceNameResolver
    {
        private static readonly Lock _lock = new();
        private static readonly Dictionary<string, (string Name, DateTime Discovered)> _deviceCache = [];
        internal static readonly Logger _logger = LoggerHelper.GetLogger();

        internal static string TryGetBluetoothName(string deviceId)
        {
            lock (_lock)
            {
                if (_deviceCache.TryGetValue(deviceId, out var entry))
                {
                    return entry.Name;
                }
            }

            return GetDeviceNameViaWmi(deviceId);
        }

        internal static string GetDeviceNameViaWmi(string deviceId)
        {
            try
            {
                var targetMac = ExtractMacAddress(deviceId);

                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE ConfigManagerErrorCode = 0");
                searcher.Options.Timeout = TimeSpan.FromSeconds(5);

                foreach (var wmiDevice in searcher.Get())
                {
                    var name = wmiDevice["Name"]?.ToString();
                    var wmiId = wmiDevice["DeviceID"]?.ToString();

                    if (IsBluetoothDevice(name) && IsDeviceRelated(deviceId, wmiId))
                    {
                        return name;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "WMI Bluetooth name lookup failed");
            }

            return null;
        }

        internal static bool IsDeviceRelated(string deviceId, string wmiDeviceId)
        {
            if (string.IsNullOrEmpty(wmiDeviceId))
            {
                return false;
            }

            foreach (var wmiGuid in ExtractGuids(wmiDeviceId))
            {
                foreach (var targetGuid in ExtractGuids(deviceId))
                {
                    if (wmiGuid.Equals(targetGuid, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        internal static bool IsBluetoothDevice(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName))
            {
                return false;
            }

            var lower = deviceName.ToLower();

            foreach (var indicator in new[]
            {
                "bluetooth",
                "bt",
                "wireless",
                "airpods",
                "headset",
                "earbuds",
                "buds",
                "headphones",
                "stereo",
                "hands-free",
                "hfp",
                "a2dp",
                "sco"
            })
            {
                if (lower.Contains(indicator))
                {
                    return true;
                }
            }

            return false;
        }

        internal static string ExtractMacAddress(string deviceId)
        {
            try
            {
                var clean = System.Text.RegularExpressions.Regex.Replace(deviceId, @"[-{}\\\#&]", "");
                var match = System.Text.RegularExpressions.Regex.Match(clean, @"[0-9A-Fa-f]{12}");
                if (!match.Success)
                {
                    return null;
                }
                var mac = match.Value.ToUpper();
                return $"{mac[0]}{mac[1]}:{mac[2]}{mac[3]}:{mac[4]}{mac[5]}:{mac[6]}{mac[7]}:{mac[8]}{mac[9]}:{mac[10]}{mac[11]}";
            }
            catch
            {
                return null;
            }
        }

        internal static List<string> ExtractGuids(string input)
        {
            var guids = new List<string>();

            if (string.IsNullOrEmpty(input))
            {
                return guids;
            }

            var fullGuids = System.Text.RegularExpressions.Regex.Matches(input, @"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b");

            foreach (System.Text.RegularExpressions.Match m in fullGuids)
                guids.Add(m.Value);

            var hexParts = System.Text.RegularExpressions.Regex.Matches(input, @"\b[0-9a-fA-F]{8}\b");

            foreach (System.Text.RegularExpressions.Match m in hexParts)
                guids.Add(m.Value);

            return guids;
        }

        internal static void CacheDeviceName(string deviceId, string name)
        {
            if (string.IsNullOrEmpty(deviceId) || string.IsNullOrEmpty(name)) return;

            lock (_lock)
            {
                _deviceCache[deviceId] = (name, DateTime.Now);

                if (_deviceCache.Count > 100)
                    TrimCache();
            }
        }

        internal static string GetCachedName(string deviceId)
        {
            lock (_lock)
            {
                return _deviceCache.TryGetValue(deviceId, out var entry) ? entry.Name : null;
            }
        }

        internal static void TrimCache()
        {
            var sorted = new List<KeyValuePair<string, (string Name, DateTime Discovered)>>(_deviceCache);

            sorted.Sort((a, b) =>
                a.Value.Discovered.CompareTo(b.Value.Discovered));

            for (int i = 0; i < sorted.Count - 100; i++)
                _deviceCache.Remove(sorted[i].Key);
        }
    }
}
