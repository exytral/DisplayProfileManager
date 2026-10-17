namespace DisplayProfileManager.Profiles.Policy
{
    internal static class ProfileRollbackPolicy
    {
        public enum RollbackTarget { None, PreviousProfile, Snapshot }

        public static RollbackTarget SelectRollbackTarget(
            bool rollbackAfterApplyFailure,
            bool rollbackToPreviousProfile,
            bool hasPreviousProfile)
        {
            if (!rollbackAfterApplyFailure)
            {
                return RollbackTarget.None;
            }
            return rollbackToPreviousProfile && hasPreviousProfile
                ? RollbackTarget.PreviousProfile
                : RollbackTarget.Snapshot;
        }
    }
}
