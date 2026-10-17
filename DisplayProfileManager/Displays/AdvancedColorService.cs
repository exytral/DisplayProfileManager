using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

using NLog;

using DisplayProfileManager.Common;
using DisplayProfileManager.Displays.Models;
using static DisplayProfileManager.Displays.Interop.DisplayConfigNative;

namespace DisplayProfileManager.Displays
{
    public class AdvancedColorService
    {
        private static readonly Logger _logger = LoggerHelper.GetLogger();

        public enum DisplayConfigColorIntent
        {
            Off,
            Acm,
            Hdr
        }

        internal enum LegacyAcmMutationResult
        {
            Applied,
            NonFatalProviderSkip,
            Failed
        }

        internal static bool IsWindows11OrGreater() => Environment.OSVersion.Version.Build >= 22000;

        private static bool IsWindows22H2OrGreater() => Environment.OSVersion.Version.Build >= 22621;

        internal static bool IsWindows24H2OrGreater() => Environment.OSVersion.Version.Build >= 26100;

        internal static bool IsLegacyAcmSupported(bool isWcgSupported) => IsWindows22H2OrGreater() && !IsWindows24H2OrGreater() && isWcgSupported;

        internal static void ApplyAdvancedColorInfo2(DisplayConfigInfo displayConfig, DisplayConfigGetAdvancedColorInfo2 colorInfo2)
        {
            if (displayConfig == null) throw new ArgumentNullException(nameof(displayConfig));

            var flags = colorInfo2.values;
            bool limitedByPolicy = (flags & DisplayConfigAdvancedColorInfo2Flags.AdvancedColorLimitedByPolicy) != 0;

            displayConfig.IsHdrSupported = !limitedByPolicy && (flags & DisplayConfigAdvancedColorInfo2Flags.HighDynamicRangeSupported) != 0;
            displayConfig.IsWcgSupported = !limitedByPolicy && (flags & DisplayConfigAdvancedColorInfo2Flags.WideColorSupported) != 0;
            displayConfig.IsHdrEnabled = colorInfo2.activeColorMode == DisplayConfigAdvancedColorMode.Hdr;
            displayConfig.IsWcgEnabled = colorInfo2.activeColorMode == DisplayConfigAdvancedColorMode.Wcg;
            displayConfig.IsAdvancedColorInfoAvailable = true;
            displayConfig.ColorEncoding = colorInfo2.colorEncoding;
            displayConfig.BitsPerColorChannel = colorInfo2.bitsPerColorChannel;
        }

        internal static void CaptureAdvancedColorState(
            DisplayConfigInfo displayConfig,
            LUID adapterId,
            uint targetId,
            bool useModernContract,
            GetAdvancedColorInfo2Delegate getInfo2,
            GetLegacyAdvancedColorInfoDelegate getLegacyInfo)
        {
            if (displayConfig == null) throw new ArgumentNullException(nameof(displayConfig));

            if (useModernContract)
            {
                if (getInfo2 == null) throw new ArgumentNullException(nameof(getInfo2));

                if (getInfo2(adapterId, targetId, out var colorInfo2))
                {
                    ApplyAdvancedColorInfo2(displayConfig, colorInfo2);
                    return;
                }

                MarkAdvancedColorUnavailable(displayConfig);
                _logger.Debug($"Failed to get Advanced Color INFO_2 for {displayConfig.DeviceName}; HDR/WCG capability and state remain unavailable.");
                return;
            }

            if (getLegacyInfo == null) throw new ArgumentNullException(nameof(getLegacyInfo));

            if (getLegacyInfo(adapterId, targetId, out var colorInfo))
            {
                ApplyLegacyAdvancedColorInfo(displayConfig, colorInfo);
                return;
            }

            MarkAdvancedColorUnavailable(displayConfig);
            _logger.Debug($"Failed to get legacy Advanced Color info for {displayConfig.DeviceName}; Advanced Color state remains unavailable.");
        }

