using System;
using System.Linq;

using NLog;

using DisplayProfileManager.Common;
using DisplayProfileManager.Vrr.Amd.Interop;
using DisplayProfileManager.Vrr.Models;

namespace DisplayProfileManager.Vrr.Amd
{
    // AMD FreeSync through the driver-installed ADLX runtime, nothing is shipped with the app
    // Untested on hardware: written without an AMD GPU, see docs/wiki/profiles.md
    public class AmdVrrBackend : IVrrBackend
    {
        private static readonly Logger _logger = LoggerHelper.GetLogger();
        private static readonly object _applyLock = new();

        internal const string Technology = "AMD FreeSync";

        public VrrCapability GetCapability()
        {
            try
            {
                lock (_applyLock)
                {
                    using var session = AdlxNative.Session.Open(out string openError);
                    if (session == null)
                    {
                        _logger.Debug($"AMD FreeSync unavailable -> {openError}");
                        return VrrCapability.Unavailable;
                    }

                    var displays = session.GetFreeSyncDisplays(out string error);
                    if (error != null) _logger.Debug($"AMD FreeSync displays could not be listed: {error}");

                    var states = displays.Select(d => new AmdVrrPlan.DisplayState(d.Supported, d.Enabled)).ToList();
                    if (!states.Any(s => s.Supported)) return VrrCapability.Unavailable;

                    return new VrrCapability(true, Technology, false, AmdVrrPlan.DecodeCurrent(states));
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "AMD FreeSync capability check failed");
                return VrrCapability.Unavailable;
            }
        }

        public bool Apply(VrrMode mode)
        {
            try
            {
                lock (_applyLock)
                {
                    return ApplyCore(mode);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "AMD FreeSync apply failed");
                return false;
            }
        }

        private static bool ApplyCore(VrrMode requested)
        {
            var mode = AmdVrrPlan.EffectiveMode(requested);
            if (mode != requested)
                _logger.Info($"AMD FreeSync has no fullscreen-only mode, applying {VrrSetting.GetModeLabel(mode)}");

            using var session = AdlxNative.Session.Open(out string openError);
            if (session == null)
            {
                _logger.Warn($"Skipping AMD FreeSync apply -> {openError}");
                return false;
            }

            var displays = session.GetFreeSyncDisplays(out string error);
            if (error != null)
            {
                _logger.Error($"AMD FreeSync displays could not be listed: {error}");
                return false;
            }

            var states = displays.Select(d => new AmdVrrPlan.DisplayState(d.Supported, d.Enabled)).ToList();
            bool enable = AmdVrrPlan.ShouldEnable(mode);
            bool success = true;

            foreach (int index in AmdVrrPlan.SelectChanges(states, mode))
            {
                var freeSync = displays[index].FreeSync;
                if (!session.SetEnabled(freeSync, enable) || session.IsEnabled(freeSync) != enable)
                {
                    _logger.Error($"AMD FreeSync could not be set to {VrrSetting.GetModeLabel(mode)} on display {index}");
                    success = false;
                }
            }

            if (success)
                _logger.Info($"AMD FreeSync set to {VrrSetting.GetModeLabel(mode)}");
            return success;
        }
    }
}
