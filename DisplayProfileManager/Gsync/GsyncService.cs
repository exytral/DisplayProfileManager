using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

using NLog;

using DisplayProfileManager.Common;
using DisplayProfileManager.Gsync.Interop;
using DisplayProfileManager.Gsync.Models;

using static DisplayProfileManager.Displays.Interop.DisplayConfigNative;
using static DisplayProfileManager.Gsync.Interop.GsyncDisplayNative;

namespace DisplayProfileManager.Gsync
{
    // Writes the global NVIDIA driver profile the same way the NVIDIA Control Panel G-SYNC switch does
    // Save and modeset sequence adapted from GsyncStatusTrayToggle (MIT), see THIRD-PARTY-LICENSES.md
    public class GsyncService : IGsyncService
    {
        private static readonly Logger _logger = LoggerHelper.GetLogger();
        private static readonly object _applyLock = new();

        // Turning G-SYNC on or off re-trains NVIDIA displays; these waits follow the monitor removal and return
        private const int ModesetWaitMs = 1500;
        private const int StepWaitMs = 200;
        private const int RemovalWaitMs = 500;
        private const int ReturnTimeoutMs = 4000;
        private const int ReturnSettleMs = 600;
        private const int RateWatchMs = 2500;
        private const int PollWaitMs = 50;

        public bool IsAvailable()
        {
            using var session = DrsSession.Open(out _);
            return session != null;
        }

        public GsyncMode? GetCurrentMode()
        {
            using var session = DrsSession.Open(out _);
            if (session == null || !session.TryReadValues(out uint[] values, out _))
            {
                return null;
            }

            return GsyncPlan.Decode(values[GsyncPlan.ApplicationMode]);
        }

        public bool ApplyGsyncSettings(GsyncSetting settings)
        {
            if (settings == null) return true;

            lock (_applyLock)
            {
                try
                {
                    return ApplyCore(settings.Mode);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "G-SYNC apply failed");
                    return false;
                }
            }
        }

        private static bool ApplyCore(GsyncMode target)
        {
            using var session = DrsSession.Open(out string openError);
            if (session == null)
            {
                _logger.Warn($"Skipping G-SYNC apply -> {openError}");
                return false;
            }

            if (!session.TryReadValues(out uint[] before, out string readError))
            {
                _logger.Error($"G-SYNC state could not be read: {readError}");
                return false;
            }

            uint[] plan = GsyncPlan.MakeTarget(target, before);
            if (GsyncPlan.SameValues(before, plan))
            {
                _logger.Debug($"G-SYNC already {GsyncSetting.GetModeLabel(target)}");
                return true;
            }

            bool onOffChange = GsyncPlan.IsOnOffChange(before, plan);
            var previous = onOffChange ? DisplaySnapshot.Query() : null;
            using var watch = onOffChange && previous != null ? new MonitorReturnWatch(previous) : null;

            if (!session.SaveGroup(GsyncPlan.SaveGroups[0], before, plan)) return false;
            long returnedAt = watch != null ? WaitForReturn(watch, previous) : 0;

            if (!session.SaveGroup(GsyncPlan.SaveGroups[1], before, plan)) return false;
            bool redrawn = true;
            if (onOffChange && previous != null)
            {
                redrawn = RedrawDisplays(ref previous);
                Thread.Sleep(StepWaitMs);
            }

            if (!session.SaveGroup(GsyncPlan.SaveGroups[2], before, plan)) return false;
            if (returnedAt > 0 && previous != null)
                redrawn &= KeepRefreshRate(previous, returnedAt + RateWatchMs);

            if (!session.Reload(out string reloadError) || !session.TryReadValues(out uint[] after, out reloadError))
            {
                _logger.Warn($"G-SYNC saved but could not be verified: {reloadError}");
                return false;
            }

            if (!GsyncPlan.SameValues(after, plan))
            {
                _logger.Warn("G-SYNC saved but the driver reports different values; another program may have changed them");
                return false;
            }

            if (!redrawn)
                _logger.Warn("G-SYNC changed, but displays could not be restored to their previous mode");

            _logger.Info($"G-SYNC set to {GsyncSetting.GetModeLabel(target)}");
            return redrawn;
        }

        #region Display Recovery

        private static long WaitForReturn(MonitorReturnWatch watch, DisplaySnapshot expected)
        {
            if (!watch.WaitRemoved(RemovalWaitMs))
            {
                Thread.Sleep(ModesetWaitMs);
                return 0;
            }

            var timer = Stopwatch.StartNew();
            watch.WaitArrived(ReturnTimeoutMs);
            while (!expected.NvidiaTargetsPresent() && timer.ElapsedMilliseconds < ReturnTimeoutMs)
                Thread.Sleep(PollWaitMs);

            long returnedAt = Environment.TickCount64;
            Thread.Sleep(ReturnSettleMs);
            return returnedAt;
        }

