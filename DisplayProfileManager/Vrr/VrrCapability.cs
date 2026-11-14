using DisplayProfileManager.Vrr.Models;

namespace DisplayProfileManager.Vrr
{
    // What variable refresh rate control this PC offers: the detected technology, whether a fullscreen-only mode exists, and the live mode if readable
    public sealed record VrrCapability(bool IsAvailable, string Technology, bool SupportsFullscreenOnly, VrrMode? CurrentMode)
    {
        public static VrrCapability Unavailable { get; } = new(false, null, false, null);
    }
}
