using System;

using DisplayProfileManager.Profiles;

namespace DisplayProfileManager.Application
{
    internal sealed class EngineeringOverrides
    {
        internal const string ForceApplyFailureVariable = "DPM_ENGINEERING_FORCE_APPLY_FAILURE";
        internal const string ForceTopologyRecoveryVariable = "DPM_ENGINEERING_FORCE_TOPOLOGY_RECOVERY";

        internal ProfileApplyFailureStage? ForceApplyFailureStage { get; }
        internal bool ForceTopologyRecovery { get; }
        internal bool AnyActive => ForceApplyFailureStage.HasValue || ForceTopologyRecovery;

        private EngineeringOverrides(ProfileApplyFailureStage? forceApplyFailureStage, bool forceTopologyRecovery)
        {
            ForceApplyFailureStage = forceApplyFailureStage;
            ForceTopologyRecovery = forceTopologyRecovery;
        }

        internal static EngineeringOverrides Resolve(Func<string, string> readVariable, Action<string> warningSink = null)
        {
            if (readVariable == null) throw new ArgumentNullException(nameof(readVariable));

            string applyRaw = readVariable(ForceApplyFailureVariable)?.Trim();
            string recoveryRaw = readVariable(ForceTopologyRecoveryVariable)?.Trim();

            ProfileApplyFailureStage? forceApplyFailureStage = applyRaw switch
            {
                null or "" or "0" => null,
                "1" => ProfileApplyFailureStage.Topology,
                "2" => ProfileApplyFailureStage.DisplayConfiguration,
                _ => InvalidApplyFailure(applyRaw, warningSink)
            };

            bool forceTopologyRecovery = recoveryRaw switch
            {
                null or "" or "0" => false,
                "1" => true,
                _ => InvalidTopologyRecovery(recoveryRaw, warningSink)
            };

            return new EngineeringOverrides(forceApplyFailureStage, forceTopologyRecovery);
        }

        private static ProfileApplyFailureStage? InvalidApplyFailure(string value, Action<string> warningSink)
        {
            warningSink?.Invoke(
                $"event=EngineeringOverrideInvalid variable={ForceApplyFailureVariable} value='{value}' -> ignored");
            return null;
        }

        private static bool InvalidTopologyRecovery(string value, Action<string> warningSink)
        {
            warningSink?.Invoke(
                $"event=EngineeringOverrideInvalid variable={ForceTopologyRecoveryVariable} value='{value}' -> ignored");
            return false;
        }
    }
}