        private static void ApplyLegacyAdvancedColorInfo(DisplayConfigInfo displayConfig, DisplayConfigGetAdvancedColorInfo colorInfo)
        {
            var flags = colorInfo.values;
            bool isSupported = (flags & DisplayConfigAdvancedColorInfoFlags.AdvancedColorSupported) != 0;
            bool isEnabled = (flags & DisplayConfigAdvancedColorInfoFlags.AdvancedColorEnabled) != 0;
            bool isWideColorEnforced = (flags & DisplayConfigAdvancedColorInfoFlags.WideColorEnforced) != 0;
            bool isForceDisabled = (flags & DisplayConfigAdvancedColorInfoFlags.AdvancedColorForceDisabled) != 0;

            // Legacy INFO has no active-mode field; use its WCG flag rather than link encoding to distinguish the provider.
            bool isWcgProvider = !isForceDisabled && isWideColorEnforced;
            bool isHdrProvider = !isForceDisabled && !isWideColorEnforced && isSupported;

            displayConfig.IsHdrSupported = isHdrProvider;
            displayConfig.IsWcgSupported = isWcgProvider;
            displayConfig.IsHdrEnabled = isHdrProvider && isEnabled;
            displayConfig.IsWcgEnabled = isWcgProvider && isEnabled;
            displayConfig.IsAdvancedColorInfoAvailable = true;
            displayConfig.ColorEncoding = colorInfo.colorEncoding;
            displayConfig.BitsPerColorChannel = (uint)colorInfo.bitsPerColorChannel;
        }

        private static void MarkAdvancedColorUnavailable(DisplayConfigInfo displayConfig)
        {
            displayConfig.IsHdrSupported = false;
            displayConfig.IsWcgSupported = false;
            displayConfig.IsHdrEnabled = false;
            displayConfig.IsWcgEnabled = false;
            displayConfig.IsAdvancedColorInfoAvailable = false;
        }

        public static bool ApplyAdvancedColorState(List<DisplayConfigInfo> displayConfigs)
        {
            _logger.Info("Applying Advanced Color state...");

            return ApplyAdvancedColorState(
                displayConfigs,
                DisplayConfigService.GetDisplayConfigs(),
                IsWindows24H2OrGreater(),
                SetHdrState,
                SetWcgState,
                SetLegacyAcmState);
        }