        // A G-SYNC on/off modeset can bring displays back at a lower refresh rate; reapply whichever snapshot is faster
        private static bool RedrawDisplays(ref DisplaySnapshot best)
        {
            var current = DisplaySnapshot.Query();
            if (current == null || !current.HasNvidiaTarget()) return true;

            if (best.IsSlowerThan(current))
                (best, current) = (current, best);

            if (best.Apply()) return true;
            if (current.IsSlowerThan(best) || !current.Apply()) return false;

            best = current;
            return true;
        }

        private static bool KeepRefreshRate(DisplaySnapshot best, long untilTick)
        {
            while (Environment.TickCount64 < untilTick)
            {
                Thread.Sleep(PollWaitMs);
                var now = DisplaySnapshot.Query();
                if (now != null && now.IsSlowerThan(best))
                {
                    Thread.Sleep(ReturnSettleMs);
                    return best.Apply();
                }
            }

            return true;
        }

        #endregion

        #region DRS Session

        private sealed class DrsSession : IDisposable
        {
            private readonly NvApiNative.Api _api;
            private IntPtr _session;
            private IntPtr _profile;

            private DrsSession(NvApiNative.Api api, IntPtr session)
            {
                _api = api;
                _session = session;
            }

            public static DrsSession Open(out string error)
            {
                var api = NvApiNative.TryGetApi();
                if (api == null)
                {
                    error = "NVAPI is not available";
                    return null;
                }

                int status = api.Initialize();
                if (status != NvApiNative.NvapiOk)
                {
                    error = $"NVAPI initialization failed ({status})";
                    return null;
                }

                status = api.CreateSession(out IntPtr handle);
                if (status != NvApiNative.NvapiOk)
                {
                    error = $"NVIDIA settings session could not be created ({status})";
                    return null;
                }

                var session = new DrsSession(api, handle);
                if (!session.Reload(out error))
                {
                    session.Dispose();
                    return null;
                }

                return session;
            }

            public bool Reload(out string error)
            {
                int status = _api.LoadSettings(_session);
                if (status != NvApiNative.NvapiOk)
                {
                    error = $"NVIDIA settings could not be loaded ({status})";
                    return false;
                }

                status = _api.GetBaseProfile(_session, out _profile);
                if (status != NvApiNative.NvapiOk)
                {
                    error = $"NVIDIA global profile could not be opened ({status})";
                    return false;
                }

                error = null;
                return true;
            }

            public bool TryReadValues(out uint[] values, out string error)
            {
                values = new uint[GsyncPlan.SettingCount];
                for (int i = 0; i < GsyncPlan.SettingCount; i++)
                {
                    var setting = NvApiNative.CreateSetting();
                    int status = _api.GetSetting(_session, _profile, GsyncPlan.SettingIds[i], setting);
                    if (status == NvApiNative.NvapiSettingNotFound)
                    {
                        values[i] = GsyncPlan.SettingDefaults[i];
                        continue;
                    }

                    if (status != NvApiNative.NvapiOk || NvApiNative.ReadSettingType(setting) != NvApiNative.DwordType)
                    {
                        error = $"setting 0x{GsyncPlan.SettingIds[i]:X8} could not be read ({status})";
                        return false;
                    }

                    values[i] = NvApiNative.ReadCurrentValue(setting);
                }

                error = null;
                return true;
            }

            public bool SaveGroup(int[] group, uint[] before, uint[] plan)
            {
                bool changed = false;
                foreach (int i in group)
                {
                    if (before[i] == plan[i]) continue;

                    int status = _api.SetSetting(_session, _profile, NvApiNative.CreateDwordSetting(GsyncPlan.SettingIds[i], plan[i]));
                    if (status != NvApiNative.NvapiOk)
                    {
                        _logger.Error($"NVIDIA rejected G-SYNC setting 0x{GsyncPlan.SettingIds[i]:X8} ({status})");
                        return false;
                    }

                    changed = true;
                }

                if (!changed) return true;

                int saveStatus = _api.SaveSettings(_session);
                if (saveStatus != NvApiNative.NvapiOk)
                {
                    _logger.Error($"NVIDIA settings could not be saved ({saveStatus})");
                    return false;
                }

                return true;
            }

            public void Dispose()
            {
                if (_session == IntPtr.Zero) return;

                _api.DestroySession(_session);
                _session = IntPtr.Zero;
            }
        }

        #endregion

        #region Display Snapshot

        private sealed class DisplaySnapshot
        {
            private DisplayConfigPathInfo[] _paths;
            private DisplayConfigModeInfo[] _modes;

            public static DisplaySnapshot Query()
            {
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    if (GetDisplayConfigBufferSizes(QueryDisplayConfigFlags.OnlyActivePaths, out uint pathCount, out uint modeCount) != ErrorSuccess)
                        return null;

                    var paths = new DisplayConfigPathInfo[pathCount];
                    var modes = new DisplayConfigModeInfo[modeCount];
                    int rc = QueryDisplayConfig(QueryDisplayConfigFlags.OnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
                    if (rc == ErrorInsufficientBuffer) continue;
                    if (rc != ErrorSuccess) return null;

                    return new DisplaySnapshot { _paths = paths[..(int)pathCount], _modes = modes[..(int)modeCount] };
                }

                return null;
            }

