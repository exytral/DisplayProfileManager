using System;
using System.Collections.Generic;
using System.Linq;

using NLog;

using DisplayProfileManager.Common;
using DisplayProfileManager.Vrr.Amd;
using DisplayProfileManager.Vrr.Models;
using DisplayProfileManager.Vrr.Nvidia;

namespace DisplayProfileManager.Vrr
{
    // Routes one vendor-neutral VRR mode to every vendor backend that finds a capable display
    public class VrrService : IVrrService
    {
        private static readonly Logger _logger = LoggerHelper.GetLogger();

        private readonly IReadOnlyList<IVrrBackend> _backends;

        public VrrService() : this([new NvidiaVrrBackend(), new AmdVrrBackend()])
        {
        }

        internal VrrService(IReadOnlyList<IVrrBackend> backends)
        {
            _backends = backends ?? throw new ArgumentNullException(nameof(backends));
        }

        public VrrCapability GetCapability() => Combine(_backends.Select(b => b.GetCapability()));

        public bool ApplyVrrSettings(VrrSetting settings)
        {
            if (settings == null) return true;

            var capable = _backends.Where(b => b.GetCapability().IsAvailable).ToList();
            if (capable.Count == 0)
            {
                _logger.Warn("Skipping VRR apply -> no variable refresh rate capable display was found");
                return false;
            }

            // Every capable backend runs even when an earlier one fails
            bool success = true;
            foreach (var backend in capable)
                success &= backend.Apply(settings.Mode);

            return success;
        }

        internal static VrrCapability Combine(IEnumerable<VrrCapability> capabilities)
        {
            var available = capabilities.Where(c => c.IsAvailable).ToList();
            if (available.Count == 0) return VrrCapability.Unavailable;

            return new VrrCapability(
                true,
                string.Join(" and ", available.Select(c => c.Technology)),
                available.Any(c => c.SupportsFullscreenOnly),
                available.Select(c => c.CurrentMode).FirstOrDefault(m => m.HasValue));
        }
    }
}