        internal static bool ApplyAdvancedColorState(
            List<DisplayConfigInfo> displayConfigs,
            List<DisplayConfigInfo> liveConfigs,
            bool isWindows24H2OrGreater,
            Func<AdapterLuid, uint, bool, bool> setHdrState,
            Func<AdapterLuid, uint, bool, bool> setWcgState,
            Func<AdapterLuid, uint, bool, bool> setLegacyAcmState)
        {
            bool allSuccessful = true;
            foreach (var profileDisplay in displayConfigs)
            {
                if (!profileDisplay.IsEnabled) continue;

                var targetKey = CcdAddress.Target(profileDisplay);
                var activeDisplay = liveConfigs.FirstOrDefault(c => CcdAddress.Target(c).Equals(targetKey));
                if (activeDisplay == null)
                {
                    if (profileDisplay.IsHdrSupported || profileDisplay.IsHdrEnabled || profileDisplay.IsWcgEnabled)
                    {
                        _logger.Warn($"Could not find active display matching TargetId {profileDisplay.TargetId} to apply Advanced Color");
                        allSuccessful = false;
                    }
                    continue;
                }

                try
                {
                    if (isWindows24H2OrGreater)
                    {
                        if (!activeDisplay.IsAdvancedColorInfoAvailable)
                        {
                            _logger.Warn($"Cannot apply Advanced Color for {activeDisplay.FriendlyName}: live HDR/WCG state is unavailable");
                            allSuccessful = false;
                            continue;
                        }

                        var currentMode = GetEffectiveAdvancedColorMode(activeDisplay.IsHdrEnabled, activeDisplay.IsWcgEnabled);
                        var desiredMode = GetEffectiveAdvancedColorMode(profileDisplay.IsHdrEnabled, profileDisplay.IsWcgEnabled);

                        if (currentMode == desiredMode)
                        {
                            _logger.Debug($"Skipping Advanced Color for {activeDisplay.FriendlyName} -> already {desiredMode}");
                            continue;
                        }

                        if ((currentMode == DisplayConfigAdvancedColorMode.Hdr || desiredMode == DisplayConfigAdvancedColorMode.Hdr) && !activeDisplay.IsHdrSupported)
                        {
                            _logger.Warn($"Cannot change HDR state for {activeDisplay.FriendlyName}: HDR is not currently supported or is limited by policy");
                            allSuccessful = false;
                            continue;
                        }

                        if ((currentMode == DisplayConfigAdvancedColorMode.Wcg || desiredMode == DisplayConfigAdvancedColorMode.Wcg) && !activeDisplay.IsWcgSupported)
                        {
                            _logger.Warn($"Cannot change WCG state for {activeDisplay.FriendlyName}: WCG is not currently supported or is limited by policy");
                            allSuccessful = false;
                            continue;
                        }

                        if (desiredMode == DisplayConfigAdvancedColorMode.Hdr)
                        {
                            _logger.Info($"Setting {activeDisplay.FriendlyName} -> Advanced Color to HDR");
                            if (!setHdrState(activeDisplay.AdapterId, activeDisplay.RawTargetId, true))
                            {
                                _logger.Error($"Failed to apply HDR setting for {activeDisplay.FriendlyName}");
                                allSuccessful = false;
                                continue;
                            }

                            // Effective Advanced Color destination remains HDR regardless of the context-dependent WCG user bit
                            continue;
                        }

                        if (currentMode == DisplayConfigAdvancedColorMode.Hdr)
                        {
                            _logger.Info($"Setting {activeDisplay.FriendlyName} -> HDR off before {desiredMode}");
                            if (!setHdrState(activeDisplay.AdapterId, activeDisplay.RawTargetId, false))
                            {
                                _logger.Error($"Failed to disable HDR for {activeDisplay.FriendlyName}");
                                allSuccessful = false;
                                continue;
                            }
                        }

                        bool wantWcg = desiredMode == DisplayConfigAdvancedColorMode.Wcg;
                        if (!wantWcg && currentMode == DisplayConfigAdvancedColorMode.Hdr && !activeDisplay.IsWcgSupported)
                        {
                            _logger.Debug($"Skipping WCG-off mutation for {activeDisplay.FriendlyName} -> WCG is not supported on this target");
                            continue;
                        }

                        _logger.Info($"Setting {activeDisplay.FriendlyName} -> WCG to {(wantWcg ? "on" : "off")}");
                        if (!setWcgState(activeDisplay.AdapterId, activeDisplay.RawTargetId, wantWcg))
                        {
                            _logger.Error($"Failed to apply WCG setting for {activeDisplay.FriendlyName}");
                            allSuccessful = false;
                        }

                        continue;
                    }

                    // Pre-24H2, legacy provider exposes HDR on HDR-capable targets and ACM otherwise
                    if (profileDisplay.IsHdrEnabled && !activeDisplay.IsHdrSupported)
                    {
                        _logger.Warn($"Cannot apply HDR for {activeDisplay.FriendlyName}: HDR is not supported by the live target");
                        allSuccessful = false;
                        continue;
                    }

                    if (activeDisplay.IsHdrSupported && activeDisplay.IsHdrEnabled != profileDisplay.IsHdrEnabled)
                    {
                        _logger.Info($"Setting {activeDisplay.FriendlyName} -> HDR to {(profileDisplay.IsHdrEnabled ? "on" : "off")}");
                        if (!setHdrState(activeDisplay.AdapterId, activeDisplay.RawTargetId, profileDisplay.IsHdrEnabled))
                        {
                            _logger.Error($"Failed to apply HDR setting for {activeDisplay.FriendlyName}");
                            allSuccessful = false;
                            continue;
                        }
                    }
                    else if (activeDisplay.IsHdrSupported)
                    {
                        _logger.Debug($"Skipping HDR for {activeDisplay.FriendlyName} -> already {(profileDisplay.IsHdrEnabled ? "on" : "off")}");
                    }

                    if (profileDisplay.IsHdrEnabled) continue;

                    bool wantLegacyAcm = profileDisplay.IsWcgEnabled;
                    if (wantLegacyAcm && !activeDisplay.IsWcgSupported && !activeDisplay.IsHdrSupported)
                    {
                        _logger.Warn($"Cannot apply legacy ACM for {activeDisplay.FriendlyName}: WCG/ACM is not supported by the live target");
                        allSuccessful = false;
                        continue;
                    }

                    if (wantLegacyAcm != activeDisplay.IsWcgEnabled)
                    {
                        _logger.Info($"Setting {activeDisplay.FriendlyName} -> legacy ACM to {(wantLegacyAcm ? "on" : "off")}");
                        bool setterSucceeded = setLegacyAcmState(activeDisplay.AdapterId, activeDisplay.RawTargetId, wantLegacyAcm);
                        var mutationResult = ClassifyLegacyAcmMutationResult(setterSucceeded, wantLegacyAcm, activeDisplay.IsHdrSupported);

                        if (mutationResult == LegacyAcmMutationResult.Failed)
                        {
                            _logger.Error($"Failed to apply legacy ACM setting for {activeDisplay.FriendlyName}");
                            allSuccessful = false;
                        }
                    }
                    else
                        _logger.Debug($"Skipping legacy ACM for {activeDisplay.FriendlyName} -> already {(wantLegacyAcm ? "on" : "off")}");
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, $"Advanced color state failed for {activeDisplay.FriendlyName} (TargetId {activeDisplay.TargetId})");
                    allSuccessful = false;
                }
            }