            public bool Apply()
            {
                if (_paths.Length == 0) return false;

                var flags = SetDisplayConfigFlags.Apply | SetDisplayConfigFlags.UseSuppliedDisplayConfig | SetDisplayConfigFlags.NoOptimization;
                return SetDisplayConfig((uint)_paths.Length, _paths, (uint)_modes.Length, _modes, flags) == ErrorSuccess;
            }

            public bool HasNvidiaTarget() => _paths.Any(IsNvidia);

            public IEnumerable<string> NvidiaMonitorPaths()
            {
                foreach (var path in _paths.Where(IsNvidia))
                {
                    var target = new DisplayConfigTargetDeviceName();
                    target.header.type = DisplayConfigDeviceInfoType.GetTargetName;
                    target.header.size = (uint)Marshal.SizeOf<DisplayConfigTargetDeviceName>();
                    target.header.adapterId = path.targetInfo.adapterId;
                    target.header.id = path.targetInfo.id;
                    if (DisplayConfigGetDeviceInfo(ref target) == ErrorSuccess && !string.IsNullOrEmpty(target.monitorDevicePath))
                        yield return target.monitorDevicePath;
                }
            }

            public bool NvidiaTargetsPresent()
            {
                var now = Query();
                if (now == null) return false;

                return _paths.Where(IsNvidia).All(expected => now._paths.Any(actual => SameTarget(expected, actual)));
            }

            public bool IsSlowerThan(DisplaySnapshot other)
            {
                foreach (var mine in _paths.Where(IsNvidia))
                {
                    foreach (var theirs in other._paths.Where(p => SameTarget(mine, p)))
                    {
                        var a = mine.targetInfo.refreshRate;
                        var b = theirs.targetInfo.refreshRate;
                        if (a.Denominator != 0 && b.Denominator != 0 && (ulong)a.Numerator * b.Denominator < (ulong)b.Numerator * a.Denominator)
                            return true;
                    }
                }

                return false;
            }

            private static bool SameTarget(DisplayConfigPathInfo a, DisplayConfigPathInfo b) =>
                a.targetInfo.id == b.targetInfo.id
                && a.targetInfo.adapterId.LowPart == b.targetInfo.adapterId.LowPart
                && a.targetInfo.adapterId.HighPart == b.targetInfo.adapterId.HighPart;

            private static bool IsNvidia(DisplayConfigPathInfo path)
            {
                var adapter = new DisplayConfigAdapterName();
                adapter.header.type = DisplayConfigDeviceInfoType.GetAdapterName;
                adapter.header.size = (uint)Marshal.SizeOf<DisplayConfigAdapterName>();
                adapter.header.adapterId = path.targetInfo.adapterId;
                return DisplayConfigGetDeviceInfo(ref adapter) == ErrorSuccess
                    && adapter.adapterDevicePath?.Contains("ven_10de", StringComparison.OrdinalIgnoreCase) == true;
            }
        }

        #endregion

        #region Monitor Return Watch

        private sealed class MonitorReturnWatch : IDisposable
        {
            private readonly HashSet<string> _monitors;
            private readonly ManualResetEventSlim _removed = new();
            private readonly ManualResetEventSlim _arrived = new();
            private readonly CmNotifyCallback _callback;
            private IntPtr _notification;

            public MonitorReturnWatch(DisplaySnapshot snapshot)
            {
                _monitors = new HashSet<string>(snapshot.NvidiaMonitorPaths(), StringComparer.OrdinalIgnoreCase);
                _callback = OnNotify;
                if (_monitors.Count == 0) return;

                var filter = new CmNotifyFilter
                {
                    cbSize = (uint)Marshal.SizeOf<CmNotifyFilter>(),
                    filterType = CmNotifyFilterTypeDeviceInterface,
                    classGuid = MonitorInterface
                };
                if (CM_Register_Notification(ref filter, IntPtr.Zero, _callback, out _notification) != CrSuccess)
                    _notification = IntPtr.Zero;
            }

            public bool WaitRemoved(int timeoutMs) => _notification != IntPtr.Zero && _removed.Wait(timeoutMs);

            public void WaitArrived(int timeoutMs) => _arrived.Wait(timeoutMs);

            private uint OnNotify(IntPtr notification, IntPtr context, uint action, IntPtr eventData, uint eventDataSize)
            {
                string link = Marshal.PtrToStringUni(eventData + EventSymbolicLinkOffset);
                if (link == null || !_monitors.Contains(link)) return CrSuccess;

                if (action == CmNotifyActionDeviceInterfaceRemoval)
                    _removed.Set();
                else if (action == CmNotifyActionDeviceInterfaceArrival)
                    _arrived.Set();

                return CrSuccess;
            }

            public void Dispose()
            {
                if (_notification != IntPtr.Zero)
                {
                    CM_Unregister_Notification(_notification);
                    _notification = IntPtr.Zero;
                }

                _removed.Dispose();
                _arrived.Dispose();
            }
        }

        #endregion
    }
}
