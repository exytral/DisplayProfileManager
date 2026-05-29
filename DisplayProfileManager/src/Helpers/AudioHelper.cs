using NLog;
using System;
using System.Collections.Generic;
using System.Management;
using System.Runtime.InteropServices;

namespace DisplayProfileManager.Helpers
{
    public class AudioHelper
    {
        private static readonly Logger logger = LoggerHelper.GetLogger();
        private static readonly object _lock = new object();
        private static readonly Dictionary<string, (string Name, DateTime Discovered)> _deviceCache = new Dictionary<string, (string Name, DateTime Discovered)>();

        #region P/Invoke

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            int EnumAudioEndpoints(EDataFlow dataFlow, uint dwStateMask, out IMMDeviceCollection ppDevices);
            int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);
            int GetDevice(string pwstrId, out IMMDevice ppDevice);
            int RegisterEndpointNotificationCallback(IntPtr pClient);
            int UnregisterEndpointNotificationCallback(IntPtr pClient);
        }

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumeratorComObject { }

        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceCollection
        {
            int GetCount(out uint pcDevices);
            int Item(uint nDevice, out IMMDevice ppDevice);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            int Activate(ref Guid iid, uint dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
            int OpenPropertyStore(uint stgmAccess, out IPropertyStore ppProperties);
            int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
            int GetState(out uint pdwState);
        }

        [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            int GetCount(out uint cProps);
            int GetAt(uint iProp, out PROPERTYKEY pkey);
            int GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
            int SetValue(ref PROPERTYKEY key, ref PROPVARIANT pv);
            int Commit();
        }

        [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPolicyConfig
        {
            [PreserveSig] int GetMixFormat(string pszDeviceName, IntPtr ppFormat);
            [PreserveSig] int GetDeviceFormat(string pszDeviceName, bool bDefault, IntPtr ppFormat);
            [PreserveSig] int ResetDeviceFormat(string pszDeviceName);
            [PreserveSig] int SetDeviceFormat(string pszDeviceName, IntPtr pEndpointFormat, IntPtr MixFormat);
            [PreserveSig] int GetProcessingPeriod(string pszDeviceName, bool bDefault, IntPtr pmftDefaultPeriod, IntPtr pmftMinimumPeriod);
            [PreserveSig] int SetProcessingPeriod(string pszDeviceName, IntPtr pmftPeriod);
            [PreserveSig] int GetShareMode(string pszDeviceName, IntPtr pMode);
            [PreserveSig] int SetShareMode(string pszDeviceName, IntPtr mode);
            [PreserveSig] int GetPropertyValue(string pszDeviceName, bool bFxStore, IntPtr key, IntPtr pv);
            [PreserveSig] int SetPropertyValue(string pszDeviceName, bool bFxStore, IntPtr key, IntPtr pv);
            [PreserveSig] int SetDefaultEndpoint(string pszDeviceName, ERole role);
            [PreserveSig] int SetEndpointVisibility(string pszDeviceName, bool bVisible);
        }

        [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
        private class PolicyConfigClientComObject { }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROPERTYKEY
        {
            public Guid fmtid;
            public uint pid;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct PROPVARIANT
        {
            [FieldOffset(0)] public ushort vt;
            [FieldOffset(8)] public IntPtr pszVal;
            [FieldOffset(8)] public IntPtr pwszVal;
        }

        private enum EDataFlow { Render, Capture, All }
        private enum ERole { Console, Multimedia, Communications }

        private const uint DeviceStateActive = 0x00000001;
        private const uint StgmRead = 0x00000000;

        private static readonly PROPERTYKEY PKEY_Device_FriendlyName = new PROPERTYKEY
        {
            fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
            pid = 14
        };

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(ref PROPVARIANT pvar);

        #endregion

        public class AudioDeviceInfo
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string SystemName { get; set; }
            public bool IsActive { get; set; }
            public DeviceType Type { get; set; }

            public override string ToString() => SystemName ?? Name ?? "Unknown Device";
        }

        public enum DeviceType { Playback, Capture }

        #region Device Enumeration

        private static IMMDeviceEnumerator CreateEnumerator() => (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();

        private static string GetDeviceFriendlyName(IMMDevice device)
        {
            IPropertyStore store = null;
            PROPVARIANT pv = new PROPVARIANT();
            try
            {
                if (device.OpenPropertyStore(StgmRead, out store) != 0) return null;

                var key = PKEY_Device_FriendlyName;
                if (store.GetValue(ref key, out pv) != 0) return null;

                if (pv.vt != 31 || pv.pwszVal == IntPtr.Zero) return null;

                return Marshal.PtrToStringUni(pv.pwszVal);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Failed to read device friendly name from property store");
                return null;
            }
            finally
            {
                PropVariantClear(ref pv);
                if (store != null)
                    Marshal.ReleaseComObject(store);
            }
        }

        private static AudioDeviceInfo DeviceToInfo(IMMDevice device, DeviceType type)
        {
            device.GetId(out var id);
            device.GetState(out var state);

            var friendlyName = GetDeviceFriendlyName(device);
            // Bluetooth devices sometimes surface as "Unknown" — try WMI correlation
            if (string.IsNullOrEmpty(friendlyName) || friendlyName.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                friendlyName = TryGetBluetoothName(id) ?? friendlyName;
            if (!string.IsNullOrEmpty(friendlyName) && !friendlyName.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                CacheDeviceName(id, friendlyName);

            return new AudioDeviceInfo
            {
                Id = id,
                Name = friendlyName,
                SystemName = friendlyName,
                IsActive = state == DeviceStateActive,
                Type = type
            };
        }

        public static List<AudioDeviceInfo> GetPlaybackDevices()
        {
            var devices = new List<AudioDeviceInfo>();
            IMMDeviceEnumerator enumerator = null;
            IMMDeviceCollection collection = null;
            try
            {
                enumerator = CreateEnumerator();
                if (enumerator.EnumAudioEndpoints(EDataFlow.Render, DeviceStateActive, out collection) == 0)
                {
                    collection.GetCount(out var count);
                    for (uint i = 0; i < count; i++)
                    {
                        IMMDevice device = null;
                        try
                        {
                            if (collection.Item(i, out device) == 0)
                                devices.Add(DeviceToInfo(device, DeviceType.Playback));
                        }
                        catch (Exception ex)
                        {
                            logger.Error(ex, "Error processing playback device");
                        }
                        finally
                        {
                            if (device != null)
                                Marshal.ReleaseComObject(device);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "GetPlaybackDevices failed");
            }
            finally
            {
                if (collection != null)
                    Marshal.ReleaseComObject(collection);
                if (enumerator != null)
                    Marshal.ReleaseComObject(enumerator);
            }

            return devices;
        }

        public static List<AudioDeviceInfo> GetCaptureDevices()
        {
            var devices = new List<AudioDeviceInfo>();
            IMMDeviceEnumerator enumerator = null;
            IMMDeviceCollection collection = null;
            try
            {
                enumerator = CreateEnumerator();
                if (enumerator.EnumAudioEndpoints(EDataFlow.Capture, DeviceStateActive, out collection) == 0)
                {
                    collection.GetCount(out var count);
                    for (uint i = 0; i < count; i++)
                    {
                        IMMDevice device = null;
                        try
                        {
                            if (collection.Item(i, out device) == 0)
                                devices.Add(DeviceToInfo(device, DeviceType.Capture));
                        }
                        catch (Exception ex) {
                            logger.Error(ex, "Error processing capture device");
                        }
                        finally
                        {
                            if (device != null) Marshal.ReleaseComObject(device);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "GetCaptureDevices failed");
            }
            finally
            {
                if (collection != null)
                    Marshal.ReleaseComObject(collection);
                if (enumerator != null)
                    Marshal.ReleaseComObject(enumerator);
            }

            return devices;
        }

        public static AudioDeviceInfo GetDefaultPlaybackDevice()
        {
            IMMDeviceEnumerator enumerator = null;
            IMMDevice device = null;
            try
            {
                enumerator = CreateEnumerator();
                if (enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Console, out device) != 0) return null;

                return DeviceToInfo(device, DeviceType.Playback);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "GetDefaultPlaybackDevice failed");
                return null;
            }
            finally
            {
                if (device != null)
                    Marshal.ReleaseComObject(device);
                if (enumerator != null)
                    Marshal.ReleaseComObject(enumerator);
            }
        }

        public static AudioDeviceInfo GetDefaultCaptureDevice()
        {
            IMMDeviceEnumerator enumerator = null;
            IMMDevice device = null;
            try
            {
                enumerator = CreateEnumerator();
                if (enumerator.GetDefaultAudioEndpoint(EDataFlow.Capture, ERole.Console, out device) != 0) return null;

                return DeviceToInfo(device, DeviceType.Capture);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "GetDefaultCaptureDevice failed");
                return null;
            }
            finally
            {
                if (device != null)
                    Marshal.ReleaseComObject(device);
                if (enumerator != null)
                    Marshal.ReleaseComObject(enumerator);
            }
        }

        #endregion

        #region Apply

        private static bool SetDefaultEndpoint(string deviceId)
        {
            IPolicyConfig policyConfig = null;
            try
            {
                policyConfig = (IPolicyConfig)new PolicyConfigClientComObject();
                var hr1 = policyConfig.SetDefaultEndpoint(deviceId, ERole.Console);
                var hr2 = policyConfig.SetDefaultEndpoint(deviceId, ERole.Multimedia);
                var hr3 = policyConfig.SetDefaultEndpoint(deviceId, ERole.Communications);
                if (hr1 != 0 || hr2 != 0 || hr3 != 0)
                {
                    logger.Warn($"SetDefaultEndpoint partial failure — HRESULT console={hr1:X} multimedia={hr2:X} comms={hr3:X}");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"SetDefaultEndpoint failed for {deviceId}");
                return false;
            }
            finally
            {
                if (policyConfig != null)
                    Marshal.ReleaseComObject(policyConfig);
            }
        }

        public static bool SetDefaultPlaybackDevice(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId))
            {
                logger.Warn("SetDefaultPlaybackDevice called with null/empty ID");
                return false;
            }
            IMMDeviceEnumerator enumerator = null;
            IMMDevice device = null;
            try
            {
                enumerator = CreateEnumerator();
                if (enumerator.GetDevice(deviceId, out device) != 0 || device == null)
                {
                    logger.Warn($"Playback device not found: {deviceId}");
                    return false;
                }
                var result = SetDefaultEndpoint(deviceId);
                if (result)
                {
                    device.GetId(out var id);
                    logger.Info($"Set default playback device: {GetCachedName(id) ?? deviceId}");
                }

                return result;
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"SetDefaultPlaybackDevice failed: {deviceId}");
                return false;
            }
            finally
            {
                if (device != null)
                    Marshal.ReleaseComObject(device);
                if (enumerator != null)
                    Marshal.ReleaseComObject(enumerator);
            }
        }

        public static bool SetDefaultCaptureDevice(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId))
            {
                logger.Warn("SetDefaultCaptureDevice called with null/empty ID");
                return false;
            }
            IMMDeviceEnumerator enumerator = null;
            IMMDevice device = null;
            try
            {
                enumerator = CreateEnumerator();
                if (enumerator.GetDevice(deviceId, out device) != 0 || device == null)
                {
                    logger.Warn($"Capture device not found: {deviceId}");
                    return false;
                }
                var result = SetDefaultEndpoint(deviceId);
                if (result)
                {
                    device.GetId(out var id);
                    logger.Info($"Set default capture device: {GetCachedName(id) ?? deviceId}");
                }

                return result;
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"SetDefaultCaptureDevice failed: {deviceId}");
                return false;
            }
            finally
            {
                if (device != null)
                    Marshal.ReleaseComObject(device);
                if (enumerator != null)
                    Marshal.ReleaseComObject(enumerator);
            }
        }

        public static bool ApplyAudioSettings(Core.AudioSetting audioSettings)
        {
            if (audioSettings == null)
            {
                logger.Debug("No audio settings to apply");
                return true;
            }

            bool allSucceeded = true;
            try
            {
                if (audioSettings.ApplyPlaybackDevice)
                {
                    if (audioSettings.HasPlaybackDevice())
                    {
                        if (!SetDefaultPlaybackDevice(audioSettings.DefaultPlaybackDeviceId))
                        {
                            logger.Warn($"Failed to set playback device: {audioSettings.PlaybackDeviceName}");
                            allSucceeded = false;
                        }
                    }
                    else
                        logger.Debug("Playback apply enabled but no device configured");
                }
                else
                    logger.Debug("Playback device apply disabled");

                if (audioSettings.ApplyCaptureDevice)
                {
                    if (audioSettings.HasCaptureDevice())
                    {
                        if (!SetDefaultCaptureDevice(audioSettings.DefaultCaptureDeviceId))
                        {
                            logger.Warn($"Failed to set capture device: {audioSettings.CaptureDeviceName}");
                            allSucceeded = false;
                        }
                    }
                    else
                        logger.Debug("Capture apply enabled but no device configured");
                }
                else
                    logger.Debug("Capture device apply disabled");

                if (!allSucceeded) logger.Warn("Some audio settings failed to apply");

                return allSucceeded;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error applying audio settings");
                return false;
            }
        }

        #endregion

        #region Bluetooth name resolution

        private static string TryGetBluetoothName(string deviceId)
        {
            lock (_lock)
                if (_deviceCache.TryGetValue(deviceId, out var entry))
                    return entry.Name;

            return GetDeviceNameViaWmi(deviceId);
        }

        private static string GetDeviceNameViaWmi(string deviceId)
        {
            try
            {
                var targetMac = ExtractMacAddress(deviceId);
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE ConfigManagerErrorCode = 0"))
                {
                    searcher.Options.Timeout = TimeSpan.FromSeconds(5);
                    foreach (var wmiDevice in searcher.Get())
                    {
                        var name = wmiDevice["Name"]?.ToString();
                        var wmiId = wmiDevice["DeviceID"]?.ToString();
                        if (IsBluetoothDevice(name) && IsDeviceRelated(deviceId, wmiId)) return name;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "WMI Bluetooth name lookup failed");
            }

            return null;
        }

        private static bool IsDeviceRelated(string deviceId, string wmiDeviceId)
        {
            if (string.IsNullOrEmpty(wmiDeviceId)) return false;
            foreach (var wmiGuid in ExtractGuids(wmiDeviceId))
                foreach (var targetGuid in ExtractGuids(deviceId))
                    if (wmiGuid.Equals(targetGuid, StringComparison.OrdinalIgnoreCase)) return true;

            return false;
        }

        private static bool IsBluetoothDevice(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName)) return false;

            var lower = deviceName.ToLower();
            foreach (var indicator in new[] { "bluetooth", "bt", "wireless", "airpods", "headset", "earbuds", "buds", "headphones", "stereo", "hands-free", "hfp", "a2dp", "sco" })
                if (lower.Contains(indicator)) return true;

            return false;
        }

        private static string ExtractMacAddress(string deviceId)
        {
            try
            {
                var clean = System.Text.RegularExpressions.Regex.Replace(deviceId, @"[-{}\\\#&]", "");
                var match = System.Text.RegularExpressions.Regex.Match(clean, @"[0-9A-Fa-f]{12}");
                if (!match.Success) return null;
                var mac = match.Value.ToUpper();
                return $"{mac[0]}{mac[1]}:{mac[2]}{mac[3]}:{mac[4]}{mac[5]}:{mac[6]}{mac[7]}:{mac[8]}{mac[9]}:{mac[10]}{mac[11]}";
            }
            catch
            {
                return null;
            }
        }

        private static List<string> ExtractGuids(string input)
        {
            var guids = new List<string>();
            if (string.IsNullOrEmpty(input)) return guids;

            var fullGuids = System.Text.RegularExpressions.Regex.Matches(input, @"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b");
            foreach (System.Text.RegularExpressions.Match m in fullGuids) guids.Add(m.Value);

            var hexParts = System.Text.RegularExpressions.Regex.Matches(input, @"\b[0-9a-fA-F]{8}\b");
            foreach (System.Text.RegularExpressions.Match m in hexParts) guids.Add(m.Value);

            return guids;
        }

        #endregion

        #region Device Name Cache

        private static void CacheDeviceName(string deviceId, string name)
        {
            if (string.IsNullOrEmpty(deviceId) || string.IsNullOrEmpty(name)) return;

            lock (_lock)
            {
                _deviceCache[deviceId] = (name, DateTime.Now);
                if (_deviceCache.Count > 100)
                    TrimCache();
            }
        }

        private static string GetCachedName(string deviceId)
        {
            lock (_lock)
            {
                return _deviceCache.TryGetValue(deviceId, out var entry) ? entry.Name : null;
            }
        }

        private static void TrimCache()
        {
            var sorted = new List<KeyValuePair<string, (string Name, DateTime Discovered)>>(_deviceCache);
            sorted.Sort((a, b) => a.Value.Discovered.CompareTo(b.Value.Discovered));
            for (int i = 0; i < sorted.Count - 100; i++)
                _deviceCache.Remove(sorted[i].Key);
        }

        #endregion
    }
}