            return allSuccessful;
        }

        internal static DisplayConfigAdvancedColorMode GetEffectiveAdvancedColorMode(bool hdrEnabled, bool wcgEnabled) => hdrEnabled ? DisplayConfigAdvancedColorMode.Hdr : (wcgEnabled ? DisplayConfigAdvancedColorMode.Wcg : DisplayConfigAdvancedColorMode.Sdr);

        internal static LegacyAcmMutationResult ClassifyLegacyAcmMutationResult(bool setterSucceeded, bool enable, bool isHdrSupported)
        {
            if (setterSucceeded)
            {
                return LegacyAcmMutationResult.Applied;
            }

            if (enable && isHdrSupported)
            {
                return LegacyAcmMutationResult.NonFatalProviderSkip;
            }

            return LegacyAcmMutationResult.Failed;
        }

        public static bool SetAdvancedColorState(AdapterLuid adapterId, uint rawTargetId, DisplayConfigColorIntent intent) =>
            SetAdvancedColorState(adapterId, rawTargetId, intent, enabled =>
            {
                var state = BuildColorStateStruct(adapterId, rawTargetId, enabled);
                return DisplayConfigSetDeviceInfo(ref state);
            });

        internal static bool SetAdvancedColorState(AdapterLuid adapterId, uint rawTargetId, DisplayConfigColorIntent intent, Func<bool, int> setNativeState)
        {
            if (setNativeState == null) throw new ArgumentNullException(nameof(setNativeState));

            try
            {
                // HDR and ACM share one enable bit; reset to SDR context first so Windows picks ACM rather than HDR
                if (intent == DisplayConfigColorIntent.Acm)
                {
                    int resetResult = setNativeState(false);
                    if (resetResult != ErrorSuccess)
                    {
                        _logger.Error($"Failed to reset Advanced Color to SDR for RawTargetId {rawTargetId}: Error {resetResult}");
                        return false;
                    }
                }

                int result = setNativeState(intent != DisplayConfigColorIntent.Off);
                if (result == ErrorSuccess)
                {
                    _logger.Info($"Set advanced color to {intent} for RawTargetId {rawTargetId}");
                    return true;
                }

                _logger.Error($"Failed to set advanced color for RawTargetId {rawTargetId}: Error {result}");
                return false;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error setting advanced color for RawTargetId {rawTargetId}");
                return false;
            }
        }

        public static bool SetHdrState(AdapterLuid adapterId, uint rawTargetId, bool enable)
        {
            if (IsWindows24H2OrGreater())
            {
                var s = new DisplayConfigSetHdrState();
                s.header.type = DisplayConfigDeviceInfoType.SetHdrState;
                s.header.size = (uint)Marshal.SizeOf(typeof(DisplayConfigSetHdrState));
                s.header.adapterId = ToNative(adapterId);
                s.header.id = rawTargetId;
                s.value = enable ? 1u : 0u;

                int result = DisplayConfigSetDeviceInfo(ref s);
                if (result == ErrorSuccess)
                {
                    _logger.Info($"Set HDR to {enable} for RawTargetId {rawTargetId}");
                    return true;
                }

                _logger.Error($"Failed to set HDR state for RawTargetId {rawTargetId}: Error {result}");
                return false;
            }
            // Pre-24H2, fall back to legacy Advanced Color path
            return SetAdvancedColorState(adapterId, rawTargetId, enable ? DisplayConfigColorIntent.Hdr : DisplayConfigColorIntent.Off);
        }

