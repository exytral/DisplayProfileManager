using System;

using DisplayProfileManager.Profiles.Models;

namespace DisplayProfileManager.Application
{
    internal enum HeadlessApplyOutcome
    {
        Complete,
        ApplyFailed,
        CurrentProfilePersistenceFailed
    }

    internal static class HeadlessApplyPolicy
    {
        public static HeadlessApplyOutcome Evaluate(ProfileApplyResult result)
        {
            ArgumentNullException.ThrowIfNull(result);

            if (!result.Success)
            {
                return HeadlessApplyOutcome.ApplyFailed;
            }
            if (!result.CurrentProfilePersisted)
            {
                return HeadlessApplyOutcome.CurrentProfilePersistenceFailed;
            }

            return HeadlessApplyOutcome.Complete;
        }
    }
}