        public static bool SetLegacyAcmState(AdapterLuid adapterId, uint rawTargetId, bool enable)
        {
            if (!enable)
            {
                return SetAdvancedColorState(adapterId, rawTargetId, DisplayConfigColorIntent.Off);
            }

            // Pre-24H2, legacy Advanced Color switch represents HDR on HDR-capable providers and ACM otherwise
            var liveConfigs = DisplayConfigService.GetDisplayConfigs();
            var display = liveConfigs.FirstOrDefault(c => CcdAddress.LuidEquals(c.AdapterId, adapterId) && c.RawTargetId == rawTargetId);
            if (display?.IsHdrSupported == true)
            {
                _logger.Warn($"ACM is not supported on HDR-capable displays before Windows 11 24H2 (RawTargetId {rawTargetId})");
                return false;
            }

            return SetAdvancedColorState(adapterId, rawTargetId, DisplayConfigColorIntent.Acm);
        }

        internal static bool GetLegacyAdvancedColorInfo(LUID adapterId, uint targetId, out DisplayConfigGetAdvancedColorInfo colorInfo)
        {
            colorInfo = new DisplayConfigGetAdvancedColorInfo();
            colorInfo.header.type = DisplayConfigDeviceInfoType.GetAdvancedColorInfo;
            colorInfo.header.size = (uint)Marshal.SizeOf(typeof(DisplayConfigGetAdvancedColorInfo));
            colorInfo.header.adapterId = adapterId;
            colorInfo.header.id = targetId;

            return DisplayConfigGetDeviceInfo(ref colorInfo) == ErrorSuccess;
        }

        internal static bool GetAdvancedColorInfo2(LUID adapterId, uint targetId, out DisplayConfigGetAdvancedColorInfo2 colorInfo)
        {
            colorInfo = new DisplayConfigGetAdvancedColorInfo2();
            colorInfo.header.type = DisplayConfigDeviceInfoType.GetAdvancedColorInfo2;
            colorInfo.header.size = (uint)Marshal.SizeOf(typeof(DisplayConfigGetAdvancedColorInfo2));
            colorInfo.header.adapterId = adapterId;
            colorInfo.header.id = targetId;

            return DisplayConfigGetDeviceInfo(ref colorInfo) == ErrorSuccess;
        }

        private static bool SetWcgState(AdapterLuid adapterId, uint rawTargetId, bool enable)
        {
            var s = new DisplayConfigSetWcgState();
            s.header.type = DisplayConfigDeviceInfoType.SetWcgState;
            s.header.size = (uint)Marshal.SizeOf(typeof(DisplayConfigSetWcgState));
            s.header.adapterId = ToNative(adapterId);
            s.header.id = rawTargetId;
            s.value = enable ? 1u : 0u;

            int result = DisplayConfigSetDeviceInfo(ref s);
            if (result == ErrorSuccess)
            {
                _logger.Info($"Set WCG to {enable} for RawTargetId {rawTargetId}");
                return true;
            }

            _logger.Error($"Failed to set WCG state for RawTargetId {rawTargetId}: Error {result}");
            return false;
        }

        private static DisplayConfigSetAdvancedColorState BuildColorStateStruct(AdapterLuid adapterId, uint rawTargetId, bool enable)
        {
            var s = new DisplayConfigSetAdvancedColorState();
            s.header.type = DisplayConfigDeviceInfoType.SetAdvancedColorState;
            s.header.size = (uint)Marshal.SizeOf(typeof(DisplayConfigSetAdvancedColorState));
            s.header.adapterId = ToNative(adapterId);
            s.header.id = rawTargetId;
            s.values = enable ? DisplayConfigSetAdvancedColorFlags.EnableAdvancedColor : 0;
            return s;
        }
    }
}
